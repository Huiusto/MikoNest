using System;
using System.IO;
using System.Linq;

namespace MiNest;

/// <summary>应用数据目录（默认 %AppData%\MikoNest，可在 设置-应用信息 更改）</summary>
public static class AppPaths
{
    /// <summary>默认数据目录（Windows: %AppData%\MikoNest；macOS: ~/Library/Application Support/MikoNest）</summary>
    public static string DefaultDataDir { get; } = OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "MikoNest")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MikoNest");

    /// <summary>当前生效的数据目录（启动时按 settings.json 的 DataDirOverride 覆盖）</summary>
    public static string DataDir { get; set; } = DefaultDataDir;

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string AccountsFile => Path.Combine(DataDir, "accounts.json");
    public static string WebView2Dir => Path.Combine(DataDir, "WebView2");

    public static string AssetsDir { get; } = Path.Combine(AppContext.BaseDirectory, "Assets");

    /// <summary>旧版默认数据目录（HoYoNest、mi-Nest），仅用于一次性迁移</summary>
    public static string[] LegacyDataDirs { get; } =
    {
        LegacyDir("HoYoNest"),
        LegacyDir("mi-Nest"),
    };

    private static string LegacyDir(string name) => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", name)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), name);

    /// <summary>把旧版设置/账号/抽卡数据迁移到 MikoNest（跳过可重建的缓存目录）</summary>
    public static void MigrateLegacyData()
    {
        foreach (var legacy in LegacyDataDirs)
        {
            try
            {
                if (string.Equals(legacy, DataDir, StringComparison.OrdinalIgnoreCase) ||
                    !Directory.Exists(legacy)) continue;

                // 新目录已经有设置文件：说明已经迁移过，不再覆盖用户当前数据。
                if (File.Exists(SettingsFile)) return;

                if (Directory.Exists(DataDir))
                {
                    CopyUserData(legacy, DataDir);
                }
                else
                {
                    try
                    {
                        Directory.Move(legacy, DataDir);
                    }
                    catch
                    {
                        CopyUserData(legacy, DataDir);
                    }
                }
                return;
            }
            catch
            {
                // 迁移失败不阻塞启动
            }
        }
    }

    private static readonly string[] RebuildableDirs = { "WebView2", "webview2-launch", "bg", "launch" };

    /// <summary>只复制用户数据，跳过体积大且可重新下载的缓存。</summary>
    private static void CopyUserData(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
        {
            var target = Path.Combine(dst, Path.GetFileName(file));
            if (!File.Exists(target)) File.Copy(file, target, false);
        }
        foreach (var dir in Directory.GetDirectories(src))
        {
            var name = Path.GetFileName(dir);
            if (RebuildableDirs.Any(d => string.Equals(name, d, StringComparison.OrdinalIgnoreCase)))
                continue;
            CopyUserData(dir, Path.Combine(dst, name));
        }
    }
}
