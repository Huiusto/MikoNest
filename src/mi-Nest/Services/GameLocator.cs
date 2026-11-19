using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MiNest.Models;

namespace MiNest.Services;

/// <summary>游戏目录定位：注册表优先，其次手动选择</summary>
public static class GameLocator
{
    public static readonly GameDef[] Games =
    {
        new()
        {
            Key = "genshin", Name = "原神", DataDir = "YuanShen_Data",
            ExeNames = new[] { "YuanShen.exe", "GenshinImpact.exe", "YS.exe" },
            RegistryPaths = new[]
            {
                @"HKCU\Software\miHoYo\Genshin Impact",
                @"HKLM\SOFTWARE\miHoYo\Genshin Impact",
                @"HKLM\SOFTWARE\WOW6432Node\miHoYo\Genshin Impact",
                @"HKCU\Software\Unity\UnityEngine\GenshinImpact",
            },
            CoverColor1 = "#1F3B63", CoverColor2 = "#54A8D0",
            OfficialUrl = "https://ys.mihoyo.com/main/?from_fab=1",
        },
        new()
        {
            Key = "starrail", Name = "星穹铁道", DataDir = "StarRail_Data",
            ExeNames = new[] { "StarRail.exe" },
            RegistryPaths = new[]
            {
                @"HKCU\Software\miHoYo\崩坏：星穹铁道",
                @"HKCU\Software\miHoYo\Star Rail",
                @"HKLM\SOFTWARE\miHoYo\崩坏：星穹铁道",
                @"HKLM\SOFTWARE\WOW6432Node\miHoYo\崩坏：星穹铁道",
                @"HKCU\Software\Unity\UnityEngine\StarRail",
            },
            CoverColor1 = "#232A4A", CoverColor2 = "#6C7BD9",
            OfficialUrl = "https://sr.mihoyo.com/main",
        },
        new()
        {
            Key = "zzz", Name = "绝区零", DataDir = "ZenlessZoneZero_Data",
            ExeNames = new[] { "ZenlessZoneZero.exe" },
            RegistryPaths = new[]
            {
                @"HKCU\Software\miHoYo\绝区零",
                @"HKCU\Software\miHoYo\ZenlessZoneZero",
                @"HKLM\SOFTWARE\miHoYo\绝区零",
                @"HKLM\SOFTWARE\WOW6432Node\miHoYo\绝区零",
                @"HKCU\Software\Unity\UnityEngine\ZenlessZoneZero",
            },
            CoverColor1 = "#4A3423", CoverColor2 = "#E8B30C",
            OfficialUrl = "https://zzz.mihoyo.com/main/",
        },
        new()
        {
            Key = "bh3", Name = "崩坏3", DataDir = "BH3_Data",
            ExeNames = new[] { "BH3.exe" },
            RegistryPaths = new[]
            {
                @"HKLM\SOFTWARE\miHoYo\Honkai Impact 3",
                @"HKLM\SOFTWARE\WOW6432Node\miHoYo\Honkai Impact 3",
                @"HKCU\Software\miHoYo\Honkai Impact 3",
            },
            CoverColor1 = "#3A1F4A", CoverColor2 = "#C77DDB",
            OfficialUrl = "https://bh3.mihoyo.com/main",
        },
    };

    public static GameDef Get(string key) => Games.First(g => g.Key == key);

    /// <summary>从注册表 / 米哈游启动器目录探测安装目录（可能返回 null）</summary>
    public static string? DetectInstallDir(GameDef g)
    {
        foreach (var reg in g.RegistryPaths)
        {
            var v = ReadRegistryPath(reg);
            if (!string.IsNullOrEmpty(v) && Directory.Exists(v))
                return v;
        }

        // 米哈游启动器统一安装目录（国服默认 D:\miHoYo\miHoYo Launcher\games\...）
        foreach (var launcher in new[]
                 {
                     @"D:\miHoYo\miHoYo Launcher\games",
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"miHoYo Launcher\games"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"miHoYo Launcher\games"),
                 })
        {
            if (!Directory.Exists(launcher)) continue;
            foreach (var dir in Directory.GetDirectories(launcher))
            {
                if (Directory.Exists(Path.Combine(dir, g.DataDir)) ||
                    (g.Key == "genshin" && Directory.Exists(Path.Combine(dir, "GenshinImpact_Data"))))
                    return dir;
            }
        }
        return null;
    }

    private static string? ReadRegistryPath(string path)
    {
        try
        {
            var parts = path.Split('\\');
            var rootName = parts[0];
            var sub = string.Join('\\', parts.Skip(1));
            using var root = rootName == "HKLM" ? Microsoft.Win32.Registry.LocalMachine : Microsoft.Win32.Registry.CurrentUser;
            using var key = root.OpenSubKey(sub);
            return key?.GetValue("InstallPath") as string
                   ?? key?.GetValue("InstallLocation") as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>在游戏目录里找到主程序 exe</summary>
    public static string? FindExe(string dir, GameDef g)
    {
        foreach (var e in g.ExeNames)
        {
            var p = Path.Combine(dir, e);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>各游戏抽卡页面在 web 缓存中出现的前缀（与 Starward 同款常量）</summary>
    private static string[] GachaPrefixes(string key) => key switch
    {
        "genshin" => new[]
        {
            "https://webstatic.mihoyo.com/hk4e/event/e20190909gacha",
            "https://gs.hoyoverse.com/genshin/event/e20190909gacha",
        },
        "starrail" => new[]
        {
            "https://webstatic.mihoyo.com/hkrpg/event/e20211215gacha",
            "https://gs.hoyoverse.com/hkrpg/event/e20211215gacha",
        },
        "zzz" => new[]
        {
            "https://webstatic.mihoyo.com/nap/event/e20230424gacha",
            "https://gs.hoyoverse.com/nap/event/e20230424gacha",
        },
        _ => Array.Empty<string>(),
    };

    private static string[] BeyondPrefixes => new[]
    {
        "https://webstatic.mihoyo.com/hk4e/event/e20250716gacha",
        "https://gs.hoyoverse.com/genshin/event/e20250716gacha",
    };

    /// <summary>
    /// 从游戏 webCaches 中提取抽卡日志 URL（移植 Starward.GachaLogClient.GetGachaUrlFromWebCache）。
    /// 游戏每次打开“抽卡记录”页面时会把带 authkey 的 URL 写入本地 web 缓存
    /// （webCaches\{版本}\Cache\Cache_Data\data_2，缓存轮转后也可能落在 data_1 / data_3）。
    /// 缓存文件在游戏运行时会被独占写入，这里做多次重试再放弃。
    /// </summary>
    public static string? FindGachaUrl(string gameDir, GameDef g)
        => FindGachaUrl(gameDir, g, out _);

    public static string? FindGachaUrl(string gameDir, GameDef g, out string? error)
        => FindUrlInWebCaches(gameDir, g, GachaPrefixes(g.Key), out error);

    /// <summary>
    /// 从原神 webCaches 里提取千星奇域「颂愿记录」页面 URL（页面前缀 e20250716gacha），
    /// 与普通祈愿缓存互不干扰。
    /// </summary>
    public static string? FindBeyondUrl(string gameDir, GameDef g)
        => FindBeyondUrl(gameDir, g, out _);

    public static string? FindBeyondUrl(string gameDir, GameDef g, out string? error)
        => FindUrlInWebCaches(gameDir, g, BeyondPrefixes, out error);

    /// <summary>在 webCaches 的所有 data_* 缓存块里按前缀找最近写入的一条 URL</summary>
    private static string? FindUrlInWebCaches(string gameDir, GameDef g, string[] prefixes, out string? error)
    {
        error = null;
        if (prefixes.Length == 0)
        {
            error = "该游戏暂不支持从缓存读取抽卡记录";
            return null;
        }

        // 兼容国服（YuanShen_Data）与国际服（GenshinImpact_Data）数据目录
        var dataDirs = new List<string> { g.DataDir };
        if (g.Key == "genshin" && !dataDirs.Contains("GenshinImpact_Data"))
            dataDirs.Add("GenshinImpact_Data");

        var foundAnyCache = false;
        var lockedAny = false;
        var readAny = false;

        foreach (var dataDir in dataDirs)
        {
            var wc = Path.Combine(gameDir, dataDir, "webCaches");
            if (!Directory.Exists(wc)) continue;

            // 候选缓存块：webCaches\Cache\Cache_Data 与 webCaches\<版本>\Cache\Cache_Data
            var cacheDirs = new List<string> { Path.Combine(wc, @"Cache\Cache_Data") };
            foreach (var versionDir in Directory.GetDirectories(wc))
                cacheDirs.Add(Path.Combine(versionDir, @"Cache\Cache_Data"));

            var files = new List<(string Path, DateTime Time)>();
            foreach (var dir in cacheDirs)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var name in new[] { "data_2", "data_1", "data_3", "data_0" })
                {
                    var f = Path.Combine(dir, name);
                    if (!File.Exists(f)) continue;
                    foundAnyCache = true;
                    files.Add((f, File.GetLastWriteTime(f)));
                }
            }

            // 最新写入的缓存块优先（游戏刚打开抽卡页面时 URL 一定在最热的那块里）
            files.Sort((a, b) => b.Time.CompareTo(a.Time));

            foreach (var (file, _) in files)
            {
                var text = ReadCacheText(file);
                if (text == null)
                {
                    lockedAny = true;
                    continue;
                }
                readAny = true;

                // 与 Starward 一致：取文件中最后一次出现的抽卡页面前缀，读到 \0 或引号为止
                string? url = null;
                foreach (var prefix in prefixes)
                {
                    var idx = text.LastIndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0) continue;
                    var rest = text.Substring(idx);
                    var end = rest.IndexOfAny(new[] { '\0', '"' });
                    var candidate = end >= 0 ? rest[..end] : rest;
                    if (!candidate.Contains("authkey", StringComparison.OrdinalIgnoreCase)) continue;
                    url = candidate;
                    break;
                }
                if (url != null) return url;
            }

            if (readAny) break;   // 已经成功读过缓存，不必再去读国际服目录
        }

        error = !foundAnyCache
            ? "没有找到游戏网页缓存（webCaches）：请先启动游戏，在游戏内打开一次「抽卡记录」页面"
            : lockedAny && !readAny
                ? "游戏正在运行并占用缓存文件，读取失败：请稍等几秒再点一次「更新记录」，或直接粘贴抽卡链接"
                : "游戏缓存里还没有抽卡链接：请先在游戏内打开一次「抽卡记录」页面（保持页面打开几秒），再回来点「更新记录」";
        return null;
    }

    /// <summary>读取缓存块（游戏运行时会被独占写入，做多次短重试）</summary>
    private static string? ReadCacheText(string path)
    {
        for (var attempt = 0; attempt < 15; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
            catch (IOException)
            {
                System.Threading.Thread.Sleep(200);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }
}
