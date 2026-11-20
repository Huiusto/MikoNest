using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MiNest.Models;

namespace MiNest.Services;

public static class ThemeService
{
    /// <summary>主题键（对应 Themes/Theme.{key}.xaml）；"System" = 跟随 Windows 浅色/深色模式</summary>
    public static readonly string[] Themes =
    {
        "System", "Light", "Dark", "MihoyoBlue", "GenshinCyan", "StarRailGold", "ZzzYellow",
        "Honkai2Orange", "Honkai3Blue", "ThemisCold", "LoliPink", "BossPurple", "HutaoRed",
    };
    public static readonly string[] ThemeNames =
    {
        "跟随系统", "白", "黑", "米蓝", "原青", "星金", "绝黄",
        "崩2橙", "崩3蓝", "未定冷", "萝莉粉", "御姐紫", "胡桃红",
    };
    public static readonly string[] ThemeColors =
    {
        "#9AA3AF", "#FFFFFF", "#1C1F26", "#1F65FF", "#0F766E", "#D8B36A", "#FFD666",
        "#E8732B", "#2456A6", "#2E5EAA", "#F58BB0", "#7A4BCB", "#B03A48",
    };

    private static string? _lastEffective;
    private static DispatcherTimer? _systemWatch;

    /// <summary>Windows 是否处于深色（夜间）应用模式</summary>
    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int v) return v == 0;
        }
        catch
        {
            // 读不到就按浅色处理
        }
        return false;
    }

    /// <summary>开始监听系统浅色/深色切换（仅在「跟随系统」时生效）</summary>
    public static void StartSystemWatcher()
    {
        if (_systemWatch != null) return;
        _systemWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _systemWatch.Tick += (_, _) =>
        {
            if (SettingsService.Settings.Theme != "System") return;
            var effective = IsSystemDark() ? "Dark" : "Light";
            if (_lastEffective != effective) Apply("System");
        };
        _systemWatch.Start();
    }

    public static void Apply(string theme)
    {
        if (string.IsNullOrEmpty(theme) || Array.IndexOf(Themes, theme) < 0)
            theme = "Light";

        // 「跟随系统」：Windows 深色/夜间模式 → 黑色主题，浅色 → 白色主题
        var effective = theme == "System" ? (IsSystemDark() ? "Dark" : "Light") : theme;
        _lastEffective = effective;

        foreach (var dict in Application.Current.Resources.MergedDictionaries)
        {
            if (dict.Source?.OriginalString?.Contains("Themes/Theme.") == true)
            {
                dict.Source = new Uri($"Themes/Theme.{effective}.xaml", UriKind.Relative);
                break;
            }
        }

        SettingsService.Settings.Theme = theme;
        SettingsService.Save();

        // 换主题后卡片底色变了：先清掉上一次的透明度覆盖，再按新主题底色重新计算
        Application.Current.Resources.Remove("CardBg");
        Application.Current.Resources.Remove("ButtonBg");
        ApplyCardOpacity();
    }

    /// <summary>
    /// 按设置里的 0-100 数值设置卡片 / 框框背景不透明度（0 = 完全透明，100 = 主题原色）。
    /// 同时覆盖 CardBg（卡片）与 ButtonBg（默认按钮，「米哈游相关」九宫格等就是按钮样式）。
    /// </summary>
    public static void ApplyCardOpacity()
    {
        var percent = Math.Clamp(SettingsService.Settings.CardOpacity, 0, 100);
        ApplyOpacityTo("CardBg", percent);
        ApplyOpacityTo("ButtonBg", percent);
    }

    private static void ApplyOpacityTo(string key, int percent)
    {
        var source = FindThemeBrush(key);
        if (source == null) return;

        var c = source.Color;
        var alpha = (byte)Math.Round(255 * percent / 100.0);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        brush.Freeze();
        Application.Current.Resources[key] = brush;
    }

    /// <summary>始终从主题字典取原始颜色（避免读到上一次写入的覆盖值）</summary>
    private static SolidColorBrush? FindThemeBrush(string key)
    {
        foreach (var dict in Application.Current.Resources.MergedDictionaries)
        {
            if (dict.Contains(key) && dict[key] is SolidColorBrush b)
                return b;
        }
        return Application.Current.TryFindResource(key) as SolidColorBrush;
    }

    public static void SetCardOpacity(int percent)
    {
        SettingsService.Settings.CardOpacity = Math.Clamp(percent, 0, 100);
        ApplyCardOpacity();
        SettingsService.Save();
    }
}
