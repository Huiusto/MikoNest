using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MiNest.Services;

/// <summary>实时便笺（每日树脂/体力）——移植自 Starward.Core.GameRecord.DailyNote</summary>
public static class DailyNoteService
{
    // 游戏记录接口的 DS2 盐（国服米游社 HyperionClient），与社区盐不同
    private const string GameDsSalt = "xV8v4Qu54lUKrEYFZkJhB8cuOh9Asafs";
    private const string GameUa = "Mozilla/5.0 (Linux; Android 13; Pixel 5 Build/TQ3A.230901.001; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/118.0.0.0 Mobile Safari/537.36 miHoYoBBS/2.90.1";

    public record GameBiz(string Key, string Name, string Biz, string Region);

    public static readonly GameBiz[] Games =
    {
        new("genshin", "原神", "hk4e_cn", "cn_gf01"),
        new("starrail", "星穹铁道", "hkrpg_cn", "prod_gf_cn"),
        new("zzz", "绝区零", "nap_cn", "prod_gf_cn"),
    };

    /// <summary>统一便笺数据</summary>
    public class Note
    {
        public string Game = "";
        public string GameName = "";
        public string Nickname = "";
        public string Uid = "";
        public string Region = "";
        public int Current;
        public int Max;
        public long RecoverSeconds;
        public int ExpeditionNow;
        public int ExpeditionMax;
        public bool DailyDone;
        public bool DailyDoneUnknown;
        public string Extra = "";
        public string Extra2 = "";
        public bool Ok;
        public string Error = "";
    }

    /// <summary>查询账号在某游戏下的角色（用 ByCookie 接口，兼容扫码/验证码登录的 cookie）</summary>
    public static async Task<List<(string uid, string nickname, string region, string level)>> GetRolesAsync(string cookie, string gameBiz)
    {
        var list = new List<(string, string, string, string)>();
        var api = await MiHoYoHttp.SendAsync("GET",
            $"https://api-takumi.mihoyo.com/binding/api/getUserGameRolesByCookie?game_biz={gameBiz}", cookie);
        if (api.Retcode == 0 && api.Data != null &&
            api.Data.Value.TryGetProperty("list", out var l) && l.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in l.EnumerateArray())
            {
                list.Add((el.GetProp("game_uid"), el.GetProp("nickname"), el.GetProp("region"), el.GetProp("level")));
            }
        }
        return list;
    }

    /// <summary>DS2 签名（Starward GameRecordClient.CreateSecret2：salt+排序后的 query）</summary>
    private static string CreateDs2(string url)
    {
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var r = Random.Shared.Next(100000, 200000).ToString();
        var q = "";
        var parts = url.Split('?');
        if (parts.Length == 2)
            q = string.Join("&", parts[1].Split('&').OrderBy(x => x).ToArray());
        var md5 = MiHoYoHttp.Md5($"salt={GameDsSalt}&t={t}&r={r}&b=&q={q}");
        return $"{t},{r},{md5}";
    }

    /// <summary>获取单个角色的实时便笺</summary>
    public static async Task<Note> GetNoteAsync(string cookie, GameBiz game, string uid, string region)
    {
        var n = new Note
        {
            Game = game.Key,
            GameName = game.Name,
            Uid = uid,
            Region = region,
            DailyDoneUnknown = true,
        };

        try
        {
            string url;
            bool needDs = true;
            string referer;
            if (game.Key == "genshin")
            {
                url = $"https://api-takumi-record.mihoyo.com/game_record/app/genshin/api/dailyNote?server={region}&role_id={uid}";
                referer = "https://webstatic.mihoyo.com/";
            }
            else if (game.Key == "starrail")
            {
                url = $"https://api-takumi-record.mihoyo.com/game_record/app/hkrpg/api/note?server={region}&role_id={uid}";
                referer = "https://webstatic.mihoyo.com/";
            }
            else
            {
                // 绝区零 note 接口不需要 DS 签名
                url = $"https://api-takumi-record.mihoyo.com/event/game_record_zzz/api/zzz/note?server={region}&role_id={uid}";
                referer = "https://act.mihoyo.com/";
                needDs = false;
            }

            var headers = new Dictionary<string, string>
            {
                ["user-agent"] = GameUa,
                ["referer"] = referer,
                ["cookie"] = cookie,
                ["x-rpc-app_version"] = "2.90.1",
                ["x-rpc-client_type"] = "5",
                ["x-rpc-device_id"] = SettingsService.Settings.DeviceId,
                ["x-rpc-device_fp"] = "0000000000000",
            };
            if (needDs)
                headers["ds"] = CreateDs2(url);

            var api = await MiHoYoHttp.SendWithHeadersAsync("GET", url, null, headers);
            if (api.Retcode != 0 || api.Data == null)
            {
                n.Ok = false;
                n.Error = $"{api.Message}（{api.Retcode}）";
                return n;
            }

            var d = api.Data.Value;
            switch (game.Key)
            {
                case "genshin":
                    FillGenshin(n, d);
                    break;
                case "starrail":
                    FillStarRail(n, d);
                    break;
                case "zzz":
                    FillZzz(n, d);
                    break;
            }
            n.Ok = true;
            n.DailyDoneUnknown = false;
        }
        catch (Exception ex)
        {
            n.Ok = false;
            n.Error = $"网络错误：{ex.Message}";
        }
        return n;
    }

    private static void FillGenshin(Note n, JsonElement d)
    {
        n.Current = GetInt(d, "current_resin");
        n.Max = GetInt(d, "max_resin");
        n.RecoverSeconds = GetLong(d, "resin_recovery_time");
        n.ExpeditionNow = GetInt(d, "current_expedition_num");
        n.ExpeditionMax = GetInt(d, "max_expedition_num");
        n.DailyDone = GetInt(d, "finished_task_num") >= GetInt(d, "total_task_num") &&
                      GetInt(d, "total_task_num") > 0;
        n.Extra = $"洞天宝钱 {GetInt(d, "current_home_coin")} / {GetInt(d, "max_home_coin")}";
        var remainDiscount = GetInt(d, "remain_resin_discount_num");
        var discountLimit = GetInt(d, "resin_discount_num_limit");
        if (discountLimit > 0)
            n.Extra2 = $"周本减半 {remainDiscount}/{discountLimit}";
    }

    private static void FillStarRail(Note n, JsonElement d)
    {
        n.Current = GetInt(d, "current_stamina");
        n.Max = GetInt(d, "max_stamina");
        n.RecoverSeconds = GetLong(d, "stamina_recover_time");
        n.ExpeditionNow = GetInt(d, "accepted_epedition_num");
        n.ExpeditionMax = GetInt(d, "total_expedition_num");
        n.DailyDone = GetInt(d, "current_train_score") >= GetInt(d, "max_train_score") &&
                      GetInt(d, "max_train_score") > 0;
        n.Extra = $"每日实训 {GetInt(d, "current_train_score")} / {GetInt(d, "max_train_score")}";
        n.Extra2 = $"后备开拓力 {GetInt(d, "current_reserve_stamina")}";
    }

    private static void FillZzz(Note n, JsonElement d)
    {
        if (d.TryGetProperty("energy", out var e) && e.ValueKind == JsonValueKind.Object &&
            e.TryGetProperty("progress", out var p) && p.ValueKind == JsonValueKind.Object)
        {
            n.Current = GetInt(p, "current");
            n.Max = GetInt(p, "max");
            n.RecoverSeconds = GetLong(e, "restore");
        }
        var cardSign = d.GetProp("card_sign");
        n.DailyDone = cardSign is "CardSign_Done" or "done" or "1";
        if (d.TryGetProperty("vhs_sale", out var v) && v.ValueKind == JsonValueKind.Object)
            n.Extra = v.GetProp("state") == "SaleState_Doing" ? "录像店经营中" : "录像店未经营";
        n.Extra2 = cardSign == "CardSign_Done" ? "刮刮卡已刮" : "刮刮卡未刮";
    }

    private static int GetInt(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.Number or JsonValueKind.String
            ? int.TryParse(v.ToString(), out var i) ? i : 0 : 0;

    private static long GetLong(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.Number or JsonValueKind.String
            ? long.TryParse(v.ToString(), out var i) ? i : 0 : 0;

    /// <summary>内置页预览条目（TeyvatGuide / Starward 页共用）</summary>
    public record PreviewItem(string GameName, string Line, string Status, System.Windows.Media.Brush StatusBrush);

    /// <summary>当前账号各游戏便笺快速预览（尽力而为，失败显示错误码）</summary>
    public static async Task<List<PreviewItem>> GetPreviewItemsAsync()
    {
        var items = new List<PreviewItem>();
        var acc = AccountService.Active;
        if (acc == null || string.IsNullOrEmpty(acc.Cookie))
        {
            items.Add(new PreviewItem("账号", "尚未登录，请前往「账号」页添加米游社账号", "未登录",
                System.Windows.Media.Brushes.Gray));
            return items;
        }

        items.Add(new PreviewItem("账号", acc.DisplayName, "已登录",
            System.Windows.Media.Brushes.SeaGreen));

        foreach (var g in Games)
        {
            try
            {
                var roles = await GetRolesAsync(acc.Cookie, g.Biz);
                if (roles.Count == 0)
                {
                    items.Add(new PreviewItem(g.Name, "未绑定角色", "—", System.Windows.Media.Brushes.Gray));
                    continue;
                }
                var (uid, nickname, region, _) = roles[0];
                var note = await GetNoteAsync(acc.Cookie, g, uid, region);
                if (note.Ok)
                {
                    items.Add(new PreviewItem(g.Name, $"{nickname}（UID {uid}）  {note.Current}/{note.Max}",
                        "正常", System.Windows.Media.Brushes.SeaGreen));
                }
                else
                {
                    items.Add(new PreviewItem(g.Name, $"{nickname}（UID {uid}）",
                        $"失败 {note.Error}", System.Windows.Media.Brushes.OrangeRed));
                }
            }
            catch
            {
                items.Add(new PreviewItem(g.Name, "获取失败", "—", System.Windows.Media.Brushes.Gray));
            }
        }
        return items;
    }
}
