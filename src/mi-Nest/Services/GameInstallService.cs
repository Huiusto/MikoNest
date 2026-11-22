using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MiNest.Services;

/// <summary>
/// 软件内安装游戏，优先走官方 Sophon chunk 清单：
/// 直接按官方文件清单把压缩块解压写入目标文件，和官方启动器一致，不下载整包、不落临时大包。
/// 每次写入都会校验块和文件 MD5；停止时删除本次已写入的文件，可随时卸载掉半成品。
/// 只有拿不到 chunk 清单时才回退到旧散文件清单或分卷包。
/// </summary>
public static class GameInstallService
{
    private const int ParallelDownloads = 8;
    private const string JournalName = ".MikoNest-install-journal";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromMinutes(30),
        };
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "MikoNest-Installer/0.1.28");
        return c;
    }

    public sealed record Progress(string Stage, long Done, long Total, double BytesPerSecond,
        long InstallDone = 0, long InstallTotal = 0);

    /// <summary>
    /// 可暂停的安装闸门：暂停时新的 chunk 会停在下载前，已经在下载的少量 chunk 会先完成；
    /// 恢复后从原位置继续，不取消任务，也不会触发清理。
    /// </summary>
    public sealed class PauseController
    {
        private volatile bool _paused;

        public bool IsPaused => _paused;
        public void Pause() => _paused = true;
        public void Resume() => _paused = false;

        public async Task WaitAsync(CancellationToken token)
        {
            while (_paused)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(120, token).ConfigureAwait(false);
            }
        }
    }

    private sealed class PkgFile
    {
        public string RemoteName { get; init; } = "";
        public long Size { get; init; }
    }

    public static async Task<string> InstallAsync(
        string gameKey, string destDir, string language, IProgress<Progress>? progress, CancellationToken token,
        PauseController? pause = null)
    {
        var existedBefore = Directory.Exists(destDir);
        Directory.CreateDirectory(destDir);

        // 官方启动器当前使用的安装方式：getGameBranches + getBuild + Sophon chunk。
        var chunkBuild = await LauncherBackgroundService.GetChunkBuildAsync(gameKey, language).ConfigureAwait(false);
        if (chunkBuild is not null)
            return await InstallFromChunksAsync(gameKey, chunkBuild, destDir, existedBefore, progress, token, pause).ConfigureAwait(false);

        // 老接口兜底。注意 getGamePackages 的版本可能落后于当前正式版本。
        var pkg = await LauncherBackgroundService.GetInstallPackageAsync(gameKey, language)
                  ?? throw new InvalidOperationException("官方安装清单获取失败，请检查网络后重试。");

        if (!string.IsNullOrEmpty(pkg.ResListUrl))
            return await InstallFromResListAsync(gameKey, pkg, destDir, language, existedBefore, progress, token).ConfigureAwait(false);

        return await InstallFromArchivesAsync(gameKey, pkg, destDir, existedBefore, progress, token).ConfigureAwait(false);
    }

    // ---------------- 官方 Sophon chunk 直装 ----------------

    private sealed class ChunkEntry
    {
        public string Rel = "";
        public long Size;
        public string Md5 = "";
        public List<SophonChunk> Chunks = new();
        public string UrlPrefix = "";
        public string UrlSuffix = "";
        public bool Compressed = true;

        public long CompressedTotal => Chunks.Sum(c => c.CompressedSize);
    }

    /// <summary>
    /// 和官方启动器一致：清单里的每个文件按 chunk 顺序下载、解压、写到目标文件对应偏移。
    /// 先写 *.miNest.part，校验完整文件 MD5 后再替换正式文件；更新已有文件时先备份旧文件，停止时回滚。
    /// </summary>
    private static async Task<string> InstallFromChunksAsync(
        string gameKey, LauncherBackgroundService.ChunkBuildInfo build, string destDir,
        bool existedBefore, IProgress<Progress>? progress, CancellationToken token, PauseController? pause)
    {
        progress?.Report(new Progress("正在读取官方文件清单…", 0, 0, 0));

        var entries = new List<ChunkEntry>();
        var folders = new List<string>();
        foreach (var manifest in build.Manifests)
        {
            token.ThrowIfCancellationRequested();
            var bytes = await DownloadManifestBytesAsync(manifest, token).ConfigureAwait(false);
            var files = SophonManifest.Parse(bytes);
            foreach (var f in files)
            {
                var rel = NormalizeRel(f.File);
                if (string.IsNullOrEmpty(rel)) continue;
                if (f.IsFolder)
                {
                    folders.Add(rel);
                    continue;
                }
                entries.Add(new ChunkEntry
                {
                    Rel = rel,
                    Size = f.Size,
                    Md5 = f.Md5,
                    Chunks = f.Chunks,
                    UrlPrefix = manifest.ChunkUrlPrefix,
                    UrlSuffix = manifest.ChunkUrlSuffix,
                    Compressed = manifest.ChunkCompressed,
                });
            }
        }

        if (entries.Count == 0)
            throw new InvalidOperationException("官方文件清单为空，请稍后重试。");

        foreach (var rel in folders)
            Directory.CreateDirectory(SafeFullPath(destDir, rel)!);

        var totalInstall = entries.Sum(x => Math.Max(0, x.Size));
        var totalDownload = entries.Sum(x => x.CompressedTotal);
        EnsureSpace(destDir, totalInstall, "安装目录");

        var journal = Path.Combine(destDir, JournalName);
        // 上次异常退出留下的半成品：回滚旧文件、删除新文件，再重新开始。
        RecoverChunkJournal(destDir, journal);

        long done = 0;
        long installed = 0;
        var sw = Stopwatch.StartNew();
        var sync = new object();
        var journalLock = new object();
        var lastReport = 0L;

        void Report(string stage, long delta, long installDelta)
        {
            long value;
            long installValue;
            lock (sync)
            {
                done += delta;
                value = done;
            }
            Interlocked.Add(ref installed, Math.Max(0, installDelta));
            installValue = Interlocked.Read(ref installed);
            if (value - Interlocked.Read(ref lastReport) < 4 * 1024 * 1024 && value < totalDownload)
                return;
            Interlocked.Exchange(ref lastReport, value);
            var speed = sw.Elapsed.TotalSeconds > 1 ? value / sw.Elapsed.TotalSeconds : 0;
            progress?.Report(new Progress(stage, value, totalDownload, speed,
                Math.Min(installValue, totalInstall), totalInstall));
        }

        void Journal(string line)
        {
            lock (journalLock)
            {
                try { File.AppendAllText(journal, line + "\n", new UTF8Encoding(false)); }
                catch { }
            }
        }

        void EnsureJournal()
        {
            lock (journalLock)
            {
                try
                {
                    if (!File.Exists(journal))
                        File.WriteAllText(journal, "# MikoNest chunk install journal\n", new UTF8Encoding(false));
                }
                catch { }
            }
        }

        EnsureJournal();
        var tasks = new List<Task>(entries.Count);
        try
        {
            var gate = new SemaphoreSlim(ParallelDownloads);
            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                if (pause != null) await pause.WaitAsync(token).ConfigureAwait(false);
                await gate.WaitAsync(token).ConfigureAwait(false);
                if (pause != null)
                {
                    try { await pause.WaitAsync(token).ConfigureAwait(false); }
                    catch { gate.Release(); throw; }
                }
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var stage = $"正在写入 {Path.GetFileName(entry.Rel)}";
                        var written = await InstallOneChunkFileAsync(
                            destDir, entry, journal, Journal, token,
                            (delta, installDelta) => Report(stage, delta, installDelta), pause).ConfigureAwait(false);
                        if (written) Journal("F " + entry.Rel);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }, token));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            progress?.Report(new Progress("正在校验安装结果…", totalDownload, totalDownload, 0, totalInstall, totalInstall));
            WriteConfig(destDir, build.Version, build.GameBiz);
            CommitChunkJournal(destDir, journal);
            var finalDir = FinishInstall(gameKey, destDir);
            progress?.Report(new Progress("安装完成", totalDownload, totalDownload, 0, totalInstall, totalInstall));
            return finalDir;
        }
        catch (OperationCanceledException)
        {
            try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch { }
            CleanupChunkJournal(destDir, journal, existedBefore);
            throw;
        }
        catch
        {
            try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch { }
            CleanupChunkJournal(destDir, journal, existedBefore);
            throw;
        }
    }

    private static async Task<byte[]> DownloadManifestBytesAsync(
        LauncherBackgroundService.ChunkManifestInfo info, CancellationToken token)
    {
        var url = JoinUrl(info.ManifestUrlPrefix, info.ManifestUrlSuffix, info.ManifestId);
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var raw = await resp.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
        var data = info.ManifestCompressed ? MaybeUnwrapZstd(raw) : raw;
        if (!string.IsNullOrEmpty(info.ManifestChecksum))
        {
            var md5 = Convert.ToHexString(MD5.HashData(data)).ToLowerInvariant();
            if (!md5.Equals(info.ManifestChecksum, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"文件清单校验失败：{info.MatchingField}");
        }
        return data;
    }

    private static async Task<bool> InstallOneChunkFileAsync(
        string destRoot, ChunkEntry entry, string journal, Action<string> journalWrite,
        CancellationToken token, Action<long, long> onDelta, PauseController? pause)
    {
        var full = SafeFullPath(destRoot, entry.Rel)
                   ?? throw new InvalidOperationException("安装清单包含越界路径：" + entry.Rel);
        var part = full + ".miNest.part";
        var bak = full + ".miNest.bak";
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        // 已安装且 MD5 一致，跳过整个文件。
        if (File.Exists(full) && new FileInfo(full).Length == entry.Size
            && await FileMd5EqualsAsync(full, entry.Md5, token).ConfigureAwait(false))
        {
            onDelta(entry.CompressedTotal, entry.Size);
            return false;
        }

        // 上次已经完整下载到 part，只差替换。
        if (File.Exists(part) && new FileInfo(part).Length == entry.Size
            && await FileMd5EqualsAsync(part, entry.Md5, token).ConfigureAwait(false))
        {
            ReplaceFileWithBackup(full, part, bak, entry.Rel, journalWrite);
            onDelta(entry.CompressedTotal, entry.Size);
            return true;
        }
        if (File.Exists(part) && entry.Size >= 0 && new FileInfo(part).Length > entry.Size)
            try { File.Delete(part); } catch { }

        await using (var fs = new FileStream(part, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                         FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            if (fs.Length > entry.Size) fs.SetLength(0);

            foreach (var chunk in entry.Chunks)
            {
                token.ThrowIfCancellationRequested();
                if (pause != null) await pause.WaitAsync(token).ConfigureAwait(false);
                var end = chunk.Offset + chunk.UncompressedSize;
                if (fs.Length >= end)
                {
                    onDelta(chunk.CompressedSize, chunk.UncompressedSize);
                    continue;
                }
                if (fs.Length > chunk.Offset)
                {
                    fs.SetLength(chunk.Offset);
                }
                else if (fs.Length < chunk.Offset)
                {
                    fs.SetLength(chunk.Offset);
                }

                var url = JoinUrl(entry.UrlPrefix, entry.UrlSuffix, chunk.Id);
                var payload = await DownloadChunkBytesAsync(url, entry.Compressed, chunk, token).ConfigureAwait(false);
                fs.Position = chunk.Offset;
                await fs.WriteAsync(payload.AsMemory(), token).ConfigureAwait(false);
                onDelta(chunk.CompressedSize, payload.Length);
            }

            if (fs.Length != entry.Size)
                throw new IOException($"文件大小不一致：{entry.Rel}");
        }

        if (!await FileMd5EqualsAsync(part, entry.Md5, token).ConfigureAwait(false))
        {
            try { File.Delete(part); } catch { }
            throw new IOException($"文件校验失败：{entry.Rel}");
        }

        ReplaceFileWithBackup(full, part, bak, entry.Rel, journalWrite);
        return true;
    }

    private static async Task<byte[]> DownloadChunkBytesAsync(
        string url, bool compressedHint, SophonChunk chunk, CancellationToken token)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var raw = await resp.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(chunk.CompressedMd5))
        {
            var md5 = Convert.ToHexString(MD5.HashData(raw)).ToLowerInvariant();
            if (!md5.Equals(chunk.CompressedMd5, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"压缩块校验失败：{chunk.Id}");
        }

        var data = LooksLikeZstd(raw) || compressedHint ? MaybeUnwrapZstd(raw, strict: compressedHint) : raw;
        if (chunk.UncompressedSize >= 0 && data.Length != chunk.UncompressedSize)
            throw new IOException($"文件块大小不一致：{chunk.Id}");
        if (!string.IsNullOrEmpty(chunk.UncompressedMd5))
        {
            var md5 = Convert.ToHexString(MD5.HashData(data)).ToLowerInvariant();
            if (!md5.Equals(chunk.UncompressedMd5, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"文件块校验失败：{chunk.Id}");
        }
        return data;
    }

    private static byte[] MaybeUnwrapZstd(byte[] raw, bool strict = false)
    {
        if (!LooksLikeZstd(raw))
        {
            if (strict) return raw;
            return raw;
        }
        try
        {
            using var decompressor = new ZstdSharp.Decompressor();
            return decompressor.Unwrap(raw).ToArray();
        }
        catch when (!strict)
        {
            return raw;
        }
    }

    private static bool LooksLikeZstd(byte[] data)
        => data.Length >= 4 && data[0] == 0x28 && data[1] == 0xB5 && data[2] == 0x2F && data[3] == 0xFD;

    private static async Task<bool> FileMd5EqualsAsync(string path, string expected, CancellationToken token)
    {
        if (string.IsNullOrEmpty(expected)) return true;
        try
        {
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var actual = Convert.ToHexString(await MD5.HashDataAsync(fs, token).ConfigureAwait(false))
                .ToLowerInvariant();
            return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void ReplaceFileWithBackup(
        string full, string part, string backup, string rel, Action<string> journalWrite)
    {
        if (File.Exists(full))
        {
            journalWrite("B " + rel);
            try { File.Move(full, backup, true); }
            catch { try { File.Delete(full); } catch { } }
        }
        File.Move(part, full, true);
    }

    private static void CommitChunkJournal(string destDir, string journal)
    {
        try
        {
            // 先删日志：登录/断电时若备份还没删完，下次启动也不会把已完成的安装回滚掉。
            var lines = File.Exists(journal) ? File.ReadAllLines(journal) : Array.Empty<string>();
            try { File.Delete(journal); } catch { }
            foreach (var line in lines)
            {
                if (!line.StartsWith("B ", StringComparison.Ordinal)) continue;
                var rel = line[2..].Trim();
                var full = SafeFullPath(destDir, rel);
                if (full != null)
                {
                    try { File.Delete(full + ".miNest.bak"); } catch { }
                }
            }
        }
        catch
        {
            // 清理备份失败不影响游戏文件本身
        }
    }

    private static void CleanupChunkJournal(string destDir, string journal, bool removeCreatedRoot)
    {
        var backups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var created = new List<string>();
        try
        {
            if (File.Exists(journal))
            {
                foreach (var line in File.ReadAllLines(journal))
                {
                    if (line.Length < 3) continue;
                    var kind = line[0];
                    var rel = line[2..].Trim();
                    if (string.IsNullOrEmpty(rel)) continue;
                    if (kind == 'B') backups.Add(rel);
                    else if (kind == 'F') created.Add(rel);
                }
            }

            // 先恢复被替换的旧文件
            foreach (var rel in backups)
            {
                var full = SafeFullPath(destDir, rel);
                if (full == null) continue;
                var bak = full + ".miNest.bak";
                if (File.Exists(bak))
                {
                    try { File.Move(bak, full, true); } catch { }
                }
            }

            // 再删除本次新写入的文件（有备份的已经恢复，不能删）
            foreach (var rel in created)
            {
                if (backups.Contains(rel)) continue;
                var full = SafeFullPath(destDir, rel);
                if (full == null) continue;
                try { if (File.Exists(full)) File.Delete(full); } catch { }
            }

            // 删除所有 part 和残留备份
            foreach (var f in Directory.EnumerateFiles(destDir, "*.miNest.part", SearchOption.AllDirectories))
                try { File.Delete(f); } catch { }
            foreach (var f in Directory.EnumerateFiles(destDir, "*.miNest.bak", SearchOption.AllDirectories))
                try { File.Delete(f); } catch { }
            try { File.Delete(journal); } catch { }
        }
        catch
        {
            // 尽力清理
        }

        if (removeCreatedRoot)
        {
            try
            {
                if (Directory.Exists(destDir) && !Directory.EnumerateFileSystemEntries(destDir).Any())
                    Directory.Delete(destDir, true);
            }
            catch { }
        }
    }

    private static void RecoverChunkJournal(string destDir, string journal)
    {
        if (File.Exists(journal))
            CleanupChunkJournal(destDir, journal, false);
    }

    private static string NormalizeRel(string path)
    {
        var rel = (path ?? "").Replace('\\', '/').Trim();
        while (rel.StartsWith("/", StringComparison.Ordinal)) rel = rel[1..];
        if (rel.Length == 0) return "";
        var parts = rel.Split('/');
        if (parts.Any(p => p == ".." || p.Contains(':')))
            throw new InvalidOperationException("安装清单包含非法路径：" + path);
        return string.Join('/', parts.Where(p => p.Length > 0));
    }

    private static string? SafeFullPath(string root, string rel)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static string JoinUrl(string prefix, string suffix, string id)
    {
        var p = (prefix ?? "").TrimEnd('/');
        var s = suffix ?? "";
        return p + "/" + id + (s.Length > 0 ? s : "");
    }

    // ---------------- 官方散文件直装 ----------------

    private static async Task<string> InstallFromResListAsync(
        string gameKey, LauncherBackgroundService.InstallPackage pkg, string destDir, string language,
        bool existedBefore, IProgress<Progress>? progress, CancellationToken token)
    {
        var baseUrl = pkg.ResListUrl.TrimEnd('/');
        progress?.Report(new Progress("正在读取官方安装清单…", 0, 0, 0));

        var files = await ReadManifestAsync($"{baseUrl}/pkg_version", token);
        if (files.Count == 0)
            throw new InvalidOperationException("官方安装清单为空，请稍后重试。");

        // 语音包：官方散文件目录里通常有 Audio_Chinese_pkg_version 等独立清单。
        List<PkgFile> audioFiles = new();
        var audioNames = language switch
        {
            "en-us" => new[] { "English(US)", "English" },
            "ja-jp" => new[] { "Japanese" },
            "ko-kr" => new[] { "Korean" },
            _ => new[] { "Chinese" },
        };
        foreach (var audioName in audioNames)
        {
            try
            {
                audioFiles = await ReadManifestAsync($"{baseUrl}/Audio_{audioName}_pkg_version", token);
                if (audioFiles.Count > 0) break;
            }
            catch (OperationCanceledException) { throw; }
            catch { /* 没有散文件语音清单时，最后再回退到语音压缩包 */ }
        }

        var all = files.Concat(audioFiles).ToList();
        var total = all.Sum(f => f.Size);
        EnsureSpace(destDir, total, "安装目录");

        var journal = Path.Combine(destDir, JournalName);
        if (!File.Exists(journal)) File.WriteAllText(journal, "# MikoNest 安装记录\n", new UTF8Encoding(false));

        long done = 0;
        var sw = Stopwatch.StartNew();
        var sync = new object();
        var lastReport = 0L;

        void Report(string stage, long delta)
        {
            long value;
            lock (sync)
            {
                done += delta;
                value = done;
            }
            if (value - Interlocked.Read(ref lastReport) < 2 * 1024 * 1024 && value < total)
                return;
            Interlocked.Exchange(ref lastReport, value);
            var speed = sw.Elapsed.TotalSeconds > 1 ? value / sw.Elapsed.TotalSeconds : 0;
            progress?.Report(new Progress(stage, value, total, speed));
        }

        try
        {
            var tasks = new List<Task>(all.Count);
            var gate = new SemaphoreSlim(ParallelDownloads);
            foreach (var f in all)
            {
                token.ThrowIfCancellationRequested();
                await gate.WaitAsync(token);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        var stage = $"正在写入 {Path.GetFileName(f.RemoteName)}";
                        var written = await DownloadOneAsync(baseUrl, f, destDir, token,
                            delta => Report(stage, delta));
                        if (written)
                            lock (sync)
                            {
                                try { File.AppendAllText(journal, f.RemoteName + "\n"); }
                                catch { }
                            }
                    }
                    finally
                    {
                        gate.Release();
                    }
                }, token));
            }

            await Task.WhenAll(tasks);

            // 星铁的中文语音已经包含在基础清单里，不需要再下载整包语音压缩包。
            if (audioFiles.Count == 0 && pkg.AudioParts.Count > 0
                && !(gameKey == "starrail" && language == "zh-cn"))
            {
                progress?.Report(new Progress("正在安装语音包…", total, total, 0));
                await ExtractAudioArchivesAsync(pkg, destDir, token);
            }

            WriteConfig(destDir, pkg);
            try { File.Delete(journal); } catch { }

            progress?.Report(new Progress("正在校验安装结果…", total, total, 0));
            var finalDir = FinishInstall(gameKey, destDir);
            progress?.Report(new Progress("安装完成", total, total, 0));
            return finalDir;
        }
        catch (OperationCanceledException)
        {
            CleanupPartial(destDir, journal, existedBefore);
            throw;
        }
    }

    private static async Task<List<PkgFile>> ReadManifestAsync(string url, CancellationToken token)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        if (resp.StatusCode == HttpStatusCode.NotFound) return new List<PkgFile>();
        resp.EnsureSuccessStatusCode();

        var list = new List<PkgFile>();
        await using var stream = await resp.Content.ReadAsStreamAsync(token);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (!reader.EndOfStream)
        {
            token.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(token);
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var name = root.TryGetProperty("remoteName", out var n) ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                name = name.Replace('\\', '/').TrimStart('/');
                if (name.Contains("../", StringComparison.Ordinal) || name.StartsWith("..", StringComparison.Ordinal))
                    continue;
                long size = 0;
                if (root.TryGetProperty("fileSize", out var s))
                {
                    if (s.ValueKind == JsonValueKind.Number) size = s.GetInt64();
                    else if (s.ValueKind == JsonValueKind.String && long.TryParse(s.GetString(), out var parsed))
                        size = parsed;
                }
                list.Add(new PkgFile { RemoteName = name, Size = size });
            }
            catch (JsonException) { /* 忽略坏行 */ }
        }
        return list;
    }

    /// <summary>下载一个散文件；返回 true 表示本次确实写入了文件（用于记录清理日志）。</summary>
    private static async Task<bool> DownloadOneAsync(string baseUrl, PkgFile file, string destRoot,
        CancellationToken token, Action<long> onDelta)
    {
        var rootFull = Path.GetFullPath(destRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(destRoot, file.RemoteName.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("安装清单包含越界路径：" + file.RemoteName);
        var part = full + ".miNest.part";
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        long existingFull = File.Exists(full) ? new FileInfo(full).Length : -1;
        if (file.Size > 0 && existingFull == file.Size)
        {
            onDelta(file.Size);
            return false;
        }
        if (existingFull >= 0 && file.Size > 0 && existingFull != file.Size)
        {
            try { File.Delete(full); } catch { }
        }

        long existing = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (file.Size > 0 && existing > file.Size)
        {
            try { File.Delete(part); } catch { }
            existing = 0;
        }
        if (file.Size > 0 && existing == file.Size)
        {
            File.Move(part, full, true);
            onDelta(file.Size);
            return true;
        }

        var url = BuildFileUrl(baseUrl, file.RemoteName);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0) req.Headers.Range = new RangeHeaderValue(existing, null);
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);
        if (resp.StatusCode == HttpStatusCode.OK) existing = 0;
        resp.EnsureSuccessStatusCode();

        await using var src = await resp.Content.ReadAsStreamAsync(token);
        await using var dst = new FileStream(part, existing > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.Read, 81920, true);

        if (existing > 0) onDelta(existing);

        var buffer = new byte[81920];
        long totalRead = existing;
        long lastDelta = existing;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var read = await src.ReadAsync(buffer, token);
            if (read <= 0) break;
            await dst.WriteAsync(buffer.AsMemory(0, read), token);
            totalRead += read;
            if (totalRead - lastDelta >= 4 * 1024 * 1024)
            {
                onDelta(totalRead - lastDelta);
                lastDelta = totalRead;
            }
        }
        onDelta(totalRead - lastDelta);
        await dst.FlushAsync(token);
        dst.Dispose();

        if (file.Size > 0 && totalRead != file.Size)
            throw new IOException($"文件大小不一致：{file.RelativeName()}");

        File.Move(part, full, true);
        return true;
    }

    private static string BuildFileUrl(string baseUrl, string remoteName)
    {
        var escaped = string.Join("/", remoteName.Split('/').Select(Uri.EscapeDataString));
        return baseUrl + "/" + escaped;
    }

    // ---------------- 分卷包回退（崩坏3） ----------------

    private static async Task<string> InstallFromArchivesAsync(
        string gameKey, LauncherBackgroundService.InstallPackage pkg, string destDir,
        bool existedBefore, IProgress<Progress>? progress, CancellationToken token)
    {
        var temp = Path.Combine(Path.GetTempPath(), "MikoNest-install-" + gameKey);
        Directory.CreateDirectory(temp);
        var files = pkg.Parts.Concat(pkg.AudioParts).ToList();
        var total = files.Sum(x => x.Size);
        EnsureSpace(destDir, pkg.TotalDiskBytes, "安装目录");
        EnsureSpace(temp, total, "临时目录");

        long done = 0;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < files.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var part = files[i];
            var name = FileName(part.Url);
            var file = Path.Combine(temp, name);
            await DownloadArchiveAsync(part.Url, file, part.Size, token, delta =>
            {
                var current = done + delta;
                var speed = sw.Elapsed.TotalSeconds > 0.5 ? current / sw.Elapsed.TotalSeconds : 0;
                progress?.Report(new Progress($"下载 {name}（{i + 1}/{files.Count}）", current, total, speed));
            });
            done += part.Size;
        }

        progress?.Report(new Progress("正在解压安装包…", total, total, 0));
        var seven = await Ensure7ZipAsync(token)
                    ?? throw new InvalidOperationException("未找到 7-Zip 解压组件，且自动下载失败。");
        if (pkg.Parts.Count > 0)
            await ExtractAsync(seven, Path.Combine(temp, FileName(pkg.Parts[0].Url)), destDir, token);
        foreach (var audio in pkg.AudioParts)
            await ExtractAsync(seven, Path.Combine(temp, FileName(audio.Url)), destDir, token);

        try { Directory.Delete(temp, true); } catch { }
        WriteConfig(destDir, pkg);
        return FinishInstall(gameKey, destDir);
    }

    private static async Task ExtractAudioArchivesAsync(
        LauncherBackgroundService.InstallPackage pkg, string destDir, CancellationToken token)
    {
        var seven = await Ensure7ZipAsync(token)
                    ?? throw new InvalidOperationException("未找到 7-Zip 解压组件，且自动下载失败。");
        var temp = Path.Combine(Path.GetTempPath(), "MikoNest-audio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            foreach (var audio in pkg.AudioParts)
            {
                token.ThrowIfCancellationRequested();
                var file = Path.Combine(temp, FileName(audio.Url));
                await DownloadArchiveAsync(audio.Url, file, audio.Size, token, _ => { });
                await ExtractAsync(seven, file, destDir, token);
            }
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    private static async Task ExtractAsync(string seven, string archive, string destDir, CancellationToken token)
    {
        if (!File.Exists(archive)) throw new InvalidOperationException($"安装包缺失：{Path.GetFileName(archive)}");
        var psi = new ProcessStartInfo(seven, $"x \"{archive}\" -o\"{destDir}\" -y -bso0 -bsp0")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("无法启动解压进程。");
        await proc.WaitForExitAsync(token);
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"解压失败（7-Zip 退出码 {proc.ExitCode}）。");
    }

    // ---------------- 收尾 / 清理 ----------------

    private static string FinishInstall(string gameKey, string destDir)
    {
        var game = GameLocator.Get(gameKey);
        var exe = GameLocator.FindExe(destDir, game);
        if (exe == null)
        {
            foreach (var sub in Directory.GetDirectories(destDir))
            {
                exe = GameLocator.FindExe(sub, game);
                if (exe != null) { destDir = sub; break; }
            }
        }
        if (exe == null)
            throw new InvalidOperationException("安装完成，但没有找到游戏主程序，请检查安装目录。");

        SettingsService.Settings.GamePaths[gameKey] = destDir;
        SettingsService.Save();
        return destDir;
    }

    private static void WriteConfig(string destDir, LauncherBackgroundService.InstallPackage pkg)
        => WriteConfig(destDir, pkg.Version, pkg.GameBiz);

    private static void WriteConfig(string destDir, string version, string gameBiz)
    {
        try
        {
            var path = Path.Combine(destDir, "config.ini");
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(path))
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    if (line.TrimStart().StartsWith("[", StringComparison.Ordinal)) continue;
                    var i = line.IndexOf('=');
                    if (i <= 0) continue;
                    values[line[..i].Trim()] = line[(i + 1)..].Trim();
                }
            }
            values["game_version"] = version;
            values["channel"] = "1";
            values["sub_channel"] = "1";
            values["cps"] = "hyp_mihoyo";
            values["sdk_version"] = values.TryGetValue("sdk_version", out var sdk) ? sdk : "";
            values["game_biz"] = gameBiz;

            var sb = new StringBuilder();
            sb.AppendLine("[General]");
            foreach (var kv in values) sb.AppendLine($"{kv.Key}={kv.Value}");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
        catch
        {
            // config.ini 写不进去不影响文件安装；游戏首次启动仍可自行生成。
        }
    }

    private static void CleanupPartial(string destDir, string journal, bool removeCreatedRoot)
    {
        var tracked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(journal))
                foreach (var line in File.ReadLines(journal))
                {
                    var rel = line.Trim().Replace('/', Path.DirectorySeparatorChar);
                    if (rel.Length == 0 || rel.StartsWith("#", StringComparison.Ordinal)) continue;
                    var full = Path.GetFullPath(Path.Combine(destDir, rel));
                    if (full.StartsWith(Path.GetFullPath(destDir), StringComparison.OrdinalIgnoreCase))
                        tracked.Add(full);
                }
        }
        catch { }

        foreach (var file in tracked)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }

        try
        {
            foreach (var part in Directory.EnumerateFiles(destDir, "*.miNest.part", SearchOption.AllDirectories))
            {
                try { File.Delete(part); } catch { }
            }
        }
        catch { }

        try { if (File.Exists(journal)) File.Delete(journal); } catch { }

        if (removeCreatedRoot)
        {
            try { Directory.Delete(destDir, true); } catch { }
        }
        else
        {
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(destDir, "*", SearchOption.AllDirectories)
                             .OrderByDescending(d => d.Length))
                {
                    try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch { }
                }
            }
            catch { }
        }
    }

    private static void EnsureSpace(string dir, long required, string label)
    {
        if (required <= 0) return;
        var root = Path.GetPathRoot(Path.GetFullPath(dir));
        if (string.IsNullOrEmpty(root)) return;
        var free = new DriveInfo(root).AvailableFreeSpace;
        if (free < required + 512L * 1024 * 1024)
            throw new InvalidOperationException(
                $"{label}空间不足：需要约 {required / (1024d * 1024 * 1024):0.0} GB，" +
                $"可用 {free / (1024d * 1024 * 1024):0.0} GB。");
    }

    // ---------------- 旧分卷包下载（仅回退路径使用） ----------------

    private static string FileName(string url)
    {
        var name = Path.GetFileName(new Uri(url).AbsolutePath);
        return string.IsNullOrWhiteSpace(name) ? "package.zip" : name;
    }

    private static async Task DownloadArchiveAsync(string url, string file, long expected,
        CancellationToken token, Action<long> onDelta)
    {
        long existing = File.Exists(file) ? new FileInfo(file).Length : 0;
        if (expected > 0 && existing >= expected)
        {
            onDelta(expected);
            return;
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0) req.Headers.Range = new RangeHeaderValue(existing, null);
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);
        if (resp.StatusCode == HttpStatusCode.OK) existing = 0;
        resp.EnsureSuccessStatusCode();

        await using var src = await resp.Content.ReadAsStreamAsync(token);
        await using var dst = new FileStream(file, existing > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long lastReport = 0, totalRead = existing;
        while (true)
        {
            var read = await src.ReadAsync(buffer, token);
            if (read <= 0) break;
            await dst.WriteAsync(buffer.AsMemory(0, read), token);
            totalRead += read;
            if (totalRead - lastReport >= 4 * 1024 * 1024)
            {
                onDelta(totalRead - existing);
                lastReport = totalRead;
            }
        }
        onDelta(totalRead - existing);
    }

    private static async Task<string?> Ensure7ZipAsync(CancellationToken token)
    {
        foreach (var p in new[]
        {
            @"C:\Program Files\7-Zip\7z.exe",
            @"C:\Program Files (x86)\7-Zip\7z.exe",
        })
            if (File.Exists(p)) return p;

        var dir = Path.Combine(AppPaths.DataDir, "tools");
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "7za.exe");
        if (File.Exists(exe) && new FileInfo(exe).Length > 100_000) return exe;

        var zip = Path.Combine(dir, "7za920.zip");
        try
        {
            using (var resp = await Http.GetAsync("https://www.7-zip.org/a/7za920.zip", token))
            {
                resp.EnsureSuccessStatusCode();
                await using var s = await resp.Content.ReadAsStreamAsync(token);
                await using var f = File.Create(zip);
                await s.CopyToAsync(f, token);
            }
            using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
            var entry = archive.GetEntry("7za.exe");
            if (entry != null)
            {
                await using var s = entry.Open();
                await using var f = File.Create(exe);
                await s.CopyToAsync(f, token);
            }
            try { File.Delete(zip); } catch { }
            return File.Exists(exe) ? exe : null;
        }
        catch
        {
            return null;
        }
    }

    private static string RelativeName(this PkgFile f) => f.RemoteName;
}

