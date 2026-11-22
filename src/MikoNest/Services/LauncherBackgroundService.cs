using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MiNest.Services;

/// <summary>
/// 官方启动器（HoYoPlay）版本背景：仿 Starward 的 BackgroundService。
/// 每个版本官方会下发多张背景（部分带 .webm 动态视频），这里全部缓存到
/// %AppData%\MikoNest\bg，启动页用左侧圆点按官方顺序逐张切换。
/// </summary>
public static class LauncherBackgroundService
{
    /// <summary>国服官方启动器 launcher_id（与 Starward 相同）</summary>
    public const string LauncherId = "jGHBHlcOq1";

    private const string ApiBase = "https://hyp-api.mihoyo.com/hyp/hyp-connect/api/getAllGameBasicInfo";
    private const string PackageApi = "https://hyp-api.mihoyo.com/hyp/hyp-connect/api/getGamePackages";

    /// <summary>缓存有效期：超过后重新向官方接口拉取</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    private static readonly Dictionary<string, string> BizMap = new()
    {
        ["genshin"] = "hk4e_cn",
        ["starrail"] = "hkrpg_cn",
        ["zzz"] = "nap_cn",
        ["bh3"] = "bh3_cn",
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static bool _headerAdded;

    public static string CacheDir => Path.Combine(AppPaths.DataDir, "bg");

    public sealed class BackgroundItem
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        /// <summary>官方给的背景类型：BACKGROUND_TYPE_VIDEO 表示带动态视频</summary>
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("image")] public string? ImageFile { get; set; }
        [JsonPropertyName("video")] public string? VideoFile { get; set; }
        [JsonPropertyName("videoUrl")] public string? VideoUrl { get; set; }

        [JsonIgnore] public string? ImagePath => Existing(ImageFile);
        [JsonIgnore] public string? VideoPath => Existing(VideoFile);

        private static string? Existing(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var path = Path.Combine(CacheDir, name);
            return File.Exists(path) ? path : null;
        }
    }

    public sealed class GameBackgrounds
    {
        [JsonPropertyName("key")] public string Key { get; set; } = "";
        [JsonPropertyName("fetched")] public DateTime FetchedAtUtc { get; set; }
        [JsonPropertyName("items")] public List<BackgroundItem> Items { get; set; } = new();

        [JsonIgnore] public bool HasAny => Items.Any(i => i.ImagePath != null);
    }

    private static string MetaFile(string key) => Path.Combine(CacheDir, $"official_{key}.json");

    /// <summary>某款游戏的某张动态壁纸下载完成（参数为游戏 key），供启动页刷新状态。</summary>
    public static event Action<string>? BackgroundReady;

    private static int _prefetchStarted;
    private static readonly Dictionary<string, Task<string?>> VideoJobs = new();

    /// <summary>
    /// 启动页打开时在后台把所有游戏的官方背景/动态壁纸都拉一遍，
    /// 这样切换游戏时无需再等下载；官方换版本时顺带清理旧文件。
    /// </summary>
    public static async Task PrefetchAllAsync()
    {
        if (Interlocked.Exchange(ref _prefetchStarted, 1) == 1) return;
        try
        {
            foreach (var key in BizMap.Keys.ToList())
            {
                var meta = await RefreshAsync(key).ConfigureAwait(false);
                if (meta == null || meta.Items.Count == 0) continue;
                BackgroundReady?.Invoke(key);

                using var gate = new SemaphoreSlim(2);
                var jobs = new List<Task>();
                foreach (var item in meta.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.VideoUrl) || item.VideoPath != null) continue;
                    var id = item.Id;
                    jobs.Add(Task.Run(async () =>
                    {
                        await gate.WaitAsync().ConfigureAwait(false);
                        try
                        {
                            await EnsureVideoAsync(key, id).ConfigureAwait(false);
                        }
                        finally
                        {
                            gate.Release();
                        }
                    }));
                }
                if (jobs.Count > 0) await Task.WhenAll(jobs).ConfigureAwait(false);
            }
            // 全部版本刷新完再统一清一次，旧版本壁纸立即删干净
            CleanupUnused();
        }
        catch
        {
            // 后台预加载失败不影响启动页
        }
        finally
        {
            Interlocked.Exchange(ref _prefetchStarted, 0);
        }
    }

    public static GameBackgrounds? GetCached(string gameKey)
    {
        try
        {
            var file = MetaFile(gameKey);
            if (!File.Exists(file)) return null;
            var meta = JsonSerializer.Deserialize<GameBackgrounds>(File.ReadAllText(file));
            if (meta == null || !meta.HasAny) return null;
            return meta;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsStale(string gameKey)
    {
        var meta = GetCached(gameKey);
        return meta == null || DateTime.UtcNow - meta.FetchedAtUtc > CacheTtl;
    }

    /// <summary>拉取/刷新某款游戏当前版本的全部背景（图片立即下载，视频按需下载）</summary>
    public static async Task<GameBackgrounds?> RefreshAsync(string gameKey, bool force = false)
    {
        SemaphoreSlim? existing = null;
        lock (RefreshGates)
        {
            RefreshGates.TryGetValue(gameKey, out existing);
            if (existing == null)
            {
                existing = new SemaphoreSlim(1, 1);
                RefreshGates[gameKey] = existing;
            }
        }
        var gate = existing;
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await RefreshCoreAsync(gameKey, force).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private static readonly Dictionary<string, SemaphoreSlim> RefreshGates = new();

    private static async Task<GameBackgrounds?> RefreshCoreAsync(string gameKey, bool force)
    {
        if (!BizMap.TryGetValue(gameKey, out var biz)) return GetCached(gameKey);
        if (!force && !IsStale(gameKey)) return GetCached(gameKey);

        try
        {
            using var doc = await GetJsonAsync($"{ApiBase}?launcher_id={LauncherId}&language=zh-cn").ConfigureAwait(false);
            if (doc == null) return GetCached(gameKey);
            var root = doc.RootElement;
            if (!root.TryGetProperty("data", out var data)) return GetCached(gameKey);
            if (!data.TryGetProperty("game_info_list", out var list)) return GetCached(gameKey);

            JsonElement? hit = null;
            foreach (var item in list.EnumerateArray())
            {
                if (item.TryGetProperty("game", out var game)
                    && game.TryGetProperty("biz", out var bizNode)
                    && bizNode.GetString() == biz)
                {
                    hit = item;
                    break;
                }
            }
            if (hit == null) return GetCached(gameKey);

            var info = hit.Value;
            if (!info.TryGetProperty("backgrounds", out var bgs) || bgs.GetArrayLength() == 0)
                return GetCached(gameKey);

            var old = GetCached(gameKey);
            var result = new GameBackgrounds { Key = gameKey, FetchedAtUtc = DateTime.UtcNow };

            foreach (var bg in bgs.EnumerateArray())
            {
                var id = bg.TryGetProperty("id", out var idNode) ? idNode.GetString() ?? "" : "";
                var imageUrl = NestedUrl(bg, "background");
                var videoUrl = NestedUrl(bg, "video");
                var type = bg.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";

                var item = new BackgroundItem { Id = id, Type = type, VideoUrl = videoUrl };
                if (imageUrl != null) item.ImageFile = await DownloadAsync(imageUrl).ConfigureAwait(false);

                // 视频：只有选中该背景时才真正下载，避免一次拉几十 MB
                var oldItem = old?.Items.FirstOrDefault(x => x.Id == id);
                if (oldItem?.VideoFile != null && File.Exists(Path.Combine(CacheDir, oldItem.VideoFile)))
                {
                    item.VideoFile = oldItem.VideoFile;
                }

                if (item.ImageFile != null || item.VideoFile != null) result.Items.Add(item);
            }

            if (result.Items.Count == 0) return GetCached(gameKey);

            await SaveMetaAsync(result).ConfigureAwait(false);
            CleanupUnused();
            return result;
        }
        catch
        {
            return GetCached(gameKey);
        }
    }

    /// <summary>选中某张背景时按需下载它的动态视频（.webm / .mp4）</summary>
    public static async Task<string?> EnsureVideoAsync(string gameKey, string backgroundId)
    {
        var jobKey = gameKey + "|" + backgroundId;
        Task<string?> job;
        lock (VideoJobs)
        {
            if (VideoJobs.TryGetValue(jobKey, out var running) && running != null)
            {
                job = running;
            }
            else
            {
                job = EnsureVideoCoreAsync(gameKey, backgroundId);
                VideoJobs[jobKey] = job;
            }
        }
        try
        {
            return await job.ConfigureAwait(false);
        }
        finally
        {
            lock (VideoJobs)
            {
                if (VideoJobs.TryGetValue(jobKey, out var current) && ReferenceEquals(current, job))
                    VideoJobs.Remove(jobKey);
            }
        }
    }

    private static async Task<string?> EnsureVideoCoreAsync(string gameKey, string backgroundId)
    {
        var meta = GetCached(gameKey);
        if (meta == null) return null;
        var item = meta.Items.FirstOrDefault(x => x.Id == backgroundId);
        if (item == null) return null;
        if (item.VideoPath != null) return item.VideoPath;
        if (string.IsNullOrWhiteSpace(item.VideoUrl)) return null;

        var name = await DownloadAsync(item.VideoUrl).ConfigureAwait(false);
        if (name == null) return null;
        item.VideoFile = name;
        await SaveMetaAsync(meta).ConfigureAwait(false);
        BackgroundReady?.Invoke(gameKey);
        return item.VideoPath;
    }

    /// <summary>官方「预下载 / 新版本」信息（版本号 / 所需空间）</summary>
    public sealed class PreDownloadInfo
    {
        public string Version = "";
        public string GameKey = "";
        public long SizeBytes;
        /// <summary>本地已安装版本；为空表示读不到 config.ini</summary>
        public string LocalVersion = "";
        /// <summary>true：官方预下载；false：正式版已有更新</summary>
        public bool IsPreDownload;
    }

    public static async Task<PreDownloadInfo?> GetPreDownloadAsync(string gameKey)
    {
        if (!BizMap.TryGetValue(gameKey, out var biz)) return null;
        try
        {
            using var doc = await GetJsonAsync(
                $"{PackageApi}?launcher_id={LauncherId}&language=zh-cn&game_ids[]={GameIdOf(biz)}")
                .ConfigureAwait(false);
            if (doc == null) return null;
            var root = doc.RootElement;
            if (!root.TryGetProperty("data", out var data)) return null;
            if (!data.TryGetProperty("game_packages", out var pkgs)) return null;

            foreach (var pkg in pkgs.EnumerateArray())
            {
                if (!pkg.TryGetProperty("game", out var g)) continue;
                if (!g.TryGetProperty("biz", out var b) || b.GetString() != biz) continue;
                if (!pkg.TryGetProperty("pre_download_game", out var pre) || pre.ValueKind == JsonValueKind.Null)
                    return null;
                var main = pre.TryGetProperty("latest", out var l) ? l
                    : pre.TryGetProperty("major", out var mj) ? mj : pre;
                var major = main.TryGetProperty("major", out var ma) ? ma : main;
                if (!major.TryGetProperty("version", out var v)) continue;
                return new PreDownloadInfo
                {
                    Version = v.GetString() ?? "",
                    GameKey = gameKey,
                    SizeBytes = ReadMajorSize(major, main),
                    IsPreDownload = true,
                };
            }
        }
        catch
        {
            // 忽略，不影响其它功能
        }
        return null;
    }

    /// <summary>
    /// 判断启动页是否显示「预下载 / 新版本」图标：
    /// 优先官方预下载（比正式版更新），否则比较本地 config.ini 的 game_version 与官方最新版本。
    /// 读不到本地版本时返回 null，避免误报。
    /// </summary>
    public static async Task<PreDownloadInfo?> GetUpdateStatusAsync(string gameKey, string? installDir)
    {
        if (!BizMap.ContainsKey(gameKey) || string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir))
            return null;

        var localVersion = ReadInstalledGameVersion(installDir);
        if (string.IsNullOrWhiteSpace(localVersion)) return null;
        var local = UpdateService.ParseTag(localVersion);
        if (local == null) return null;

        var build = await GetChunkBuildAsync(gameKey, "zh-cn").ConfigureAwait(false);
        var latest = build is not null
            ? (build.Version, build.TotalUncompressedBytes)
            : await GetLatestPackageAsync(gameKey).ConfigureAwait(false);
        var latestTag = UpdateService.ParseTag(latest.Item1);
        if (latestTag == null) return null;

        var pre = await GetPreDownloadAsync(gameKey).ConfigureAwait(false);
        var preTag = pre == null ? null : UpdateService.ParseTag(pre.Version);
        if (pre != null && preTag != null && preTag > latestTag && preTag > local)
        {
            pre.LocalVersion = localVersion;
            pre.GameKey = gameKey;
            return pre;
        }

        if (latestTag > local)
        {
            return new PreDownloadInfo
            {
                Version = latest.Item1,
                GameKey = gameKey,
                SizeBytes = latest.Item2,
                LocalVersion = localVersion,
                IsPreDownload = false,
            };
        }
        return null;
    }

    /// <summary>从游戏目录 config.ini 读取 game_version（官方启动器与游戏本体都会写这个字段）。</summary>
    private static string? ReadInstalledGameVersion(string dir)
    {
        try
        {
            var path = Path.Combine(dir, "config.ini");
            if (!File.Exists(path)) return null;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '[' || line[0] == ';' || line[0] == '#') continue;
                var i = line.IndexOf('=');
                if (i <= 0) continue;
                if (!line[..i].Trim().Equals("game_version", StringComparison.OrdinalIgnoreCase)) continue;
                return line[(i + 1)..].Trim().Trim('"', '\'');
            }
        }
        catch
        {
            // 权限 / 文件占用时不影响启动页
        }
        return null;
    }

    /// <summary>官方当前 build 里的一个文件清单（game / asb / 各语言语音）。</summary>
    public sealed class ChunkManifestInfo
    {
        public string MatchingField = "";
        public string ManifestId = "";
        public string ManifestChecksum = "";
        public bool ManifestCompressed = true;
        public string ManifestUrlPrefix = "";
        public string ManifestUrlSuffix = "";
        public bool ChunkCompressed = true;
        public string ChunkUrlPrefix = "";
        public string ChunkUrlSuffix = "";
        public long CompressedSize;
        public long UncompressedSize;
        public long FileCount;
    }

    /// <summary>官方 Sophon chunk 构建信息：当前版本 + 需要下载的清单。</summary>
    public sealed class ChunkBuildInfo
    {
        public string Version = "";
        public string GameBiz = "";
        public string BuildId = "";
        public string Tag = "";
        public List<ChunkManifestInfo> Manifests = new();
        public long TotalUncompressedBytes => Manifests.Sum(x => x.UncompressedSize);
    }

    /// <summary>
    /// 通过 getGameBranches + getBuild 拿到当前正式版本和 chunk 清单。
    /// getGamePackages 的原神数据可能停留在旧版本（5.5.0），安装和更新检查都必须优先走这里。
    /// </summary>
    public static async Task<ChunkBuildInfo?> GetChunkBuildAsync(string gameKey, string language)
    {
        var biz = BizMap.TryGetValue(gameKey, out var b) ? b : null;
        if (string.IsNullOrEmpty(biz)) return null;
        var gameId = GameIdOf(biz);
        if (string.IsNullOrEmpty(gameId)) return null;

        try
        {
            var branchUrl = $"https://hyp-api.mihoyo.com/hyp/hyp-connect/api/getGameBranches" +
                            $"?launcher_id={LauncherId}&language={language}&game_ids[]={gameId}";
            using var branchDoc = await GetJsonAsync(branchUrl).ConfigureAwait(false);
            if (branchDoc == null) return null;
            if (!branchDoc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("game_branches", out var branches) ||
                branches.ValueKind != JsonValueKind.Array || branches.GetArrayLength() == 0)
                return null;

            var package = branches[0].TryGetProperty("main", out var main) && main.ValueKind == JsonValueKind.Object
                ? main
                : default;
            if (package.ValueKind != JsonValueKind.Object) return null;
            var packageId = Str(package, "package_id");
            var password = Str(package, "password");
            var branch = Str(package, "branch");
            var tag = Str(package, "tag");
            if (string.IsNullOrEmpty(packageId) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(branch))
                return null;

            var buildUrl = $"https://downloader-api.mihoyo.com/downloader/sophon_chunk/api/getBuild" +
                           $"?branch={Uri.EscapeDataString(branch)}&package_id={Uri.EscapeDataString(packageId)}" +
                           $"&password={Uri.EscapeDataString(password)}";
            using var buildDoc = await GetJsonAsync(buildUrl).ConfigureAwait(false);
            if (buildDoc == null) return null;
            if (!buildDoc.RootElement.TryGetProperty("data", out var build) ||
                !build.TryGetProperty("manifests", out var manifests) ||
                manifests.ValueKind != JsonValueKind.Array)
                return null;

            var result = new ChunkBuildInfo
            {
                Version = Str(build, "tag") is { Length: > 0 } t ? t : tag,
                Tag = tag,
                GameBiz = biz,
                BuildId = Str(build, "build_id"),
            };

            foreach (var m in manifests.EnumerateArray())
            {
                var field = Str(m, "matching_field");
                if (string.IsNullOrEmpty(field)) continue;
                // 语音清单按所选语言取；其它带 '-' 的清单（mini-xx）跳过。
                if (field.Contains('-') && !field.Equals(language, StringComparison.OrdinalIgnoreCase))
                    continue;

                var manifest = m.TryGetProperty("manifest", out var mf) && mf.ValueKind == JsonValueKind.Object
                    ? mf : default;
                var manifestDownload = m.TryGetProperty("manifest_download", out var md) && md.ValueKind == JsonValueKind.Object
                    ? md : default;
                var chunkDownload = m.TryGetProperty("chunk_download", out var cd) && cd.ValueKind == JsonValueKind.Object
                    ? cd : default;
                var stats = m.TryGetProperty("stats", out var st) && st.ValueKind == JsonValueKind.Object
                    ? st : default;
                var id = Str(manifest, "id");
                if (string.IsNullOrEmpty(id)) continue;

                result.Manifests.Add(new ChunkManifestInfo
                {
                    MatchingField = field,
                    ManifestId = id,
                    ManifestChecksum = Str(manifest, "checksum"),
                    ManifestCompressed = ToLong(manifestDownload, "compression") != 0,
                    ManifestUrlPrefix = Str(manifestDownload, "url_prefix"),
                    ManifestUrlSuffix = Str(manifestDownload, "url_suffix"),
                    ChunkCompressed = ToLong(chunkDownload, "compression") != 0,
                    ChunkUrlPrefix = Str(chunkDownload, "url_prefix"),
                    ChunkUrlSuffix = Str(chunkDownload, "url_suffix"),
                    CompressedSize = ToLong(stats, "compressed_size"),
                    UncompressedSize = ToLong(stats, "uncompressed_size"),
                    FileCount = ToLong(stats, "file_count"),
                });
            }

            return result.Manifests.Count > 0 ? result : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>安装包信息（用于「获取游戏」弹窗显示所需空间）</summary>
    public static async Task<(string Version, long SizeBytes)> GetLatestPackageAsync(string gameKey)
    {
        if (!BizMap.TryGetValue(gameKey, out var biz)) return ("", 0);
        try
        {
            using var doc = await GetJsonAsync(
                $"{PackageApi}?launcher_id={LauncherId}&language=zh-cn&game_ids[]={GameIdOf(biz)}")
                .ConfigureAwait(false);
            if (doc == null) return ("", 0);
            var root = doc.RootElement;
            if (!root.TryGetProperty("data", out var data)) return ("", 0);
            if (!data.TryGetProperty("game_packages", out var pkgs)) return ("", 0);
            foreach (var pkg in pkgs.EnumerateArray())
            {
                if (!pkg.TryGetProperty("game", out var g)) continue;
                if (!g.TryGetProperty("biz", out var b) || b.GetString() != biz) continue;
                if (!pkg.TryGetProperty("main", out var main)) continue;
                if (!main.TryGetProperty("major", out var major)) continue;
                var ver = major.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
                var p = await GetInstallPackageAsync(gameKey, "zh-cn").ConfigureAwait(false);
                return (ver, p?.TotalDiskBytes ?? ReadMajorSize(major, main));
            }
        }
        catch
        {
            // 忽略
        }
        return ("", 0);
    }

    /// <summary>安装包总体积：优先累加 decompressed_size（解压后所需空间），没有时用分卷 size</summary>
    private static long ReadMajorSize(JsonElement major, JsonElement main)
    {
        if (major.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number)
            return sz.GetInt64();
        long total = 0;
        if (major.TryGetProperty("game_pkgs", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in arr.EnumerateArray())
            {
                foreach (var key in new[] { "decompressed_size", "size" })
                {
                    if (!part.TryGetProperty(key, out var s)) continue;
                    if (s.ValueKind == JsonValueKind.Number) { total += s.GetInt64(); break; }
                    if (s.ValueKind == JsonValueKind.String && long.TryParse(s.GetString(), out var n))
                    {
                        total += n;
                        break;
                    }
                }
            }
        }
        return total;
    }

    public sealed record InstallPart(string Url, long Size, long DecompressedSize);

    public sealed class InstallPackage
    {
        public string Version { get; init; } = "";
        public string GameBiz { get; init; } = "";
        /// <summary>官方散文件安装清单目录（存在时可逐文件直接写入，不需要解压分卷包）</summary>
        public string ResListUrl { get; init; } = "";
        public List<InstallPart> Parts { get; init; } = new();
        public List<InstallPart> AudioParts { get; init; } = new();
        public long TotalDownloadBytes => Parts.Sum(x => x.Size) + AudioParts.Sum(x => x.Size);
        public long TotalDiskBytes => Parts.Sum(x => x.DecompressedSize) + AudioParts.Sum(x => x.DecompressedSize);
    }

    /// <summary>完整安装包清单（game_pkgs 分卷 + 对应语音包），供软件内直接下载安装使用。</summary>
    public static async Task<InstallPackage?> GetInstallPackageAsync(string gameKey, string language)
    {
        if (!BizMap.TryGetValue(gameKey, out var biz)) return null;
        using var doc = await GetJsonAsync(
            $"{PackageApi}?launcher_id={LauncherId}&language=zh-cn&game_ids[]={GameIdOf(biz)}")
            .ConfigureAwait(false);
        if (doc == null) return null;
        var root = doc.RootElement;
        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("game_packages", out var pkgs))
            return null;
        foreach (var pkg in pkgs.EnumerateArray())
        {
            if (!pkg.TryGetProperty("game", out var g) || !g.TryGetProperty("biz", out var b) || b.GetString() != biz)
                continue;
            if (!pkg.TryGetProperty("main", out var main) || !main.TryGetProperty("major", out var major))
                continue;

            var result = new InstallPackage
            {
                Version = major.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "",
                GameBiz = biz,
                ResListUrl = Str(major, "res_list_url").TrimEnd('/'),
            };

            if (major.TryGetProperty("game_pkgs", out var parts))
            {
                foreach (var p in parts.EnumerateArray())
                {
                    var url = Str(p, "url");
                    if (string.IsNullOrEmpty(url)) continue;
                    result.Parts.Add(new InstallPart(url, ToLong(p, "size"), ToLong(p, "decompressed_size")));
                }
            }

            if (major.TryGetProperty("audio_pkgs", out var audios))
            {
                foreach (var a in audios.EnumerateArray())
                {
                    var lang = Str(a, "language");
                    if (!string.IsNullOrEmpty(lang) && lang != language) continue;
                    var url = Str(a, "url");
                    if (string.IsNullOrEmpty(url)) continue;
                    result.AudioParts.Add(new InstallPart(url, ToLong(a, "size"), ToLong(a, "decompressed_size")));
                    break;
                }
            }

            return result.Parts.Count > 0 ? result : null;
        }
        return null;
    }

    private static long ToLong(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.GetInt64();
        return v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var n) ? n : 0;
    }

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>官方 launcher 接口里的 game_id（不是 biz）</summary>
    private static string GameIdOf(string biz) => biz switch
    {
        "hk4e_cn" => "1Z8W5NHUQb",
        "hkrpg_cn" => "64kMb5iAWu",
        "nap_cn" => "x6znKlJ0xK",
        "bh3_cn" => "osvnlOc0S8",
        _ => "",
    };

    private static async Task<JsonDocument?> GetJsonAsync(string url)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var raw = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonDocument.Parse(raw);
        }
        catch
        {
            return null;
        }
    }

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36";

    private static string? NestedUrl(JsonElement bg, string node)
    {
        if (!bg.TryGetProperty(node, out var n)) return null;
        if (n.ValueKind == JsonValueKind.Null) return null;
        if (!n.TryGetProperty("url", out var u)) return null;
        var s = u.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static async Task<string?> DownloadAsync(string url)
    {
        try
        {
            var name = Path.GetFileName(new Uri(url).AbsolutePath);
            if (string.IsNullOrWhiteSpace(name)) return null;
            Directory.CreateDirectory(CacheDir);
            var path = Path.Combine(CacheDir, name);
            if (File.Exists(path) && new FileInfo(path).Length > 0) return name;

            if (!_headerAdded)
            {
                Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
                _headerAdded = true;
            }

            var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
            if (bytes.Length == 0) return null;
            if (File.Exists(path) && new FileInfo(path).Length > 0) return name;

            // 先写临时文件再原子替换，避免后台预加载与页面加载同时下载同一张图时写坏文件
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".part";
            await File.WriteAllBytesAsync(temp, bytes).ConfigureAwait(false);
            try
            {
                File.Move(temp, path, true);
            }
            catch
            {
                try { File.Delete(temp); } catch { }
                if (!File.Exists(path)) return null;
            }
            return name;
        }
        catch
        {
            return null;
        }
    }

    private static async Task SaveMetaAsync(GameBackgrounds meta)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(MetaFile(meta.Key), json).ConfigureAwait(false);
        }
        catch
        {
            // 写缓存失败不影响本次显示
        }
    }

    /// <summary>清理不再被引用的旧壁纸/视频（版本更新后留下的文件）</summary>
    private static void CleanupUnused()
    {
        try
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in BizMap.Keys)
            {
                var meta = GetCached(key);
                if (meta == null) continue;
                foreach (var item in meta.Items)
                {
                    if (item.ImageFile != null) keep.Add(item.ImageFile);
                    if (item.VideoFile != null) keep.Add(item.VideoFile);
                }
            }
            foreach (var file in Directory.GetFiles(CacheDir))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith("official_", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json"))
                    continue;
                if (keep.Contains(name)) continue;
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < TimeSpan.FromMinutes(10)) continue;
                File.Delete(file);
            }
        }
        catch
        {
            // 忽略
        }
    }
}
