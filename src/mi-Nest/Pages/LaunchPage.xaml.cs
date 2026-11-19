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
    private readonly List<(Button Dot, int Index)> _dots = new();
    private List<LauncherBackgroundService.BackgroundItem> _bgItems = new();  // 当前游戏官方背景列表
    private int _bgIndex;                                    // 当前显示第几张官方背景
    private string? _bgId;                                    // 当前显示的官方背景版本 ID
    private LauncherBackgroundService.PreDownloadInfo? _preDownload;

    public LaunchPage(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        _selectedKey = "genshin";
        Loaded += OnLoaded;
        IsVisibleChanged += OnPageVisibleChanged;
        Unloaded += (_, _) => StopVideo();
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

    /// <summary>左侧圆点数量 = 当前游戏官方下发的背景数量（官方一版给几张就有几个点）</summary>
    private void BuildDots()
    {
        DotStack.Children.Clear();
        _dots.Clear();

        var count = Math.Max(_bgItems.Count, 1);
        for (var i = 0; i < count; i++)
        {
            var index = i;
            var dot = new Button
            {
                Style = (Style)FindResource("DotButtonStyle"),
                ToolTip = count > 1 ? $"背景 {i + 1} / {count}" : "当前背景",
                Tag = "inactive",
            };
            dot.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, $"背景 {i + 1}");
            dot.Click += (_, _) => SelectBackground(index);
            DotStack.Children.Add(dot);
            _dots.Add((dot, index));
        }
        UpdateDotStates();
    }

    private void UpdateDotStates()
    {
        foreach (var (dot, index) in _dots)
            dot.Tag = index == _bgIndex ? "active" : "inactive";
    }

    /// <summary>点某个圆点：切到官方下发的第 N 张背景（选择了官方背景即清除该游戏的自定义图）</summary>
    private void SelectBackground(int index)
    {
        if (index < 0) return;
        SettingsService.Settings.GameBackgrounds.Remove(_selectedKey);
        SettingsService.Settings.CustomBackground = null;   // 旧版全局自定义背景一并清除
        SettingsService.Settings.GameBackgroundIndex[_selectedKey] = index;
        SettingsService.Save();

        _bgIndex = index;
        BtnResetBg.Visibility = Visibility.Collapsed;
        ShowOfficialBackground();
        UpdateDotStates();
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
        // 每款游戏可单独设置自定义背景；未设置时显示官方启动器下发的当前版本背景
        var key = _selectedKey;
        var cached = LauncherBackgroundService.GetCached(key);
        _bgItems = cached?.Items ?? new List<LauncherBackgroundService.BackgroundItem>();

        var custom = SettingsService.Settings.GameBackgrounds.TryGetValue(key, out var p) && File.Exists(p)
            ? p
            : null;
        if (custom == null)
            custom = SettingsService.Settings.CustomBackground; // 兼容旧版全局自定义背景
        if (custom != null && File.Exists(custom))
        {
            StopVideo();
            BgImage.Source = new BitmapImage(new Uri(custom));
            BtnResetBg.Visibility = Visibility.Visible;
            _bgId = null;
            foreach (var (dot, _) in _dots) dot.Tag = "inactive";
            BuildDots();          // 圆点仍按官方数量显示，点击即回到官方壁纸
            foreach (var (dot, _) in _dots) dot.Tag = "inactive";
            return;
        }

        BtnResetBg.Visibility = Visibility.Collapsed;

        // 当前游戏记住上次选中的第几张背景（默认第一张 = 官方最新）
        _bgIndex = SettingsService.Settings.GameBackgroundIndex.TryGetValue(key, out var saved)
            ? Math.Clamp(saved, 0, Math.Max(0, _bgItems.Count - 1))
            : 0;

        BuildDots();
        ShowOfficialBackground();

        // 再后台向官方接口核对：版本更新后自动换成新背景（含动态背景）
        _ = RefreshOfficialBackgroundAsync(key);
    }

    /// <summary>按 _bgIndex 显示官方背景（静态壁纸 + 动态视频按需下载播放）</summary>
    private void ShowOfficialBackground()
    {
        if (_bgItems.Count == 0)
        {
            SetBundledBackground(_selectedKey);
            StopVideo();
            _bgId = null;
            return;
        }

        _bgIndex = Math.Clamp(_bgIndex, 0, _bgItems.Count - 1);
        var item = _bgItems[_bgIndex];
        _bgId = item.Id;

        if (item.ImagePath != null) SetImageFromFile(item.ImagePath);
        else SetBundledBackground(_selectedKey);

        StopVideo();
        if (item.VideoPath != null) PlayVideo(item.VideoPath);
        else _ = EnsureVideoPlayAsync(item);
        UpdateDotStates();
    }

    /// <summary>动态背景视频体积较大，只有切到这一张时才下载，下载完自动播放</summary>
    private async Task EnsureVideoPlayAsync(LauncherBackgroundService.BackgroundItem item)
    {
        if (string.IsNullOrWhiteSpace(item.VideoUrl)) return;
        var path = await LauncherBackgroundService.EnsureVideoAsync(_selectedKey, item.Id);
        if (path == null) return;
        if (item.Id != _bgId) return;   // 等待期间用户已切到别的背景
        PlayVideo(path);
    }

    /// <summary>向官方启动器接口拉取当前版本背景；若与本页显示的版本不同则切换</summary>
    private async Task RefreshOfficialBackgroundAsync(string key)
    {
        var meta = await LauncherBackgroundService.RefreshAsync(key);
        if (meta == null || key != _selectedKey) return;   // 等待期间已切换到别的游戏

        // 用户为该游戏设置了自定义背景时，官方背景不覆盖
        if (HasCustomBackground(key)) return;

        var oldIndex = _bgIndex;
        _bgItems = meta.Items;

        _bgIndex = SettingsService.Settings.GameBackgroundIndex.TryGetValue(key, out var saved)
            ? Math.Clamp(saved, 0, Math.Max(0, _bgItems.Count - 1))
            : Math.Clamp(oldIndex, 0, Math.Max(0, _bgItems.Count - 1));

        BuildDots();
        ShowOfficialBackground();
    }

    private static bool HasCustomBackground(string key)
    {
        if (SettingsService.Settings.GameBackgrounds.TryGetValue(key, out var p) && File.Exists(p)) return true;
        var old = SettingsService.Settings.CustomBackground;
        return !SettingsService.Settings.GameBackgrounds.ContainsKey(key)
               && !string.IsNullOrWhiteSpace(old) && File.Exists(old);
    }

    private void SetImageFromFile(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            BgImage.Source = bmp;
        }
        catch
        {
            SetBundledBackground(_selectedKey);
        }
    }

    private void SetBundledBackground(string key)
    {
        try
        {
            // 内置兜底背景：星穹铁道使用 Assets/bg_StarRail.jpg，其余为 Assets/bg_{key}.jpg
            var bgName = key == "starrail" ? "bg_StarRail.jpg" : $"bg_{key}.jpg";
            BgImage.Source = new BitmapImage(new Uri($"pack://application:,,,/Assets/{bgName}"));
        }
        catch
        {
            BgImage.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/background.jpg"));
        }
    }

    // ---------- 动态背景（官方视频） ----------

    private void PlayVideo(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            BgVideo.Source = new Uri(path);
            BgVideo.Position = TimeSpan.Zero;
            BgVideo.Visibility = Visibility.Visible;
            BgVideo.Play();
        }
        catch
        {
            BgVideo.Visibility = Visibility.Collapsed;
        }
    }

    private void StopVideo()
    {
        try
        {
            BgVideo.Stop();
            BgVideo.Source = null;
        }
        catch
        {
            // 忽略
        }
        BgVideo.Visibility = Visibility.Collapsed;
    }

    private void BgVideo_MediaEnded(object sender, RoutedEventArgs e)
    {
        // 循环播放动态背景（与 Starward 首页一致）
        try
        {
            BgVideo.Position = TimeSpan.Zero;
            BgVideo.Play();
        }
        catch
        {
            BgVideo.Visibility = Visibility.Collapsed;
        }
    }

    private void BgVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        // 当前系统缺少视频解码器（如未安装 VP9 扩展）时静默回落到静态壁纸
        BgVideo.Visibility = Visibility.Collapsed;
    }

    /// <summary>页面不可见（切到其它页面/最小化）时暂停视频，回到本页再继续</summary>
    private void OnPageVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible) BgVideo.Pause();
        else if (BgVideo.Source != null) BgVideo.Play();
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

        // 当前游戏没在运行：按钮恢复可点击（切到其它游戏时不能继承上一款游戏的置灰）
        BtnMain.IsEnabled = true;

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
            LocateHint.Text = "点击「获取游戏」会弹出安装窗口（安装路径 / 语言 / 磁盘空间），确认后由官方启动器完成下载安装；已安装的游戏会自动定位并显示「开始游戏」。";
        }

        _ = RefreshPreDownloadAsync();
    }

    /// <summary>官方「预下载」：有新版本预下载包时在右下角显示按钮</summary>
    private async Task RefreshPreDownloadAsync()
    {
        var key = _selectedKey;
        var info = await LauncherBackgroundService.GetPreDownloadAsync(key);
        if (key != _selectedKey) return;
        _preDownload = info;
        if (info == null)
        {
            BtnPreDownload.Visibility = Visibility.Collapsed;
            BtnPreDownload.Content = "";
            return;
        }
        BtnPreDownload.Content = $"预下载 {info.Version} · {FormatSize(info.SizeBytes)}";
        BtnPreDownload.Visibility = Visibility.Visible;
    }

    private static string FormatSize(long bytes)
    {
        const double gb = 1024d * 1024 * 1024;
        return bytes >= gb ? $"{bytes / gb:0.00} GB" : $"{bytes / (1024d * 1024):0} MB";
    }

    private void BtnPreDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_preDownload == null) return;
        var g = GameLocator.Get(_selectedKey);
        var dlg = new MiNest.Windows.InstallGameDialog(
            g, DefaultGamesRoot(), true, _preDownload.Version, _preDownload.SizeBytes)
        {
            Owner = Window.GetWindow(this),
        };
        if (dlg.ShowDialog() != true) return;
        OpenOfficialLauncher(g);
    }

    /// <summary>「获取游戏」：仿官方启动器的安装弹窗（不再跳转官网）</summary>
    private async Task InstallGameAsync()
    {
        var g = GameLocator.Get(_selectedKey);
        var (ver, size) = await LauncherBackgroundService.GetLatestPackageAsync(_selectedKey);
        var dlg = new MiNest.Windows.InstallGameDialog(g, DefaultGamesRoot(), false, ver, size)
        {
            Owner = Window.GetWindow(this),
        };
        if (dlg.ShowDialog() != true) return;
        OpenOfficialLauncher(g);
    }

    /// <summary>调起本机米哈游官方启动器完成下载/安装（找不到时回退到游戏官网）</summary>
    private void OpenOfficialLauncher(GameDef g)
    {
        var exe = FindOfficialLauncher();
        if (exe != null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
                return;
            }
            catch
            {
                // 回退到官网
            }
        }

        if (!string.IsNullOrEmpty(g.OfficialUrl)) _main.OpenInApp(g.OfficialUrl);
        MessageBox.Show(
            "未找到本机的米哈游官方启动器，已在软件内打开该游戏官网下载页。\n安装完成后回到本页点「定位游戏」选择安装目录即可启动。",
            "mi-Nest", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string? DefaultGamesRoot()
    {
        var exe = FindOfficialLauncher();
        return exe != null ? Path.Combine(Path.GetDirectoryName(exe)!, "games") : null;
    }

    private static string? FindOfficialLauncher()
    {
        var candidates = new List<string>();
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        candidates.Add(Path.Combine(pf, "HoYoPlay", "launcher.exe"));
        candidates.Add(Path.Combine(pf86, "HoYoPlay", "launcher.exe"));
        candidates.Add(Path.Combine(pf, "miHoYo Launcher", "launcher.exe"));
        candidates.Add(Path.Combine("D:\\miHoYo\\miHoYo Launcher", "launcher.exe"));
        candidates.Add(Path.Combine("C:\\miHoYo\\miHoYo Launcher", "launcher.exe"));

        // 已定位的游戏目录一般是 ...\miHoYo Launcher\games\<游戏>，向上找启动器
        foreach (var g in GameLocator.Games)
        {
            if (!SettingsService.Settings.GamePaths.TryGetValue(g.Key, out var dir) || string.IsNullOrWhiteSpace(dir))
                continue;
            var parent = Directory.GetParent(dir.TrimEnd('\\'));
            var grand = parent?.Parent;
            if (grand != null) candidates.Add(Path.Combine(grand.FullName, "launcher.exe"));
        }

        return candidates.FirstOrDefault(File.Exists);
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

        // 未定位：优先注册表自动检测；检测不到时弹出安装窗口（不再跳转官网）
        if (_locatedDir == null)
        {
            await InstallGameAsync();
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
