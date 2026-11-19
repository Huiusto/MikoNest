using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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
    }

    /// <summary>从 GitHub Release 检查 / 下载新版本（只依赖公开 API，无需登录）</summary>
    public static class UpdateService
    {
        private const string RepoApi = "https://api.github.com/repos/Huiusto/mi-Nest";
        public const string RepoReleases = "https://github.com/Huiusto/mi-Nest/releases";

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "mi-Nest-Updater");
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
            try
            {
                using var client = CreateClient();
                var url = RepoApi + "/releases?per_page=10";
                using var resp = await client.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                    return (null, $"GitHub 返回 {resp.StatusCode}");
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
                    bestVer = ver;
                    best = new UpdateInfo
                    {
                        Version = tag?.TrimStart('v', 'V') ?? ver.ToString(3),
                        ReleasePage = page ?? RepoReleases,
                        AssetUrl = assetUrl,
                        AssetName = assetName,
                        AssetSize = assetSize,
                    };
                }

                return best == null
                    ? (null, "仓库里还没有已发布的版本记录")
                    : (best, null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        /// <summary>下载安装包到临时目录，progress 回调 (已下载字节, 总字节)</summary>
        public static async Task<string> DownloadAsync(UpdateInfo info, Action<long, long>? progress = null)
        {
            if (string.IsNullOrEmpty(info.AssetUrl)) throw new InvalidOperationException("该版本没有可下载的安装包");

            var dir = Path.Combine(Path.GetTempPath(), "mi-Nest-update");
            Directory.CreateDirectory(dir);
            var fileName = string.IsNullOrWhiteSpace(info.AssetName) ? "mi-Nest-Setup.exe" : info.AssetName!;
            var target = Path.Combine(dir, fileName);

            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "mi-Nest-Updater");
            using var resp = await client.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? info.AssetSize;

            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(target);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await src.ReadAsync(buffer)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read));
                done += read;
                progress?.Invoke(done, total);
            }
            dst.Flush();
            if (total > 0 && done != total)
                throw new IOException($"下载不完整：{done}/{total} 字节");

            return target;
        }
    }
}
