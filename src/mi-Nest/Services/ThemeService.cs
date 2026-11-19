using System;
using System.Windows;
using System.Windows.Media;
using MiNest.Models;

namespace MiNest.Services;

public static class ThemeService
{
    /// <summary>主题键（对应 Themes/Theme.{key}.xaml）</summary>
    public static readonly string[] Themes =
    {
        "Light", "Dark", "MihoyoBlue", "GenshinCyan", "StarRailGold", "ZzzYellow",
        "Honkai2Orange", "Honkai3Blue", "ThemisCold", "HutaoRed",
    };
    public static readonly string[] ThemeNames =
    {
        "白", "黑", "米蓝", "原青", "星金", "绝黄",
        "崩2橙", "崩3蓝", "未定冷", "胡桃红",
    };
    public static readonly string[] ThemeColors =
    {
        "#FFFFFF", "#1C1F26", "#1F65FF", "#0F766E", "#D8B36A", "#FFD666",
        "#E8732B", "#2456A6", "#2E5EAA", "#B03A48",
    };

    public static void Apply(string theme)
    {
        if (string.IsNullOrEmpty(theme) || Array.IndexOf(Themes, theme) < 0)
            theme = "Light";

        foreach (var dict in Application.Current.Resources.MergedDictionaries)
        {
            if (dict.Source?.OriginalString?.Contains("Themes/Theme.") == true)
            {
                dict.Source = new Uri($"Themes/Theme.{theme}.xaml", UriKind.Relative);
                break;
            }
        }

        SettingsService.Settings.Theme = theme;
        SettingsService.Save();

        // 换主题后卡片底色变了：先清掉上一次的透明度覆盖，再按新主题底色重新计算
        Application.Current.Resources.Remove("CardBg");
        ApplyCardOpacity();
    }

    /// <summary>按设置里的 0-100 数值设置卡片背景不透明度（0 = 完全透明，100 = 主题原色）</summary>
    public static void ApplyCardOpacity()
    {
        var percent = Math.Clamp(SettingsService.Settings.CardOpacity, 0, 100);
        var source = Application.Current.TryFindResource("CardBg") as SolidColorBrush;
        if (source == null)
        {
            // 覆盖层已存在时 TryFindResource 会返回覆盖层，回退到主题字典里找原色
            foreach (var dict in Application.Current.Resources.MergedDictionaries)
            {
                if (dict.Contains("CardBg") && dict["CardBg"] is SolidColorBrush b)
                {
                    source = b;
                    break;
                }
            }
        }
        if (source == null) return;

        var c = source.Color;
        var alpha = (byte)Math.Round(255 * percent / 100.0);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        brush.Freeze();
        Application.Current.Resources["CardBg"] = brush;
    }

    public static void SetCardOpacity(int percent)
    {
        SettingsService.Settings.CardOpacity = Math.Clamp(percent, 0, 100);
        ApplyCardOpacity();
        SettingsService.Save();
    }
}
