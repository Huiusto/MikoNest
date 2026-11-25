using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MiNest.Services;

/// <summary>米哈游开放接口通用 HTTP 客户端（DS 签名 / Cookie / 统一解析）</summary>
public static class MiHoYoHttp
{
    // 米游社客户端版本号（社区接口的通用参数；与 TeyvatGuide 现行 2.119.0 对齐）
    public const string AppVersion = "2.119.0";
    // 社区接口 DS 盐值（与 TeyvatGuide Beta v0.13.1 / 2.119.0 版本线一致）
    public const string Salt = "xV8v4Qu54lUKNrEY0lmhS5TfxyuLbcwB";
    public const string X4Salt = "xV8v4Qu54lUKrEYFZkJhB8cuOh9Asafs";
    public const string X6Salt = "t0qEgfub6cvueAPgR5m9aQWWVciEer7v";
    // K2/LK2 盐值对齐 TeyvatGuide（社区点赞/分享/看帖 DS1 签名）
    public const string K2Salt = "09d39d16528ca0e4900a39ecc07d5062";
    public const string LK2Salt = "b99b74d9023a6511231be2c7209b6fdb";
    // 设备指纹（TeyvatGuide 用固定占位值）
    public const string DeviceFp = "0000000000000";
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) miHoYoBBS/2.119.0";

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static string GetDs()
        => GetDsType1(Salt);

    /// <summary>DS 类型 1（isSign）：salt&amp;t&amp;r，无 body/query（用于点赞/分享/看帖等签名接口）。
    /// 官方算法为 md5("salt=&lt;salt&gt;&amp;t=..&amp;r=..")，r 为 6 位随机字母数字（TeyvatGuide getRandomString(6)）。</summary>
    public static string GetDsType1(string salt)
    {
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var r = RandomString(6);
        var md5 = Md5($"salt={salt}&t={t}&r={r}");
        return $"{t},{r},{md5}";
    }

    /// <summary>DS 类型 2：md5("salt=&lt;salt&gt;&amp;t=..&amp;r=..&amp;b=body&amp;q=query")（用于签到等 POST 接口）。
    /// r 为 6 位随机数字 100000-200000（TeyvatGuide getRandomNumber）。
    /// 注意 GET 请求应传空 body、query 传排序后的参数串。</summary>
    public static string GetDsType2(string salt, string body, string query = "")
    {
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var r = _rng.Next(100000, 200001).ToString();
        var md5 = Md5($"salt={salt}&t={t}&r={r}&b={body}&q={query}");
        return $"{t},{r},{md5}";
    }

    private static readonly Random _rng = new();

    /// <summary>6 位随机字母数字串（小写+大写+数字，对齐 TeyvatGuide getRandomString 默认字符集）</summary>
    private static string RandomString(int length)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var sb = new System.Text.StringBuilder(length);
        for (var i = 0; i < length; i++)
            sb.Append(chars[_rng.Next(chars.Length)]);
        return sb.ToString();
    }

    public static string Md5(string input)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static HttpRequestMessage Create(HttpMethod method, string url, string? cookie = null, string? bodyJson = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (bodyJson != null)
            req.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

        req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        if (!string.IsNullOrEmpty(cookie))
            req.Headers.TryAddWithoutValidation("Cookie", cookie);
        req.Headers.TryAddWithoutValidation("x-rpc-app_version", AppVersion);
        req.Headers.TryAddWithoutValidation("x-rpc-client_type", "5");
        req.Headers.TryAddWithoutValidation("x-rpc-device_id", SettingsService.Settings.DeviceId);
        req.Headers.TryAddWithoutValidation("x-rpc-device_fp", DeviceFp);
        req.Headers.TryAddWithoutValidation("x-requested-with", "com.mihoyo.hyperion");
        // GET 请求的 DS 需要把排序后的 query 一起签名（官方公式 salt=&t=&r=&b=&q=）
        var query = "";
        var idx = url.IndexOf('?');
        if (idx > 0)
            query = string.Join("&", url[(idx + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries).OrderBy(x => x));
        req.Headers.TryAddWithoutValidation("DS", GetDsType2(Salt, "", query));
        req.Headers.TryAddWithoutValidation("Referer", "https://www.miyoushe.com/");
        return req;
    }

    public static async Task<ApiResponse> SendAsync(string method, string url, string? cookie = null, string? bodyJson = null)
    {
        try
        {
            using var req = Create(new HttpMethod(method), url, cookie, bodyJson);
            using var resp = await Client.SendAsync(req);
            var raw = await resp.Content.ReadAsStringAsync();
            return Parse(raw);
        }
        catch (Exception ex)
        {
            return new ApiResponse { Ok = false, Message = $"网络错误：{ex.Message}" };
        }
    }

    /// <summary>解析米哈游标准返回体 {retcode, message, data}</summary>
    public static ApiResponse Parse(string raw)
    {
        var result = new ApiResponse { Raw = raw };
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("retcode", out var rc) && rc.TryGetInt32(out var code))
                result.Retcode = code;
            if (root.TryGetProperty("message", out var msg))
                result.Message = msg.GetString() ?? "";
            if (root.TryGetProperty("data", out var data) && data.ValueKind != JsonValueKind.Null)
                result.Data = data.Clone();
        }
        catch
        {
            result.Message = "响应解析失败";
        }
        return result;
    }

    /// <summary>无签名要求的普通 GET（如抽卡日志接口），可选带 Cookie / 自定义 UA</summary>
    public static async Task<ApiResponse> GetPlainAsync(string url, string? cookie = null, string? userAgent = null)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", userAgent ?? UserAgent);
            if (!string.IsNullOrEmpty(cookie))
                req.Headers.TryAddWithoutValidation("Cookie", cookie);
            using var resp = await Client.SendAsync(req);
            var raw = await resp.Content.ReadAsStringAsync();
            return Parse(raw);
        }
        catch (Exception ex)
        {
            return new ApiResponse { Ok = false, Message = $"网络错误：{ex.Message}" };
        }
    }

    /// <summary>
    /// 按给定请求头发送请求（不加默认 DS），用于通行证新接口（扫码/验证码，X4 DS 由调用方计算）。
    /// 若响应头带 x-rpc-aigis（米哈游极验风控挑战），会写入 ApiResponse.Aigis。
    /// </summary>
    public static async Task<ApiResponse> SendWithHeadersAsync(string method, string url, string? bodyJson, IDictionary<string, string> headers)
    {
        try
        {
            using var req = new HttpRequestMessage(new HttpMethod(method), url);
            if (!string.IsNullOrEmpty(bodyJson))
                req.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
            foreach (var kv in headers)
                req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            using var resp = await Client.SendAsync(req);
            var raw = await resp.Content.ReadAsStringAsync();
            var parsed = Parse(raw);
            if (resp.Headers.TryGetValues("x-rpc-aigis", out var aigisValues))
                parsed.Aigis = string.Join(";", aigisValues);
            if (resp.Headers.TryGetValues("Set-Cookie", out var setCookieValues))
                parsed.SetCookies.AddRange(setCookieValues);
            return parsed;
        }
        catch (Exception ex)
        {
            return new ApiResponse { Ok = false, Message = $"网络错误：{ex.Message}" };
        }
    }

    /// <summary>
    /// 发起一次“登录类”GET 请求，并捕获响应 Set-Cookie 拼成 cookie 字符串。
    /// 用于二维码/验证码登录后换取登录态。
    /// </summary>
    public static async Task<(bool ok, string message, string? cookie)> GetLoginCookieAsync(string url)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            req.Headers.TryAddWithoutValidation("x-rpc-app_version", AppVersion);
            req.Headers.TryAddWithoutValidation("x-rpc-client_type", "5");
            req.Headers.TryAddWithoutValidation("x-rpc-sdk_version", "2.12.1");
            req.Headers.TryAddWithoutValidation("x-rpc-device_id", SettingsService.Settings.DeviceId);
            req.Headers.TryAddWithoutValidation("Referer", "https://www.miyoushe.com/");

            using var resp = await Client.SendAsync(req);
            var raw = await resp.Content.ReadAsStringAsync();
            var api = Parse(raw);
            if (api.Retcode != 0)
                return (false, $"{api.Message}（{api.Retcode}）", null);

            var sb = new StringBuilder();
            if (resp.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var setCookie in setCookies)
                {
                    var kv = setCookie.Split(';')[0];
                    if (!string.IsNullOrWhiteSpace(kv))
                        sb.Append(kv).Append("; ");
                }
            }
            var cookie = sb.ToString().TrimEnd(' ', ';');
            return (true, "ok", string.IsNullOrEmpty(cookie) ? null : cookie);
        }
        catch (Exception ex)
        {
            return (false, $"网络错误：{ex.Message}", null);
        }
    }
}

public class ApiResponse
{
    public bool Ok { get; set; } = true;
    public string Raw { get; set; } = "";
    public int Retcode { get; set; } = -9999;
    public string Message { get; set; } = "";
    public JsonElement? Data { get; set; }
    /// <summary>响应头 x-rpc-aigis（极验挑战数据，JSON），非空表示接口要求先完成滑块/图形验证。</summary>
    public string? Aigis { get; set; }
    /// <summary>响应下发的 Set-Cookie 原始条目（登录类接口可能用 Set-Cookie 兜底下发 stoken/cookie_token/ltoken）。</summary>
    public List<string> SetCookies { get; set; } = new();
}

public static class JsonExtensions
{
    public static string GetProp(this JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "";
}
