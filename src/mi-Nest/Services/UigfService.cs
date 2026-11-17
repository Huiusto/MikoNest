using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using MiNest.Models;

namespace MiNest.Services;

/// <summary>
/// 抽卡记录格式转换中心（仿 FlyingMouse Format / TeyvatGuide 实用脚本）：
/// 导入：UIGF JSON（v1~v4，含多游戏节）、CSV、官方 API JSON（根数组）、多文件合并去重；
/// 导出：UIGF v4 / UIGF v3 / CSV / 官方 API JSON（Starward 兼容）。
/// </summary>
public static class UigfService
{
    /// <summary>支持导入的抽卡记录文件格式</summary>
    public static readonly string[] ImportFilters =
    {
        "抽卡记录文件|*.json;*.csv;*.txt",
        "UIGF JSON|*.json",
        "CSV 记录|*.csv;*.txt",
        "所有文件|*.*",
    };

    public sealed class UigfData
    {
        public string Version { get; set; } = "v4.0";
        public string Uid { get; set; } = "";
        public string Lang { get; set; } = "zh-cn";
        public string ExportApp { get; set; } = "mi-Nest";
        public string ExportAppVersion { get; set; } = "1.0.0";
        public long ExportTimestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        public List<GachaItem> Items { get; set; } = new();
    }

    // ---------- 导入 ----------

    /// <summary>解析 UIGF JSON（v1~v4）：兼容 info+list、多游戏节（hk4e/hkrpg/nap 等）与根数组（官方 API / Starward）</summary>
    public static UigfData Parse(string json)
    {
        var data = new UigfData();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var items = new List<GachaItem>();

        if (root.ValueKind == JsonValueKind.Array)
        {
            // 官方 API 风格：根数组 = 抽卡记录
            foreach (var el in root.EnumerateArray())
                items.Add(ReadItem(el));
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object)
                ReadInfo(info, data);

            // UIGF v4：游戏节 hk4e / hkrpg / nap / bh3 / bh2 / nxx ...
            foreach (var gameKey in new[] { "hk4e", "hkrpg", "nap", "bh3", "bh2", "nxx", "zzz" })
            {
                if (root.TryGetProperty(gameKey, out var g) && g.ValueKind == JsonValueKind.Object)
                {
                    if (data.Uid.Length == 0)
                    {
                        if (g.TryGetProperty("uid", out var guid) && guid.ValueKind != JsonValueKind.Null)
                            data.Uid = guid.ToString();
                        else if (g.TryGetProperty("info", out var ginfo) && ginfo.ValueKind == JsonValueKind.Object)
                            data.Uid = ginfo.GetProp("uid");
                    }
                    if (g.TryGetProperty("lang", out var glang) && glang.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(glang.GetString()))
                        data.Lang = glang.GetString()!;
                    if (g.TryGetProperty("list", out var glist) && glist.ValueKind == JsonValueKind.Array)
                        foreach (var el in glist.EnumerateArray())
                            items.Add(ReadItem(el));
                }
            }

            // v3 及以下：root.list
            if (root.TryGetProperty("list", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var el in list.EnumerateArray())
                    items.Add(ReadItem(el));
        }

        data.Items = items;
        if (data.Items.Count == 0)
            throw new FormatException("未能从文件中识别出抽卡记录（缺少 list / hk4e / 数组结构）");
        return data;
    }

    /// <summary>解析 CSV（UIGF 标准表头，按列名识别、不依赖列顺序）</summary>
    public static UigfData ParseCsv(string csv)
    {
        var rows = SplitCsvRows(csv);
        if (rows.Count < 2) throw new FormatException("CSV 内容为空或缺少表头");

        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(string name) => header.FindIndex(h => h == name);
        int cUid = Col("uid"), cGacha = Col("gacha_type"), cItemId = Col("item_id"),
            cCount = Col("count"), cTime = Col("time"), cName = Col("name"),
            cItemType = Col("item_type"), cRank = Col("rank_type"), cId = Col("id"),
            cLang = Col("lang");
        if (cGacha < 0 || cName < 0) throw new FormatException("CSV 缺少必要列（gacha_type / name）");

        var data = new UigfData();
        foreach (var row in rows.Skip(1))
        {
            string Cell(int c) => c >= 0 && c < row.Count ? row[c].Trim() : "";
            var item = new GachaItem
            {
                Uid = Cell(cUid),
                GachaType = Cell(cGacha),
                ItemId = Cell(cItemId),
                Count = Cell(cCount),
                Time = Cell(cTime),
                Name = Cell(cName),
                ItemType = Cell(cItemType),
                RankType = Cell(cRank),
                Id = Cell(cId),
            };
            if (item.Id.Length == 0 && item.Name.Length == 0) continue;
            data.Items.Add(item);
            if (data.Uid.Length == 0 && item.Uid.Length > 0) data.Uid = item.Uid;
            if (cLang >= 0 && Cell(cLang).Length > 0) data.Lang = Cell(cLang);
        }
        if (data.Items.Count == 0) throw new FormatException("CSV 中没有有效抽卡记录行");
        return data;
    }

    /// <summary>合并多份记录：按 id 去重（缺失 id 按 时间+名称 去重），按时间升序</summary>
    public static UigfData Merge(IEnumerable<UigfData> sources)
    {
        var merged = new UigfData();
        var seenIds = new HashSet<string>();
        var seenKeys = new HashSet<string>();
        var all = new List<GachaItem>();

        foreach (var src in sources)
        {
            if (merged.Uid.Length == 0) merged.Uid = src.Uid;
            foreach (var item in src.Items)
            {
                if (!string.IsNullOrEmpty(item.Id))
                {
                    if (!seenIds.Add(item.Id)) continue;
                }
                else
                {
                    var key = $"{item.Time}|{item.Name}|{item.RankType}|{item.GachaType}";
                    if (!seenKeys.Add(key)) continue;
                }
                all.Add(item);
            }
        }

        merged.Items = all
            .OrderBy(i => i.Time, StringComparer.Ordinal)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .ToList();
        return merged;
    }

    // ---------- 导出 ----------

    /// <summary>导出 UIGF v4 JSON（info + hk4e 游戏节标准结构）</summary>
    public static string ToUigfV4(UigfData data)
    {
        var list = data.Items.Select(i => (object?)new Dictionary<string, object?>
        {
            ["uigf_gacha_type"] = UigfGachaType(i.GachaType),
            ["gacha_type"] = i.GachaType,
            ["item_id"] = i.ItemId,
            ["count"] = string.IsNullOrEmpty(i.Count) ? "1" : i.Count,
            ["time"] = i.Time,
            ["name"] = i.Name,
            ["item_type"] = i.ItemType,
            ["rank_type"] = i.RankType,
            ["id"] = i.Id,
            ["uid"] = data.Uid,
        }).ToList();

        var doc = new Dictionary<string, object?>
        {
            ["info"] = new Dictionary<string, object?>
            {
                ["export_timestamp"] = data.ExportTimestamp,
                ["export_app"] = data.ExportApp,
                ["export_app_version"] = data.ExportAppVersion,
                ["version"] = "v4.0",
            },
            ["hk4e"] = new Dictionary<string, object?>
            {
                ["uid"] = data.Uid,
                ["lang"] = data.Lang,
                ["list"] = list,
            },
        };
        return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>导出 UIGF v3 JSON（info + list，兼容 v3 及以下工具）</summary>
    public static string ToUigfV3(UigfData data)
    {
        var list = data.Items.Select(i => (object?)new Dictionary<string, object?>
        {
            ["uigf_gacha_type"] = UigfGachaType(i.GachaType),
            ["gacha_type"] = i.GachaType,
            ["item_id"] = i.ItemId,
            ["count"] = string.IsNullOrEmpty(i.Count) ? "1" : i.Count,
            ["time"] = i.Time,
            ["name"] = i.Name,
            ["item_type"] = i.ItemType,
            ["rank_type"] = i.RankType,
            ["id"] = i.Id,
        }).ToList();

        var doc = new Dictionary<string, object?>
        {
            ["info"] = new Dictionary<string, object?>
            {
                ["uid"] = data.Uid,
                ["lang"] = data.Lang,
                ["export_timestamp"] = data.ExportTimestamp,
                ["export_app"] = data.ExportApp,
                ["export_app_version"] = data.ExportAppVersion,
                ["uigf_version"] = "v3.0",
            },
            ["list"] = list,
        };
        return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>导出官方 API JSON（根数组，Starward / 官方结构兼容）</summary>
    public static string ToOfficialJson(UigfData data)
    {
        var list = data.Items.Select(i => (object?)new Dictionary<string, object?>
        {
            ["gacha_type"] = i.GachaType,
            ["item_id"] = i.ItemId,
            ["count"] = string.IsNullOrEmpty(i.Count) ? "1" : i.Count,
            ["time"] = i.Time,
            ["name"] = i.Name,
            ["item_type"] = i.ItemType,
            ["rank_type"] = i.RankType,
            ["id"] = i.Id,
            ["uid"] = data.Uid,
        }).ToList();
        return JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>导出 CSV（兼容 UIGF 标准列）</summary>
    public static string ToCsv(UigfData data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("uid,gacha_type,item_id,count,time,name,item_type,rank_type,id,lang");
        foreach (var item in data.Items)
        {
            sb.Append(Csv(item.Uid)).Append(',')
              .Append(Csv(item.GachaType)).Append(',')
              .Append(Csv(item.ItemId)).Append(',')
              .Append(Csv(item.Count)).Append(',')
              .Append(Csv(item.Time)).Append(',')
              .Append(Csv(item.Name)).Append(',')
              .Append(Csv(item.ItemType)).Append(',')
              .Append(Csv(item.RankType)).Append(',')
              .Append(Csv(item.Id)).Append(',')
              .Append(Csv(data.Lang)).AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>uigf_gacha_type 映射（301/400 合并，500 保留）</summary>
    private static string UigfGachaType(string gachaType) => gachaType switch
    {
        "301" or "400" => "301",
        _ => gachaType,
    };

    private static string Csv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    // ---------- 内部 ----------

    private static void ReadInfo(JsonElement info, UigfData data)
    {
        data.Version = info.GetProp("uigf_version") is { } v && !string.IsNullOrEmpty(v) ? v : data.Version;
        data.Uid = info.GetProp("uid") is { } u && !string.IsNullOrEmpty(u) ? u : data.Uid;
        data.Lang = info.GetProp("lang") is { } l && !string.IsNullOrEmpty(l) ? l : data.Lang;
        data.ExportApp = info.GetProp("export_app") is { } a && !string.IsNullOrEmpty(a) ? a : data.ExportApp;
        data.ExportAppVersion = info.GetProp("export_app_version") is { } av && !string.IsNullOrEmpty(av) ? av : data.ExportAppVersion;
        if (info.TryGetProperty("export_timestamp", out var ts) && ts.TryGetInt64(out var t))
            data.ExportTimestamp = t;
    }

    private static GachaItem ReadItem(JsonElement el) => new()
    {
        Uid = el.GetProp("uid"),
        GachaType = el.GetProp("gacha_type"),
        ItemId = el.GetProp("item_id"),
        Count = el.GetProp("count"),
        Name = el.GetProp("name"),
        RankType = el.GetProp("rank_type"),
        ItemType = el.GetProp("item_type"),
        Time = el.GetProp("time"),
        Id = el.GetProp("id"),
        PoolName = el.GetProp("pool_name"),
    };

    private static List<List<string>> SplitCsvRows(string csv)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var ch = csv[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(ch);
            }
            else if (ch == '"') inQuotes = true;
            else if (ch == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear();
                rows.Add(row); row = new List<string>();
            }
            else field.Append(ch);
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }
}
