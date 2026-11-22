using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MiNest.Avalonia.Theme;
using MiNest.Models;
using MiNest.Services;

namespace MiNest.Avalonia.Views;

/// <summary>启动页：官方版本壁纸轮换（靠左圆点切换）、游戏切换、开始游戏 / 安装游戏。</summary>
public partial class LaunchView : UserControl
{
    private sealed record GameTab(string Key, string Name, string Sub, string Download);

    private static readonly GameTab[] Games =
    {
        new("genshin", "原神", "Genshin Impact", "https://ys.mihoyo.com/"),
        new("starrail", "崩坏：星穹铁道", "Honkai: Star Rail", "https://sr.mihoyo.com/"),
        new("zzz", "绝区零", "Zenless Zone Zero", "https://zzz.mihoyo.com/"),
        new("bh3", "崩坏3", "Honkai Impact 3rd", "https://bh3.mihoyo.com/"),
    };

    private string _gameKey = "genshin";
    private int _index;
    private IReadOnlyList<LauncherBackgroundService.BackgroundItem> _items = Array.Empty<LauncherBackgroundService.BackgroundItem>();
    private readonly Dictionary<string, Button> _tabs = new();

    public LaunchView() : this(null) { }

    public LaunchView(MainWindow? main = null)
    {
        InitializeComponent();
        BuildTabs();
        SelectGame(SettingsService.Settings.GamePaths.Count > 0 && File.Exists(SettingsService.Settings.GamePaths.First().Value)
            ? SettingsService.Settings.GamePaths.First().Key
            : "genshin", refresh: true);
    }

    private void BuildTabs()
    {
        foreach (var g in Games)
        {
            var btn = new Button
            {
                Content = g.Name,
                FontSize = 11.5,
                Padding = new Thickness(12, 6),
                CornerRadius = new CornerRadius(11),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.Parse("#CCFFFFFF")),
                BorderThickness = new Thickness(0),
                Tag = g.Key,
            };
            btn.Click += (_, _) => SelectGame(g.Key, refresh: true);
            _tabs[g.Key] = btn;
            GameChips.Children.Add(btn);
        }
    }

    private void SelectGame(string key, bool refresh)
    {
        _gameKey = Games.Any(g => g.Key == key) ? key : "genshin";
        var game = Games.First(g => g.Key == _gameKey);
        TxtGameName.Text = game.Name;
        TxtGameSub.Text = $"{game.Sub} · 官方最新版本壁纸";
        TxtVersion.Text = $"版本 {AppInfo.Version} · {(OperatingSystem.IsMacOS() ? "macOS" : "Windows")}";

        foreach (var (k, b) in _tabs)
        {
            var active = k == _gameKey;
            b.Background = new SolidColorBrush(active ? Color.Parse("#33FFFFFF") : Colors.Transparent);
            b.Foreground = new SolidColorBrush(active ? Colors.White : Color.Parse("#CCFFFFFF"));
            b.FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal;
        }

        _index = SettingsService.Settings.GameBackgroundIndex.TryGetValue(_gameKey, out var i) ? i : 0;
        LoadCached();
        if (refresh) _ = RefreshAsync();

        // 有自定义壁纸的游戏直接用自定义图
        var custom = SettingsService.Settings.GameBackgrounds.TryGetValue(_gameKey, out var p) ? p : null;
        if (!string.IsNullOrEmpty(custom) && File.Exists(custom)) ShowImage(custom);
    }

    private void LoadCached()
    {
        var cached = LauncherBackgroundService.GetCached(_gameKey);
        _items = cached?.Items.Where(x => x.ImagePath != null).ToList() ?? new List<LauncherBackgroundService.BackgroundItem>();
        BuildDots();
        ApplyCurrent();
        TxtSync.Text = _items.Count > 0 ? $"官方壁纸 {_items.Count} 张 · 已缓存" : "正在同步官方壁纸…";
    }

    private async Task RefreshAsync()
    {
        try
        {
            var cached = await LauncherBackgroundService.RefreshAsync(_gameKey);
            if (cached == null) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cached.Key != _gameKey) return;
                _items = cached.Items.Where(x => x.ImagePath != null).ToList();
                BuildDots();
                ApplyCurrent();
                TxtSync.Text = _items.Count > 0 ? $"官方壁纸 {_items.Count} 张 · 已同步" : "官方暂未提供壁纸";
            });
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() => TxtSync.Text = "壁纸同步失败（可稍后重试）");
        }
    }

    private void BuildDots()
    {
        DotPanel.Children.Clear();
        var count = Math.Max(1, _items.Count);
        if (_index >= count) _index = 0;

        for (var i = 0; i < count; i++)
        {
            var idx = i;
            var dot = new Button
            {
                Width = 10,
                Height = 10,
                MinWidth = 10,
                MinHeight = 10,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(i == _index ? Colors.White : Color.Parse("#66FFFFFF")),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            global::Avalonia.Controls.ToolTip.SetTip(dot, $"第 {i + 1} 张壁纸");
            dot.Click += (_, _) =>
            {
                _index = idx;
                SettingsService.Settings.GameBackgroundIndex[_gameKey] = idx;
                SettingsService.Save();
                BuildDots();
                ApplyCurrent();
            };
            DotPanel.Children.Add(dot);
        }
    }

    private void ApplyCurrent()
    {
        if (_items.Count == 0) return;
        var item = _items[Math.Clamp(_index, 0, _items.Count - 1)];
        if (item.ImagePath != null) ShowImage(item.ImagePath);
    }

    private void ShowImage(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Backdrop.Source = new Bitmap(stream);
        }
        catch { }
    }

    private void BtnPlay_Click(object sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dir = SettingsService.Settings.GamePaths.TryGetValue(_gameKey, out var p) ? p : null;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            ShowDialog("还没有设置游戏目录", $"请在「设置 → 游戏目录」里指定{_gameKey}的安装位置后再开始游戏。");
            return;
        }

        var exeNames = _gameKey switch
        {
            "genshin" => new[] { "YuanShen.exe", "GenshinImpact.exe" },
            "starrail" => new[] { "StarRail.exe" },
            "zzz" => new[] { "ZenlessZoneZero.exe" },
            _ => new[] { "BH3.exe" },
        };
        var exe = exeNames.Select(n => Path.Combine(dir, n)).FirstOrDefault(File.Exists);
        if (exe == null)
        {
            ShowDialog("没有找到游戏主程序", $"目录：{dir}\n请确认游戏已完整安装，或在设置里重新选择目录。");
            return;
        }

        try { Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = dir, UseShellExecute = true }); }
        catch (Exception ex) { ShowDialog("启动失败", ex.Message); }
    }

    private void BtnGameSettings_Click(object sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var game = Games.First(g => g.Key == _gameKey);
        var dir = SettingsService.Settings.GamePaths.TryGetValue(_gameKey, out var p) ? p : "";
        var win = new Window
        {
            Title = $"游戏设置 · {game.Name}",
            Width = 500,
            Height = 330,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (IBrush?)Application.Current?.Resources["CardBg"] ?? Brushes.White,
            ShowInTaskbar = false,
        };
        var body = new StackPanel { Margin = new Thickness(22), Spacing = 12 };
        body.Children.Add(new TextBlock { Text = $"游戏设置 · {game.Name}", FontSize = 18, FontWeight = FontWeight.Bold });
        body.Children.Add(new TextBlock
        {
            Text = $"安装目录：{(string.IsNullOrEmpty(dir) ? "尚未定位" : dir)}",
            FontSize = 12, TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(new TextBlock
        {
            Text = "可在下方打开目录或重新定位游戏。",
            FontSize = 11.5, Foreground = new SolidColorBrush(Color.Parse("#8A94A6")),
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var open = new Button { Content = "打开安装目录", Classes = { "ghost" }, Height = 34, Padding = new Thickness(14, 0) };
        open.Click += (_, _) => OpenPath(dir);
        var locate = new Button { Content = "重新定位游戏", Classes = { "ghost" }, Height = 34, Padding = new Thickness(14, 0) };
        locate.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top == null) return;
            var folders = await top.StorageProvider.OpenFolderPickerAsync(new global::Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = $"选择{game.Name}安装目录", AllowMultiple = false,
            });
            var folder = folders.FirstOrDefault();
            var path = folder?.Path?.LocalPath;
            if (string.IsNullOrEmpty(path)) return;
            SettingsService.Settings.GamePaths[_gameKey] = path;
            SettingsService.Save();
            win.Close();
            ShowDialog("已更新游戏目录", path);
        };
        row.Children.Add(open);
        row.Children.Add(locate);
        body.Children.Add(row);

        var close = new Button { Content = "关闭", Classes = { "primary" }, Height = 34, Padding = new Thickness(22, 0), HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => win.Close();
        body.Children.Add(close);
        win.Content = body;
        win.ShowDialog((Window)TopLevel.GetTopLevel(this)!);
    }

    private static void OpenPath(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path)) return;
            if (OperatingSystem.IsMacOS()) Process.Start("open", path);
            else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { }
    }

    private void BtnGet_Click(object sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var game = Games.First(g => g.Key == _gameKey);
        var win = new Window
        {
            Title = $"获取{game.Name}",
            Width = 420,
            Height = 230,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#F7FFFFFF")),
        };

        var text = new TextBlock
        {
            Text = $"将打开{game.Name}官方下载页面，下载安装包后回到 MikoNest 设置好游戏目录即可一键启动。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.Parse("#4A5468")),
        };
        var open = new Button { Content = "打开官网", Classes = { "primary" }, Height = 36, Padding = new Thickness(20, 0) };
        var cancel = new Button { Content = "取消", Classes = { "ghost" }, Height = 36, Padding = new Thickness(18, 0) };
        cancel.Click += (_, _) => win.Close();
        open.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(game.Download) { UseShellExecute = true }); } catch { }
            win.Close();
        };

        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = game.Name, FontSize = 18, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(text);
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, open },
        });
        win.Content = panel;
        win.ShowDialog((Window)TopLevel.GetTopLevel(this)!);
    }

    private void ShowDialog(string title, string message)
    {
        var win = new Window
        {
            Title = title,
            Width = 420,
            Height = 200,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#F7FFFFFF")),
        };
        var ok = new Button { Content = "好", Classes = { "primary" }, Height = 34, Padding = new Thickness(22, 0), HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => win.Close();
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 14 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 });
        panel.Children.Add(ok);
        win.Content = panel;
        win.ShowDialog((Window)TopLevel.GetTopLevel(this)!);
    }
}
