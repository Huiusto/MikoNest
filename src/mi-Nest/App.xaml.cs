using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using MiNest.Services;

namespace MiNest;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 首次以 mi-Nest 启动时，自动迁移旧版 HoYoNest 的数据目录（设置/账号/抽卡记录）
        AppPaths.MigrateLegacyData();

        // 单实例互斥：防止多个 mi-Nest 进程并存（多进程会互相覆盖 settings.json 造成状态错乱）
        _singleInstanceMutex = new Mutex(true, "mi-Nest_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("mi-Nest 已在运行，请勿重复启动。", "mi-Nest",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // 全局崩溃日志（用于排查问题，正式发布时可移除）
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogCrash(args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash(args.Exception);
            args.Handled = false;
        };

        // 加载设置并应用主题（在窗口显示之前）
        SettingsService.Load();
        LocalizationService.Init(SettingsService.Settings.Language);
        ThemeService.Apply(SettingsService.Settings.Theme);

        // 确保数据目录存在
        Directory.CreateDirectory(AppPaths.DataDir);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        base.OnExit(e);
    }

    private static void LogCrash(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.AppendAllText(Path.Combine(AppPaths.DataDir, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch
        {
            // 忽略
        }
    }
}
