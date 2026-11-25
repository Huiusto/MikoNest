using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MiNest.Services;

namespace MiNest;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;

    /// <summary>第二次启动时用它唤醒已运行的实例（而不是直接用 Win32 强显窗口）。</summary>
    internal const string ShowEventName = "MikoNest_ShowWindow_Event";
    internal static EventWaitHandle? ShowEvent;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 安装器专用：静默下载当前启动页的所有官方动态壁纸，装完第一次打开即可直接播放。
        if (e.Args.Any(a => string.Equals(a, PrefetchWallpapersArg, StringComparison.OrdinalIgnoreCase)))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            await PrefetchWallpapersAsync();
            Shutdown(0);
            return;
        }

        // 首次以 MikoNest 启动时，自动迁移旧版 HoYoNest / mi-Nest 的数据目录（设置/账号/抽卡记录）
        AppPaths.MigrateLegacyData();

        // 单实例互斥：防止多个 MikoNest 进程并存（多进程会互相覆盖 settings.json 造成状态错乱）
        _singleInstanceMutex = new Mutex(true, "MikoNest_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            // 已有一个 MikoNest 在运行：通知它自己恢复窗口（隐藏到托盘时也走 WPF 的 Show 流程，避免画面变黑）
            SignalExistingInstance();
            Shutdown();
            return;
        }

        // 本实例的「恢复窗口」信号量：托盘 / 第二次启动都通过它唤醒
        try
        {
            ShowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        }
        catch
        {
            ShowEvent = null;
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
        MigrateLegacyAutoStart(SettingsService.Settings.AutoStartOnBoot);
        LocalizationService.Init(SettingsService.Settings.Language);
        ThemeService.Apply(SettingsService.Settings.Theme);
        ThemeService.StartSystemWatcher();

        // 确保数据目录存在
        Directory.CreateDirectory(AppPaths.DataDir);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        // 打开软件即静默检查更新；命中更高版本时直接弹出更新窗口。
        // 本地调试窗口模式下跳过，避免和指定的调试弹窗重复。
        var debugWindow = Environment.GetEnvironmentVariable("MI_NEST_DEBUG_WINDOW");
        if (string.IsNullOrEmpty(debugWindow))
        {
            _ = window.Dispatcher.BeginInvoke(new Action(async () =>
            {
                await UpdatePromptService.CheckAndPromptAsync(window);
            }));
        }

        // 仅用于本地界面自测：设置 MI_NEST_DEBUG_WINDOW=settings / update 可在启动后直接打开对应窗口。
        if (!string.IsNullOrEmpty(debugWindow))
        {
            _ = window.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (debugWindow == "settings")
                {
                    var g = MiNest.Services.GameLocator.Get("genshin");
                    new MiNest.Windows.GameSettingsWindow("genshin", g, null) { Owner = window }.ShowDialog();
                }
                else if (debugWindow == "update")
                {
                    var info = new MiNest.Services.UpdateInfo
                    {
                        Version = "0.1.40",
                        ReleasePage = MiNest.Services.UpdateService.RepoReleases,
                        AssetUrl = "https://github.com/Huiusto/MikoNest/releases/download/v0.1.40/MikoNest-Setup-0.1.40.exe",
                        AssetName = "MikoNest-Setup-0.1.40.exe",
                        AssetSize = 82400000,
                        ReleaseNotes = "## 更新内容\n\n- 启动页「获取游戏」改为「安装游戏」。\n- 打开软件自动检查更新，发现新版本立即弹出更新窗口。\n- 更新窗口增加本次版本更新日志，点击「开始更新」1 秒后自动最小化并在后台下载安装。\n- 设置页「界面配色」与「语言切换」「应用维护」卡片高度对齐。",
                    };
                    new MiNest.Windows.UpdateWindow(new Version(0, 1, 24, 0), info) { Owner = window }.ShowDialog();
                }
            }));
        }
    }

    /// <summary>把旧版 mi-Nest 的开机启动项迁移到 MikoNest，避免升级后开机启动失效。</summary>
    private static void MigrateLegacyAutoStart(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run");
            if (key == null) return;
            var hadLegacy = key.GetValue("mi-Nest") != null;
            if (enabled && !string.IsNullOrEmpty(Environment.ProcessPath))
                key.SetValue("MikoNest", "\"" + Environment.ProcessPath + "\"");
            if (hadLegacy) key.DeleteValue("mi-Nest", false);
        }
        catch
        {
            // 注册表迁移失败不阻塞启动
        }
    }

    private const string PrefetchWallpapersArg = "--prefetch-wallpapers";

    /// <summary>
    /// 安装阶段预取壁纸。网络慢或官方接口暂时不可用时最多等 6 分钟，
    /// 能下多少缓存多少；失败不会让安装失败。
    /// </summary>
    private static async Task PrefetchWallpapersAsync()
    {
        AppPaths.MigrateLegacyData();
        Directory.CreateDirectory(AppPaths.DataDir);
        try { SettingsService.Load(); } catch { }

        var sw = Stopwatch.StartNew();
        var task = LauncherBackgroundService.PrefetchAllAsync();
        await Task.WhenAny(task, Task.Delay(TimeSpan.FromMinutes(6)));

        try
        {
            var keys = new[] { "genshin", "starrail", "zzz", "bh3" };
            var images = 0;
            var videos = 0;
            long bytes = 0;
            foreach (var key in keys)
            {
                var meta = LauncherBackgroundService.GetCached(key);
                if (meta == null) continue;
                foreach (var item in meta.Items)
                {
                    if (item.ImagePath != null)
                    {
                        images++;
                        try { bytes += new FileInfo(item.ImagePath).Length; } catch { }
                    }
                    if (item.VideoPath != null)
                    {
                        videos++;
                        try { bytes += new FileInfo(item.VideoPath).Length; } catch { }
                    }
                }
            }
            File.WriteAllText(Path.Combine(AppPaths.DataDir, "wallpaper_prefetch.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 壁纸预取{(task.IsCompleted ? "完成" : "超时结束")}，" +
                $"图片 {images} 张，动态壁纸 {videos} 个，缓存 {bytes / 1024d / 1024d:F1} MB，" +
                $"耗时 {sw.Elapsed.TotalSeconds:F1} 秒。");
        }
        catch
        {
            // 日志失败不影响安装
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { ShowEvent?.Dispose(); } catch { }
        _singleInstanceMutex?.ReleaseMutex();
        base.OnExit(e);
    }

    /// <summary>通知已运行的实例恢复窗口；信号不可用时退回 Win32 强显。</summary>
    private static void SignalExistingInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var ev))
            {
                ev.Set();
                ev.Dispose();
                return;
            }
        }
        catch
        {
            // 打开失败则走兜底
        }
        RestoreExistingWindow();
    }

    #region 已运行时还原窗口（替代旧版「请勿重复启动」提示）

    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int pid);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>
    /// 再次双击桌面图标时：找到已在运行的 MikoNest 主窗口，取消最小化并置前。
    /// 若它是最小化或被「关闭到托盘」，都用 SW_RESTORE / SW_SHOW 还原到桌面。
    /// </summary>
    private static void RestoreExistingWindow()
    {
        try
        {
            var selfId = Environment.ProcessId;
            var others = Process.GetProcessesByName("MikoNest")
                .Where(p => p.Id != selfId)
                .Select(p => p.Id)
                .ToHashSet();
            if (others.Count == 0) return;

            IntPtr target = IntPtr.Zero;

            // 优先用 .NET 给出的主窗口句柄（最小化时依然有效）
            foreach (var id in others)
            {
                try
                {
                    using var proc = Process.GetProcessById(id);
                    var h = proc.MainWindowHandle;
                    if (h != IntPtr.Zero)
                    {
                        target = h;
                        break;
                    }
                }
                catch
                {
                    // 进程可能刚好退出
                }
            }

            // 兜底：该进程窗口被隐藏（如隐藏到托盘）时枚举顶层窗口，取面积最大的那个
            if (target == IntPtr.Zero)
            {
                long bestArea = 0;
                var callback = new EnumWindowsProc((hWnd, _) =>
                {
                    int pid;
                    GetWindowThreadProcessId(hWnd, out pid);
                    if (!others.Contains(pid)) return true;
                    RECT r;
                    if (!GetWindowRect(hWnd, out r)) return true;
                    var area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                    if (area > bestArea && area > 100000)
                    {
                        bestArea = area;
                        target = hWnd;
                    }
                    return true;
                });
                EnumWindows(callback, IntPtr.Zero);
                GC.KeepAlive(callback);
            }

            if (target == IntPtr.Zero) return;
            if (IsIconic(target)) ShowWindow(target, SW_RESTORE);
            ShowWindow(target, SW_SHOW);
            SetForegroundWindow(target);
        }
        catch
        {
            // 还原失败时静默退出，不再弹提示
        }
    }

    #endregion

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
