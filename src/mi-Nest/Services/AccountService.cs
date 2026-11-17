using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MiNest.Models;
using Microsoft.Web.WebView2.Core;

namespace MiNest.Services;

/// <summary>
/// 账号管理（仿 TeyvatGuide）：支持 扫码 / 验证码 / Cookie 登录与多账号管理。
/// Cookie 使用 DPAPI（当前 Windows 用户）加密后存储。
/// </summary>
public static class AccountService
{
    private static readonly List<Account> Accounts = new();

    public static IReadOnlyList<Account> All => Accounts;

    public static Account? Active => Accounts.FirstOrDefault(a => a.IsActive) ?? Accounts.FirstOrDefault();

    public static event Action? Changed;

    static AccountService() => Load();

    public static void Load()
    {
        Accounts.Clear();
        try
        {
            if (File.Exists(AppPaths.AccountsFile))
            {
                var json = File.ReadAllText(AppPaths.AccountsFile);
                var list = JsonSerializer.Deserialize<List<Account>>(json) ?? new List<Account>();
                foreach (var a in list)
                {
                    a.Cookie = Decrypt(a.CookieEncrypted) ?? "";
                    if (!string.IsNullOrEmpty(a.Cookie))
                        Accounts.Add(a);
                }
            }
        }
        catch
        {
            // 读取失败按空处理
        }
        Changed?.Invoke();
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            var copy = Accounts.Select(a => new Account
            {
                Id = a.Id,
                Nickname = a.Nickname,
                Uid = a.Uid,
                IsActive = a.IsActive,
                LastLogin = a.LastLogin,
                CookieEncrypted = Encrypt(a.Cookie) ?? "",
            }).ToList();
            File.WriteAllText(AppPaths.AccountsFile,
                JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 写入失败不阻断
        }
    }

    public static void SetActive(Account a)
    {
        foreach (var x in Accounts) x.IsActive = ReferenceEquals(x, a);
        Save();
        Changed?.Invoke();
    }

    /// <summary>新增或按 UID 去重更新账号，并自动切换为当前账号</summary>
    public static void AddOrUpdate(Account a)
    {
        var ex = Accounts.FirstOrDefault(x => x.Uid == a.Uid && !string.IsNullOrEmpty(x.Uid));
        if (ex != null)
        {
            ex.Nickname = a.Nickname;
            ex.Cookie = a.Cookie;
            ex.LastLogin = DateTime.Now;
            SetActive(ex);
        }
        else
        {
            a.LastLogin = DateTime.Now;
            Accounts.Add(a);
            foreach (var x in Accounts) x.IsActive = ReferenceEquals(x, a);
            Save();
            Changed?.Invoke();
        }
    }

    public static void Remove(Account a)
    {
        Accounts.Remove(a);
        if (a.IsActive && Accounts.Count > 0)
            Accounts[0].IsActive = true;
        Save();
        Changed?.Invoke();
    }

    // ---------- 加密 ----------

    public static string? Encrypt(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return null;
        try
        {
            var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(data);
        }
        catch
        {
            return null;
        }
    }

    public static string? Decrypt(string encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return null;
        try
        {
            var data = ProtectedData.Unprotect(Convert.FromBase64String(encrypted), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch
        {
            return null;
        }
    }

    // ---------- Cookie 校验 ----------

    /// <summary>校验 Cookie 并取昵称/UID（现行可用端点 binding/getUserGameRolesByCookie，旧的 getUserFullInfo 已 404）</summary>
    public static async Task<(bool ok, string nickname, string uid, string message)> ValidateCookieAsync(string cookie)
    {
        var api = await MiHoYoHttp.SendWithHeadersAsync("GET",
            "https://api-takumi.mihoyo.com/binding/api/getUserGameRolesByCookie?game_biz=hk4e_cn",
            null, BuildValidateHeaders(cookie));
        if (api.Retcode == 0 && api.Data != null)
        {
            var nickname = "";
            var uid = "";
            if (api.Data.Value.TryGetProperty("list", out var list) &&
                list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0)
            {
                var first = list[0];
                nickname = first.GetProp("nickname");
                uid = first.GetProp("game_uid");
            }
            if (string.IsNullOrEmpty(nickname))
                nickname = "米游社用户";
            return (true, nickname, uid, "ok");
        }
        return (false, "", "", $"{api.Message}（{api.Retcode}）");
    }

    /// <summary>takumi 校验接口请求头（带 Cookie，无需 DS）</summary>
    private static Dictionary<string, string> BuildValidateHeaders(string cookie)
    {
        return new Dictionary<string, string>
        {
            ["user-agent"] = $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) miHoYoBBS/{BbsVersion}",
            ["x-rpc-app_version"] = BbsVersion,
            ["x-rpc-client_type"] = "5",
            ["x-requested-with"] = "com.mihoyo.hyperion",
            ["referer"] = "https://webstatic.mihoyo.com",
            ["cookie"] = cookie,
        };
    }

    // ---------- 二维码登录 ----------
    // 2025+ 米游社通行证新接口：account/ma-cn-passport（旧 /account/auth/api/web/createQrCode 已下线，返回 404）

    private const string X4Salt = "xV8v4Qu54lUKrEYFZkJhB8cuOh9Asafs";
    private const string BbsVersion = "2.115.0";
    private const string QrUa = "Mozilla/5.0 miHoYoBBS/2.115.0 Capture/2.2.0";
    private const string MobileUa = "Mozilla/5.0 (Linux; Android 12) Mobile miHoYoBBS/2.115.0";
    private const string AppId = "bll8iq97cem8";
    // 仿 TeyvatGuide toolFunc.ts getInitDeviceInfo：device_name=12 位、device_model=6 位随机大写数字
    private static readonly Random DevRandom = new();

    private static string RandomDeviceName(int len)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return new string(Enumerable.Range(0, len).Select(_ => chars[DevRandom.Next(chars.Length)]).ToArray());
    }

    private static string DeviceName => RandomDeviceName(12);
    private static string DeviceModel => RandomDeviceName(6);

    private static readonly Random DsRandom = new();

    /// <summary>X4 DS（类型 2：salt&amp;t&amp;r&amp;b=body&amp;q=query）</summary>
    private static string GetDsX4(string bodyJson)
    {
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var r = DsRandom.Next(100000, 200000).ToString();
        var hash = $"salt={X4Salt}&t={t}&r={r}&b={bodyJson}&q=";
        var md5 = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(hash))).ToLowerInvariant();
        return $"{t},{r},{md5}";
    }

    private static string DeviceId => SettingsService.Settings.DeviceId;

    /// <summary>扫码接口公共请求头（UA 需与米游社 App 一致，扫码得到的 stoken 才能用于米游社）</summary>
    private static Dictionary<string, string> BuildQrHeaders(string bodyJson)
    {
        var headers = new Dictionary<string, string>
        {
            ["user-agent"] = QrUa,
            ["accept"] = "*/*",
            ["accept-language"] = "zh-cn",
            ["x-rpc-client_type"] = "3",
            ["x-rpc-app_version"] = BbsVersion,
            ["x-rpc-sdk_version"] = BbsVersion,
            ["x-rpc-account_version"] = BbsVersion,
            ["x-rpc-app_id"] = AppId,
            ["x-rpc-game_biz"] = "bbs_cn",
            ["x-rpc-device_id"] = DeviceId,
            ["x-rpc-device_fp"] = "",
            ["x-rpc-device_name"] = DeviceName,
            ["x-rpc-device_model"] = DeviceModel,
            ["DS"] = GetDsX4(bodyJson),
            ["content-type"] = "application/json",
        };
        return headers;
    }

    public static async Task<(bool ok, string message, string? qrPayload, string? ticket)> CreateQrAsync()
    {
        var api = await MiHoYoHttp.SendWithHeadersAsync("POST",
            "https://passport-api.mihoyo.com/account/ma-cn-passport/app/createQRLogin", "{}", BuildQrHeaders("{}"));
        if (api.Retcode == 0 && api.Data != null)
            return (true, "ok", api.Data.Value.GetProp("url"), api.Data.Value.GetProp("ticket"));
        return (false, $"{api.Message}（{api.Retcode}）", null, null);
    }

    /// <summary>轮询二维码状态；status=Confirmed 后自动用 stoken 补齐 cookie_token/ltoken 组装登录 Cookie</summary>
    public static async Task<(bool ok, string message, string? cookie)> PollQrAsync(string ticket)
    {
        var body = JsonSerializer.Serialize(new { ticket });
        var api = await MiHoYoHttp.SendWithHeadersAsync("POST",
            "https://passport-api.mihoyo.com/account/ma-cn-passport/app/queryQRLoginStatus", body, BuildQrHeaders(body));
        if (api.Retcode != 0) return (false, $"{api.Message}（{api.Retcode}）", null);
        if (api.Data == null) return (false, "无数据", null);

        var status = api.Data.Value.GetProp("status");
        switch (status)
        {
            case "Init":
            case "Created":
                return (false, "等待扫码…", null);
            case "Scanned":
                return (false, "已扫码，请在手机上确认", null);
            case "Confirmed":
                return await BuildCookieFromLoginDataAsync(api.Data.Value);
            default:
                return (false, $"未知状态：{status}", null);
        }
    }

    /// <summary>把通行证登录响应（tokens + user_info）组装为可用 Cookie（口径同 TeyvatGuide：account_id/stuid/ltuid=aid，mid 独立）</summary>
    private static async Task<(bool ok, string message, string? cookie)> BuildCookieFromLoginDataAsync(JsonElement data)
    {
        var mid = "";
        var aid = "";
        if (data.TryGetProperty("user_info", out var ui))
        {
            mid = ui.GetProp("mid");
            aid = ui.GetProp("aid");
        }

        string? stoken = null;
        if (data.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Array)
        {
            foreach (var tok in tokens.EnumerateArray())
            {
                var tt = tok.GetProp("token_type");
                var tk = tok.GetProp("token");
                if (tt == "1" && !string.IsNullOrEmpty(tk))
                    stoken = tk;
            }
        }

        if (string.IsNullOrEmpty(stoken) || string.IsNullOrEmpty(aid) || string.IsNullOrEmpty(mid))
            return (false, "登录凭证解析失败", null);

        // 用 stoken 换取 cookie_token / ltoken（失败不阻断，核心 stoken+mid 已可用）
        var cookieToken = "";
        var ltoken = "";
        try { cookieToken = await GetCookieTokenAsync(mid, stoken); } catch { }
        try { ltoken = await GetLTokenAsync(mid, stoken); } catch { }

        var sb = new StringBuilder();
        // v1 字段（TeyvatGuide 口径）
        sb.Append($"account_id={aid}; ").Append($"stuid={aid}; ").Append($"ltuid={aid}; ")
          .Append($"mid={mid}; ").Append($"stoken={stoken}; ");
        if (!string.IsNullOrEmpty(ltoken))
            sb.Append($"ltoken={ltoken}; ");
        if (!string.IsNullOrEmpty(cookieToken))
            sb.Append($"cookie_token={cookieToken}; ");
        // v2 字段（新版 takumi/bbs 接口的登录态，与 v1 并存无冲突，兼容性更好）
        sb.Append($"ltuid_v2={aid}; ").Append($"account_id_v2={aid}; ").Append($"account_mid_v2={mid}; ");
        if (!string.IsNullOrEmpty(ltoken))
            sb.Append($"ltoken_v2={ltoken}; ");
        if (!string.IsNullOrEmpty(cookieToken))
            sb.Append($"cookie_token_v2={cookieToken}; ");
        sb.Append($"ltmid_v2={mid}; ");
        return (true, "登录成功", sb.ToString().TrimEnd(' ', ';'));
    }

    /// <summary>stoken 换取 cookie_token（通行证接口，需 X4 DS + mid/stoken Cookie）</summary>
    private static async Task<string> GetCookieTokenAsync(string mid, string stoken)
    {
        var url = $"https://passport-api.mihoyo.com/account/auth/api/getCookieAccountInfoBySToken?stoken={stoken}";
        var headers = BuildStokenHeaders(mid, stoken, "stoken=" + stoken);
        var api = await MiHoYoHttp.SendWithHeadersAsync("GET", url, null, headers);
        if (api.Retcode == 0 && api.Data != null)
            return api.Data.Value.GetProp("cookie_token");
        return "";
    }

    /// <summary>stoken 换取 ltoken</summary>
    private static async Task<string> GetLTokenAsync(string mid, string stoken)
    {
        var url = $"https://passport-api.mihoyo.com/account/auth/api/getLTokenBySToken?stoken={stoken}";
        var headers = BuildStokenHeaders(mid, stoken, "stoken=" + stoken);
        var api = await MiHoYoHttp.SendWithHeadersAsync("GET", url, null, headers);
        if (api.Retcode == 0 && api.Data != null)
            return api.Data.Value.GetProp("ltoken");
        return "";
    }

    /// <summary>带 stoken 的通行证请求头（GET，DS 用 query 参与签名）</summary>
    private static Dictionary<string, string> BuildStokenHeaders(string mid, string stoken, string query)
    {
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var r = DsRandom.Next(100000, 200000).ToString();
        var hash = $"salt={X4Salt}&t={t}&r={r}&b=&q={query}";
        var md5 = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(hash))).ToLowerInvariant();
        return new Dictionary<string, string>
        {
            ["user-agent"] = $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) miHoYoBBS/{BbsVersion}",
            ["x-rpc-app_version"] = BbsVersion,
            ["x-rpc-client_type"] = "5",
            ["x-requested-with"] = "com.mihoyo.hyperion",
            ["referer"] = "https://webstatic.mihoyo.com",
            ["x-rpc-device_id"] = DeviceId,
            ["DS"] = $"{t},{r},{md5}",
            ["cookie"] = $"mid={mid};stoken={stoken}",
        };
    }

    // ---------- 验证码登录 ----------

    /// <summary>RSA 加密（JSEncrypt 兼容，PKCS#1 v1.5）</summary>
    private static string RsaEncrypt(string plain)
    {
        try
        {
            const string pem = "-----BEGIN PUBLIC KEY-----\n" +
                "MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQDDvekdPMHN3AYhm/vktJT+YJr7cI5DcsNKqdsx5DZX0gDuWFuIjzdwButrIYPNmRJ1G8ybDIF7oDW2eEpm5sMbL9zs\n" +
                "9ExXCdvqrn51qELbqj0XxtMTIpaCHFSI50PfPpTFV9Xt/hmyVwokoOXFlAEgCn+Q\n" +
                "CgGs52bFoYMtyi+xEQIDAQAB\n-----END PUBLIC KEY-----";
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            var enc = rsa.Encrypt(Encoding.UTF8.GetBytes(plain), RSAEncryptionPadding.Pkcs1);
            return Convert.ToBase64String(enc);
        }
        catch
        {
            return "";
        }
    }

    private static Dictionary<string, string> BuildMobileHeaders()
    {
        return new Dictionary<string, string>
        {
            ["x-rpc-aigis"] = "",
            ["x-rpc-app_version"] = BbsVersion,
            ["x-rpc-client_type"] = "2",
            ["x-rpc-app_id"] = AppId,
            ["x-rpc-device_fp"] = "",
            ["x-rpc-device_name"] = DeviceName,
            ["x-rpc-device_id"] = DeviceId,
            ["x-rpc-device_model"] = DeviceModel,
            ["user-agent"] = MobileUa,
            ["content-type"] = "application/json",
        };
    }

    /// <summary>验证码接口公共请求头（仿 TeyvatGuide passportReq：aigis 显式空串 + 假设备指纹 + device_name/model）</summary>
    private static Dictionary<string, string> BuildCaptchaHeaders()
    {
        var headers = BuildMobileHeaders();
        headers["referer"] = "https://user.miyoushe.com/";
        headers["x-rpc-game_biz"] = "hk4e_cn";
        headers["x-rpc-aigis"] = "";
        headers["x-rpc-device_fp"] = "0000000000000";
        headers["x-rpc-device_name"] = DeviceName;
        headers["x-rpc-device_model"] = DeviceModel;
        return headers;
    }

    public static async Task<(bool ok, string message)> SendSmsCodeAsync(string mobile)
    {
        var body = JsonSerializer.Serialize(new { area_code = RsaEncrypt("+86"), mobile = RsaEncrypt(mobile) });
        var api = await MiHoYoHttp.SendWithHeadersAsync("POST",
            "https://passport-api.mihoyo.com/account/ma-cn-verifier/verifier/createLoginCaptcha", body, BuildCaptchaHeaders());
        if (api.Retcode == 0) return (true, "验证码已发送");
        return (false, SmsFriendlyMessage(api, "发送验证码"));
    }

    public static async Task<(bool ok, string message, string? cookie)> LoginBySmsAsync(string mobile, string code)
    {
        var body = JsonSerializer.Serialize(new
        {
            area_code = RsaEncrypt("+86"),
            mobile = RsaEncrypt(mobile),
            action_type = "login",
            captcha = code,
        });
        var api = await MiHoYoHttp.SendWithHeadersAsync("POST",
            "https://passport-api.mihoyo.com/account/ma-cn-passport/app/loginByMobileCaptcha", body, BuildCaptchaHeaders());
        if (api.Retcode == 0 && api.Data != null)
            return await BuildCookieFromLoginDataAsync(api.Data.Value);
        return (false, SmsFriendlyMessage(api, "登录"), null);
    }

    /// <summary>把验证码接口的失败码转成可读提示：区分极验挑战（x-rpc-aigis）与网络风控（-3001）。</summary>
    private static string SmsFriendlyMessage(ApiResponse api, string stage)
    {
        if (!string.IsNullOrEmpty(api.Aigis))
            return $"{stage}失败：米哈游要求先完成滑块验证（极验风控），请改用「扫码登录」或「网页登录」。";
        return api.Retcode switch
        {
            -3001 => $"{stage}失败：米哈游对当前网络环境的短信验证码接口做了风控（-3001），请改用「扫码登录 / Cookie 登录 / 网页登录」；使用加速器切回国内网络后可重试。",
            _ => $"{api.Message}（{api.Retcode}）",
        };
    }

    // ---------- 极验（aigis）滑块验证 ----------

    /// <summary>极验挑战信息：来自响应头 x-rpc-aigis（JSON {session_id, data}），data 为极验初始化参数 JSON 字符串。</summary>
    public class SmsChallengeInfo
    {
        public string SessionId { get; set; } = "";
        public string DataJson { get; set; } = "";
    }

    /// <summary>解析响应头中的极验挑战；无挑战返回 null。</summary>
    public static SmsChallengeInfo? ParseSmsChallenge(ApiResponse api)
    {
        if (string.IsNullOrWhiteSpace(api.Aigis)) return null;
        try
        {
            using var doc = JsonDocument.Parse(api.Aigis);
            var root = doc.RootElement;
            var info = new SmsChallengeInfo
            {
                SessionId = root.TryGetProperty("session_id", out var s) ? s.GetString() ?? "" : "",
                DataJson = root.TryGetProperty("data", out var d) ? d.GetString() ?? "" : "",
            };
            return string.IsNullOrEmpty(info.SessionId) || string.IsNullOrEmpty(info.DataJson) ? null : info;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>验证成功后拼接 aigis 字符串（session_id;base64(validateJson)），仿 TeyvatGuide。</summary>
    public static string BuildAigisString(string sessionId, string validateJson)
        => $"{sessionId};{Convert.ToBase64String(Encoding.UTF8.GetBytes(validateJson))}";

    /// <summary>发送验证码（可带 aigis 重试）；返回 (是否成功, 提示, 极验挑战)。</summary>
    public static async Task<(bool ok, string message, SmsChallengeInfo? challenge)> SendSmsCodeDetailedAsync(string mobile, string? aigis = null)
    {
        var headers = BuildCaptchaHeaders();
        if (!string.IsNullOrEmpty(aigis)) headers["x-rpc-aigis"] = aigis;
        var body = JsonSerializer.Serialize(new { area_code = RsaEncrypt("+86"), mobile = RsaEncrypt(mobile) });
        var api = await MiHoYoHttp.SendWithHeadersAsync("POST",
            "https://passport-api.mihoyo.com/account/ma-cn-verifier/verifier/createLoginCaptcha", body, headers);
        if (api.Retcode == 0) return (true, "验证码已发送", null);
        var ch = ParseSmsChallenge(api);
        if (ch != null) return (false, SmsFriendlyMessage(api, "发送验证码"), ch);
        return (false, SmsFriendlyMessage(api, "发送验证码"), null);
    }

    /// <summary>验证码登录（可带 aigis 重试）；返回 (是否成功, 提示, cookie, 极验挑战)。</summary>
    public static async Task<(bool ok, string message, string? cookie, SmsChallengeInfo? challenge)> LoginBySmsDetailedAsync(string mobile, string code, string? aigis = null)
    {
        var headers = BuildCaptchaHeaders();
        if (!string.IsNullOrEmpty(aigis)) headers["x-rpc-aigis"] = aigis;
        var body = JsonSerializer.Serialize(new
        {
            area_code = RsaEncrypt("+86"),
            mobile = RsaEncrypt(mobile),
            action_type = "login",
            captcha = code,
        });
        var api = await MiHoYoHttp.SendWithHeadersAsync("POST",
            "https://passport-api.mihoyo.com/account/ma-cn-passport/app/loginByMobileCaptcha", body, headers);
        if (api.Retcode == 0 && api.Data != null)
        {
            var built = await BuildCookieFromLoginDataAsync(api.Data.Value);
            return (built.ok, built.ok ? "登录成功" : built.message, built.cookie, null);
        }
        var ch = ParseSmsChallenge(api);
        if (ch != null) return (false, SmsFriendlyMessage(api, "登录"), null, ch);
        return (false, SmsFriendlyMessage(api, "登录"), null, null);
    }

    // ---------- 从内置浏览器导入 ----------

    /// <summary>从应用内 WebView2（米游社登录态）导入 Cookie；同名键去重（保留最后写入的值），避免多域重复导致登录态解析失败。</summary>
    public static async Task<(bool ok, string message, string? cookie)> ImportFromWebViewAsync(CoreWebView2CookieManager manager)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // passport-api 域含 stoken（抽卡/签到必需）；takumi 域含 cookie_token/ltoken；miyoushe 域含 ltuid/account_id
        foreach (var domain in new[]
                 {
                     "https://www.miyoushe.com", "https://bbs-api.miyoushe.com",
                     "https://api-takumi.mihoyo.com", "https://passport-api.mihoyo.com",
                     "https://api-account.mihoyo.com", "https://user.miyoushe.com",
                 })
        {
            try
            {
                var cookies = await manager.GetCookiesAsync(domain);
                foreach (var c in cookies)
                {
                    if (!string.IsNullOrEmpty(c.Name) && !string.IsNullOrEmpty(c.Value))
                        map[c.Name] = c.Value;
                }
            }
            catch
            {
                // 忽略单个域读取失败
            }
        }
        var cookie = string.Join("; ", map.Select(kv => $"{kv.Key}={kv.Value}"));
        if (string.IsNullOrEmpty(cookie))
            return (false, "内置浏览器中未找到米游社登录态，请先在“社区 / 视频”页面登录后重试", null);

        // stoken 是抽卡记录 / 签到等接口的必需凭证；网页端登录态若缺 stoken，接口会返回登录失效
        if (!cookie.Contains("stoken"))
            return (false, "检测到登录态缺少 stoken（游戏角色/签到接口必需）。请改用「扫码登录」或「Cookie 登录」（在米游社 App 登录后复制完整 Cookie）重试", cookie);
        return (true, "ok", cookie);
    }
}
