using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WpfSize = System.Windows.Size;

namespace MiNest.Native;

/// <summary>
/// 把外部 Win32 窗口（如哔哩哔哩客户端）嵌入 WPF 页面。
/// 从视觉树移除时自动把窗口还原到原父窗口并恢复原尺寸位置。
/// </summary>
public class ForeignWindowHost : HwndHost
{
    private IntPtr _child;
    private IntPtr _originalParent;
    private RECT _originalRect;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr h);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr h, out RECT r);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr h, int cmd);

    private const int GWL_STYLE = -16;
    private const long WS_CHILD = 0x40000000L;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const int SW_RESTORE = 9;

    public ForeignWindowHost(IntPtr child)
    {
        _child = child;
        _originalParent = GetParent(child);
        GetWindowRect(child, out _originalRect);
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        // 记录原父与原始尺寸后，改为子窗口并挂到本宿主
        var style = GetWindowLongPtr(_child, GWL_STYLE);
        SetWindowLongPtr(_child, GWL_STYLE, new IntPtr(style.ToInt64() | WS_CHILD));
        SetParent(_child, hwndParent.Handle);
        ShowWindow(_child, SW_RESTORE);
        return new HandleRef(this, _child);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        // 还原：挂回原父窗口并恢复原尺寸位置
        if (_child != IntPtr.Zero)
        {
            var style = GetWindowLongPtr(_child, GWL_STYLE);
            SetWindowLongPtr(_child, GWL_STYLE, new IntPtr(style.ToInt64() & ~WS_CHILD));
            var parent = _originalParent == IntPtr.Zero ? IntPtr.Zero : _originalParent;
            SetParent(_child, parent);
            if (parent != IntPtr.Zero)
                SetWindowPos(_child, IntPtr.Zero,
                    _originalRect.Left, _originalRect.Top,
                    _originalRect.Right - _originalRect.Left,
                    _originalRect.Bottom - _originalRect.Top,
                    SWP_NOZORDER | SWP_NOACTIVATE);
            _child = IntPtr.Zero;
        }
    }

    protected override void OnWindowPositionChanged(Rect rc)
    {
        if (_child == IntPtr.Zero) return;
        SetWindowPos(_child, IntPtr.Zero, 0, 0,
            Math.Max(1, (int)rc.Width), Math.Max(1, (int)rc.Height),
            SWP_NOZORDER | SWP_NOACTIVATE);
    }

    protected override WpfSize MeasureOverride(WpfSize constraint) => constraint;
}
