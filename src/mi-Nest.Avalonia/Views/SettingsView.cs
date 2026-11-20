using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MiNest.Avalonia.Services;
using MiNest.Avalonia.Theme;
using MiNest.Services;

namespace MiNest.Avalonia.Views;

/// <summary>设置页：界面配色、不透明度、语言、游戏目录、关闭行为、开源说明与维护。</summary>
public class SettingsView : UserControl
{
    private readonly MainWindow? _main;
    private readonly WrapPanel _themeWrap = new() { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
    private readonly StackPanel _gamesPanel = new() { Spacing = 8 };

    public SettingsView(MainWindow? main = null)
    {
        _main = main;
        var body = new StackPanel { Spacing = 14, Margin = new Thickness(22, 18, 22, 22) };

        body.Children.Add(Card("界面配色", "选择强调色；「跟随系统」会随 macOS / Windows 的深浅色自动切换。", BuildThemes()));
        body.Children.Add(Card("界面不透明度", "统一控制所有卡片与框框的底色透明度。", BuildOpacity()));
        body.Children.Add(Card("语言切换", "默认跟随系统；切换后重新打开应用生效。", BuildLanguage()));
        body.Children.Add(Card("游戏目录", "用于「开始游戏」与桌面版抽卡记录同步，可分别指定每款游戏的安装目录。", BuildGames()));
        body.Children.Add(Card("关闭行为", "关闭窗口时是最小化到托盘还是直接退出。", BuildCloseBehavior()));
        body.Children.Add(Card("应用信息", "数据目录、版本与更新。", BuildInfo()));
        body.Children.Add(Card("开源说明", "本项目的壁纸同步、抽卡记录解析与界面思路参考了以下开源项目。", BuildCredits()));

        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        RefreshThemes();
    }

    private static Control Card(string title, string desc, Control content)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold });
        stack.Children.Add(new TextBlock { Text = desc, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#8A94A6")) });
        stack.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 6, 0, 6), Background = (IBrush)Application.Current!.Resources["Divider"]! });
        stack.Children.Add(content);
        return new Border
        {
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16, 14),
            Background = (IBrush)Application.Current!.Resources["CardBg"]!,
            BorderBrush = (IBrush)Application.Current!.Resources["CardBorder"]!,
            BorderThickness = new Thickness(1),
            Child = stack,
        };
    }

    private Control BuildThemes()
    {
        return _themeWrap;
    }

    private void RefreshThemes()
    {
        _themeWrap.Children.Clear();
        foreach (var t in UiTheme.Themes)
        {
            var dot = new Border
            {
                Width = 16, Height = 16, CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.Parse(t.Swatch)),
                BorderBrush = new SolidColorBrush(Color.Parse("#33000000")), BorderThickness = new Thickness(1),
            };
            var text = new TextBlock { Text = t.Name, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { dot, text } };
            var btn = new Button
            {
                Content = content,
                Height = 32, Padding = new Thickness(10, 0),
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1),
                Tag = t.Key,
            };
            var active = SettingsService.Settings.Theme == t.Key;
            btn.BorderBrush = active ? (IBrush)Application.Current!.Resources["Accent"]! : (IBrush)Application.Current!.Resources["CardBorder"]!;
            btn.Background = active ? (IBrush)Application.Current!.Resources["AccentSoft"]! : Brushes.Transparent;
            btn.Click += (_, _) =>
            {
                SettingsService.Settings.Theme = t.Key;
                SettingsService.Save();
                UiTheme.Apply(t.Key, SettingsService.Settings.CardOpacity);
                _main?.ReloadTheme();
                RefreshThemes();
            };
            _themeWrap.Children.Add(btn);
        }
    }

    private Control BuildOpacity()
    {
        var value = new TextBlock { Text = $"{SettingsService.Settings.CardOpacity}%", FontSize = 12, Width = 44, TextAlignment = TextAlignment.Right };
        var slider = new Slider { Minimum = 20, Maximum = 100, Value = SettingsService.Settings.CardOpacity, Width = 300 };
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            var v = (int)Math.Round(slider.Value);
            value.Text = $"{v}%";
            SettingsService.Settings.CardOpacity = v;
            UiTheme.Apply(SettingsService.Settings.Theme, v);
            SettingsService.Save();
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { slider, value } };
        return row;
    }

    private Control BuildLanguage()
    {
        var box = new ComboBox { Width = 220, FontSize = 12 };
        var options = new System.Collections.Generic.List<string>();
        var codes = new System.Collections.Generic.List<string>();
        foreach (var (code, display) in LanguageCatalog.Languages)
        {
            codes.Add(code);
            options.Add(display);
        }
        box.ItemsSource = options;
        box.SelectedIndex = Math.Max(0, codes.IndexOf(SettingsService.Settings.Language));
        box.SelectionChanged += (_, _) =>
        {
            SettingsService.Settings.Language = codes[Math.Max(0, box.SelectedIndex)];
            SettingsService.Save();
        };
        return box;
    }

    private Control BuildGames()
    {
        _gamesPanel.Children.Clear();
        foreach (var (key, name) in new[] { ("genshin", "原神"), ("starrail", "星穹铁道"), ("zzz", "绝区零"), ("bh3", "崩坏3") })
        {
            var path = SettingsService.Settings.GamePaths.TryGetValue(key, out var p) ? p : "";
            var box = new TextBox { Text = path, FontSize = 11.5, Watermark = "游戏安装目录", Width = 360 };
            box.TextChanged += (_, _) =>
            {
                SettingsService.Settings.GamePaths[key] = box.Text ?? "";
                SettingsService.Save();
            };
            var browse = new Button { Content = "浏览…", Classes = { "ghost" }, Height = 30, Padding = new Thickness(12, 0) };
            browse.Click += async (_, _) =>
            {
                var window = TopLevel.GetTopLevel(this) as Window;
                if (window?.StorageProvider == null) return;
                var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false, Title = $"选择{name}安装目录" });
                if (folders.Count == 0) return;
                var dir = folders[0].TryGetLocalPath();
                if (dir == null) return;
                SettingsService.Settings.GamePaths[key] = dir;
                SettingsService.Save();
                box.Text = dir;
            };
            var label = new TextBlock { Text = name, FontSize = 11.5, Width = 58, VerticalAlignment = VerticalAlignment.Center };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { label, box, browse } };
            _gamesPanel.Children.Add(row);
        }
        return _gamesPanel;
    }

    private Control BuildCloseBehavior()
    {
        var toggle = new ToggleSwitch
        {
            IsChecked = string.Equals(SettingsService.Settings.CloseBehavior, "Tray", StringComparison.OrdinalIgnoreCase),
            Content = "关闭窗口时最小化到托盘（不退出）",
            FontSize = 12,
        };
        toggle.IsCheckedChanged += (_, _) =>
        {
            SettingsService.Settings.CloseBehavior = toggle.IsChecked == true ? "Tray" : "Exit";
            SettingsService.Save();
        };
        return toggle;
    }

    private Control BuildInfo()
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(new TextBlock { Text = $"版本 {AppInfo.Version}", FontSize = 12 });
        stack.Children.Add(new TextBlock { Text = $"数据目录：{AppPaths.DataDir}", FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#8A94A6")) });

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var openDir = new Button { Content = "打开数据目录", Classes = { "ghost" }, Height = 32, Padding = new Thickness(14, 0) };
        openDir.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                if (OperatingSystem.IsMacOS()) Process.Start("open", AppPaths.DataDir);
                else Process.Start(new ProcessStartInfo(AppPaths.DataDir) { UseShellExecute = true });
            }
            catch { }
        };
        var check = new Button { Content = "检查更新", Classes = { "primary" }, Height = 32, Padding = new Thickness(16, 0) };
        check.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://github.com/Huiusto/mi-Nest/releases") { UseShellExecute = true }); } catch { }
        };
        row.Children.Add(openDir);
        row.Children.Add(check);
        stack.Children.Add(row);
        return stack;
    }

    private Control BuildCredits()
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock
        {
            Text = "Starward（Scighost）—— 抽卡记录解析与启动页壁纸同步思路；TeyvatGuide（DGP-Studio 生态）—— 设备 ID 与 UIGF 数据结构。仅参考实现思路，代码按本仓库许可独立编写。",
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#8A94A6")),
        });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var (label, url) in new[]
                 {
                     ("Starward", "https://github.com/Scighost/Starward"),
                     ("TeyvatGuide", "https://github.com/DGP-Studio/Snap.Hutao"),
                     ("本项目", "https://github.com/Huiusto/mi-Nest"),
                 })
        {
            var btn = new Button { Content = label, Classes = { "ghost" }, Height = 30, Padding = new Thickness(12, 0) };
            btn.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } };
            row.Children.Add(btn);
        }
        stack.Children.Add(row);
        return stack;
    }
}
