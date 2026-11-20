using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
            // 已有一个 mi-Nest 在运行：不再弹「请勿重复启动」，直接把它的窗口还原到桌面前台
            RestoreExistingWindow();
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
        ThemeService.StartSystemWatcher();

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
    /// 再次双击桌面图标时：找到已在运行的 mi-Nest 主窗口，取消最小化并置前。
    /// 若它是最小化或被「关闭到托盘」，都用 SW_RESTORE / SW_SHOW 还原到桌面。
    /// </summary>
    private static void RestoreExistingWindow()
    {
        try
        {
            var selfId = Environment.ProcessId;
            var others = Process.GetProcessesByName("mi-Nest")
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
