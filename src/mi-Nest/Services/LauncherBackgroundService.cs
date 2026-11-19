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
/// 官方启动器（HoYoPlay）版本背景：仿 Starward 的 BackgroundService。
/// 每个版本官方会下发多张背景（部分带 .webm 动态视频），这里全部缓存到
/// %AppData%\mi-Nest\bg，启动页用左侧圆点按官方顺序逐张切换。
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
        return item.VideoPath;
    }

    /// <summary>官方「预下载」信息（版本号 / 所需空间），没有预下载时返回 null</summary>
    public sealed class PreDownloadInfo
    {
        public string Version = "";
        public string GameKey = "";
        public long SizeBytes;
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
                };
            }
        }
        catch
        {
            // 忽略，不影响其它功能
        }
        return null;
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
                return (ver, ReadMajorSize(major, main));
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
            await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
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
