using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using MiNest.Models;

namespace MiNest.Services;

/// <summary>
/// 每日签到 / 米游币每日任务（仿 TeyvatGuide 实用脚本）。
/// 游戏签到使用米游社 App 现行 luna 接口（event/luna），覆盖 6 款游戏板块的签到福利。
/// </summary>
public static class SignService
{
    public record GameCheckin(string Key, string Name, string ActId, string Host, string Region, string GameBiz);

    public static readonly GameCheckin[] Games =
    {
        new("genshin", "原神", "e202311201442471", "hk4e", "cn_gf01", "hk4e_cn"),
        new("starrail", "星穹铁道", "e202304121516551", "hkrpg", "prod_gf_cn", "hkrpg_cn"),
        new("zzz", "绝区零", "e202406242138391", "zzz", "prod_gf_cn", "nap_cn"),
        new("bh3", "崩坏3", "e202306201626331", "bh3", "pc01", "bh3_cn"),
        new("bh2", "崩坏学园2", "e202203291431091", "bh2", "prod_cn", "bh2_cn"),
        new("nxx", "未定事件簿", "e202202251749321", "nxx", "cn01", "nxx_cn"),
    };

    // X6 DS 盐（luna 签到接口），取自 TeyvatGuide / sign-task 现行配置（2026 仍在用）
    private const string X6Salt = "t0qEgfub6cvueAPgR5m9aQWWVciEer7v";
    private const string BbsUa = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) miHoYoBBS/2.115.0";
    private const string MobileUa = "Mozilla/5.0 (Linux; Android 12) Mobile miHoYoBBS/2.115.0";

    // 社区接口域名（官方现行：bbs-api.miyoushe.com）
    private const string BbsHost = "https://bbs-api.miyoushe.com";

    /// <summary>米游币任务默认板块（原神 gid=2）。板块 id 随官方更新可能变化。</summary>
    public const int DefaultGid = 2;

    public static GameCheckin Get(string key) => Array.Find(Games, g => g.Key == key)!;

    /// <summary>判断响应码是否为「登录状态失效」，此时应引导用户重新登录。</summary>
    public static bool IsLoginExpired(int retcode)
        => retcode == 1004 || retcode == -100 || retcode == -2000;

    /// <summary>查询账号在某游戏下的角色列表；失败时 error 返回原因（登录失效等）。
    /// 对齐 TeyvatGuide：getUserGameRolesByCookie（cookie_token/account_id 子集 + X4 DS 签名 + 完整请求头）。</summary>
    public static async Task<(List<SignRole> roles, string? error)> GetRolesAsync(string cookie, string gameBiz)
    {
        var result = new List<SignRole>();
        var ck = BuildSubCookie(cookie, "account_id", "cookie_token");
        var headers = ReqHeader(ck, "GET", "", MiHoYoHttp.X4Salt, isSign: false, clientType: 5);
        var api = await MiHoYoHttp.SendWithHeadersAsync("GET",
            "https://api-takumi.mihoyo.com/binding/api/getUserGameRolesByCookie", null, headers);
        if (IsLoginExpired(api.Retcode))
            return (result, "账号登录状态已失效：请到「账号」页重新登录；若当前网络为海外，请切换国内网络或开启加速器后重新登录（米哈游接口对海外 IP 会直接拒绝）");
        if (api.Retcode != 0)
            return (result, $"{api.Message}（{api.Retcode}）");
        if (api.Data != null && api.Data.Value.TryGetProperty("list", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in list.EnumerateArray())
            {
                if (!string.IsNullOrEmpty(gameBiz) && el.GetProp("game_biz") != gameBiz) continue;
                result.Add(new SignRole
                {
                    Uid = el.GetProp("game_uid"),
                    Nickname = el.GetProp("nickname"),
                    Region = el.GetProp("region"),
                    GameBiz = el.GetProp("game_biz"),
                    Level = el.GetProp("level"),
                });
            }
        }
        return (result, null);
    }

    private static string LunaUrl(GameCheckin game, string ep)
        => $"https://api-takumi.mihoyo.com/event/luna/{game.Host}/{ep}";

    /// <summary>查询某角色今日签到状态（luna info，Mobile UA，对齐 TeyvatGuide getLunaInfo）</summary>
    public static async Task<bool> IsSignedTodayAsync(string cookie, GameCheckin game, string uid, string region)
    {
        var url = $"{LunaUrl(game, "info")}?lang=zh-cn&act_id={game.ActId}&region={region}&uid={uid}";
        var headers = LunaInfoHeaders(cookie, game);
        var api = await MiHoYoHttp.SendWithHeadersAsync("GET", url, null, headers);
        if (api.Retcode == 0 && api.Data != null &&
            api.Data.Value.TryGetProperty("is_sign", out var s))
            return s.ValueKind == JsonValueKind.True || s.GetString() == "true";
        return false;
    }

    /// <summary>查询角色签到状态；登录失效返回 null（调用方应停止并提示重新登录）。</summary>
    public static async Task<bool?> GetSignStatusAsync(string cookie, GameCheckin game, string uid, string region)
    {
        var url = $"{LunaUrl(game, "info")}?lang=zh-cn&act_id={game.ActId}&region={region}&uid={uid}";
        var headers = LunaInfoHeaders(cookie, game);
        var api = await MiHoYoHttp.SendWithHeadersAsync("GET", url, null, headers);
        if (IsLoginExpired(api.Retcode)) return null;
        if (api.Retcode == 0 && api.Data != null &&
            api.Data.Value.TryGetProperty("is_sign", out var s))
            return s.ValueKind == JsonValueKind.True || s.GetString() == "true";
        return false;
    }

    /// <summary>luna info/home 读接口请求头（Mobile UA + cookie_token/account_id + x-rpc-signgame，无 DS，对齐 TeyvatGuide）</summary>
    private static Dictionary<string, string> LunaInfoHeaders(string cookie, GameCheckin game)
    {
        var ck = BuildSubCookie(cookie, "account_id", "cookie_token");
        return new Dictionary<string, string>
        {
            ["user-agent"] = MobileUa,
            ["referer"] = "https://act.mihoyo.com",
            ["cookie"] = TransCookie(ck),
            ["x-rpc-signgame"] = game.Host,
        };
    }

    /// <summary>对单个账号执行某游戏全部角色的每日签到（luna sign，X6 DS）</summary>
    public static async Task<(bool ok, string message, List<SignLogEntry> log)> SignGameAsync(string cookie, GameCheckin game)
    {
        var log = new List<SignLogEntry>();
        var (roles, err) = await GetRolesAsync(cookie, game.GameBiz);
        if (err != null)
        {
            log.Add(new SignLogEntry { Text = $"{game.Name}：{err}", IsError = true });
            return (false, err, log);
        }
        if (roles.Count == 0)
        {
            log.Add(new SignLogEntry { Text = $"{game.Name}：未找到已绑定的角色", IsError = true });
            return (false, "未找到角色", log);
        }

        var allOk = true;
        foreach (var role in roles)
        {
            // 已签到则跳过；登录失效时停止整个签到流程
            var status = await GetSignStatusAsync(cookie, game, role.Uid, role.Region);
            if (status == null)
            {
                allOk = false;
                log.Add(new SignLogEntry { Text = $"{game.Name}：账号登录状态已失效，请到「账号」页重新登录", IsError = true });
                return (false, "登录状态失效", log);
            }
            if (status == true)
            {
                log.Add(new SignLogEntry { Text = $"{game.Name}：{role.Nickname}（{role.Uid}）今日已签到" });
                continue;
            }

            var body = JsonSerializer.Serialize(new { lang = "zh-cn", act_id = game.ActId, region = role.Region, uid = role.Uid });
            // luna sign 写接口：getRequestHeader 全套（PC UA + X6 类型2 + client_type 覆盖 2 + referer webstatic）+ x-rpc-signgame
            var ck = BuildSubCookie(cookie, "account_id", "cookie_token");
            var headers = ReqHeader(ck, "POST", body, MiHoYoHttp.X6Salt, isSign: false, clientType: 2, referer: "https://webstatic.mihoyo.com");
            headers["x-rpc-signgame"] = game.Host;
            var api = await MiHoYoHttp.SendWithHeadersAsync("POST", LunaUrl(game, "sign"), body, headers);

            if (api.Retcode == 0 && api.Data != null)
            {
                var success = api.Data.Value.TryGetProperty("success", out var sc) && sc.GetInt32() == 0;
                if (success)
                    log.Add(new SignLogEntry { Text = $"{game.Name}：{role.Nickname}（{role.Uid}）签到成功" });
                else
                {
                    allOk = false;
                    log.Add(new SignLogEntry { Text = $"{game.Name}：{role.Nickname}（{role.Uid}）签到未生效", IsError = true });
                }
            }
            else if (api.Retcode == -5003)
                log.Add(new SignLogEntry { Text = $"{game.Name}：{role.Nickname}（{role.Uid}）今日已签到" });
            else if (IsLoginExpired(api.Retcode))
            {
                allOk = false;
                log.Add(new SignLogEntry { Text = $"{game.Name}：账号登录状态已失效，请到「账号」页重新登录", IsError = true });
            }
            else if (api.Retcode == 1034)
            {
                allOk = false;
                log.Add(new SignLogEntry { Text = $"{game.Name}：{role.Nickname}（{role.Uid}）触发验证码（1034），请到米游社 App 手动签到", IsError = true });
            }
            else
            {
                allOk = false;
                log.Add(new SignLogEntry { Text = $"{game.Name}：{role.Nickname}（{role.Uid}）{api.Message}（{api.Retcode}）", IsError = true });
            }
        }
        return (allOk, allOk ? "完成" : "部分失败", log);
    }

    /// <summary>一键完成米游币每日任务：社区打卡 + 看帖 + 点赞 + 分享（对齐 TeyvatGuide 现行接口）</summary>
    public static async Task<(bool ok, string message, List<SignLogEntry> log)> MiyousheDailyAsync(string cookie, int gid = DefaultGid)
    {
        var log = new List<SignLogEntry>();
        var allOk = true;

        // 看帖/点赞/分享用 cookie 子集（cookie_token/account_id），与 TeyvatGuide 口径一致
        var taskCookie = BuildSubCookie(cookie, "cookie_token", "account_id");

        // 1. 社区打卡（现行接口 apihub/app/api/signIn；对齐 TeyvatGuide：stoken 三件套 + Mobile UA + X6 类型2 DS + referer app.mihoyo.com）
        var signBody = $"{{\"gids\":{gid}}}";
        var signCookie = BuildSubCookie(cookie, "stoken", "stuid", "mid");
        var signHeaders = ReqHeader(signCookie, "POST", signBody, MiHoYoHttp.X6Salt, isSign: false,
            mobileUa: true, clientType: 2, referer: "https://app.mihoyo.com");
        var checkin = await MiHoYoHttp.SendWithHeadersAsync("POST", $"{BbsHost}/apihub/app/api/signIn", signBody, signHeaders);
        if (IsLoginExpired(checkin.Retcode))
        {
            log.Add(new SignLogEntry { Text = "米游币任务：账号登录状态已失效，请到「账号」页重新登录（若当前为海外网络，请切换国内网络/加速器）", IsError = true });
            return (false, "登录状态失效", log);
        }
        if (checkin.Retcode == 0)
            log.Add(new SignLogEntry { Text = $"米游社打卡成功（板块 {gid}）" });
        else if (checkin.Retcode == -5003 || checkin.Retcode == 1008 || checkin.Retcode == 1034)
            log.Add(new SignLogEntry { Text = "米游社今日已打卡（或触发验证 1034，请到米游社 App 手动打卡）" });
        else
        {
            allOk = false;
            log.Add(new SignLogEntry { Text = $"米游社打卡：{checkin.Message}（{checkin.Retcode}）", IsError = true });
        }

        // 2. 看帖/点赞/分享共用一篇帖子（固定使用官方「米游币任务体系调整说明」公告帖，TeyvatGuide 同款）
        var postId = "74971515";

        // 3. 看帖（read=1 计入浏览任务，post/wapi/getPostFull，K2 类型1 签名 + client_type 2）
        var readHeaders = ReqHeader(taskCookie, "GET", "", MiHoYoHttp.K2Salt, isSign: true, clientType: 2);
        var readApi = await MiHoYoHttp.SendWithHeadersAsync("GET",
            $"{BbsHost}/post/wapi/getPostFull?post_id={postId}&read=1", null, readHeaders);
        if (IsLoginExpired(readApi.Retcode))
        {
            log.Add(new SignLogEntry { Text = "米游币任务：账号登录状态已失效，请到「账号」页重新登录", IsError = true });
            return (false, "登录状态失效", log);
        }
        if (readApi.Retcode == 0)
            log.Add(new SignLogEntry { Text = "看帖成功" });
        else
        {
            allOk = false;
            log.Add(new SignLogEntry { Text = $"看帖：{readApi.Message}（{readApi.Retcode}）", IsError = true });
        }

        // 4. 点赞（apihub/api/upvotePost，K2 类型1 签名 + client_type 2）
        var upBody = $"{{\"post_id\":\"{postId}\",\"is_cancel\":false}}";
        var upHeaders = ReqHeader(taskCookie, "POST", upBody, MiHoYoHttp.K2Salt, isSign: true, clientType: 2);
        var up = await MiHoYoHttp.SendWithHeadersAsync("POST", $"{BbsHost}/apihub/api/upvotePost", upBody, upHeaders);
        if (IsLoginExpired(up.Retcode))
        {
            log.Add(new SignLogEntry { Text = "米游币任务：账号登录状态已失效，请到「账号」页重新登录", IsError = true });
            return (false, "登录状态失效", log);
        }
        if (up.Retcode == 0)
            log.Add(new SignLogEntry { Text = "点赞成功" });
        else
        {
            allOk = false;
            log.Add(new SignLogEntry { Text = $"点赞：{up.Message}（{up.Retcode}）", IsError = true });
        }

        // 5. 分享（apihub/api/getShareConf，K2 类型1 签名 + client_type 2；获取分享配置即计入分享任务）
        var shareHeaders = ReqHeader(taskCookie, "GET", "", MiHoYoHttp.K2Salt, isSign: true, clientType: 2);
        var share = await MiHoYoHttp.SendWithHeadersAsync("GET",
            $"{BbsHost}/apihub/api/getShareConf?entity_id={postId}&entity_type=1", null, shareHeaders);
        if (IsLoginExpired(share.Retcode))
        {
            log.Add(new SignLogEntry { Text = "米游币任务：账号登录状态已失效，请到「账号」页重新登录", IsError = true });
            return (false, "登录状态失效", log);
        }
        if (share.Retcode == 0)
            log.Add(new SignLogEntry { Text = "分享成功" });
        else
        {
            allOk = false;
            log.Add(new SignLogEntry { Text = $"分享：{share.Message}（{share.Retcode}）", IsError = true });
        }

        return (allOk, allOk ? "完成" : "部分失败", log);
    }

    /// <summary>从完整 cookie 提取指定字段构造子 cookie（TeyvatGuide 各任务接口只带所需子集）</summary>
    private static string BuildSubCookie(string cookie, params string[] keys)
    {
        var parts = new List<string>();
        foreach (var seg in cookie.Split(';'))
        {
            var kv = seg.Trim().Split('=');
            if (kv.Length < 2) continue;
            foreach (var k in keys)
                if (kv[0].Trim() == k) { parts.Add($"{kv[0].Trim()}={string.Join("=", kv.Skip(1))}"); break; }
        }
        return string.Join("; ", parts);
    }

    /// <summary>cookie 序列化（TeyvatGuide transCookie：按 key 排序 + "k=v;" 无空格）</summary>
    private static string TransCookie(string cookie)
    {
        var dict = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var seg in cookie.Split(';'))
        {
            var seg2 = seg.Trim();
            if (seg2.Length == 0) continue;
            var eq = seg2.IndexOf('=');
            if (eq <= 0) continue;
            dict[seg2[..eq].Trim()] = seg2[(eq + 1)..].Trim();
        }
        var sb = new System.Text.StringBuilder();
        foreach (var kv in dict) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
        return sb.ToString();
    }

    /// <summary>
    /// 构建完整请求头（对齐 TeyvatGuide getRequestHeader 全套 + 可选覆盖）：
    /// UA / x-rpc-app_version / x-rpc-client_type / x-requested-with / referer / x-rpc-device_id / x-rpc-device_fp / DS / cookie。
    /// isSign=true 用类型1 DS（点赞/分享/看帖）；false 用类型2 DS（签到 POST）。
    /// </summary>
    private static Dictionary<string, string> ReqHeader(string cookie, string method, string data,
        string salt, bool isSign, bool mobileUa = false, int clientType = 5, string referer = "https://webstatic.mihoyo.com")
    {
        return new Dictionary<string, string>
        {
            ["user-agent"] = mobileUa ? MobileUa : BbsUa,
            ["x-rpc-app_version"] = "2.115.0",
            ["x-rpc-client_type"] = clientType.ToString(),
            ["x-requested-with"] = "com.mihoyo.hyperion",
            ["referer"] = referer,
            ["x-rpc-device_id"] = SettingsService.Settings.DeviceId,
            ["x-rpc-device_fp"] = MiHoYoHttp.DeviceFp,
            ["DS"] = isSign ? MiHoYoHttp.GetDsType1(salt) : MiHoYoHttp.GetDsType2(salt, data),
            ["cookie"] = TransCookie(cookie),
        };
    }

    /// <summary>对某个账号执行“全部游戏签到 + 米游币每日任务”</summary>
    public static async Task<List<SignLogEntry>> SignAllForAccountAsync(string cookie)
    {
        var log = new List<SignLogEntry>();
        foreach (var game in Games)
        {
            var (_, _, gameLog) = await SignGameAsync(cookie, game);
            log.AddRange(gameLog);
        }
        var (_, _, bbsLog) = await MiyousheDailyAsync(cookie);
        log.AddRange(bbsLog);
        return log;
    }
}
