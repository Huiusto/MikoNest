using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MiNest.Avalonia.Views;
using MiNest.Avalonia.Theme;
using MiNest.Services;

namespace MiNest.Avalonia;

public partial class MainWindow : Window
{
    private sealed record NavItem(string Key, string Glyph, string Title, string Subtitle);

    private static readonly NavItem[] Items =
    {
        new("launch", "▶", "启动", "同步官方版本壁纸 · 预下载 · 一键启动"),
        new("gacha", "◈", "抽卡记录", "祈愿 / 跃迁 / 调频 · 本地统计"),
        new("tasks", "✓", "任务 / 签到", "每日签到与任务状态"),
        new("mhy", "☁", "米哈游相关", "官方社区与工具入口"),
        new("account", "☺", "账号", "米哈游通行证与角色"),
        new("settings", "⚙", "设置", "外观、语言与维护"),
    };

    private readonly Dictionary<string, Control> _pages = new();
    private readonly Dictionary<string, Button> _navButtons = new();
    private readonly Dictionary<string, Border> _navGlyphs = new();
    private bool _collapsed;
    private string _current = "launch";

    public MainWindow()
    {
        InitializeComponent();

        BtnBrand.Click += (_, _) => ToggleSidebar();

        BuildNav();
        _navGlyphs["account"] = NavGlyphAccount;
        _navGlyphs["settings"] = NavGlyphSettings;
        ShowPage(Environment.GetEnvironmentVariable("MI_NEST_START") is { Length: > 0 } start &&
                 Items.Any(i => i.Key == start) ? start : "launch");

        Opened += (_, _) =>
        {
            TxtAccount.Text = DescribeAccount();
        };
    }

    private static string DescribeAccount() => "本地模式 · 无需登录";

    private void BuildNav()
    {
        foreach (var item in Items)
        {
            if (item.Key is "account" or "settings") continue;

            var label = new TextBlock
            {
                Text = item.Title,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
            };
            label.Classes.Add("nav-label");

            var glyph = new TextBlock
            {
                Text = item.Glyph,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            glyph.Classes.Add("navGlyphText");
            var tile = new Border { Child = glyph, Classes = { "navGlyph" } };

            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
            };
            stack.Children.Add(tile);
            stack.Children.Add(label);

            var btn = new Button
            {
                Content = stack,
                Classes = { "nav" },
                Tag = item.Key,
            };
            btn.Click += (_, _) => ShowPage(item.Key);
            _navButtons[item.Key] = btn;
            _navGlyphs[item.Key] = tile;
            NavPanel.Children.Add(btn);
        }
    }

    private void ShowPage(string key)
    {
        var item = Items.FirstOrDefault(i => i.Key == key);
        if (item == null) return;

        PageTitle.Text = item.Title;
        PageSubtitle.Text = item.Subtitle;
        _current = key;

        foreach (var (k, btn) in _navButtons)
        {
            var active = k == key;
            btn.Classes.Set("active", active);
            if (_navGlyphs.TryGetValue(k, out var tile))
            {
                tile.Classes.Set("active", active);
                if (tile.Child is TextBlock glyph)
                    glyph.Classes.Set("active", active);
            }
        }
        BtnAccount.Classes.Set("active", key == "account");
        BtnSettings.Classes.Set("active", key == "settings");

        if (!_pages.TryGetValue(key, out var page))
        {
            page = key switch
            {
                "launch" => new LaunchView(this),
                "gacha" => new GachaView(),
                "settings" => new SettingsView(this),
                "account" => InfoView.Account(),
                "tasks" => InfoView.Tasks(),
                _ => InfoView.Mhy(),
            };
            _pages[key] = page;
        }

        PageHost.Content = page;
    }

    /// <summary>设置页改动主题后立即刷新强调色</summary>
    public void ReloadTheme()
    {
        UiTheme.Apply(SettingsService.Settings.Theme, SettingsService.Settings.CardOpacity);
    }

    private void ToggleSidebar()
    {
        _collapsed = !_collapsed;
        Sidebar.Width = _collapsed ? 76 : 212;

        BrandText.IsVisible = !_collapsed;
        LblAccount.IsVisible = !_collapsed;
        LblSettings.IsVisible = !_collapsed;

        foreach (var btn in _navButtons.Values)
        {
            if (btn.Content is StackPanel sp && sp.Children.Count > 1 && sp.Children[1] is TextBlock label)
                label.IsVisible = !_collapsed;
            btn.HorizontalContentAlignment = _collapsed
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Left;
            btn.Padding = _collapsed ? new Thickness(0, 8) : new Thickness(12, 8);
        }

        BtnAccount.HorizontalContentAlignment = _collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        BtnSettings.HorizontalContentAlignment = _collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
    }

    private void NavAccount_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => ShowPage("account");
    private void NavSettings_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => ShowPage("settings");
}
