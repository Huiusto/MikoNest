using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MiNest.Models;

namespace MiNest.Services;

/// <summary>
/// 抽卡记录本地库（学习 HoYo.Gacha：本地持久化 + 多账号/多游戏 + UIGF/SRGF 标准导入导出）。
/// 数据保存在 %AppData%\mi-Nest\gacha_records.json，按 (游戏 + UID + 记录ID) 去重合并。
/// </summary>
public static class GachaStore
{
    private sealed class StoreFile
    {
        public int Version { get; set; } = 1;
        public List<GachaItem> Records { get; set; } = new();
    }

    private static StoreFile _store = new();
    private static string StorePath => Path.Combine(AppPaths.DataDir, "gacha_records.json");

    public static int TotalCount => _store.Records.Count;

    static GachaStore() => Load();

    public static void Load()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                var json = File.ReadAllText(StorePath);
                var loaded = JsonSerializer.Deserialize<StoreFile>(json);
                if (loaded != null) _store = loaded;
                // 主文件被写空时用备份兜底，避免历史记录凭空消失
                if (_store.Records.Count == 0 && File.Exists(StorePath + ".bak"))
                {
                    var bak = JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(StorePath + ".bak"));
                    if (bak?.Records.Count > 0) _store = bak;
                }
            }
        }
        catch
        {
            _store = new StoreFile();
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            // 覆盖前留一份备份，避免异常写空导致历史记录彻底丢失
            if (File.Exists(StorePath))
            {
                try { File.Copy(StorePath, StorePath + ".bak", overwrite: true); } catch { }
            }
            File.WriteAllText(StorePath,
                JsonSerializer.Serialize(_store, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 本地库写入失败不阻断应用
        }
    }

    /// <summary>按 UID / 游戏过滤（参数为 null 表示不限），按时间倒序</summary>
    public static List<GachaItem> Query(string? uid = null, string? gameKey = null)
        => _store.Records
            .Where(r => (uid == null || r.Uid == uid) && (gameKey == null || r.GameKey == gameKey))
            .OrderByDescending(r => r.Time)
            .ThenByDescending(r => r.Id)
            .ToList();

    /// <summary>某游戏下出现过记录的 UID 列表（按 UID 排序）</summary>
    public static List<string> GetUids(string? gameKey = null)
        => _store.Records
            .Where(r => gameKey == null || r.GameKey == gameKey)
            .Select(r => r.Uid)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

    /// <summary>合并新记录（按 游戏+UID+记录ID 去重），返回新增条数</summary>
    public static int Merge(IEnumerable<GachaItem> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return 0; // 空拉取不覆盖本地库已有记录

        var existing = _store.Records
            .Select(r => $"{r.GameKey}|{r.Uid}|{r.Id}")
            .ToHashSet();
        var added = 0;
        foreach (var it in items)
        {
            if (string.IsNullOrWhiteSpace(it.Id) || string.IsNullOrWhiteSpace(it.Uid)) continue;
            var key = $"{it.GameKey}|{it.Uid}|{it.Id}";
            if (!existing.Contains(key))
            {
                existing.Add(key);
                _store.Records.Add(it);
                added++;
            }
        }
        Save();
        return added;
    }

    /// <summary>清空某个 UID 在某游戏下的本地记录</summary>
    public static int Clear(string uid, string? gameKey = null)
    {
        var removed = _store.Records.RemoveAll(r =>
            r.Uid == uid && (gameKey == null || r.GameKey == gameKey));
        if (removed > 0) Save();
        return removed;
    }

    /// <summary>导出 UIGF（统一可交换抽卡记录标准 v4.0，原神/绝区零/崩坏3 通用）</summary>
    public static void ExportUigf(string uid, string gameKey, string path)
    {
        var list = Query(uid, gameKey).OrderBy(x => x.Time).ToList();
        var payload = new
        {
            info = new
            {
                uid,
                lang = "zh-cn",
                region_time_zone = 8,
                export_timestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
                export_app = "mi-Nest",
                export_app_version = "0.1.5",
                uigf_version = "v4.0",
            },
            list = list.Select(x => new
            {
                gacha_type = x.GachaType,
                item_id = x.ItemId,
                count = "1",
                time = x.Time,
                name = x.Name,
                item_type = x.ItemType,
                rank_type = x.RankType,
                id = x.Id,
            }),
        };
        File.WriteAllText(path,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
    }

    /// <summary>导出 SRGF（星穹铁道抽卡记录标准 v1.0）</summary>
    public static void ExportSrgf(string uid, string gameKey, string path)
    {
        var list = Query(uid, gameKey).OrderBy(x => x.Time).ToList();
        var payload = new
        {
            info = new
            {
                uid,
                lang = "zh-cn",
                export_timestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
                export_app = "mi-Nest",
                export_app_version = "0.1.5",
                srgf_version = "v1.0",
            },
            list = list.Select(x => new
            {
                uid = x.Uid,
                gacha_id = x.GachaType,
                gacha_type = x.GachaType,
                item_id = x.ItemId,
                count = "1",
                time = x.Time,
                name = x.Name,
                item_type = x.ItemType,
                rank_type = x.RankType,
                id = x.Id,
            }),
        };
        File.WriteAllText(path,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
    }

    /// <summary>导出 CSV（仅导出）</summary>
    public static void ExportCsv(string uid, string gameKey, string path)
    {
        var list = Query(uid, gameKey).OrderBy(x => x.Time).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("时间,卡池,名称,类型,品质,UID,ID");
        foreach (var it in list)
            sb.AppendLine($"{it.Time},{it.PoolName},{it.Name},{it.ItemType},{it.RankType}★,{it.Uid},{it.Id}");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    /// <summary>从 UIGF / SRGF 文件导入记录（返回解析条数 / 新增条数）</summary>
    public static (bool ok, string message, int total, int added) ImportFile(string path, string gameKey)
    {
        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("list", out var listArr) || listArr.ValueKind != JsonValueKind.Array)
                return (false, "文件中未找到 list 数组（不是有效的 UIGF / SRGF 文件）", 0, 0);

            var items = new List<GachaItem>();
            foreach (var el in listArr.EnumerateArray())
            {
                var gachaType = el.GetProp("gacha_type");
                var uid = el.GetProp("uid");
                // SRGF 的记录可能缺 uid（放在 info 里），回退到 info.uid
                if (string.IsNullOrEmpty(uid))
                {
                    uid = root.TryGetProperty("info", out var info) ? info.GetProp("uid") : "";
                }

                items.Add(new GachaItem
                {
                    Uid = uid,
                    GameKey = gameKey,
                    GachaType = gachaType,
                    ItemId = el.GetProp("item_id"),
                    Name = el.GetProp("name"),
                    RankType = el.GetProp("rank_type"),
                    ItemType = el.GetProp("item_type"),
                    Count = el.GetProp("count"),
                    Time = el.GetProp("time"),
                    Id = el.GetProp("id"),
                    PoolName = GachaService.PoolName(gameKey, gachaType),
                });
            }

            var added = Merge(items);
            return (true, $"解析 {items.Count} 条记录", items.Count, added);
        }
        catch (Exception ex)
        {
            return (false, $"导入失败：{ex.Message}", 0, 0);
        }
    }
}
