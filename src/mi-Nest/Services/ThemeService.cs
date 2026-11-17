using System;
using System.Windows;

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
    }
}
