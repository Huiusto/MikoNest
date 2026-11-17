using System;
using System.IO;

namespace MiNest;

/// <summary>应用数据目录（默认 %AppData%\mi-Nest，可在 设置-应用信息 更改）</summary>
public static class AppPaths
{
    /// <summary>默认数据目录</summary>
    public static string DefaultDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mi-Nest");

    /// <summary>当前生效的数据目录（启动时按 settings.json 的 DataDirOverride 覆盖）</summary>
    public static string DataDir { get; set; } = DefaultDataDir;

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string AccountsFile => Path.Combine(DataDir, "accounts.json");
    public static string WebView2Dir => Path.Combine(DataDir, "WebView2");

    public static string AssetsDir { get; } = Path.Combine(AppContext.BaseDirectory, "Assets");

    /// <summary>旧版（HoYoNest）默认数据目录，仅用于一次性迁移</summary>
    public static string LegacyDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HoYoNest");

    /// <summary>若新数据目录不存在而旧目录存在，则把旧设置/账号/抽卡数据迁移过来（跳过 WebView2 缓存）</summary>
    public static void MigrateLegacyData()
    {
        try
        {
            if (Directory.Exists(DataDir) || !Directory.Exists(LegacyDataDir)) return;
            CopyDir(LegacyDataDir, DataDir);
        }
        catch
        {
            // 迁移失败不阻塞启动
        }
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), false);
        foreach (var dir in Directory.GetDirectories(src))
        {
            if (string.Equals(Path.GetFileName(dir), "WebView2", StringComparison.OrdinalIgnoreCase))
                continue; // 缓存体积大且可重建
            CopyDir(dir, Path.Combine(dst, Path.GetFileName(dir)));
        }
    }
}
