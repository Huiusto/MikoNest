using System;
using System.IO;
using System.Text.Json;
using MiNest.Models;

namespace MiNest.Services;

public static class SettingsService
{
    public static AppSettings Settings { get; private set; } = new();

    public static void Load()
    {
        // 第一阶段：用默认路径读取一次，取 DataDirOverride（若有则切换数据目录，再读正式配置）
        try
        {
            var defaultFile = Path.Combine(AppPaths.DefaultDataDir, "settings.json");
            if (File.Exists(defaultFile))
            {
                var probeJson = File.ReadAllText(defaultFile);
                using var probe = JsonDocument.Parse(probeJson);
                if (probe.RootElement.TryGetProperty("DataDirOverride", out var dd) &&
                    dd.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(dd.GetString()))
                {
                    AppPaths.DataDir = dd.GetString()!.Trim();
                }
            }
        }
        catch
        {
            // 探测失败沿用默认目录
        }

        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            // 反序列化失败时记录原因（用于排查），并回退到默认设置
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                File.AppendAllText(Path.Combine(AppPaths.DataDir, "crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] settings.json 反序列化失败，已回退默认设置: {ex}\n\n");
            }
            catch { }
            Settings = new AppSettings();
        }

        if (string.IsNullOrWhiteSpace(Settings.DeviceId))
        {
            Settings.DeviceId = Guid.NewGuid().ToString("D");
        }
        else if (Settings.DeviceId.Length == 32 && !Settings.DeviceId.Contains('-'))
        {
            // 迁移旧的 32 位无连字符格式为带连字符 UUID（对齐 TeyvatGuide device_id=v4()）
            Settings.DeviceId = Guid.ParseExact(Settings.DeviceId, "N").ToString("D");
            Save();
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(AppPaths.SettingsFile,
                JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 设置写入失败不阻断应用
        }
    }
}
