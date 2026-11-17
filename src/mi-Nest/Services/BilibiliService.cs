using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MiNest.Services;

/// <summary>哔哩哔哩视频条目</summary>
public sealed record BiliVideo(
    string Bvid, long Aid, long Cid, string Title, string Cover,
    long Duration, long View, long Like, long Danmaku, string Author, long AuthorUid, string Desc);

/// <summary>哔哩哔哩评论条目</summary>
public sealed record BiliComment(long Rpid, long Uid, string User, string Message, long Like, string Time);

/// <summary>
/// 哔哩哔哩接口（精选视频流 / 播放地址 / 评论 / 点赞 / 收藏 / 关注）。
/// 写操作需要登录 Cookie 与 WBI 签名；播放 CDN 需要 Referer=bilibili.com（由 WebView2 拦截注入）。
/// </summary>
public static class BilibiliService
{
    public const string UA =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    private static readonly CookieContainer Cookies = new();
    private static readonly HttpClient Client = CreateClient();

    // WBI 签名（公开算法）：mixin key 重排表
    private static readonly int[] MixinTab =
    {
        46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35, 27, 43, 25, 56, 51, 24, 36, 19, 29, 9, 12, 54,
        7, 28, 17, 26, 0, 1, 60, 37, 6, 24, 43, 55, 13, 44, 48, 16, 59, 52, 42, 22, 41, 30, 11, 33, 21, 40, 57, 14,
        5, 4, 20, 49, 34, 38, 39, 64
    };
    private const string WbiSalt = "ea1db124af3c70624746945fa59731a9";

    private static string? _mixinKey;
    private static long _wbiFetchedAt;

    static BilibiliService()
    {
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { CookieContainer = Cookies, UseCookies = true };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(UA);
        c.DefaultRequestHeaders.Referrer = new Uri("https://www.bilibili.com/");
        return c;
    }

    /// <summary>把内置浏览器（WebView2）中 bilibili.com 的 Cookie 同步进本服务</summary>
    public static void SyncCookies(IEnumerable<CoreWebView2CookieLike> cookies)
    {
        foreach (var c in cookies)
        {
            try
            {
                Cookies.Add(new Cookie(c.Name, c.Value, "/", c.Domain.StartsWith('.') ? c.Domain.TrimStart('.') : c.Domain));
            }
            catch
            {
                // 忽略无效 Cookie
            }
        }
    }

    public sealed record CoreWebView2CookieLike(string Name, string Value, string Domain);

    // ---------- 登录态 ----------

    /// <summary>判断是否已登录 B 站（nav 接口）</summary>
    public static async Task<bool> IsLoggedInAsync()
    {
        try
        {
            var json = await Client.GetStringAsync("https://api.bilibili.com/x/web-interface/nav");
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("data", out var data) &&
                   data.TryGetProperty("isLogin", out var isLogin) && isLogin.GetBoolean();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>获取当前登录用户名（未登录返回空）</summary>
    public static async Task<(bool ok, string uname, long uid)> GetLoginUserAsync()
    {
        try
        {
            var json = await Client.GetStringAsync("https://api.bilibili.com/x/web-interface/nav");
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("isLogin", out var isLogin) || !isLogin.GetBoolean())
                return (false, "", 0);
            var uname = data.TryGetProperty("uname", out var u) ? u.GetString() ?? "" : "";
            var uid = data.TryGetProperty("mid", out var m) ? m.GetInt64() : 0;
            return (true, uname, uid);
        }
        catch
        {
            return (false, "", 0);
        }
    }

    // ---------- WBI 签名 ----------

    private static async Task<string> GetMixinKeyAsync()
    {
        if (_mixinKey != null && Environment.TickCount64 - _wbiFetchedAt < 60_000)
            return _mixinKey;
        try
        {
            var json = await Client.GetStringAsync("https://api.bilibili.com/x/web-interface/nav");
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("wbi_img", out var wbi))
                return "";
            var img = wbi.TryGetProperty("img_url", out var i) ? FileName(i.GetString() ?? "") : "";
            var sub = wbi.TryGetProperty("sub_url", out var s) ? FileName(s.GetString() ?? "") : "";
            if (img.Length == 0 || sub.Length == 0) return "";

            var raw = img + sub;
            var sb = new StringBuilder(32);
            for (var j = 0; j < 32; j++)
                sb.Append(raw[MixinTab[j] % raw.Length]);
            _mixinKey = sb.ToString();
            _wbiFetchedAt = Environment.TickCount64;
            return _mixinKey;
        }
        catch
        {
            return _mixinKey ?? "";
        }
    }

    private static string FileName(string url)
    {
        var name = url.Substring(url.LastIndexOf('/') + 1);
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    /// <summary>对参数字典做 WBI 签名，返回 (queryString 追加参数, wts)</summary>
    private static async Task<string> SignParamsAsync(Dictionary<string, object> p)
    {
        var key = await GetMixinKeyAsync();
        var wts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        p["wts"] = wts;

        var sb = new StringBuilder();
        foreach (var kv in p.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (sb.Length > 0) sb.Append('&');
            sb.Append(Uri.EscapeDataString(kv.Key)).Append('=')
              .Append(Uri.EscapeDataString(Convert.ToString(kv.Value) ?? ""));
        }

        var md5 = MD5.HashData(Encoding.UTF8.GetBytes(sb.ToString() + WbiSalt));
        var wrid = Convert.ToHexString(md5).ToLowerInvariant();
        return $"{sb}&w_rid={wrid}";
    }

    // ---------- 视频流 ----------

    /// <summary>热门/精选视频列表（每页 20 条）</summary>
    public static async Task<List<BiliVideo>> GetPopularAsync(int page = 1)
    {
        var result = new List<BiliVideo>();
        try
        {
            var json = await Client.GetStringAsync($"https://api.bilibili.com/x/web-interface/popular?ps=20&pn={page}");
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("code", out var code) || code.GetInt32() != 0 ||
                !doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var el in list.EnumerateArray())
            {
                var bvid = GetStr(el, "bvid");
                if (string.IsNullOrEmpty(bvid)) continue;
                long cid = el.TryGetProperty("cid", out var c) ? c.GetInt64() : 0;
                long aid = el.TryGetProperty("aid", out var a) ? a.GetInt64() : 0;
                var stat = el.TryGetProperty("stat", out var s) ? s : default;
                long authorUid = 0;
                var author = "";
                if (el.TryGetProperty("owner", out var owner))
                {
                    author = GetStr(owner, "name");
                    authorUid = owner.TryGetProperty("mid", out var mid) ? mid.GetInt64() : 0;
                }
                result.Add(new BiliVideo(
                    bvid, aid, cid,
                    GetStr(el, "title"), GetStr(el, "pic"), GetInt(el, "duration"),
                    stat.ValueKind == JsonValueKind.Object ? GetInt(stat, "view") : 0,
                    stat.ValueKind == JsonValueKind.Object ? GetInt(stat, "like") : 0,
                    stat.ValueKind == JsonValueKind.Object ? GetInt(stat, "danmaku") : 0,
                    author, authorUid, GetStr(el, "desc")));
            }
        }
        catch
        {
            // 网络异常时返回空列表
        }
        return result;
    }

    /// <summary>获取视频可直接播放的 mp4 地址（HTML5 接口；多档清晰度/参数自动回退）</summary>
    public static async Task<string?> GetPlayUrlAsync(string bvid, long cid, int qn = 64)
    {
        foreach (var (q, f) in new[] { (qn: qn, fnval: 1), (qn: 48, fnval: 1), (qn: 32, fnval: 1), (qn: 64, fnval: 0) })
        {
            try
            {
                var json = await Client.GetStringAsync(
                    $"https://api.bilibili.com/x/player/playurl?bvid={bvid}&cid={cid}&qn={q}&fnval={f}&fnver=0&otype=json");
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("code", out var code) || code.GetInt32() != 0 ||
                    !doc.RootElement.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("durl", out var durl) || durl.ValueKind != JsonValueKind.Array ||
                    durl.GetArrayLength() == 0)
                    continue;
                var url = durl[0].GetProperty("url").GetString();
                if (!string.IsNullOrEmpty(url)) return url;
            }
            catch
            {
                // 尝试下一档
            }
        }
        return null;
    }

    /// <summary>获取全部可用清晰度的直链（客户端式画质切换用）。
    /// 返回 qn(64=1080P/48=720P/32=480P/16=360P) -> mp4 url；某档获取失败则不含该项。</summary>
    public static async Task<Dictionary<int, string>> GetPlayUrlsAsync(string bvid, long cid)
    {
        var result = new Dictionary<int, string>();
        foreach (var q in new[] { 64, 48, 32, 16 })
        {
            try
            {
                var json = await Client.GetStringAsync(
                    $"https://api.bilibili.com/x/player/playurl?bvid={bvid}&cid={cid}&qn={q}&fnval=1&fnver=0&otype=json");
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("code", out var code) || code.GetInt32() != 0 ||
                    !doc.RootElement.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("durl", out var durl) || durl.ValueKind != JsonValueKind.Array ||
                    durl.GetArrayLength() == 0)
                    continue;
                var url = durl[0].GetProperty("url").GetString();
                if (!string.IsNullOrEmpty(url)) result[q] = url;
            }
            catch
            {
                // 跳过该档
            }
        }
        return result;
    }

    // ---------- 评论 ----------

    /// <summary>获取评论列表（优先现行 v2 接口，无需 WBI，稳定返回 data.replies）</summary>
    public static async Task<List<BiliComment>> GetCommentsAsync(long aid, int page = 1)
    {
        var result = new List<BiliComment>();
        try
        {
            var json = await Client.GetStringAsync($"https://api.bilibili.com/x/v2/reply?type=1&oid={aid}&sort=2&pn={page}&ps=20");
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("code", out var code) || code.GetInt32() != 0 ||
                !doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("replies", out var replies) || replies.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var el in replies.EnumerateArray())
            {
                long uid = 0;
                var user = "匿名";
                if (el.TryGetProperty("member", out var member))
                {
                    user = GetStr(member, "uname");
                    uid = member.TryGetProperty("mid", out var mid) ? mid.GetInt64() : 0;
                }
                var content = el.TryGetProperty("content", out var ct) ? ct : default;
                var time = el.TryGetProperty("ctime", out var ctime)
                    ? DateTimeOffset.FromUnixTimeSeconds(ctime.GetInt64()).LocalDateTime.ToString("MM-dd HH:mm")
                    : "";
                result.Add(new BiliComment(
                    GetInt(el, "rpid"), uid, user,
                    content.ValueKind == JsonValueKind.Object ? GetStr(content, "message") : "",
                    GetInt(el, "like"), time));
            }
        }
        catch
        {
            // 网络异常时返回空列表
        }
        return result;
    }

    // ---------- 写操作基础 ----------

    private static string? GetCsrf()
    {
        try
        {
            return Cookies.GetCookies(new Uri("https://www.bilibili.com"))["bili_jct"]?.Value ?? null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>安全 POST：带 Referer/Origin 头，非 JSON（WAF/HTML）时返回可读信息而非“网络异常”</summary>
    private static async Task<(int code, string message, bool ok)> PostJsonAsync(
        string url, Dictionary<string, string> form, string referer = "https://www.bilibili.com/")
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Referrer = new Uri(referer);
            req.Headers.TryAddWithoutValidation("Origin", "https://www.bilibili.com");
            req.Content = new FormUrlEncodedContent(form);
            var r = await Client.SendAsync(req);
            var body = await r.Content.ReadAsStringAsync();
            if (body.Length > 500) body = body[..500];
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("code", out var codeEl)) return (-1, $"响应异常：{(int)r.StatusCode}", false);
            var code = codeEl.GetInt32();
            var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            return (code, string.IsNullOrEmpty(msg) ? $"HTTP {(int)r.StatusCode}" : msg, true);
        }
        catch (Exception ex)
        {
            return (-1, "网络异常：" + ex.Message, false);
        }
    }

    /// <summary>查询登录用户默认收藏夹 id（收藏视频需要 add_media_ids / 取消需要 del_media_ids）</summary>
    public static async Task<long> GetDefaultFavFolderAsync(long mid)
    {
        try
        {
            var p = new Dictionary<string, object> { ["up_mid"] = mid };
            var q = await SignParamsAsync(p);
            var json = await Client.GetStringAsync($"https://api.bilibili.com/x/v3/fav/folder/created/list-all?{q}");
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("code", out var code) || code.GetInt32() != 0 ||
                !doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array ||
                list.GetArrayLength() == 0)
                return 0;
            // 优先“默认收藏夹”，否则取第一个收藏夹
            foreach (var el in list.EnumerateArray())
            {
                if (GetStr(el, "title").Contains("默认"))
                    return el.TryGetProperty("id", out var id) ? id.GetInt64() : 0;
            }
            return list[0].TryGetProperty("id", out var first) ? first.GetInt64() : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>把 B 站接口 code 转成用户可读文案</summary>
    private static string CodeMessage(int code, string fallback)
    {
        return code switch
        {
            0 => "成功",
            -101 => "未登录，请先登录 B 站账号",
            -111 => "csrf 校验失败，请重新登录 B 站账号",
            -352 => "风控校验失败，请稍后重试",
            -404 => "目标不存在或已删除",
            -412 => "请求被风控拦截，请稍后重试",
            34005 => "今天对该视频的投币已达上限",
            -350 => "评论内容包含敏感词或重复",
            _ => string.IsNullOrEmpty(fallback) ? $"操作失败（{code}）" : $"{fallback}（{code}）",
        };
    }

    // ---------- 点赞 / 收藏 / 关注 / 发评论（需要登录 + WBI + csrf） ----------

    /// <summary>点赞/取消点赞视频，返回 (ok, 提示)</summary>
    public static async Task<(bool ok, string msg)> LikeAsync(long aid, bool like = true)
    {
        try
        {
            var csrf = GetCsrf();
            var p = new Dictionary<string, object> { ["type"] = 1, ["rid"] = aid, ["like"] = like ? 1 : 0 };
            var q = await SignParamsAsync(p);
            var form = new Dictionary<string, string>
            {
                ["type"] = "1", ["rid"] = aid.ToString(), ["like"] = like ? "1" : "0",
                ["csrf"] = csrf ?? "",
            };
            var (code, msg, ok) = await PostJsonAsync($"https://api.bilibili.com/x/web-interface/dynamic/like?{q}",
                form, $"https://www.bilibili.com/video/av{aid}");
            if (!ok) return (false, msg);
            if (code == 0) return (true, like ? "已点赞" : "已取消点赞");
            return (false, CodeMessage(code, msg));
        }
        catch
        {
            return (false, "网络异常，操作失败");
        }
    }

    /// <summary>收藏/取消收藏视频（收藏到默认收藏夹）</summary>
    public static async Task<(bool ok, string msg)> CollectAsync(long aid, bool collect = true)
    {
        try
        {
            var csrf = GetCsrf();
            if (string.IsNullOrEmpty(csrf)) return (false, "未登录，请先登录 B 站账号");

            var (_, _, uid) = await GetLoginUserAsync();
            var fid = uid > 0 ? await GetDefaultFavFolderAsync(uid) : 0;
            if (fid <= 0) return (false, "未找到收藏夹，请先在 B 站创建一个收藏夹");

            // act=1 添加用 add_media_ids；act=0 移除用 del_media_ids（B 站现行 v3 接口要求）
            var form = new Dictionary<string, string>
            {
                ["rid"] = aid.ToString(), ["type"] = "2", ["act"] = collect ? "1" : "0",
                ["csrf"] = csrf,
            };
            form[collect ? "add_media_ids" : "del_media_ids"] = fid.ToString();

            var p = new Dictionary<string, object>
            {
                ["rid"] = aid, ["type"] = 2, ["act"] = collect ? 1 : 0,
                [collect ? "add_media_ids" : "del_media_ids"] = fid,
            };
            var q = await SignParamsAsync(p);
            var (code, msg, ok) = await PostJsonAsync($"https://api.bilibili.com/x/v3/fav/resource/deal?{q}",
                form, $"https://www.bilibili.com/video/av{aid}");
            if (!ok) return (false, msg);
            if (code == 0) return (true, collect ? "已收藏" : "已取消收藏");
            return (false, CodeMessage(code, msg));
        }
        catch
        {
            return (false, "网络异常，操作失败");
        }
    }

    /// <summary>关注/取关 UP 主</summary>
    public static async Task<(bool ok, string msg)> FollowAsync(long uid, bool follow = true)
    {
        try
        {
            var csrf = GetCsrf();
            var p = new Dictionary<string, object> { ["fid"] = uid, ["act"] = follow ? 1 : 2, ["re_src"] = 11 };
            var q = await SignParamsAsync(p);
            var form = new Dictionary<string, string>
            {
                ["fid"] = uid.ToString(), ["act"] = follow ? "1" : "2", ["re_src"] = "11",
                ["csrf"] = csrf ?? "",
            };
            var (code, msg, ok) = await PostJsonAsync($"https://api.bilibili.com/x/relation/modify?{q}",
                form, $"https://space.bilibili.com/{uid}");
            if (!ok) return (false, msg);
            if (code == 0) return (true, follow ? "已关注" : "已取消关注");
            return (false, CodeMessage(code, msg));
        }
        catch
        {
            return (false, "网络异常，操作失败");
        }
    }

    /// <summary>发表评论（需要登录 + csrf）</summary>
    public static async Task<(bool ok, string msg)> SendCommentAsync(long aid, string message)
    {
        try
        {
            var csrf = GetCsrf();
            if (string.IsNullOrEmpty(csrf)) return (false, "未登录，请先登录 B 站账号");
            var p = new Dictionary<string, object>
            {
                ["type"] = 1, ["oid"] = aid, ["message"] = message, ["plat"] = 1,
            };
            var q = await SignParamsAsync(p);
            var form = new Dictionary<string, string>
            {
                ["type"] = "1", ["oid"] = aid.ToString(), ["message"] = message, ["plat"] = "1",
                ["csrf"] = csrf,
            };
            var (code, msg, ok) = await PostJsonAsync($"https://api.bilibili.com/x/v2/reply/add?{q}",
                form, $"https://www.bilibili.com/video/av{aid}");
            if (!ok) return (false, msg);
            if (code == 0) return (true, "评论成功");
            return (false, CodeMessage(code, msg));
        }
        catch
        {
            return (false, "网络异常，评论失败");
        }
    }

    // ---------- 投币 / 点赞状态 / 关注状态（与 B 站实时同步） ----------

    /// <summary>投币（1 枚），需要登录 + WBI</summary>
    public static async Task<(bool ok, string msg)> CoinAsync(long aid)
    {
        try
        {
            var csrf = Cookies.GetCookies(new Uri("https://www.bilibili.com"))["bili_jct"]?.Value ?? "";
            var p = new Dictionary<string, object> { ["aid"] = aid, ["multiply"] = 1, ["coin_type"] = 1, ["coins"] = 1 };
            var q = await SignParamsAsync(p);
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["aid"] = aid.ToString(), ["multiply"] = "1", ["coin_type"] = "1", ["coins"] = "1", ["csrf"] = csrf,
            });
            var req = new HttpRequestMessage(HttpMethod.Post, $"https://api.bilibili.com/x/web-interface/coin/add?{q}");
            req.Headers.Referrer = new Uri($"https://www.bilibili.com/video/av{aid}");
            req.Headers.TryAddWithoutValidation("Origin", "https://www.bilibili.com");
            req.Content = form;
            var r = await Client.SendAsync(req);
            var json = await r.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var code = doc.RootElement.GetProperty("code").GetInt32();
            var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            if (code == 0) return (true, "投币成功 +1");
            return (false, CodeMessage(code, msg));
        }
        catch
        {
            return (false, "网络异常，投币失败");
        }
    }

    /// <summary>查询是否已点赞某视频（1=已赞）</summary>
    public static async Task<bool> GetLikedAsync(long aid)
    {
        try
        {
            var json = await Client.GetStringAsync($"https://api.bilibili.com/x/web-interface/archive/has/like?aid={aid}");
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("code", out var code) || code.GetInt32() != 0 ||
                !doc.RootElement.TryGetProperty("data", out var data))
                return false;
            return data.GetInt32() == 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>查询与 UP 主的关注关系（0=未关注，1=已关注，2=互关）</summary>
    public static async Task<int> GetRelationAsync(long uid)
    {
        try
        {
            var json = await Client.GetStringAsync($"https://api.bilibili.com/x/relation?fid={uid}");
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("code", out var code) || code.GetInt32() != 0 ||
                !doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("relation", out var rel))
                return 0;
            return rel.GetInt32();
        }
        catch
        {
            return 0;
        }
    }

    // ---------- 工具 ----------

    private static string GetStr(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static long GetInt(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;
}
