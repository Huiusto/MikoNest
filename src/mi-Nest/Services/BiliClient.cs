using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace MiNest.Services;

/// <summary>哔哩哔哩客户端（Electron）检测 / 启动 / 主窗口获取</summary>
public static class BiliClient
{
    /// <summary>常见安装路径（按顺序探测）</summary>
    private static readonly string[] Candidates =
    {
        @"D:\bilibili\哔哩哔哩.exe",
        @"C:\Program Files\bilibili\哔哩哔哩.exe",
        @"C:\Program Files\bilibili\BiliBili\BiliApp.exe",
        @"C:\Program Files (x86)\bilibili\BiliBili\BiliApp.exe",
        Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Programs\bilibili\BiliBili.exe"),
        Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\bilibili\BiliApp.exe"),
    };

    /// <summary>可能的进程名（exe 主名 / Electron 常见名）</summary>
    private static readonly string[] ProcessNames = { "哔哩哔哩", "BiliApp", "BiliBili", "bilibili" };

    /// <summary>查找客户端安装路径，未找到返回 null</summary>
    public static string? FindInstallPath()
    {
        foreach (var c in Candidates)
            if (File.Exists(c)) return c;
        return null;
    }

    /// <summary>获取带主窗口的客户端进程（未运行返回 null）</summary>
    public static Process? GetMainProcess()
    {
        foreach (var name in ProcessNames)
        {
            try
            {
                var p = Process.GetProcessesByName(name).FirstOrDefault(x =>
                {
                    try { return x.MainWindowHandle != IntPtr.Zero; }
                    catch { return false; }
                });
                if (p != null) return p;
            }
            catch { /* 进程不存在则跳过 */ }
        }
        return null;
    }

    /// <summary>客户端主窗口句柄（未运行返回 Zero）</summary>
    public static IntPtr GetMainWindow()
        => GetMainProcess()?.MainWindowHandle ?? IntPtr.Zero;

    /// <summary>客户端是否正在运行（有主窗口）</summary>
    public static bool IsRunning() => GetMainWindow() != IntPtr.Zero;

    /// <summary>启动客户端。成功返回进程，失败返回 null 并给出 err。</summary>
    public static Process? Launch(out string? err)
    {
        var path = FindInstallPath();
        if (path == null)
        {
            err = "未找到哔哩哔哩客户端，请确认已安装（默认查找 D:\\bilibili 与 Program Files\\bilibili）。";
            return null;
        }
        try
        {
            var p = Process.Start(new ProcessStartInfo(path)
            {
                WorkingDirectory = Path.GetDirectoryName(path) ?? "",
                UseShellExecute = true,
            });
            err = null;
            return p;
        }
        catch (Exception ex)
        {
            err = "启动哔哩哔哩失败：" + ex.Message;
            return null;
        }
    }

    /// <summary>等待主窗口出现（最多 waitMs 毫秒），超时返回 Zero</summary>
    public static IntPtr WaitForMainWindow(int waitMs = 30000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < waitMs)
        {
            var h = GetMainWindow();
            if (h != IntPtr.Zero) return h;
            Thread.Sleep(500);
        }
        return IntPtr.Zero;
    }
}
