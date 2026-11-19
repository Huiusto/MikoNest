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

    /// <summary>
    /// 从游戏 webCaches 中提取抽卡日志 URL（移植 Starward.GachaLogClient.GetGachaUrlFromWebCache）。
    /// 游戏每次打开“抽卡记录”页面时会把带 authkey 的 URL 写入本地 web 缓存
    /// （webCaches\{版本}\Cache\Cache_Data\data_2）。
    /// </summary>
    public static string? FindGachaUrl(string gameDir, GameDef g)
    {
        // 兼容国服（YuanShen_Data）与国际服（GenshinImpact_Data）数据目录
        var dataDirs = new List<string> { g.DataDir };
        if (g.Key == "genshin" && !dataDirs.Contains("GenshinImpact_Data"))
            dataDirs.Add("GenshinImpact_Data");

        foreach (var dataDir in dataDirs)
        {
            var wc = Path.Combine(gameDir, dataDir, "webCaches");
            if (!Directory.Exists(wc)) continue;

            var bestTime = DateTime.MinValue;
            string? bestUrl = null;
            foreach (var versionDir in Directory.GetDirectories(wc))
            {
                // Starward 同款：抽卡 URL 只存在于 data_2；data_1/data_3 会被日常页面 URL 覆盖，不可混用
                var f = Path.Combine(versionDir, @"Cache\Cache_Data\data_2");
                if (!File.Exists(f)) continue;
                var lastWrite = File.GetLastWriteTime(f);
                try
                {
                    using var fs = File.Open(f, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    var ms = new MemoryStream();
                    fs.CopyTo(ms);
                    var text = Encoding.UTF8.GetString(ms.ToArray());
                    // 优先完整 webstatic 页面 URL；且必须带抽卡特征（必须含 gacha），
                    // 否则会被日常活动页（如 blue-post）的 authkey 链接干扰，拉取必然失败
                    var m = Regex.Match(text,
                        @"https://webstatic\.mihoyo\.com[^\x00-\x20""'\\]{0,1500}");
                    if (m.Success && m.Value.Contains("gacha", StringComparison.OrdinalIgnoreCase))
                    {
                        if (lastWrite > bestTime)
                        {
                            bestTime = lastWrite;
                            bestUrl = m.Value;
                        }
                        continue;
                    }
                    m = Regex.Match(text,
                        @"https?://[^\x00-\x20""'\\]{0,1500}getGachaLog[^\x00-\x20""'\\]{0,300}");
                    if (m.Success && lastWrite > bestTime)
                    {
                        bestTime = lastWrite;
                        bestUrl = m.Value;
                    }
                    // 再兜底：直接搜含 gacha 的 webstatic 链接（部分缓存里前面会夹带其它 URL）
                    m = Regex.Match(text,
                        @"https://webstatic\.mihoyo\.com/[^\x00-\x20""'\\]{0,1500}gacha[^\x00-\x20""'\\]{0,600}");
                    if (m.Success && m.Value.Contains("authkey"))
                    {
                        if (lastWrite > bestTime)
                        {
                            bestTime = lastWrite;
                            bestUrl = m.Value;
                        }
                    }
                }
                catch
                {
                    // 单个缓存文件读取失败忽略，继续找其他版本
                }
            }
            if (bestUrl != null) return bestUrl;
        }
        return null;
    }
}
