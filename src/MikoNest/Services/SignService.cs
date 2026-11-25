using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MiNest.Models;

namespace MiNest.Services;

/// <summary>
/// 每日签到 / 米游币每日任务，接口与签名方式对齐 TeyvatGuide「实用脚本」页
/// （src/request/lunaReq.ts、apiHubReq.ts、takumiReq.ts、utils/network/getRequestHeader.ts）。
/// </summary>
public static class SignService
{
    // ---------- 游戏签到配置（act_id / host 与 TeyvatGuide getActConf 一致） ----------
    // Icon：Assets/game 下的游戏小图标文件名；CheckinEnabled=false 仅作展示，不参与签到。
    public record GameCheckin(string Key, string Name, string ActId, string Host, string GameBiz,
        string Icon = "", bool CheckinEnabled = true);

    public static readonly GameCheckin[] Games =
    {
        new("genshin", "原神", "e202311201442471", "hk4e", "hk4e_cn", "game_genshin.png"),
        new("starrail", "星穹铁道", "e202304121516551", "hkrpg", "hkrpg_cn", "game_starrail.png"),
        new("zzz", "绝区零", "e202406242138391", "zzz", "nap_cn", "game_zzz.png"),
        new("bh3", "崩坏3", "e202306201626331", "bh3", "bh3_cn", "game_bh3.jpg"),
        new("bh2", "崩坏学园2", "e202203291431091", "bh2", "bh2_cn", "game_bh2.jpg"),
        new("nxx", "未定事件簿", "e202202251749321", "nxx", "nxx_cn", "game_nxx.jpg"),
        // 以下两款暂无签到配置，仅用于图标展示
        new("miliastra", "星布谷地", "", "", "", "game_miliastra.png", false),
        new("bhyj", "崩坏：因缘精灵", "", "", "", "game_bhyj.png", false),
    };

    public static GameCheckin? Get(string key) => Array.Find(Games, g => g.Key == key);

    public static GameCheckin? GetByBiz(string gameBiz) => Array.Find(Games, g => g.GameBiz == gameBiz);

    // ---------- 常量 ----------
    public const int DefaultGid = 2;                       // 米游币任务默认板块（原神）
    private const string BbsApi = "https://bbs-api.miyoushe.com";
    private const string TakumiApi = "https://api-takumi.mihoyo.com";
    private const string PassportApi = "https://passport-api.mihoyo.com";
    private const string LunaApi = "https://api-takumi.mihoyo.com/event/luna";
    private const string AppVersion = "2.119.0";
    private const string MobileUa = "Mozilla/5.0 (Linux; Android 12) Mobile miHoYoBBS/2.119.0";
    private const string PcUa = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) miHoYoBBS/2.119.0";
    private const string PostId = "74971515";              // 官方「米游币任务体系调整说明」公告帖

    // ---------- 结果模型 ----------

    /// <summary>一条执行日志</summary>
    public class LogLine
    {
        public string Text { get; init; } = "";
        public string Level { get; init; } = "INFO";
        public LogLine(string text, string level = "INFO") { Text = text; Level = level; }
    }

    public record RolesResult(List<SignRole> Roles, string? Error, int Retcode, bool LoginExpired);

    public record SignStateResult(bool? Signed, string Message, int Retcode, bool LoginExpired);

    public record ActionResult(bool Ok, string Message, int Retcode, List<LogLine> Log);

    public class MissionItem
    {
        public int Id;
        public string Key = "";
        public string Name = "";
        public int Process;
        public int Total = 1;
        public bool Done;
        public int Reward;
        public int CycleTimes;
        public bool HasCycle;
    }

    public class MissionSnapshot
    {
        public List<MissionItem> Items = new();
        public int TodayPoints;
        public int TotalPoints;
        public int UserPoints;
    }

    // ---------- 登录态判断 ----------

    public static bool IsLoginExpired(int retcode)
        => retcode == -100 || retcode == -2000 || retcode == 10001 || retcode == 1004;

    public static bool HasStoken(string cookie) => !string.IsNullOrEmpty(CookieValue(cookie, "stoken"));
    public static bool HasCookieToken(string cookie) => !string.IsNullOrEmpty(CookieValue(cookie, "cookie_token"));

    public static string CookieValue(string cookie, string key)
    {
        if (string.IsNullOrEmpty(cookie)) return "";
        foreach (var seg in cookie.Split(';'))
        {
            var s = seg.Trim();
            if (s.Length == 0) continue;
            var eq = s.IndexOf('=');
            if (eq <= 0) continue;
            if (s[..eq].Trim() == key) return s[(eq + 1)..].Trim();
        }
        return "";
    }

    // ---------- 角色 ----------

    /// <summary>拉取该账号绑定过的全部游戏角色，依次尝试两条参考实现链路并合并去重：
    /// 1) TeyvatGuide cookie_token 链路：api-takumi getUserGameRolesByCookie（X4 DS2，client_type 5）；
    /// 2) Starward CookieToken 链路：passport-api getUserGameRolesByCookieToken，按 game_biz 循环，完整 Cookie、不签 DS，
    ///    兼容扫码 / 短信登录后只带回 stoken、cookie_token 暂未换取成功的账号。
    /// 仅当两条链路都返回登录失效类 retcode 时才提示凭证过期；其余错误如实透出 message/retcode，不再笼统误判。</summary>
    public static async Task<RolesResult> GetAllRolesAsync(string cookie)
    {
        // 先用 stoken 走 V2 换票补齐缺失的 cookie_token / ltoken，避免刚登录或旧账号误判过期。
        try
        {
            var refreshed = await AccountService.RefreshCookieAsync(cookie);
            if (!string.IsNullOrEmpty(refreshed)) cookie = refreshed;
        }
        catch { }

        var roles = new List<SignRole>();
        var seen = new HashSet<string>();
        var lastRetcode = 0;
        var lastMessage = "";
        var sawExpired = false;

        // 1) takumi 角色链路（对齐 MikoMiko GetGameRolesAsync）：完整 Cookie、不带 DS，逐款游戏循环
        var roleHeaders = BuildTakumiRoleHeader(cookie);
        foreach (var game in Games.Where(g => g.CheckinEnabled && !string.IsNullOrEmpty(g.GameBiz)))
        {
            var api = await MiHoYoHttp.SendWithHeadersAsync("GET",
                $"{TakumiApi}/binding/api/getUserGameRolesByCookie?game_biz={game.GameBiz}",
                null, roleHeaders);
            lastRetcode = api.Retcode;
            lastMessage = api.Message;
            if (api.Retcode == 0 && api.Data is { } d)
                MergeRoles(roles, seen, ParseRoleList(d));
            else if (IsLoginExpired(api.Retcode)) sawExpired = true;
        }

        // 2) Starward CookieToken 链路（getUserGameRolesByCookieToken，完整 Cookie、无 DS，逐款游戏循环）
        var tokenHeaders = BuildCookieTokenHeader(cookie);
        foreach (var game in Games.Where(g => g.CheckinEnabled && !string.IsNullOrEmpty(g.GameBiz)))
        {
            var api = await MiHoYoHttp.SendWithHeadersAsync("GET",
                $"{PassportApi}/binding/api/getUserGameRolesByCookieToken?game_biz={game.GameBiz}",
                null, tokenHeaders);
            if (api.Retcode == 0 && api.Data is { } d)
            {
                MergeRoles(roles, seen, ParseRoleList(d));
            }
            else
            {
                lastRetcode = api.Retcode;
                lastMessage = api.Message;
                if (IsLoginExpired(api.Retcode)) sawExpired = true;
            }
        }

        if (roles.Count > 0)
            return new RolesResult(roles, null, 0, false);

        // 不把账号标记为全局失效：LoginExpired 保持 false，仅在错误文案上区分过期 / 其他原因
        if (sawExpired)
            return new RolesResult(roles, "暂无法获取游戏角色：登录凭证可能已过期，请到「账号」页重新登录", lastRetcode, false);

        var err = string.IsNullOrEmpty(lastMessage) ? "未获取到绑定游戏角色" : $"{lastMessage}（{lastRetcode}）";
        return new RolesResult(roles, err, lastRetcode, false);
    }

    /// <summary>按 game_biz+uid 合并去重角色列表。</summary>
    private static void MergeRoles(List<SignRole> roles, HashSet<string> seen, List<SignRole> incoming)
    {
        foreach (var r in incoming)
        {
            var key = $"{r.GameBiz}|{r.Uid}";
            if (string.IsNullOrEmpty(r.Uid) || !seen.Add(key)) continue;
            roles.Add(r);
        }
    }

    private static List<SignRole> ParseRoleList(JsonElement data)
    {
        var roles = new List<SignRole>();
        if (!data.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array) return roles;
        foreach (var el in list.EnumerateArray())
        {
            roles.Add(new SignRole
            {
                Uid = el.GetProp("game_uid"),
                Nickname = el.GetProp("nickname"),
                Region = el.GetProp("region"),
                GameBiz = el.GetProp("game_biz"),
                Level = el.GetProp("level"),
                RegionName = el.TryGetProperty("region_name", out var rn) && rn.ValueKind == JsonValueKind.String
                    ? rn.GetString() ?? ""
                    : "",
            });
        }
        return roles;
    }

    // ---------- 游戏签到 ----------

    /// <summary>兼容「抽卡记录」等页面：按 game_biz 过滤角色，返回 (roles, error)</summary>
    public static async Task<(List<SignRole> roles, string? error)> GetRolesAsync(string cookie, string gameBiz)
    {
        var all = await GetAllRolesAsync(cookie);
        if (all.Error != null) return (new List<SignRole>(), all.Error);
        var roles = string.IsNullOrEmpty(gameBiz)
            ? all.Roles
            : all.Roles.Where(r => r.GameBiz == gameBiz).ToList();
        return (roles, null);
    }

    public static async Task<SignStateResult> GetSignStateAsync(string cookie, GameCheckin game, SignRole role)
    {
        if (!game.CheckinEnabled || string.IsNullOrEmpty(game.ActId))
            return new SignStateResult(null, "该游戏暂不支持自动签到", -1, false);

        var query = SortedQuery(new()
        {
            ["lang"] = "zh-cn",
            ["act_id"] = game.ActId,
            ["region"] = role.Region,
            ["uid"] = role.Uid,
        });
        var url = $"{LunaApi}/{game.Host}/info?{query}";
        // 对齐 TeyvatGuide lunaReq.getLunaInfo：Cookie 仅 cookie_token+account_id，不带 stoken，无 DS
        var ck = SubCookie(cookie, "account_id", "cookie_token");
        if (string.IsNullOrEmpty(CookieValue(ck, "cookie_token")))
            return new SignStateResult(null, "缺少 cookie_token 凭证，请到「账号」页重新登录", -1, false);
        var headers = new Dictionary<string, string>
        {
            ["user-agent"] = MobileUa,
            ["referer"] = "https://act.mihoyo.com",
            ["cookie"] = TransCookie(ck),
            ["x-rpc-signgame"] = game.Host,
        };
        var api = await MiHoYoHttp.SendWithHeadersAsync("GET", url, null, headers);
        // -100/-2000 等只代表该签到接口未通过，不把账号判定为全局登录失效；保留原始 retcode/message 便于定位
        if (IsLoginExpired(api.Retcode))
            return new SignStateResult(null, $"签到接口暂不可用（{api.Message}，{api.Retcode}）", api.Retcode, false);
        if (api.Retcode != 0)
            return new SignStateResult(null, $"{api.Message}（{api.Retcode}）", api.Retcode, false);

        var signed = false;
        if (api.Data is { } data && data.TryGetProperty("is_sign", out var s)) signed = AsBool(s);
        return new SignStateResult(signed, "", 0, false);
    }

    /// <summary>单角色签到（luna sign，X6 类型 2 DS）</summary>
    public static async Task<ActionResult> SignRoleAsync(string cookie, GameCheckin game, SignRole role)
    {
        var log = new List<LogLine>();
        if (!game.CheckinEnabled || string.IsNullOrEmpty(game.ActId))
        {
            log.Add(new LogLine($"{game.Name} 暂不支持自动签到", "WARN"));
            return new ActionResult(false, "暂不支持自动签到", -1, log);
        }

        var ck = SubCookie(cookie, "account_id", "cookie_token");
        if (string.IsNullOrEmpty(CookieValue(ck, "cookie_token")))
        {
            log.Add(new LogLine($"{game.Name}-{RegionLabel(role)}-{role.Uid} 缺少 cookie_token 凭证，请到「账号」页重新登录", "WARN"));
            return new ActionResult(false, "缺少 cookie_token 凭证", -1, log);
        }

        // 字段顺序必须与发送的 JSON 完全一致，否则 DS 校验失败
        var body = JsonSerializer.Serialize(new SignBody("zh-cn", game.ActId, role.Region, role.Uid));
        // 对齐 TeyvatGuide lunaReq.lunaSign：X6 类型 2 DS，client_type 2，PC UA / webstatic referer
        var headers = BuildHeader(ck,
            "POST", body, MiHoYoHttp.X6Salt, isSign: false,
            clientType: 2, ua: PcUa, referer: "https://webstatic.mihoyo.com");
        headers["x-rpc-signgame"] = game.Host;

        var api = await MiHoYoHttp.SendWithHeadersAsync("POST", $"{LunaApi}/{game.Host}/sign", body, headers);
        var label = $"{game.Name}-{RegionLabel(role)}-{role.Uid}";

        if (api.Retcode == 0 && api.Data is { } data)
        {
            var success = data.TryGetProperty("success", out var sc) && AsInt(sc) == 0;
            if (success)
            {
                log.Add(new LogLine($"{label} 签到成功"));
                return new ActionResult(true, "签到成功", 0, log);
            }
            var risk = data.TryGetProperty("is_risk", out var rk) && AsBool(rk);
            var msg = risk ? "触发风险验证（请在米游社 App 手动签到）" : "签到未生效";
            log.Add(new LogLine($"{label} {msg}", "ERROR"));
            return new ActionResult(false, msg, 0, log);
        }
        if (api.Retcode == -5003)
        {
            log.Add(new LogLine($"{label} 今日已签到"));
            return new ActionResult(true, "今日已签到", api.Retcode, log);
        }
        if (api.Retcode == 1034)
        {
            log.Add(new LogLine($"{label} 触发验证码（1034），请到米游社 App 手动签到", "WARN"));
            return new ActionResult(false, "触发验证码", api.Retcode, log);
        }
        if (IsLoginExpired(api.Retcode))
        {
            var msg = $"签到接口暂不可用（{api.Message}，{api.Retcode}）";
            log.Add(new LogLine($"{label} {msg}", "WARN"));
            return new ActionResult(false, msg, api.Retcode, log);
        }
        log.Add(new LogLine($"{label} {api.Message}（{api.Retcode}）", "ERROR"));
        return new ActionResult(false, api.Message, api.Retcode, log);
    }

    // ---------- 米游币任务 ----------

    /// <summary>拉取米游币任务列表 + 完成状态（getMissions 列表 / wapi getUserMissionsState 状态），口径对齐 TeyvatGuide。
    /// 状态拉取失败不影响任务列表展示，也不判定登录失效。</summary>
    public static async Task<(MissionSnapshot? snapshot, string? error, bool loginExpired)> GetMissionsAsync(string cookie)
    {
        var q = SortedQuery(new() { ["point_sn"] = "myb" });

        // 任务列表 getMissions：对齐 TeyvatGuide，cookie_token+account_id，X4 类型 2 DS
        var listHeaders = BuildHeader(SubCookie(cookie, "cookie_token", "account_id"),
            "GET", q, MiHoYoHttp.X4Salt, isSign: false);
        var listApi = await MiHoYoHttp.SendWithHeadersAsync("GET", $"{BbsApi}/apihub/wapi/getMissions?{q}", null, listHeaders);
        if (listApi.Retcode != 0 && !IsLoginExpired(listApi.Retcode))
            return (null, $"获取任务列表失败：{listApi.Message}（{listApi.Retcode}）", false);

        var items = new List<MissionItem>();
        if (listApi.Data is { } ld)
        {
            foreach (var arrName in new[] { "missions", "more_missions" })
            {
                if (!ld.TryGetProperty(arrName, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
                foreach (var el in arr.EnumerateArray())
                {
                    items.Add(new MissionItem
                    {
                        Id = AsInt(el, "id"),
                        Key = el.GetProp("mission_key"),
                        Name = el.GetProp("name"),
                        Total = Math.Max(1, AsInt(el, "threshold")),
                        Reward = AsInt(el, "points"),
                        CycleTimes = AsInt(el, "continuous_cycle_times"),
                        HasCycle = AsInt(el, "continuous_cycle_times") > 0,
                    });
                }
            }
        }

        // 任务状态：对齐 TeyvatGuide apiHubReq.getUserMissionsState，仅 wapi 链路，X4 类型 2 DS，cookie_token+account_id
        JsonElement? sd = null;
        var ck = SubCookie(cookie, "cookie_token", "account_id");
        if (!string.IsNullOrEmpty(CookieValue(ck, "cookie_token")))
        {
            var stateHeaders = BuildHeader(ck, "GET", q, MiHoYoHttp.X4Salt, isSign: false);
            var stateApi = await MiHoYoHttp.SendWithHeadersAsync("GET", $"{BbsApi}/apihub/wapi/getUserMissionsState?{q}", null, stateHeaders);
            if (stateApi.Retcode == 0 && stateApi.Data is { } sdEl2) sd = sdEl2;
        }

        var snap = new MissionSnapshot { Items = items };
        if (sd is { } stateData)
        {
            snap.TodayPoints = AsInt(stateData, "already_received_points");
            snap.TotalPoints = AsInt(stateData, "today_total_points");
            snap.UserPoints = AsInt(stateData, "total_points");
            if (stateData.TryGetProperty("states", out var states) && states.ValueKind == JsonValueKind.Array)
            {
                foreach (var st in states.EnumerateArray())
                {
                    var id = AsInt(st, "mission_id");
                    var item = items.FirstOrDefault(i => i.Id == id);
                    if (item == null) continue;
                    item.Process = Math.Min(item.Total, AsInt(st, "happened_times"));
                    item.Done = AsInt(st, "process") == 1;
                }
            }
        }
        // 状态拉取失败不影响任务列表展示，也不判定登录失效
        return (snap, null, false);
    }

    /// <summary>米游社社区打卡（apihub/app/api/signIn，X6 类型 2 DS + 移动端 UA）</summary>
    public static async Task<ActionResult> BbsCheckInAsync(string cookie, int gid = DefaultGid)
    {
        var log = new List<LogLine>();
        if (!HasStoken(cookie))
        {
            log.Add(new LogLine("登录态缺少 stoken，无法打卡。请到「账号」页重新扫码 / Cookie 登录", "ERROR"));
            return new ActionResult(false, "缺少 stoken", -1, log);
        }
        var ck = SubCookie(cookie, "stoken", "stuid", "mid");
        var body = $"{{\"gids\":{gid}}}";
        var headers = BuildHeader(ck, "POST", body, MiHoYoHttp.X6Salt, isSign: false,
            clientType: 2, ua: MobileUa, referer: "https://app.mihoyo.com");
        var api = await MiHoYoHttp.SendWithHeadersAsync("POST", $"{BbsApi}/apihub/app/api/signIn", body, headers);

        if (api.Retcode == 0)
        {
            log.Add(new LogLine("米游社打卡成功"));
            return new ActionResult(true, "打卡成功", 0, log);
        }
        if (api.Retcode == -5003 || api.Retcode == 1008)
        {
            log.Add(new LogLine("米游社今日已打卡"));
            return new ActionResult(true, "今日已打卡", api.Retcode, log);
        }
        if (api.Retcode == 1034)
        {
            log.Add(new LogLine("打卡触发验证码（1034），请到米游社 App 手动打卡", "WARN"));
            return new ActionResult(false, "触发验证码", api.Retcode, log);
        }
        if (IsLoginExpired(api.Retcode))
        {
            log.Add(new LogLine("登录状态已失效，请重新登录", "ERROR"));
            return new ActionResult(false, "登录状态已失效", api.Retcode, log);
        }
        log.Add(new LogLine($"打卡失败：{api.Message}（{api.Retcode}）", "ERROR"));
        return new ActionResult(false, api.Message, api.Retcode, log);
    }

    /// <summary>浏览帖子 / 点赞 / 分享，用于完成米游币浏览类任务（K2 类型 1 签名）</summary>
    public static async Task<List<LogLine>> DoCommunityActionsAsync(string cookie)
    {
        var log = new List<LogLine>();
        var ck = SubCookie(cookie, "cookie_token", "account_id");
        if (string.IsNullOrEmpty(CookieValue(ck, "cookie_token")))
            return log;

        // 看帖（read=1）
        var readHeaders = BuildHeader(ck, "GET", "", MiHoYoHttp.K2Salt, isSign: true, clientType: 2);
        var readApi = await MiHoYoHttp.SendWithHeadersAsync("GET",
            $"{BbsApi}/post/wapi/getPostFull?post_id={PostId}&read=1", null, readHeaders);
        if (readApi.Retcode == 0) log.Add(new LogLine("浏览帖子成功"));
        else if (!IsLoginExpired(readApi.Retcode)) log.Add(new LogLine($"浏览帖子：{readApi.Message}（{readApi.Retcode}）", "WARN"));

        // 点赞
        var upBody = $"{{\"is_cancel\":false,\"post_id\":\"{PostId}\"}}";
        var upHeaders = BuildHeader(ck, "POST", upBody, MiHoYoHttp.K2Salt, isSign: true, clientType: 2);
        var upApi = await MiHoYoHttp.SendWithHeadersAsync("POST", $"{BbsApi}/apihub/api/upvotePost", upBody, upHeaders);
        if (upApi.Retcode == 0) log.Add(new LogLine("点赞成功"));
        else if (!IsLoginExpired(upApi.Retcode)) log.Add(new LogLine($"点赞：{upApi.Message}（{upApi.Retcode}）", "WARN"));

        // 分享配置（计入分享任务）
        var shareHeaders = BuildHeader(ck, "GET", "", MiHoYoHttp.K2Salt, isSign: true, clientType: 2);
        var shareApi = await MiHoYoHttp.SendWithHeadersAsync("GET",
            $"{BbsApi}/apihub/api/getShareConf?entity_id={PostId}&entity_type=1", null, shareHeaders);
        if (shareApi.Retcode == 0) log.Add(new LogLine("分享成功"));
        else if (!IsLoginExpired(shareApi.Retcode)) log.Add(new LogLine($"分享：{shareApi.Message}（{shareApi.Retcode}）", "WARN"));

        return log;
    }

    // ---------- 工具 ----------

    public static string RegionLabel(SignRole role)
    {
        if (!string.IsNullOrEmpty(role.RegionName)) return role.RegionName;
        return role.Region switch
        {
            "cn_gf01" => "国服·天空岛",
            "cn_qd01" => "国服·世界树",
            "prod_gf_cn" => "国服",
            "prod_qd_cn" => "国服",
            "cn01" => "国服",
            "pc01" => "国服",
            "prod_cn" => "国服",
            _ => string.IsNullOrEmpty(role.Region) ? "国服" : role.Region,
        };
    }

    private record SignBody(string lang, string act_id, string region, string uid);

    private static bool AsBool(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => string.Equals(el.GetString(), "true", StringComparison.OrdinalIgnoreCase),
        JsonValueKind.Number => el.TryGetInt32(out var n) && n != 0,
        _ => false,
    };

    private static int AsInt(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Number) return el.TryGetInt32(out var n) ? n : 0;
        if (el.ValueKind == JsonValueKind.String) return int.TryParse(el.GetString(), out var n) ? n : 0;
        if (el.ValueKind == JsonValueKind.True) return 1;
        return 0;
    }

    private static int AsInt(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var el) ? AsInt(el) : 0;

    /// <summary>按字典序拼接 query（DS 签名与真实 URL 必须一致）</summary>
    private static string SortedQuery(Dictionary<string, string> param)
        => string.Join("&", param.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                             .Select(kv => $"{kv.Key}={kv.Value}"));

    /// <summary>从完整 cookie 提取指定字段（TeyvatGuide 各接口只带所需子集）</summary>
    private static string SubCookie(string cookie, params string[] keys)
    {
        var dict = new Dictionary<string, string>();
        foreach (var seg in cookie.Split(';'))
        {
            var s = seg.Trim();
            if (s.Length == 0) continue;
            var eq = s.IndexOf('=');
            if (eq <= 0) continue;
            var k = s[..eq].Trim();
            if (keys.Contains(k)) dict[k] = s[(eq + 1)..].Trim();
        }
        return string.Join("; ", dict.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                              .Select(kv => $"{kv.Key}={kv.Value}"));
    }

    /// <summary>cookie 序列化：按 key 字典序 + "k=v;"（TeyvatGuide transCookie）</summary>
    private static string TransCookie(string cookie)
    {
        var dict = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var seg in cookie.Split(';'))
        {
            var s = seg.Trim();
            if (s.Length == 0) continue;
            var eq = s.IndexOf('=');
            if (eq <= 0) continue;
            dict[s[..eq].Trim()] = s[(eq + 1)..].Trim();
        }
        return string.Concat(dict.Select(kv => $"{kv.Key}={kv.Value};"));
    }

    /// <summary>
    /// takumi 角色链路请求头（对齐 MikoMiko GetGameRolesAsync）：完整 Cookie、不签 DS，
    /// PC UA / app_version / client_type 5 / hyperion 标识 / webstatic referer。
    /// </summary>
    private static Dictionary<string, string> BuildTakumiRoleHeader(string cookie)
    {
        return new Dictionary<string, string>
        {
            ["user-agent"] = PcUa,
            ["x-rpc-app_version"] = AppVersion,
            ["x-rpc-client_type"] = "5",
            ["x-requested-with"] = "com.mihoyo.hyperion",
            ["referer"] = "https://webstatic.mihoyo.com",
            ["cookie"] = TransCookie(cookie),
        };
    }

    /// <summary>
    /// CookieToken 链路请求头（对齐 Starward HyperionClient.GetGameRolesAsync）：完整 Cookie、不签 DS，
    /// 仅 UA + Cookie + Referer（act.mihoyo.com），不带 device / client_type 等附加头。
    /// </summary>
    private static Dictionary<string, string> BuildCookieTokenHeader(string cookie)
    {
        return new Dictionary<string, string>
        {
            ["user-agent"] = PcUa,
            ["referer"] = "https://act.mihoyo.com/",
            ["cookie"] = TransCookie(cookie),
        };
    }

    /// <summary>
    /// 构建社区接口请求头（对齐 TeyvatGuide getRequestHeader）：
    /// UA / x-rpc-app_version / x-rpc-client_type / x-requested-with / referer / device_id / device_fp / DS / cookie。
    /// </summary>
    private static Dictionary<string, string> BuildHeader(string cookie, string method, string data,
        string salt, bool isSign, int clientType = 5, string? ua = null,
        string referer = "https://webstatic.mihoyo.com")
    {
        var body = method == "GET" ? "" : data;
        var query = method == "GET" ? data : "";
        return new Dictionary<string, string>
        {
            ["user-agent"] = ua ?? PcUa,
            ["x-rpc-app_version"] = AppVersion,
            ["x-rpc-client_type"] = clientType.ToString(),
            ["x-requested-with"] = "com.mihoyo.hyperion",
            ["referer"] = referer,
            ["x-rpc-device_id"] = SettingsService.Settings.DeviceId,
            ["x-rpc-device_fp"] = MiHoYoHttp.DeviceFp,
            ["DS"] = isSign ? MiHoYoHttp.GetDsType1(salt) : MiHoYoHttp.GetDsType2(salt, body, query),
            ["cookie"] = TransCookie(cookie),
        };
    }
}
