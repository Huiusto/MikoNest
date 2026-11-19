using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MiNest.Models;

namespace MiNest.Services;

/// <summary>抽卡记录：池定义、authkey、拉取（Starward 同款接口口径）</summary>
public static class GachaService
{
    private const string GenAuthKeyUrl = "https://api-takumi.mihoyo.com/binding/api/genAuthKey";
    private const string BbsUa = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) miHoYoBBS/2.115.0";
    private const string GachaUa = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) miHoYoBBS/2.115.0";

    /// <summary>各游戏卡池定义（UIGF / SRGF 通用编号）</summary>
    public static readonly Dictionary<string, GachaPoolDef[]> Pools = new()
    {
        ["genshin"] = new[]
        {
            new GachaPoolDef("302", "常驻祈愿"),
            new GachaPoolDef("200", "角色活动祈愿"),
            new GachaPoolDef("301", "武器活动祈愿"),
            new GachaPoolDef("500", "集录祈愿"),
            new GachaPoolDef("100", "新手祈愿"),
        },
        ["starrail"] = new[]
        {
            new GachaPoolDef("1", "群星跃迁"),
            new GachaPoolDef("11", "角色活动跃迁"),
            new GachaPoolDef("12", "光锥活动跃迁"),
            new GachaPoolDef("21", "角色联动跃迁"),
            new GachaPoolDef("2", "新手跃迁"),
        },
        ["zzz"] = new[]
        {
            new GachaPoolDef("2", "独家频段"),
            new GachaPoolDef("3", "音擎频段"),
            new GachaPoolDef("1", "常驻频段"),
            new GachaPoolDef("5", "邦布频段"),
        },
        ["bh3"] = new[]
        {
            new GachaPoolDef("1", "标配补给"),
            new GachaPoolDef("2", "精准补给"),
            new GachaPoolDef("3", "扩充补给"),
            new GachaPoolDef("4", "装备补给"),
            new GachaPoolDef("5", "人偶补给"),
        },
    };

    public static string PoolName(string gameKey, string code)
    {
        var list = Pools.TryGetValue(gameKey, out var p) ? p : Array.Empty<GachaPoolDef>();
        return list.FirstOrDefault(x => x.Code == code)?.Name ?? $"卡池 {code}";
    }

    /// <summary>确保链接带查询参数</summary>
    public static string NormalizeUrl(string url)
        => url.Contains('?') ? url : url + "?";

    /// <summary>把游戏缓存里的抽卡页面 URL 换成 getGachaLog API 地址（移植 Starward GetGachaUrlPrefix）</summary>
    public static string ToApiUrl(string url, string gameKey)
    {
        if (string.IsNullOrEmpty(url)) return url;

        var api = gameKey switch
        {
            "genshin" => "https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog",
            "starrail" => "https://public-operation-hkrpg.mihoyo.com/common/hkrpg_gacha_record/api/getGachaLog",
            "zzz" => "https://public-operation-nap.mihoyo.com/common/gacha_record/api/getGachaLog",
            _ => null,
        };
        if (api == null) return url;

        var q = url.IndexOf('?');
        if (q < 0) return url;
        var auth = url[q..].Replace("#/log", "");
        var result = api + auth;

        var biz = gameKey switch
        {
            "genshin" => "hk4e_cn",
            "starrail" => "hkrpg_cn",
            "zzz" => "nap_cn",
            _ => null,
        };
        if (biz != null && !auth.Contains("game_biz="))
            result += "&game_biz=" + biz;
        return result;
    }

    /// <summary>用米游社登录态生成抽卡 authkey（部分网络环境会被风控拦截，失败时提示改用缓存链接）</summary>
    public static async Task<(bool ok, string message, string? url)> GenAuthKeyAsync(
        string cookie, string gameBiz, string region, string uid)
    {
        var mid = ExtractCookie(cookie, "account_mid_v2");
        if (string.IsNullOrEmpty(mid)) mid = ExtractCookie(cookie, "ltmid_v2");
        if (string.IsNullOrEmpty(mid)) mid = ExtractCookie(cookie, "mid");
        if (string.IsNullOrEmpty(mid)) mid = ExtractCookie(cookie, "stuid");
        if (string.IsNullOrEmpty(mid)) mid = ExtractCookie(cookie, "account_id_v2");
        if (string.IsNullOrEmpty(mid)) mid = ExtractCookie(cookie, "ltuid_v2");
        if (string.IsNullOrEmpty(mid)) mid = ExtractCookie(cookie, "account_id");
        if (string.IsNullOrEmpty(mid))
            return (false, "Cookie 缺少 mid/stuid（请重新登录）", null);

        var body = JsonSerializer.Serialize(new
        {
            auth_appid = "webview_gacha",
            authkey_type = 2,
            game_biz = gameBiz,
            mid,
            region,
            uid,
        });

        var headers = new Dictionary<string, string>
        {
            ["user-agent"] = BbsUa,
            ["referer"] = "https://webstatic.mihoyo.com",
            ["cookie"] = cookie,
            ["x-rpc-app_version"] = "2.115.0",
            ["x-rpc-client_type"] = "5",
        };
        var api = await MiHoYoHttp.SendWithHeadersAsync("POST", GenAuthKeyUrl, body, headers);
        if (api.Retcode != 0 || api.Data == null)
            return (false, $"{api.Message}（{api.Retcode}）", null);

        var authkey = api.Data.Value.GetProp("authkey");
        if (string.IsNullOrEmpty(authkey))
            return (false, "响应中未找到 authkey", null);

        var ver = api.Data.Value.GetProp("authkey_ver");
        var signType = api.Data.Value.GetProp("sign_type");
        if (string.IsNullOrEmpty(ver)) ver = "1";
        if (string.IsNullOrEmpty(signType)) signType = "2";

        var apiPrefix = gameBiz switch
        {
            "hk4e_cn" => "https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog",
            "hkrpg_cn" => "https://public-operation-hkrpg.mihoyo.com/common/hkrpg_gacha_record/api/getGachaLog",
            "nap_cn" => "https://public-operation-nap.mihoyo.com/common/gacha_record/api/getGachaLog",
            _ => "https://api-takumi.mihoyo.com/gacha_record/api/getGachaLog",
        };
        var url = apiPrefix +
                  $"?authkey_ver={ver}&sign_type={signType}&auth_appid=webview_gacha&init_type=301" +
                  $"&lang=zh-cn&authkey={Uri.EscapeDataString(authkey)}&game_biz={gameBiz}&region={region}&uid={uid}";
        return (true, "authkey 生成成功", url);
    }

    private static string ExtractCookie(string cookie, string name)
    {
        foreach (var part in cookie.Split(';'))
        {
            var kv = part.Trim().Split('=');
            if (kv.Length == 2 && kv[0].Trim() == name)
                return kv[1].Trim();
        }
        return "";
    }

    /// <summary>拉取单个卡池全部记录</summary>
    public static async Task<(bool ok, string message, List<GachaItem> items)> FetchPoolAsync(
        string baseUrl, string gachaType, string? gameKey = null)
    {
        var list = new List<GachaItem>();
        var baseUri = new Uri(NormalizeUrl(baseUrl));
        var page = 1;
        var endId = "0";

        try
        {
            while (page < 100)
            {
                await Task.Delay(Random.Shared.Next(200, 300));

                var query = System.Web.HttpUtility.ParseQueryString(baseUri.Query);
                query["gacha_type"] = gachaType;
                query["page"] = page.ToString();
                query["size"] = "20";
                query["end_id"] = endId;
                query["lang"] = "zh-cn";

                var url = baseUri.GetLeftPart(UriPartial.Path) + "?" + query.ToString();
                if (gameKey == "starrail" && gachaType is "11" or "12" or "21")
                    url = url.Replace("/getGachaLog", "/getLdGachaLog");

                var api = await MiHoYoHttp.GetPlainAsync(url, userAgent: GachaUa);
                if (!api.Ok || api.Retcode != 0)
                {
                    var friendly = api.Retcode is -1 or -100 or -101 or -109 or -110
                        ? "authkey 已过期（游戏缓存里的链接约 24 小时失效）：请先在游戏内打开一次「抽卡记录」页面，再回来点「更新记录」"
                        : $"{api.Message}（{api.Retcode}）";
                    return (false, friendly, list);
                }

                if (api.Data == null ||
                    !api.Data.Value.TryGetProperty("list", out var arr) ||
                    arr.ValueKind != JsonValueKind.Array)
                    break;

                var pageItems = new List<GachaItem>();
                foreach (var el in arr.EnumerateArray())
                {
                    pageItems.Add(new GachaItem
                    {
                        Uid = el.GetProp("uid"),
                        GachaType = el.GetProp("gacha_type"),
                        ItemId = el.GetProp("item_id"),
                        Name = el.GetProp("name"),
                        RankType = el.GetProp("rank_type"),
                        ItemType = el.GetProp("item_type"),
                        Time = el.GetProp("time"),
                        Id = el.GetProp("id"),
                    });
                }

                if (pageItems.Count == 0) break;
                list.AddRange(pageItems);
                endId = pageItems[^1].Id;
                if (pageItems.Count < 20) break;
                page++;
            }

            return (true, $"共 {list.Count} 条", list);
        }
        catch (Exception ex)
        {
            return (false, $"网络错误：{ex.Message}", list);
        }
    }

    /// <summary>拉取指定游戏全部卡池记录（多账号登录态下同一 authkey 只能取对应 UID）</summary>
    public static async Task<(bool ok, string message, List<GachaItem> items)> FetchAllAsync(string baseUrl, string gameKey)
    {
        var all = new List<GachaItem>();
        var pools = Pools.TryGetValue(gameKey, out var p) ? p : Array.Empty<GachaPoolDef>();
        if (pools.Length == 0)
            pools = new[] { new GachaPoolDef("", "默认") };

        foreach (var pool in pools)
        {
            var (ok, msg, items) = await FetchPoolAsync(baseUrl, pool.Code, gameKey);
            if (!ok)
                return (false, $"{pool.Name}：{msg}", all);
            all.AddRange(items);
        }

        return (true, $"共 {all.Count} 条记录", all);
    }
}
