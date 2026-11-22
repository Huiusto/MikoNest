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
        var body = new StackPanel { Spacing = 16, Margin = new Thickness(26, 20, 26, 26) };

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
        var stack = new StackPanel { Spacing = 8 };

        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        head.Children.Add(new Border
        {
            Width = 3, Height = 16, CornerRadius = new CornerRadius(2),
            Background = UiTheme.Brush("BrandGradient", "#4C6FFF"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        head.Children.Add(new TextBlock
        {
            Text = title, FontSize = 15.5, FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(head);

        stack.Children.Add(new TextBlock
        {
            Text = desc, FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
            Foreground = UiTheme.Brush("TextSecondary", "#68718A"),
            Margin = new Thickness(13, 0, 0, 2),
        });
        stack.Children.Add(new Border
        {
            Margin = new Thickness(13, 2, 0, 6),
            Child = content,
        });

        return new Border
        {
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(20, 18, 20, 18),
            Background = UiTheme.Brush("CardBg", "#FFFFFF"),
            BorderBrush = UiTheme.Brush("CardBorder", "#E4E9F5"),
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 12 30 0 #101B2A4D"),
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
                Height = 34, Padding = new Thickness(12, 0, 14, 0),
                CornerRadius = new CornerRadius(11),
                BorderThickness = new Thickness(1),
                FontSize = 11.5,
                Tag = t.Key,
            };
            var active = SettingsService.Settings.Theme == t.Key;
            btn.BorderBrush = active ? UiTheme.Brush("Accent", "#4C6FFF") : UiTheme.Brush("CardBorder", "#E4E9F5");
            btn.Background = active ? UiTheme.Brush("AccentSoft", "#224C6FFF") : UiTheme.Brush("InputBg", "#F3F6FC");
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
        check.Click += async (_, _) => await CheckUpdateAsync(check);
        var changelog = new Button { Content = "更新日志", Classes = { "ghost" }, Height = 32, Padding = new Thickness(14, 0) };
        changelog.Click += (_, _) => ShowChangelog();
        row.Children.Add(openDir);
        row.Children.Add(check);
        row.Children.Add(changelog);
        stack.Children.Add(row);
        return stack;
    }

    private void ShowChangelog()
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        var win = new Window
        {
            Title = "更新日志",
            Width = 620,
            Height = 660,
            CanResize = false,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Background = (IBrush?)Application.Current?.Resources["PageBg"] ?? Brushes.White,
            ShowInTaskbar = false,
        };

        var entries = new (string Version, string Date, string Text)[]
        {
            ("0.1.28", "2026-11-22", "软件由原名 mi-Nest 正式更名为 MikoNest；启用全新蓝白 Q 版软件图标与蔚蓝圆润侧栏字标；安装目录、主程序、快捷方式、开机启动项与用户数据目录同步迁移，原设置、账号与抽卡记录自动继承。(๑•̀ㅂ•́)و✧"),
            ("0.1.27", "2026-11-22", "原神武器活动祈愿 5★ 保底条改为 0-80，0-65 绿、66-80 红；Windows 版千星奇域常驻颂愿 3★ 保底条改为 0-90，0-70 绿、71-80 红，81-90 红色收尾。(๑•̀ㅂ•́)و✧"),
            ("0.1.26", "2026-11-21", "Windows 版设置页「界面配色」「语言切换」「应用维护」三张卡片降低并统一高度；「应用维护」的「检查更新」按钮在更新时显示 0% 到 100% 的下载进度；「发现新版本」更新弹窗尺寸缩小。(￣▽￣)ノ"),
            ("0.1.25", "2026-11-21", "修复启动页点击「安装游戏」时，旧安装目录仍存在但找不到游戏主程序，只会提示定位失败而无法进入安装的问题；现在会直接打开安装窗口重新选择路径并开始安装。(๑•̀ㅂ•́)و✧"),
            ("0.1.24", "2026-11-21", "启动页「获取游戏」改为「安装游戏」；打开软件自动检查 MikoNest 更新，发现新版本立即弹出更新窗口，窗口内展示本次 Release 更新日志，点击「开始更新」1 秒后自动最小化并在后台下载安装；设置页「界面配色」与「语言切换」「应用维护」卡片等高。(￣▽￣)ノ"),
            ("0.1.23", "2026-11-21", "游戏设置新增「卸载游戏」；启动页首页固定显示自定义顺序里的第一款游戏；修复停止安装并清理后主按钮误显示「开始游戏」，以及更新弹窗右上角关闭按钮只显示一半的问题。(￣▽￣)ノ"),
            ("0.1.22", "2026-11-21", "安装详情里的「停止安装并清理」移到「游戏设置」，游戏设置不再显示「卸载游戏」；安装进度配色改为跟随 Windows 系统强调色；启动页「全部游戏」下拉支持拖动排序并自动保存。(￣▽￣)ノ"),
            ("0.1.21", "2026-11-21", "安装软件时自动预取启动页动态壁纸；安装游戏进度内嵌在启动页主按钮位置，鼠标靠近显示下载详情，可暂停、继续、停止并清理；同一新版本自动更新只提示一次，取消即忽略该版本。(￣▽￣)ノ"),
            ("0.1.20", "2026-11-20", "启动页「游戏设置」改为三条横杠图标；安装游戏改为官方启动器式逐文件直装，可中途停止并清理；开始游戏前自动检查更新并弹出提醒。"),
            ("0.1.19", "2026-11-20", "设置页新增「更新日志」；启动页新增「游戏设置」；抽卡 UID 下拉只保留具体 UID（无账号显示「无账号」）；常驻颂愿 4★ 0-90、3★ 0-10 保底条；绝区零 / 星铁 / 崩坏3 限定 4★、5★ 加入金色 UP!。ヾ(≧▽≦*)o"),
            ("0.1.18", "2026-11-20", "修复扫码登录换取 cookie_token 失败；修复从启动页点击社区不切换的问题。(＾▽＾)"),
            ("0.1.17", "2026-11-20", "修复扫码登录后缺少 stoken / cookie_token；抽卡更新后保持当前 UID；4★ 保底条改为 0-10。(￣▽￣)ノ"),
            ("0.1.16", "2026-11-20", "启动页后台预加载全部动态壁纸；官方换版本后清理旧壁纸；修复托盘启动黑屏。(＾▽＾)"),
            ("0.1.15", "2026-11-20", "签到图形验证码在软件内完成验证并自动重试；修复一键更新无法覆盖旧版本。(≧ω≦)"),
            ("0.1.13", "2026-11-20", "新增 Avalonia 跨平台 UI 与 macOS 安装包；四游戏抽卡接口与保底条、UP!、配色与语言抽屉优化。"),
            ("0.1.12", "2026-11-19", "版本迭代与稳定性修复。"),
            ("0.1.11", "2026-11-19", "版本迭代与界面细节优化。"),
            ("0.1.10", "2026-11-19", "版本迭代与稳定性修复。"),
            ("0.1.9", "2026-11-19", "版本迭代与稳定性修复。"),
            ("0.1.8", "2026-11-19", "版本迭代与稳定性修复。"),
            ("0.1.7", "2026-11-19", "启动页接入官方壁纸接口；获取游戏改为安装弹窗；修复托盘还原与弹窗裁切。_(:з」∠)_"),
            ("0.1.5", "2026-11-17", "首个公开版本：启动游戏、社区、抽卡记录、每日签到、HOYO-MiX、账号与设置。"),
        };

        var body = new StackPanel { Margin = new Thickness(22), Spacing = 10 };
        body.Children.Add(new TextBlock { Text = "更新日志  (≧▽≦)", FontSize = 22, FontWeight = FontWeight.Bold });
        body.Children.Add(new TextBlock
        {
            Text = "MikoNest 从首个公开版本到 0.1.28 的更新与修复记录",
            FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#8A94A6")),
        });

        foreach (var (version, date, text) in entries)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 12),
                Background = (IBrush?)Application.Current?.Resources["CardBg"] ?? Brushes.White,
                BorderBrush = (IBrush?)Application.Current?.Resources["CardBorder"] ?? Brushes.LightGray,
                BorderThickness = new Thickness(1),
            };
            var inner = new StackPanel { Spacing = 6 };
            inner.Children.Add(new TextBlock { Text = $"v{version}  {date}", FontSize = 14.5, FontWeight = FontWeight.Bold });
            inner.Children.Add(new TextBlock { Text = text, FontSize = 12, LineHeight = 19, TextWrapping = TextWrapping.Wrap });
            card.Child = inner;
            body.Children.Add(card);
        }

        win.Content = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        if (owner != null) win.ShowDialog(owner); else win.Show();
    }

    private bool _checkingUpdate;

    /// <summary>检查更新 → 确认 → 下载安装包 → 静默安装并重启。macOS 版只引导到下载页。</summary>
    private async Task CheckUpdateAsync(Button button)
    {
        if (_checkingUpdate) return;
        _checkingUpdate = true;

        var owner = TopLevel.GetTopLevel(this) as Window;
        var current = typeof(SettingsView).Assembly.GetName().Version ?? new Version(0, 1, 0, 0);
        button.IsEnabled = false;
        button.Content = "检查中…";

        try
        {
            var (info, error) = await UpdateService.GetLatestAsync();
            if (info == null)
            {
                await MessageDialog.Show(owner, "检查更新失败",
                    "没能连上 GitHub 的发布接口。\n\n" + (error ?? "未知错误") +
                    "\n\n可以稍后重试，或打开 Release 页手动下载。");
                return;
            }

            var latest = UpdateService.ParseTag(info.Version);
            if (latest != null && latest <= current)
            {
                await MessageDialog.Show(owner, "已是最新版本",
                    $"当前版本 v{current.ToString(3)}，已经是仓库里的最新发布。");
                return;
            }

            if (!OperatingSystem.IsWindows())
            {
                var open = await MessageDialog.Confirm(owner, "发现新版本",
                    $"发现 v{info.Version}（当前 v{current.ToString(3)}）。\n\n" +
                    "macOS 版请到 Release 页下载对应的 .zip 包，解压后替换原来的 .app。",
                    "打开下载页", "稍后");
                if (open) OpenUrl(info.ReleasePage);
                return;
            }

            if (string.IsNullOrEmpty(info.AssetUrl))
            {
                var open = await MessageDialog.Confirm(owner, "发现新版本",
                    $"发现 v{info.Version}（当前 v{current.ToString(3)}），但该版本没有附带安装包。\n\n是否打开下载页手动下载？",
                    "打开下载页", "稍后");
                if (open) OpenUrl(info.ReleasePage);
                return;
            }

            var sizeText = info.AssetSize > 0 ? $"（{info.AssetSize / 1024d / 1024d:F1} MB）" : "";
            var ok = await MessageDialog.Confirm(owner, "发现新版本",
                $"发现 v{info.Version}（当前 v{current.ToString(3)}）。\n\n" +
                $"安装包：{info.AssetName} {sizeText}\n\n" +
                "点「下载并安装」后会自下载、静默安装并重启软件；安装时会弹出管理员授权窗口，请点「是」。",
                "下载并安装", "稍后");
            if (!ok) return;

            button.Content = "下载 0%";
            var path = await UpdateService.DownloadAsync(info, (done, total) =>
            {
                if (total <= 0) return;
                var pct = (int)Math.Round(done * 100d / total);
                button.Content = $"下载 {pct}%";
            });

            button.Content = "安装中…";
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = BuildInstallerArgs(),
                UseShellExecute = true,
                Verb = "runas",
            });
            await Task.Delay(1200);
            if (Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
                life.Shutdown();
        }
        catch (Exception ex)
        {
            await MessageDialog.Show(owner, "更新失败",
                ex.Message + "\n\n安装包可能已保存在临时目录，也可以到 Release 页手动下载。");
        }
        finally
        {
            _checkingUpdate = false;
            button.IsEnabled = true;
            button.Content = "检查更新";
        }
    }

    /// <summary>静默安装参数：便携版就地覆盖当前目录，已安装版沿用原安装目录（/D 必须放最后）。</summary>
    private static string BuildInstallerArgs()
    {
        try
        {
            var dir = AppContext.BaseDirectory.TrimEnd('\\', '/');
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var inProgramFiles = dir.StartsWith(pf, StringComparison.OrdinalIgnoreCase)
                                 || dir.StartsWith(pf86, StringComparison.OrdinalIgnoreCase);
            return inProgramFiles ? "/S" : "/S /D=" + dir;
        }
        catch
        {
            return "/S";
        }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
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
                     ("本项目", "https://github.com/Huiusto/MikoNest"),
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

