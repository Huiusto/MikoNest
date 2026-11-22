using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace MiNest.Avalonia.Theme;

/// <summary>
/// 跨平台主题：把 WPF 版的 13 套配色映射成 Avalonia 的「明/暗 + 强调色」。
/// macOS 上同样生效，深浅色跟随系统由 Avalonia ThemeVariant.Default 负责。
/// </summary>
public static class UiTheme
{
    public sealed record ThemeDef(string Key, string Name, string Accent, bool Dark, string Swatch);

    public static readonly IReadOnlyList<ThemeDef> Themes = new[]
    {
        new ThemeDef("System", "跟随系统", "#0A84FF", false, "#9AA3AF"),
        new ThemeDef("Light", "白", "#0A84FF", false, "#FFFFFF"),
        new ThemeDef("Dark", "黑", "#0A84FF", true, "#15181E"),
        new ThemeDef("GenshinCyan", "原神青", "#3FB6C9", false, "#3FB6C9"),
        new ThemeDef("Honkai2Orange", "崩坏橙", "#F08A24", false, "#F08A24"),
        new ThemeDef("Honkai3Blue", "崩坏蓝", "#4C7DF0", false, "#4C7DF0"),
        new ThemeDef("LoliPink", "萝莉粉", "#E86A9A", false, "#E86A9A"),
        new ThemeDef("BossPurple", "御姐紫", "#7A4BCB", false, "#7A4BCB"),
        new ThemeDef("HutaoRed", "胡桃红", "#D94F5C", false, "#D94F5C"),
        new ThemeDef("MihoyoBlue", "米哈游蓝", "#3A8EE6", false, "#3A8EE6"),
        new ThemeDef("StarRailGold", "星穹金", "#C9A227", true, "#C9A227"),
        new ThemeDef("ZzzYellow", "绝区黄", "#F2C94C", true, "#F2C94C"),
        new ThemeDef("ThemisCold", "未定冷", "#5C7C99", false, "#5C7C99"),
    };

    public static ThemeDef Resolve(string? key)
    {
        foreach (var t in Themes)
            if (string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase)) return t;
        return Themes[0];
    }

    /// <summary>
    /// 代码里取主题画刷。App.axaml 的颜色放在 ThemeDictionaries 里，
    /// 直接用 Resources["key"] 拿不到（会得到 null），这里按当前明暗主题查。
    /// </summary>
    public static IBrush Brush(string key, string fallback)
    {
        var app = Application.Current;
        if (app == null) return new SolidColorBrush(Color.Parse(fallback));

        if (app.Resources.TryGetValue(key, out var top) && top is IBrush b0) return b0;

        var variant = app.ActualThemeVariant;
        if (app.Resources.ThemeDictionaries.TryGetValue(variant, out var dict) && dict is ResourceDictionary rd
            && rd.TryGetValue(key, out var v1) && v1 is IBrush b1)
            return b1;

        foreach (var d in app.Resources.ThemeDictionaries.Values)
        {
            if (d is ResourceDictionary r && r.TryGetValue(key, out var v2) && v2 is IBrush b2)
                return b2;
        }
        return new SolidColorBrush(Color.Parse(fallback));
    }

    public static bool SystemIsDark()
    {
        return Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
    }

    /// <summary>应用主题：强调色写进资源，卡片透明度按 CardOpacity 计算。</summary>
    public static void Apply(string? themeKey, int cardOpacity)
    {
        if (Application.Current == null) return;
        var def = Resolve(themeKey);
        var dark = def.Key == "System" ? SystemIsDark() : def.Dark;
        Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        var accent = Color.Parse(def.Accent);
        var res = Application.Current.Resources;
        res["Accent"] = new SolidColorBrush(accent);
        res["AccentSoft"] = new SolidColorBrush(Color.FromArgb(36, accent.R, accent.G, accent.B));
        res["AccentText"] = new SolidColorBrush(dark ? Lighten(accent, 0.35) : Darken(accent, 0.12));
        ApplyCardOpacity(cardOpacity, dark);
    }

    public static void ApplyCardOpacity(int percent, bool? dark = null)
    {
        if (Application.Current == null) return;
        var isDark = dark ?? (Application.Current.ActualThemeVariant == ThemeVariant.Dark);
        var alpha = (byte)(Math.Clamp(percent, 0, 100) * 255 / 100);
        var baseColor = isDark ? Color.Parse("#1A202B") : Colors.White;
        Application.Current.Resources["CardBg"] = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
    }

    private static Color Lighten(Color c, double amount)
        => Color.FromRgb(
            (byte)Math.Min(255, c.R + (255 - c.R) * amount),
            (byte)Math.Min(255, c.G + (255 - c.G) * amount),
            (byte)Math.Min(255, c.B + (255 - c.B) * amount));

    private static Color Darken(Color c, double amount)
        => Color.FromRgb(
            (byte)(c.R * (1 - amount)),
            (byte)(c.G * (1 - amount)),
            (byte)(c.B * (1 - amount)));
}
