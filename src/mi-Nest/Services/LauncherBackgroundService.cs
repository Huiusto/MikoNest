using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MiNest.Services;

/// <summary>
/// 官方启动器（HoYoPlay）版本背景：仿 Starward 的 BackgroundService，
/// 调用 launcher 的 getAllGameBasicInfo 接口拿到当前版本的背景图 / 动态视频，
/// 下载并缓存到 %AppData%\mi-Nest\bg，启动页据此显示（背景随版本自动更新，视频背景可播放）。
/// </summary>
public static class LauncherBackgroundService
{
    /// <summary>国服官方启动器 launcher_id（Starward 使用同一值）</summary>
    public const string LauncherId = "jGHBHlcOq1";

    private const string ApiBase = "https://hyp-api.mihoyo.com/hyp/hyp-connect/api/getAllGameBasicInfo";

    /// <summary>缓存有效期：超过后重新向官方接口拉取（版本更新后会自动换成新背景）</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    /// <summary>gameKey -> 官方 biz（hk4e_cn / hkrpg_cn / nap_cn / bh3_cn）</summary>
    private static readonly Dictionary<string, string> BizMap = new()
    {
        ["genshin"] = "hk4e_cn",
        ["starrail"] = "hkrpg_cn",
        ["zzz"] = "nap_cn",
        ["bh3"] = "bh3_cn",
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(40) };

    private static bool _headerAdded;

    public static string CacheDir => Path.Combine(AppPaths.DataDir, "bg");

    /// <summary>缓存的一版官方背景：图片（版本壁纸）+ 可选动态视频</summary>
    public sealed class CachedBackground
    {
        [JsonPropertyName("key")] public string Key { get; set; } = "";
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("image")] public string? ImageFile { get; set; }
        [JsonPropertyName("video")] public string? VideoFile { get; set; }
        [JsonPropertyName("fetched")] public DateTime FetchedAtUtc { get; set; }

        [JsonIgnore] public string? ImagePath => Existing(ImageFile);
        [JsonIgnore] public string? VideoPath => Existing(VideoFile);

        private static string? Existing(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var path = Path.Combine(CacheDir, name);
            return File.Exists(path) ? path : null;
        }
    }

    private static string MetaFile(string key) => Path.Combine(CacheDir, $"official_{key}.json");

    /// <summary>读取本地缓存的官方背景（不联网，供启动页立即显示）</summary>
    public static CachedBackground? GetCached(string gameKey)
    {
        try
        {
            var file = MetaFile(gameKey);
            if (!File.Exists(file)) return null;
            var meta = JsonSerializer.Deserialize<CachedBackground>(File.ReadAllText(file));
            if (meta == null) return null;
            if (meta.ImagePath == null && meta.VideoPath == null) return null;
            return meta;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>缓存是否过期（用于决定是否向官方接口刷新）</summary>
    public static bool IsStale(string gameKey)
    {
        var meta = GetCached(gameKey);
        return meta == null || DateTime.UtcNow - meta.FetchedAtUtc > CacheTtl;
    }

    /// <summary>
    /// 向官方接口拉取该游戏当前版本的背景并下载到本地缓存。
    /// 返回最新背景；网络失败时返回已有缓存（没有缓存则返回 null）。
    /// </summary>
    public static async Task<CachedBackground?> RefreshAsync(string gameKey, bool force = false)
    {
        var biz = BizMap.TryGetValue(gameKey, out var b) ? b : null;
        if (biz == null) return GetCached(gameKey);

        if (!force && !IsStale(gameKey)) return GetCached(gameKey);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{ApiBase}?launcher_id={LauncherId}&language=zh-cn");
            req.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return GetCached(gameKey);
            var raw = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("retcode", out var rc) && rc.GetInt32() != 0) return GetCached(gameKey);
            if (!root.TryGetProperty("data", out var data)) return GetCached(gameKey);
            if (!data.TryGetProperty("game_info_list", out var list)) return GetCached(gameKey);

            JsonElement? hit = null;
            foreach (var item in list.EnumerateArray())
            {
                if (!item.TryGetProperty("game", out var game)) continue;
                if (game.TryGetProperty("biz", out var bizNode) && bizNode.GetString() == biz)
                {
                    hit = item;
                    break;
                }
            }
            if (hit == null) return GetCached(gameKey);

            var info = hit.Value;
            if (!info.TryGetProperty("backgrounds", out var bgs) || bgs.GetArrayLength() == 0)
                return GetCached(gameKey);

            // 官方接口按上线时间倒序，第一项 = 当前版本最新背景
            var bg = bgs[0];
            var id = bg.TryGetProperty("id", out var idNode) ? idNode.GetString() ?? "" : "";

            var imageUrl = NestedUrl(bg, "background");
            var videoUrl = NestedUrl(bg, "video");

            var meta = new CachedBackground { Key = gameKey, Id = id, FetchedAtUtc = DateTime.UtcNow };
            if (imageUrl != null) meta.ImageFile = await DownloadAsync(imageUrl).ConfigureAwait(false);
            if (videoUrl != null && IsVideo(videoUrl))
                meta.VideoFile = await DownloadAsync(videoUrl).ConfigureAwait(false);

            // 下载失败时保留旧缓存，避免把可用背景弄丢
            if (meta.ImagePath == null) return GetCached(gameKey);

            Directory.CreateDirectory(CacheDir);
            await File.WriteAllTextAsync(MetaFile(gameKey),
                JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
            CleanupUnused();
            return meta;
        }
        catch
        {
            return GetCached(gameKey);
        }
    }

    private static string? NestedUrl(JsonElement bg, string node)
    {
        if (!bg.TryGetProperty(node, out var n)) return null;
        if (!n.TryGetProperty("url", out var u)) return null;
        var s = u.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static bool IsVideo(string url)
    {
        var ext = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
        return ext is ".mp4" or ".webm" or ".mkv";
    }

    private static async Task<string?> DownloadAsync(string url)
    {
        try
        {
            var name = Path.GetFileName(new Uri(url).AbsolutePath);
            if (string.IsNullOrWhiteSpace(name)) return null;
            var dir = CacheDir;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, name);
            if (File.Exists(path) && new FileInfo(path).Length > 0) return name;

            if (!_headerAdded)
            {
                Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
                _headerAdded = true;
            }

            var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
            if (bytes.Length == 0) return null;
            await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
            return name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>清理不再被任何游戏引用的旧背景文件（版本更新后留下的旧壁纸/视频）</summary>
    private static void CleanupUnused()
    {
        try
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in BizMap.Keys)
            {
                var meta = GetCached(key);
                if (meta?.ImageFile != null) keep.Add(meta.ImageFile);
                if (meta?.VideoFile != null) keep.Add(meta.VideoFile);
            }
            foreach (var file in Directory.GetFiles(CacheDir))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith("official_", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json"))
                    continue;
                if (keep.Contains(name)) continue;
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < TimeSpan.FromMinutes(10)) continue; // 可能正被下载
                File.Delete(file);
            }
        }
        catch
        {
            // 清理失败不影响使用
        }
    }
}
