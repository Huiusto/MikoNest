using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MiNest.Services
{
    /// <summary>GitHub Release 里查到的一个版本信息</summary>
    public sealed class UpdateInfo
    {
        public string Version { get; init; } = "";
        public string ReleasePage { get; init; } = "";
        public string? AssetUrl { get; init; }
        public long AssetSize { get; init; }
        public string? AssetName { get; init; }
        /// <summary>Release 正文（更新日志），用于更新弹窗展示本次更新内容。</summary>
        public string ReleaseNotes { get; init; } = "";
    }

    /// <summary>
    /// 从 GitHub Release 检查 / 下载新版本（公开 API，无需登录）。
    /// 直连 github.com 在部分网络下不可用，因此 API 与安装包都准备了镜像回退。
    /// </summary>
    public static class UpdateService
    {
        private const string RepoApi = "https://api.github.com/repos/Huiusto/mi-Nest";
        public const string RepoReleases = "https://github.com/Huiusto/mi-Nest/releases";

        /// <summary>GitHub API 镜像（国内网络可用），按顺序尝试</summary>
        private static readonly string[] ApiEndpoints =
        {
            RepoApi + "/releases?per_page=10",
            "https://gh-proxy.com/" + RepoApi + "/releases?per_page=10",
        };

        /// <summary>Release 安装包下载镜像前缀，按顺序尝试</summary>
        private static readonly string[] AssetProxies =
        {
            "",
            "https://ghfast.top/",
            "https://gh-proxy.com/",
        };

        private static HttpClient CreateClient(TimeSpan timeout)
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = true };
            var client = new HttpClient(handler) { Timeout = timeout };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "MikoNest-Updater");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            return client;
        }

        /// <summary>把 "v0.1.8" / "0.1.8" / "0.1.8-beta" 解析成可比较的版本号</summary>
        public static Version? ParseTag(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            var s = tag.Trim().TrimStart('v', 'V');
            var cut = s.IndexOfAny(new[] { '-', '+', '_' });
            if (cut > 0) s = s.Substring(0, cut);
            var parts = s.Split('.');
            var nums = new List<int>();
            foreach (var p in parts)
            {
                if (int.TryParse(p, out var n)) nums.Add(n);
                else break;
            }
            while (nums.Count < 3) nums.Add(0);
            if (nums.Count > 4) nums = nums.Take(4).ToList();
            try { return new Version(nums[0], nums[1], nums[2], nums.Count > 3 ? nums[3] : 0); }
            catch { return null; }
        }

        /// <summary>取仓库里最新的（非草稿、非预发布）Release，包含安装包直链</summary>
        public static async Task<(UpdateInfo? info, string? error)> GetLatestAsync()
        {
            var errors = new List<string>();
            foreach (var url in ApiEndpoints)
            {
                try
                {
                    var info = await GetLatestFromAsync(url);
                    if (info != null) return (info, null);
                    errors.Add("接口没有已发布版本");
                }
                catch (Exception ex)
                {
                    errors.Add(Short(ex));
                }
            }
            return (null, errors.Count > 0 ? string.Join("；", errors.Distinct()) : "未知错误");
        }

        private static async Task<UpdateInfo?> GetLatestFromAsync(string url)
        {
            using var client = CreateClient(TimeSpan.FromSeconds(15));
            using var resp = await client.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"GitHub 返回 {(int)resp.StatusCode}");
            var text = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(text);

            UpdateInfo? best = null;
            Version? bestVer = null;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("draft", out var d) && d.GetBoolean()) continue;
                if (item.TryGetProperty("prerelease", out var p) && p.GetBoolean()) continue;
                var tag = item.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                var ver = ParseTag(tag);
                if (ver == null) continue;
                if (bestVer != null && ver <= bestVer) continue;

                string? assetUrl = null, assetName = null;
                long assetSize = 0;
                if (item.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in assets.EnumerateArray())
                    {
                        var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                        if (string.IsNullOrEmpty(name)) continue;
                        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                        assetUrl = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                        assetName = name;
                        if (a.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number)
                            assetSize = sz.GetInt64();
                        break;
                    }
                }

                var page = item.TryGetProperty("html_url", out var h) ? h.GetString() : RepoReleases;
                var notes = item.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String
                    ? b.GetString() ?? ""
                    : "";
                bestVer = ver;
                best = new UpdateInfo
                {
                    Version = tag?.TrimStart('v', 'V') ?? ver.ToString(3),
                    ReleasePage = page ?? RepoReleases,
                    AssetUrl = assetUrl,
                    AssetName = assetName,
                    AssetSize = assetSize,
                    ReleaseNotes = notes,
                };
            }
            return best;
        }

        /// <summary>下载安装包到临时目录，progress 回调 (已下载字节, 总字节)</summary>
        public static async Task<string> DownloadAsync(UpdateInfo info, Action<long, long>? progress = null,
            CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(info.AssetUrl)) throw new InvalidOperationException("该版本没有可下载的安装包");

            var dir = Path.Combine(Path.GetTempPath(), "MikoNest-update");
            Directory.CreateDirectory(dir);

            // 注意：绝不能再用 MikoNest.exe 这个名字——安装脚本会 taskkill 同名进程，
            // 会把正在运行的安装程序自己杀掉，导致「点更新没有任何反应」。
            var target = Path.Combine(dir, $"MikoNest-Setup-{info.Version}.exe");
            if (File.Exists(target)) File.Delete(target);

            var errors = new List<string>();
            foreach (var prefix in AssetProxies)
            {
                var url = prefix + info.AssetUrl;
                try
                {
                    await DownloadOneAsync(url, target, info, progress, token);
                    return target;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    errors.Add(Short(ex));
                    try { if (File.Exists(target)) File.Delete(target); } catch { }
                }
            }
            throw new IOException("下载失败：" + string.Join("；", errors.Distinct()));
        }

        private static async Task DownloadOneAsync(string url, string target, UpdateInfo info,
            Action<long, long>? progress, CancellationToken token)
        {
            using var client = CreateClient(TimeSpan.FromMinutes(10));
            using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? info.AssetSize;

            await using var src = await resp.Content.ReadAsStreamAsync(token);
            await using var dst = File.Create(target);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, token)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), token);
                done += read;
                progress?.Invoke(done, total);
            }
            await dst.FlushAsync(token);
            if (total > 0 && done != total)
                throw new IOException($"下载不完整：{done}/{total} 字节");
        }

        /// <summary>
        /// 静默安装参数：便携版就地覆盖当前目录，已安装版沿用原来的安装目录。
        /// /D 必须是最后一个参数，且不能带引号。
        /// </summary>
        public static string BuildInstallerArgs()
        {
            try
            {
                var dir = AppContext.BaseDirectory.TrimEnd('\\', '/');
                var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                var inProgramFiles = dir.StartsWith(pf, StringComparison.OrdinalIgnoreCase)
                                     || dir.StartsWith(pf86, StringComparison.OrdinalIgnoreCase);
                return inProgramFiles ? "/S" : "/S /D=" + dir;
            }
            catch
            {
                return "/S";
            }
        }

        private static string Short(Exception ex)
        {
            var msg = ex.Message;
            return msg.Length > 60 ? msg.Substring(0, 60) + "…" : msg;
        }
    }
}
