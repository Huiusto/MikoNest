using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using MiNest.Models;
using MiNest.Services;

namespace MiNest.Pages;

/// <summary>
/// 启动页（仿 Starward，简洁版）：
/// 背景跟随所选游戏切换（四款游戏各有专属官方宣传图），顶部「米哈云游 / 全部游戏」，
/// 底部预约游戏 + 右下角「定位游戏→开始游戏」；左侧竖排圆点切换器（鼠标靠近浮现、离开消失）。
/// </summary>
public partial class LaunchPage : UserControl
{
    private readonly MainWindow _main;
    private string _selectedKey;
    private string? _locatedDir;
    private readonly HashSet<string> _runningGames = new();   // 正在运行的游戏（按键名分别记录）
    private readonly List<(Button Dot, string Key)> _dots = new();

    public LaunchPage(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        _selectedKey = "genshin";
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AutoSelectLocatedGame();
        ApplyBackground();
        BuildAllGamesMenu();
        BuildDots();
        RefreshState();
    }

    /// <summary>打开软件时自动选中已定位的游戏：哪个游戏被定位了，启动页就显示哪个游戏的启动画面（背景）</summary>
    private void AutoSelectLocatedGame()
    {
        var located = GameLocator.Games
            .Where(g => (SettingsService.Settings.GamePaths.TryGetValue(g.Key, out var dp) && Directory.Exists(dp))
                        || GameLocator.DetectInstallDir(g) != null)
            .Select(g => g.Key)
            .ToList();
        if (located.Count > 0 && !located.Contains(_selectedKey))
            _selectedKey = located[0];
    }

    // ---------- 左侧圆点切换器 ----------

    private void BuildDots()
    {
        DotStack.Children.Clear();
        _dots.Clear();
        foreach (var g in GameLocator.Games)
        {
            var dot = new Button
            {
                Style = (Style)FindResource("DotButtonStyle"),
                ToolTip = g.Name,
                Tag = "inactive",
            };
            dot.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, g.Name);
            dot.Click += (_, _) => SelectGame(g.Key);
            DotStack.Children.Add(dot);
            _dots.Add((dot, g.Key));
        }
        UpdateDotStates();
    }

    private void UpdateDotStates()
    {
        foreach (var (dot, key) in _dots)
            dot.Tag = key == _selectedKey ? "active" : "inactive";
    }

    /// <summary>鼠标靠近内容区左边缘（约 130px 内）时浮现圆点切换器，移开后淡出。</summary>
    private void RootGrid_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (pos.X <= 140)
        {
            if (DotPanel.Visibility != Visibility.Visible)
            {
                DotPanel.Visibility = Visibility.Visible;
                DotPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
            }
        }
        else if (DotPanel.Visibility == Visibility.Visible)
        {
            var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            anim.Completed += (_, _) => DotPanel.Visibility = Visibility.Collapsed;
            DotPanel.BeginAnimation(OpacityProperty, anim);
        }
    }

    // 鼠标完全离开本页面时隐藏圆点（防止从左侧移出窗口后残留）
    private void OnRootMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (DotPanel.Visibility == Visibility.Visible)
        {
            DotPanel.Visibility = Visibility.Collapsed;
            DotPanel.BeginAnimation(OpacityProperty, null);
        }
    }

    // ---------- 背景 ----------

    private void ApplyBackground()
    {
        // 每款游戏可单独设置自定义背景；未设置时使用该游戏专属官图
        var custom = SettingsService.Settings.GameBackgrounds.TryGetValue(_selectedKey, out var p) && File.Exists(p)
            ? p
            : null;
        if (custom == null)
            custom = SettingsService.Settings.CustomBackground; // 兼容旧版全局自定义背景
        if (custom != null && File.Exists(custom))
        {
            BgImage.Source = new BitmapImage(new Uri(custom));
            BtnResetBg.Visibility = Visibility.Visible;
            return;
        }

        BtnResetBg.Visibility = Visibility.Collapsed;
        try
        {
            // 每款游戏一张专属背景：星穹铁道使用 Assets/bg_StarRail.jpg，其余为 Assets/bg_{key}.jpg
            var bgName = _selectedKey == "starrail" ? "bg_StarRail.jpg" : $"bg_{_selectedKey}.jpg";
            BgImage.Source = new BitmapImage(new Uri($"pack://application:,,,/Assets/{bgName}"));
        }
        catch
        {
            BgImage.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/background.jpg"));
        }
    }

    private void BtnChangeBg_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.OpenFileDialog
        {
            Title = $"选择「{GameLocator.Get(_selectedKey).Name}」启动页背景图",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.webp|所有文件|*.*",
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            SettingsService.Settings.GameBackgrounds[_selectedKey] = dlg.FileName;
            SettingsService.Save();
            ApplyBackground();
        }
    }

    private void BtnResetBg_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.Settings.GameBackgrounds.Remove(_selectedKey);
        SettingsService.Settings.CustomBackground = null; // 一并清除旧版全局背景，回到该游戏专属官图
        SettingsService.Save();
        ApplyBackground();
    }

    // ---------- 全部游戏菜单 ----------

    private void BuildAllGamesMenu()
    {
        AllGamesMenuPanel.Children.Clear();
        foreach (var g in GameLocator.Games)
        {
            var located = (SettingsService.Settings.GamePaths.TryGetValue(g.Key, out var dp) && Directory.Exists(dp))
                          || GameLocator.DetectInstallDir(g) != null;

            var btn = new Button
            {
                Tag = g.Key,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(14, 7, 14, 7),
                Margin = new Thickness(0, 2, 0, 2),
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = g.Name,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimary"),
            });
            sp.Children.Add(new TextBlock
            {
                Text = located ? "● 已定位" : "○ 未定位",
                FontSize = 10.5,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = located ? (Brush)FindResource("Success") : (Brush)FindResource("Warning"),
            });
            btn.Content = sp;
            btn.Click += (_, _) =>
            {
                BtnAllGames.IsChecked = false;
                SelectGame(g.Key);
            };
            AllGamesMenuPanel.Children.Add(btn);
        }
    }

    private void SelectGame(string key)
    {
        if (key == _selectedKey) return;
        _selectedKey = key;
        ApplyBackground();
        UpdateDotStates();
        RefreshState();
    }

    // ---------- 定位 / 启动 ----------

    private string? ResolveDir()
    {
        var g = GameLocator.Get(_selectedKey);
        if (SettingsService.Settings.GamePaths.TryGetValue(_selectedKey, out var p) && Directory.Exists(p))
            return p;

        var detected = GameLocator.DetectInstallDir(g);
        if (detected != null)
        {
            SettingsService.Settings.GamePaths[_selectedKey] = detected;
            SettingsService.Save();
        }
        return detected;
    }

    private void RefreshState()
    {
        var g = GameLocator.Get(_selectedKey);
        _locatedDir = ResolveDir();
        var exe = _locatedDir != null ? GameLocator.FindExe(_locatedDir, g) : null;

        GameLabel.Text = $"当前游戏：{g.Name}";

        if (_runningGames.Contains(_selectedKey))
        {
            // 该游戏运行中：按钮保持「游戏启动中」，直到这款游戏的进程退出
            BtnMain.Content = "游戏启动中";
            BtnMain.IsEnabled = false;
            LocateHint.Text = _locatedDir != null ? $"已定位：{_locatedDir}" : LocateHint.Text;
            return;
        }

        if (exe != null)
        {
            BtnMain.Content = "开始游戏";
            LocateHint.Text = $"已定位：{_locatedDir}";
        }
        else if (_locatedDir != null)
        {
            BtnMain.Content = "开始游戏";
            LocateHint.Text = $"已定位目录，但未找到主程序（{string.Join(" / ", g.ExeNames)}）";
        }
        else
        {
            BtnMain.Content = "获取游戏";
            LocateHint.Text = "点击「获取游戏」将打开该游戏官网（可自行下载安装）；定位到游戏后按钮将变为「开始游戏」，也可在「全部游戏」中切换游戏并单独更换背景。";
        }
    }

    private async void BtnMain_Click(object sender, RoutedEventArgs e)
    {
        var g = GameLocator.Get(_selectedKey);
        _locatedDir = ResolveDir();
        var exe = _locatedDir != null ? GameLocator.FindExe(_locatedDir, g) : null;

        if (exe != null)
        {
            var key = _selectedKey;   // 记录本次启动的是哪款游戏
            _runningGames.Add(key);
            BtnMain.Content = "游戏启动中";
            BtnMain.IsEnabled = false;

            Process? proc = null;
            var workDir = Path.GetDirectoryName(exe) ?? _locatedDir;
            try
            {
                // 先以非 shell 方式启动，拿到进程句柄用于判断游戏是否结束
                proc = Process.Start(new ProcessStartInfo(exe)
                {
                    WorkingDirectory = workDir,
                    UseShellExecute = false,
                });
            }
            catch
            {
                // 需要提升权限时回退到 shell 启动，再按进程名跟踪
                try
                {
                    Process.Start(new ProcessStartInfo(exe)
                    {
                        WorkingDirectory = workDir,
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex)
                {
                    RestoreLaunchState(key);
                    MessageBox.Show($"启动失败：{ex.Message}", "mi-Nest", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            // 启动 3 秒后把窗口最小化，交给游戏前台运行
            await Task.Delay(3000);
            var win = Window.GetWindow(this);
            if (win != null) win.WindowState = WindowState.Minimized;

            // 「游戏启动中」保持显示，直到游戏进程退出
            try
            {
                if (proc == null) proc = await FindGameProcessAsync(exe, 10);
                if (proc != null) await proc.WaitForExitAsync();
            }
            catch
            {
                // 进程不可跟踪时不再阻塞按钮
            }
            RestoreLaunchState(key);
            return;
        }

        // 未定位：优先注册表自动检测；检测不到时打开官网（软件内），不再弹文件夹选择
        if (_locatedDir == null)
        {
            if (!string.IsNullOrEmpty(g.OfficialUrl))
                _main.OpenInApp(g.OfficialUrl);
            else
                PickFolder();
        }
        else
        {
            MessageBox.Show("未在目录中找到游戏主程序，请点击下方「定位游戏」按钮手动选择。", "mi-Nest", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>按进程名轮询游戏进程（shell 启动拿不到句柄时使用）</summary>
    private static async Task<Process?> FindGameProcessAsync(string exe, int timeoutSeconds)
    {
        var name = Path.GetFileNameWithoutExtension(exe);
        for (var i = 0; i < timeoutSeconds * 4; i++)
        {
            var procs = Process.GetProcessesByName(name);
            if (procs.Length > 0) return procs[0];
            await Task.Delay(250);
        }
        return null;
    }

    /// <summary>指定游戏结束（或启动失败）后，仅恢复该游戏的按钮状态</summary>
    private void RestoreLaunchState(string key)
    {
        _runningGames.Remove(key);
        if (key != _selectedKey) return;   // 已切到别的游戏，当前页面不受影响

        BtnMain.Content = "开始游戏";
        BtnMain.IsEnabled = true;
        RefreshState();
    }

    private void BtnRelocate_Click(object sender, RoutedEventArgs e) => PickFolder();

    private void PickFolder()
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = $"请选择「{GameLocator.Get(_selectedKey).Name}」的安装目录（包含 {string.Join(" / ", GameLocator.Get(_selectedKey).ExeNames)} 的文件夹）",
            UseDescriptionForTitle = true,
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            SettingsService.Settings.GamePaths[_selectedKey] = dlg.SelectedPath;
            SettingsService.Save();
            RefreshState();
        }
    }

    // ---------- 顶部 / 预约 ----------

    private void BtnMhyy_Click(object sender, RoutedEventArgs e)
        => _main.OpenInApp("https://mhyy.mihoyo.com/");

    private void Reserve_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string url)
            _main.OpenInApp(url);
    }
}
