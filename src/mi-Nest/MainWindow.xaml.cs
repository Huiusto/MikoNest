using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using MiNest.Models;
using MiNest.Pages;
using MiNest.Services;
using Microsoft.Web.WebView2.Core;
using WinForms = System.Windows.Forms;

namespace MiNest;

public partial class MainWindow : Window
{
    public const string CommunityUrl = "https://www.miyoushe.com/dby";

    /// <summary>账号页待显示提示（网页登录完成后回填）</summary>
    public static string? PendingAccountNotice;

    /// <summary>内置导航项：key -> (Segoe MDL2 图标, 显示名)</summary>
    public static readonly Dictionary<string, (string Icon, string Label)> BuiltinNavItems = new()
    {
        ["启动"] = ("\uE768", "启动"),
        ["社区"] = ("\uE716", "社区"),
        ["抽卡记录"] = ("\uE8D7", "抽卡记录"),
        ["任务/签到"] = ("\uE73E", "任务/签到"),
        ["米哈游相关"] = ("\uE774", "米哈游相关"),
    };

    private static readonly string[] DefaultNavOrder =
        { "启动", "社区", "抽卡记录", "任务/签到", "米哈游相关" };

    private readonly Dictionary<string, RadioButton> _navButtons = new();
    private readonly Dictionary<string, RadioButton> _navRbs = new();
    private readonly Dictionary<string, UserControl> _nativePages = new();
    private bool _switching;
    private string? _pendingUrl;
    private bool _pendingVideoFeed;
    private bool _videoBridgeReady;

    // B 站精选流状态
    private readonly List<BiliVideo> _biliQueue = new();
    private readonly Dictionary<string, Dictionary<int, string>> _playUrlCache = new();
    private readonly Dictionary<long, bool> _biliLiked = new();
    private readonly Dictionary<long, bool> _biliCollected = new();
    private readonly Dictionary<long, bool> _biliFollowed = new();
    private readonly HashSet<long> _biliCoined = new();
    private readonly Dictionary<long, bool> _biliLikeCache = new();
    private readonly Dictionary<long, int> _biliFollowCache = new();
    private int _biliIndex = -1;
    private int _biliPage = 1;
    private bool _biliLoading;
    private bool _waitingBiliLogin;

    // 通用设置：系统托盘 / 游戏退出监视
    private WinForms.NotifyIcon? _trayIcon;
    private readonly DispatcherTimer _gameWatchTimer;
    private readonly HashSet<string> _runningGames = new();
    private bool _forceExit;

    /// <summary>隐藏启动参数 --screenshots=目录：自动导出全部页面截图后退出（用于 README 配图）</summary>
    private string? _screenshotDir;

    public bool IsWebVisible => WebHost.Visibility == Visibility.Visible;

    public MainWindow()
    {
        InitializeComponent();

        // 隐藏启动参数：--screenshots <目录>（自动导出页面截图后退出）
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 2 && args[1] == "--screenshots")
            _screenshotDir = args[2];

        // DPI 适配：窗口初始尺寸不超过工作区（防止高分屏下底部内容被挤出屏幕外）
        var wa = SystemParameters.WorkArea;
        if (Width > wa.Width) Width = wa.Width;
        if (Height > wa.Height) Height = wa.Height;

        _navButtons["账号"] = NavAccount;
        _navButtons["设置"] = NavSettings;

        // 首次启动默认把「HOYO-MiX（第三方）」固定到左侧项目栏（应用一次后不再自动追加）
        var s0 = SettingsService.Settings;
        if (!s0.DefaultPinApplied)
        {
            s0.DefaultPinApplied = true;
            if (!s0.PinnedLinks.Any(p => p.Url == "https://hoyomix.amarea.cn/"))
                PinLink("HOYO-MiX", "https://hoyomix.amarea.cn/");
            else
                SettingsService.Save();
        }

        // 动态构建中间导航（顺序按 NavOrder，视频按开关显隐）
        RebuildNav();

        // 在全部控件创建完成后才触发首次页面（避免 XAML 加载期提前触发导致空引用）
        if (_navRbs.TryGetValue("启动", out var launch)) launch.IsChecked = true;

        // 通用设置：退出游戏后弹回主界面 + 关闭行为（托盘）
        _gameWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _gameWatchTimer.Tick += GameWatch_Tick;
        UpdateGameWatch();
        ApplyCloseBehavior();
        Closing += MainWindow_Closing;

        // 网页按钮提示随语言设置
        BtnBack.ToolTip = LocalizationService.Get("web_back");
        BtnForward.ToolTip = LocalizationService.Get("web_forward");
        BtnRefresh.ToolTip = LocalizationService.Get("web_refresh");

        Loaded += OnLoaded;

        // 胡桃风格背景（隐藏设置里开关，配置持久化）
        ApplyBackdrop();
    }

    // ---------- 胡桃风格背景 ----------

    /// <summary>整窗背景：自定义软件背景图（优先） / 胡桃风格背景 / 无。</summary>
    public void ApplyBackdrop()
    {
        // 1) 隐藏设置里的「自定义软件背景图」
        var custom = SettingsService.Settings.CustomAppBackground;
        if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom))
        {
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.UriSource = new Uri(custom);
                img.EndInit();
                img.Freeze();
                BackdropImage.Source = img;
                BackdropImage.Visibility = Visibility.Visible;

                var alpha = (byte)Math.Round(255 * Math.Clamp(SettingsService.Settings.CustomAppBackgroundVeil, 0, 100) / 100.0);
                BackdropVeil.Background = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255));
                BackdropVeil.Visibility = Visibility.Visible;
            }
            catch
            {
                BackdropImage.Source = null;
                BackdropImage.Visibility = Visibility.Collapsed;
                BackdropVeil.Visibility = Visibility.Collapsed;
            }
            return;
        }

        // 2) 胡桃风格背景
        var on = SettingsService.Settings.HutaoBackdrop;
        if (on)
        {
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.UriSource = new Uri("pack://application:,,,/Assets/hutao_bg.png", UriKind.Absolute);
                img.EndInit();
                img.Freeze();
                BackdropImage.Source = img;
            }
            catch
            {
                // 资源缺失时不显示背景，不影响其他功能
                BackdropImage.Source = null;
            }
            BackdropVeil.Background = new SolidColorBrush(Color.FromArgb(0x4D, 0xFF, 0xF6, 0xF2));
            BackdropImage.Visibility = Visibility.Visible;
            BackdropVeil.Visibility = Visibility.Visible;
        }
        else
        {
            BackdropImage.Visibility = Visibility.Collapsed;
            BackdropImage.Source = null;
            BackdropVeil.Visibility = Visibility.Collapsed;
        }
    }

    // ---------- 通用设置：启动设置 / 关闭设置 ----------

    /// <summary>退出游戏后自动弹出主界面：按开关启停监视</summary>
    public void UpdateGameWatch()
    {
        var on = SettingsService.Settings.BringFrontOnGameExit;
        if (on && !_gameWatchTimer.IsEnabled)
        {
            _runningGames.Clear();
            _gameWatchTimer.Start();
        }
        else if (!on && _gameWatchTimer.IsEnabled)
            _gameWatchTimer.Stop();
    }

    /// <summary>米哈游 PC 游戏进程名</summary>
    private static readonly string[] GameProcessNames = { "YuanShen", "GenshinImpact", "StarRail", "ZenlessZoneZero", "BH3" };

    private void GameWatch_Tick(object? sender, EventArgs e)
    {
        if (!SettingsService.Settings.BringFrontOnGameExit) { _gameWatchTimer.Stop(); return; }
        var now = new HashSet<string>();
        foreach (var name in GameProcessNames)
        {
            try { if (Process.GetProcessesByName(name).Length > 0) now.Add(name); }
            catch { /* 权限等异常忽略 */ }
        }
        // 之前运行、现在已退出的游戏 → 弹回启动器主界面（回到「启动」页）
        foreach (var gone in _runningGames)
        {
            if (!now.Contains(gone))
            {
                BringToFront();
                break;
            }
        }
        _runningGames.Clear();
        _runningGames.UnionWith(now);
    }

    /// <summary>把启动器窗口弹到前台并回到「启动」页</summary>
    public void BringToFront()
    {
        try
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Show();
            ShowInTaskbar = true;
            Activate();
            Topmost = true;
            Topmost = false;
            ShowPage("启动");
        }
        catch { /* 忽略 */ }
    }

    /// <summary>通用设置-关闭设置：按设置创建 / 销毁系统托盘图标</summary>
    public void ApplyCloseBehavior()
    {
        if (SettingsService.Settings.CloseBehavior == "Tray") EnsureTrayIcon();
        else DisposeTrayIcon();
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon != null) return;
        try
        {
            _trayIcon = new WinForms.NotifyIcon
            {
                Icon = (Environment.ProcessPath is string exePath
                    ? System.Drawing.Icon.ExtractAssociatedIcon(exePath)
                    : null) ?? System.Drawing.SystemIcons.Application,
                Text = "mi-Nest",
                Visible = true,
            };
            _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
            _trayIcon.ContextMenuStrip = new WinForms.ContextMenuStrip();
            _trayIcon.ContextMenuStrip.Items.Add("打开 mi-Nest", null, (_, _) => RestoreFromTray());
            _trayIcon.ContextMenuStrip.Items.Add("退出启动器", null, (_, _) => ShutdownApp());
        }
        catch { _trayIcon = null; }
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon == null) return;
        try
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        catch { /* 忽略 */ }
        _trayIcon = null;
    }

    private void RestoreFromTray()
    {
        try
        {
            Show();
            ShowInTaskbar = true;
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
        }
        catch { /* 忽略 */ }
    }

    /// <summary>托盘菜单「退出启动器」：强制结束进程</summary>
    private void ShutdownApp()
    {
        _forceExit = true;
        DisposeTrayIcon();
        Application.Current.Shutdown();
    }

    /// <summary>关闭按钮（×）：按「关闭设置」决定最小化到托盘或退出</summary>
    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceExit) return;   // 托盘菜单主动退出放行
        if (SettingsService.Settings.CloseBehavior == "Tray")
        {
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
            EnsureTrayIcon();
        }
    }

    /// <summary>从「米哈游相关」固定一个链接到左侧导航栏（单固定：固定新项时移除旧固定项，项目栏只保留最新固定的一个）</summary>
    public bool PinLink(string name, string url)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) return false;
        var s = SettingsService.Settings;
        if (s.PinnedLinks.Any(p => p.Url == url)) return false;
        // 只保留最新固定的一个链接
        foreach (var old in s.PinnedLinks) s.NavOrder.Remove($"pin:{old.Key}");
        s.PinnedLinks.Clear();
        var link = new PinnedLink { Name = name, Url = url };
        s.PinnedLinks.Add(link);
        s.NavOrder.Add($"pin:{link.Key}");
        SettingsService.Save();
        RebuildNav();
        return true;
    }

    /// <summary>取消固定：从导航栏与固定列表移除</summary>
    public void UnpinLink(string pinKey)
    {
        var s = SettingsService.Settings;
        s.PinnedLinks.RemoveAll(p => p.Key == pinKey);
        s.NavOrder.Remove($"pin:{pinKey}");
        SettingsService.Save();
        RebuildNav();
    }

    /// <summary>当前有效导航顺序（中间区：内置项 + 固定项；设置-管理项目栏 使用）</summary>
    public List<string> GetNavOrderKeys()
    {
        var s = SettingsService.Settings;
        var order = s.NavOrder.Count > 0
            ? s.NavOrder.Where(k => BuiltinNavItems.ContainsKey(k) || k.StartsWith("pin:")).ToList()
            : DefaultNavOrder.ToList();
        foreach (var def in DefaultNavOrder)
            if (!order.Contains(def)) order.Insert(Math.Min(order.Count, Array.IndexOf(DefaultNavOrder, def)), def);
        return order;
    }

    /// <summary>设置-管理项目栏：上移/下移导航项并持久化</summary>
    public void MoveNavItem(string key, int delta)
    {
        var s = SettingsService.Settings;
        var order = GetNavOrderKeys();
        var i = order.IndexOf(key);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= order.Count) return;
        (order[i], order[j]) = (order[j], order[i]);
        s.NavOrder = order;
        SettingsService.Save();
        RebuildNav();
    }

    /// <summary>设置-隐藏页：内置导航项显示 / 隐藏（默认全部显示）并持久化</summary>
    public void SetNavVisible(string key, bool visible)
    {
        var list = SettingsService.Settings.DisabledNavItems;
        if (visible) list.Remove(key);
        else if (!list.Contains(key)) list.Add(key);
        SettingsService.Save();
        RebuildNav();
    }

    /// <summary>按 NavOrder 重建左侧中间导航（内置项 + 固定链接项）</summary>
    public void RebuildNav()
    {
        var s = SettingsService.Settings;
        var order = GetNavOrderKeys();

        NavMiddlePanel.Children.Clear();
        _navButtons.Clear();
        _navButtons["账号"] = NavAccount;
        _navButtons["设置"] = NavSettings;
        _navRbs.Clear();

        foreach (var key in order)
        {
            if (s.DisabledNavItems.Contains(key)) continue;   // 隐藏设置里被关闭的内置项

            RadioButton rb;
            if (key.StartsWith("pin:"))
            {
                var link = s.PinnedLinks.FirstOrDefault(p => $"pin:{p.Key}" == key);
                if (link == null) continue;
                // HOYO-MiX 固定项用音符图标，其余固定项用图钉
                var icon = link.Url == "https://hoyomix.amarea.cn/" ? "🎵" : "📌";
                rb = MakeNavRb(icon, link.Name, link.Name);   // 固定项：自定义名原样显示
            }
            else
            {
                rb = MakeNavRb(BuiltinNavItems[key].Icon, key, LocalizationService.Get(LocalizationService.NavKey(key)));
                _navButtons[key] = rb;
            }
            _navRbs[key] = rb;
            NavMiddlePanel.Children.Add(rb);
        }

        ApplyAccountNavVisibility();
    }

    /// <summary>按设置显示 / 隐藏左侧「账号」按钮；若正停在账号页则退回「启动」页</summary>
    public void ApplyAccountNavVisibility()
    {
        var hide = SettingsService.Settings.HideAccountNav;
        NavAccount.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
        if (hide && NavAccount.IsChecked == true)
        {
            NavAccount.IsChecked = false;
            ShowPage("启动");
        }
    }

    private RadioButton MakeNavRb(string icon, string tag, string display)
    {
        var rb = new RadioButton
        {
            Style = (Style)FindResource("SidebarNavItem"),
            GroupName = "nav",
            Content = icon,
            Tag = tag,
        };
        LocalizationService.SetKey(rb, display);
        System.Windows.Automation.AutomationProperties.SetName(rb, display);
        rb.Checked += Nav_Checked;
        return rb;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // 使用持久化用户数据目录，米游社 / B 站登录态会保留
            var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebView2Dir,
                new CoreWebView2EnvironmentOptions());
            await WebView.EnsureCoreWebView2Async(env);

            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            WebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
            // 整体缩小：窗口缩小后网页内容也等比缩放（85%），社区/视频/米游社等内嵌页面随之变小
            WebView.ZoomFactor = 0.85;
            // 客户端式视频页：允许自动播放（打开视频页即播放，无需点击手势）
            WebView.CoreWebView2.PermissionRequested += (_, e) =>
            {
                if (e.PermissionKind == CoreWebView2PermissionKind.Autoplay)
                    e.State = CoreWebView2PermissionState.Allow;
            };

            // 页面内点击任何新窗口/外链，一律在当前 WebView 中打开（不跳转浏览器）
            WebView.CoreWebView2.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (!string.IsNullOrEmpty(args.Uri))
                    WebView.CoreWebView2.Navigate(args.Uri);
            };
            WebView.CoreWebView2.HistoryChanged += (_, _) => UpdateNavButtons();
            WebView.CoreWebView2.NavigationStarting += (_, _) => UpdateNavButtons();
            WebView.CoreWebView2.NavigationCompleted += (_, _) => UpdateNavButtons();

            // 哔哩哔哩视频 CDN 需要 Referer=bilibili.com 才能播放，这里统一注入（https + http 都覆盖）
            // playurl 可能返回多种 CDN 域名（bilivideo / mountaintoys / hdslb / akamaized / szbdyd 等）
            foreach (var cdn in new[]
            {
                "https://*.bilivideo.com/*", "http://*.bilivideo.com/*",
                "https://*.bilivideo.cn/*", "http://*.bilivideo.cn/*",
                "https://*.hdslb.com/*", "http://*.hdslb.com/*",
                "https://*.mountaintoys.cn/*", "http://*.mountaintoys.cn/*",
                "https://*.akamaized.net/*", "http://*.akamaized.net/*",
                "https://*.szbdyd.com/*", "http://*.szbdyd.com/*",
            })
                WebView.CoreWebView2.AddWebResourceRequestedFilter(cdn, CoreWebView2WebResourceContext.All);
            WebView.CoreWebView2.WebResourceRequested += (_, args) =>
            {
                args.Request.Headers.SetHeader("Referer", "https://www.bilibili.com/");
                args.Request.Headers.SetHeader("User-Agent", BilibiliService.UA);
            };

            // 视频页：滚轮/按钮 → postMessage 桥接
            if (!_videoBridgeReady)
            {
                _videoBridgeReady = true;
                WebView.CoreWebView2.WebMessageReceived += WebView_WebMessageReceived;
            }

            if (_pendingVideoFeed)
            {
                _pendingVideoFeed = false;
                ShowVideoFeed();
            }
            if (!string.IsNullOrEmpty(_pendingUrl))
            {
                WebView.CoreWebView2.Navigate(_pendingUrl);
                _pendingUrl = null;
            }

            // 隐藏参数：导出全部页面截图后退出（README 配图用）
            if (!string.IsNullOrEmpty(_screenshotDir))
                _ = RunScreenshotExportAsync(_screenshotDir);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"WebView2 初始化失败：{ex.Message}\n\n请确认已安装 WebView2 Runtime（一般随 Edge 自动安装）。",
                "mi-Nest", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }



    // ---------- 自动导出页面截图（隐藏启动参数 --screenshots=目录） ----------

    private async Task RunScreenshotExportAsync(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            // 截图模式：临时使用标准 Chrome UA，避免米游社等站点对 WebView2 默认 UA 返回空壳
            try
            {
                WebView.CoreWebView2.Settings.UserAgent =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";
            }
            catch { /* 忽略 */ }
            var keys = new List<string> { "启动", "社区", "抽卡记录", "任务/签到", "米哈游相关", "账号", "设置" };
            foreach (var pin in SettingsService.Settings.PinnedLinks)
                keys.Add($"pin:{pin.Key}");

            foreach (var key in keys)
            {
                ShowPage(key);
                if (WebHost.Visibility == Visibility.Visible && WebView.CoreWebView2 != null)
                {
                    // 网页页面：等待导航完成 + 预热抓帧 + 渲染缓冲（避免截到空白）
                    var tcs = new TaskCompletionSource<bool>();
                    void OnNav(object? _, CoreWebView2NavigationCompletedEventArgs __) => tcs.TrySetResult(true);
                    WebView.CoreWebView2.NavigationCompleted += OnNav;
                    await Task.WhenAny(tcs.Task, Task.Delay(25000));
                    WebView.CoreWebView2.NavigationCompleted -= OnNav;
                    await Task.Delay(1500);
                    // 预热一帧：触发 WebView2 合成器输出
                    try
                    {
                        using var warm = new MemoryStream();
                        await WebView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, warm);
                    }
                    catch { /* 预热失败不影响后续 */ }
                    await Task.Delay(2000);
                }
                else
                {
                    await Task.Delay(2500);
                }
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                var bmp = await CaptureCompositeAsync();
                var safe = string.Concat(key.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
                var path = Path.Combine(dir, safe + ".png");
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                await using var fs = File.Create(path);
                enc.Save(fs);
            }

            MessageBox.Show($"页面截图已导出到：\n{dir}", "mi-Nest", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"截图导出失败：{ex.Message}", "mi-Nest", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
        }
    }

    /// <summary>合成截图：WPF 窗口渲染 + WebView2 硬件内容（CapturePreviewAsync）覆盖到对应区域</summary>
    private async Task<BitmapSource> CaptureCompositeAsync()
    {
        var w = Math.Max(1, (int)ActualWidth);
        var h = Math.Max(1, (int)ActualHeight);
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(this);

        if (WebHost.Visibility == Visibility.Visible && WebView.CoreWebView2 != null)
        {
            using var ms = new MemoryStream();
            await WebView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
            ms.Position = 0;
            var webBmp = new BitmapImage();
            webBmp.BeginInit();
            webBmp.CacheOption = BitmapCacheOption.OnLoad;
            webBmp.StreamSource = ms;
            webBmp.EndInit();

            var pos = WebView.TransformToAncestor(this).Transform(new Point(0, 0));
            var scaleX = rtb.PixelWidth / Math.Max(1, ActualWidth);
            var scaleY = rtb.PixelHeight / Math.Max(1, ActualHeight);

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawImage(rtb, new Rect(0, 0, rtb.Width, rtb.Height));
                dc.DrawImage(webBmp, new Rect(
                    pos.X * scaleX, pos.Y * scaleY,
                    WebView.ActualWidth * scaleX, WebView.ActualHeight * scaleY));
            }
            var outRtb = new RenderTargetBitmap(rtb.PixelWidth, rtb.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            outRtb.Render(dv);
            return outRtb;
        }
        return rtb;
    }

    /// <summary>在软件内打开一个网页（用于 米哈云游/预约 等）</summary>
    public void OpenInApp(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        ShowWeb(url);
    }

    /// <summary>启动本机安装的 TeyvatGuide（提瓦特指南）桌面应用</summary>
    private static string? FindTeyvatGuideExe()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "TeyvatGuide"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "TeyvatGuide"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "TeyvatGuide"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mi-Nest", "tools", "TeyvatGuidePortable", "PFiles", "TeyvatGuide"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mi-Nest", "tools", "TeyvatGuide"),
        };
        foreach (var root in roots)
        {
            var exe = Path.Combine(root, "TeyvatGuide.exe");
            if (File.Exists(exe)) return exe;
        }
        return null;
    }

    private void LaunchTeyvatGuide()
    {
        var exe = FindTeyvatGuideExe();
        if (exe == null)
        {
            MessageBox.Show("尚未安装 TeyvatGuide（提瓦特指南）。\n\n可前往 GitHub 下载安装：\nhttps://github.com/BTMuli/TeyvatGuide/releases\n\n安装后重启 mi-Nest 即可通过左侧项目栏打开。",
                "mi-Nest", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动 TeyvatGuide 失败：{ex.Message}", "mi-Nest", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>启动本机安装的 Starward（米哈游 PC 启动器 / 抽卡记录）桌面应用</summary>
    private static string? FindStarwardExe()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mi-Nest", "tools", "Starward"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Starward"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Starward"),
        };
        foreach (var root in roots)
        {
            // 便携解压：可能是 Starward\Starward.exe 或直接 Starward.exe
            var direct = Path.Combine(root, "Starward.exe");
            var nested = Path.Combine(root, "Starward", "Starward.exe");
            if (File.Exists(direct)) return direct;
            if (File.Exists(nested)) return nested;
        }
        return null;
    }

    private void LaunchStarward()
    {
        var exe = FindStarwardExe();
        if (exe == null)
        {
            MessageBox.Show("尚未安装 Starward。\n\n可前往 GitHub 下载安装：\nhttps://github.com/Scighost/Starward/releases\n\n安装后重启 mi-Nest 即可通过左侧项目栏打开。",
                "mi-Nest", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动 Starward 失败：{ex.Message}", "mi-Nest", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (_switching) return;
        if (sender is RadioButton rb && rb.Tag is string label)
            ShowPage(MapLabelToKey(label));
    }

    /// <summary>导航显示名 → 逻辑 key（固定项按名称反查 pin key；内置项按显示名反查 key）</summary>
    private static string MapLabelToKey(string label)
    {
        if (label.StartsWith("pin:")) return label;
        var link = SettingsService.Settings.PinnedLinks.FirstOrDefault(p => p.Name == label);
        if (link != null) return $"pin:{link.Key}";
        foreach (var kv in BuiltinNavItems)
            if (kv.Value.Label == label) return kv.Key;
        return label;
    }

    public void ShowPage(string key)
    {
        _waitingBiliLogin = false;
        _switching = true;
        try
        {
            switch (key)
            {
                case "社区":
                    ShowWeb(CommunityUrl);
                    return;
                default:
                    if (key.StartsWith("pin:"))
                    {
                        // 米哈游相关页面固定到导航栏的快捷链接：软件内打开
                        var link = SettingsService.Settings.PinnedLinks
                            .FirstOrDefault(p => $"pin:{p.Key}" == key);
                        if (link != null) ShowWeb(link.Url);
                        return;
                    }
                    break;
            }

            WebHost.Visibility = Visibility.Collapsed;

            if (!_nativePages.TryGetValue(key, out var page))
            {
                page = key switch
                {
                    "启动" => new LaunchPage(this),
                    "抽卡记录" => new GachaPage(),
                    "任务/签到" => new TaskPage(this),
                    "实时便笺" => new DailyNotePage(this),
                    "米哈游相关" => new MhyLinksPage(this),
                    "账号" => new AccountPage(this),
                    "设置" => new SettingsPage(this),
                    _ => new LaunchPage(this),
                };
                _nativePages[key] = page;
            }
            PageHost.Content = page;

            if (_navRbs.TryGetValue(key, out var rb))
                rb.IsChecked = true;
        }
        finally
        {
            _switching = false;
        }
        UpdateNavButtons();
    }

    private void ShowWeb(string url)
    {
        WebHost.Visibility = Visibility.Visible;
        if (WebView.CoreWebView2 != null)
            WebView.CoreWebView2.Navigate(url);
        else
            _pendingUrl = url;
        UpdateNavButtons();
    }

    private void UpdateNavButtons()
    {
        var web = IsWebVisible && WebView.CoreWebView2 != null;
        BtnBack.IsEnabled = web && WebView.CanGoBack;
        BtnForward.IsEnabled = web && WebView.CanGoForward;
        BtnRefresh.IsEnabled = web;
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (IsWebVisible && WebView.CanGoBack) WebView.GoBack();
    }

    private void BtnForward_Click(object sender, RoutedEventArgs e)
    {
        if (IsWebVisible && WebView.CanGoForward) WebView.GoForward();
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (IsWebVisible && WebView.CoreWebView2 != null) WebView.CoreWebView2.Reload();
    }

    // ---------- 网页扫码登录（账号页兜底：官方页面登录后自动导入） ----------

    /// <summary>打开米游社官方页面供扫码/验证码登录，检测到登录态后自动导入账号并返回账号页。</summary>
    public async Task OpenMiyousheLoginAsync()
    {
        if (WebView.CoreWebView2 == null) return;
        ShowWeb(CommunityUrl);

        for (var i = 0; i < 150; i++) // 最多等 5 分钟
        {
            await Task.Delay(2000);
            try
            {
                var cookies = await WebView.CoreWebView2.CookieManager.GetCookiesAsync("https://www.miyoushe.com");
                var hasLogin = cookies.Any(c => c.Name is "stuid" or "stoken" or "account_id" && !string.IsNullOrEmpty(c.Value));
                if (!hasLogin) continue;

                var (ok, msg, cookie) = await AccountService.ImportFromWebViewAsync(WebView.CoreWebView2.CookieManager);
                if (!ok)
                {
                    PendingAccountNotice = $"导入失败：{msg}";
                    ShowPage("账号");
                    return;
                }
                if (string.IsNullOrEmpty(cookie))
                {
                    PendingAccountNotice = "未检测到登录态，请确认已在页面内登录后重试。";
                    ShowPage("账号");
                    return;
                }
                var (vok, nickname, uid, vmsg) = await AccountService.ValidateCookieAsync(cookie);
                if (!vok)
                {
                    PendingAccountNotice = $"登录态无效：{vmsg}";
                    ShowPage("账号");
                    return;
                }
                AccountService.AddOrUpdate(new Account { Nickname = nickname, Uid = uid, Cookie = cookie });
                PendingAccountNotice = $"已自动导入账号：{nickname}（{uid}）";
                ShowPage("账号");
                return;
            }
            catch
            {
                // 轮询期间页面仍在导航，忽略瞬时异常
            }
        }
        PendingAccountNotice = "等待登录超时（5 分钟），也可在页面内登录后手动点击「从内置浏览器导入登录态」。";
        ShowPage("账号");
    }

    // ---------- 视频页：B 站「精选」全屏刷视频 ----------

    private async void ShowVideoFeed()
    {
        WebHost.Visibility = Visibility.Visible;

        if (WebView.CoreWebView2 == null)
        {
            _pendingVideoFeed = true;
            return;
        }

        _biliQueue.Clear();
        _playUrlCache.Clear();
        _biliIndex = -1;
        _biliPage = 1;
        _biliLoading = false;

        WebView.CoreWebView2.NavigateToString(BiliFeed.Html);
        // 等待页面加载完成（避免时序问题导致首屏空白/0 数据）
        var navTcs = new TaskCompletionSource<bool>();
        void OnNav(object? _, CoreWebView2NavigationCompletedEventArgs __) => navTcs.TrySetResult(true);
        WebView.CoreWebView2.NavigationCompleted += OnNav;
        WebView.CoreWebView2.NavigateToString(BiliFeed.Html);
        var loaded = await Task.WhenAny(navTcs.Task, Task.Delay(5000));
        WebView.CoreWebView2.NavigationCompleted -= OnNav;
        if (loaded != navTcs.Task)
        {
            await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildToast("页面加载超时，请稍后重试"));
            return;
        }
        await Task.Delay(300);

        // 登录态显示
        await SyncBiliCookiesAsync();
        var (ok, uname, _) = await BilibiliService.GetLoginUserAsync();
        await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildLogin(ok, uname));

        await LoadBiliBatchAsync();
        await NextVideoAsync();
        UpdateNavButtons();
    }

    private async Task SyncBiliCookiesAsync()
    {
        try
        {
            if (WebView.CoreWebView2 == null) return;
            var cookies = await WebView.CoreWebView2.CookieManager.GetCookiesAsync("https://www.bilibili.com");
            BilibiliService.SyncCookies(cookies.Select(c => new BilibiliService.CoreWebView2CookieLike(c.Name, c.Value, c.Domain)));
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>加载下一批精选视频并预取播放地址（空结果自动重试 3 次）</summary>
    private async Task LoadBiliBatchAsync()
    {
        if (WebView.CoreWebView2 == null || _biliLoading) return;
        _biliLoading = true;
        try
        {
            var items = new List<BiliVideo>();
            for (var attempt = 0; attempt < 3 && items.Count == 0; attempt++)
            {
                items = await BilibiliService.GetPopularAsync(_biliPage);
                if (items.Count == 0 && attempt < 2)
                    await Task.Delay(1500);
            }
            if (items.Count == 0) return;
            _biliPage++;

            // 并行预取播放地址（最多同时 4 个）
            var tasks = items.Select(v => PrefetchAsync(v)).ToList();
            await Task.WhenAll(tasks);

            _biliQueue.AddRange(items);
        }
        catch
        {
            // 加载失败静默，可稍后重试
        }
        finally
        {
            _biliLoading = false;
        }
    }

    private async Task PrefetchAsync(BiliVideo v)
    {
        if (_playUrlCache.ContainsKey(v.Bvid)) return;
        var urls = await BilibiliService.GetPlayUrlsAsync(v.Bvid, v.Cid);
        if (urls.Count > 0) _playUrlCache[v.Bvid] = urls;
    }

    private async Task PlayCurrentAsync()
    {
        if (WebView.CoreWebView2 == null || _biliIndex < 0 || _biliIndex >= _biliQueue.Count) return;
        var v = _biliQueue[_biliIndex];

        if (!_playUrlCache.TryGetValue(v.Bvid, out var urls) || urls == null || urls.Count == 0)
            urls = await BilibiliService.GetPlayUrlsAsync(v.Bvid, v.Cid);
        if (urls.Count == 0)
        {
            // 多画质全部失败：退化为单地址重试一次
            var single = await BilibiliService.GetPlayUrlAsync(v.Bvid, v.Cid);
            if (single == null)
            {
                await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildToast("该视频暂时无法播放，自动跳过"));
                _biliIndex++;
                await PlayCurrentAsync();
                return;
            }
            urls = new Dictionary<int, string> { [64] = single };
        }
        var best = urls.ContainsKey(64) ? 64 : urls.Keys.Max();
        var url = urls[best];

        _biliLiked.TryGetValue(v.Aid, out var liked);
        _biliCollected.TryGetValue(v.Aid, out var collected);
        _biliFollowed.TryGetValue(v.AuthorUid, out var followed);
        var coined = _biliCoined.Contains(v.Aid);

        await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildPlay(v, url, urls));
        if (liked) await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildLike(true, v.Like + 1));
        if (coined) await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildCoin(true));
        if (collected) await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildCollect(true));
        if (followed) await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildFollow(true));

        // 已登录时与 B 站同步点赞 / 关注状态（缓存避免重复查询）
        if (await BilibiliService.IsLoggedInAsync())
        {
            if (_biliLikeCache.TryGetValue(v.Aid, out var hasLiked))
            {
                if (hasLiked) await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildLike(true, v.Like + 1));
            }
            else
            {
                var h = await BilibiliService.GetLikedAsync(v.Aid);
                _biliLikeCache[v.Aid] = h;
                if (h) await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildLike(true, v.Like + 1));
            }
            if (_biliFollowCache.TryGetValue(v.AuthorUid, out var rel))
            {
                if (rel >= 1) await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildFollow(true));
            }
            else
            {
                var r = await BilibiliService.GetRelationAsync(v.AuthorUid);
                _biliFollowCache[v.AuthorUid] = r;
                if (r >= 1) await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildFollow(true));
            }
        }

        // 接近队尾时预加载下一批
        if (_biliIndex >= _biliQueue.Count - 4)
            _ = LoadBiliBatchAsync();
    }

    private async Task NextVideoAsync()
    {
        if (WebView.CoreWebView2 == null) return;
        if (_biliIndex >= _biliQueue.Count - 1)
            await LoadBiliBatchAsync();
        if (_biliQueue.Count == 0)
        {
            await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildToast("暂时没有视频，请稍后重试"));
            return;
        }
        if (_biliIndex < _biliQueue.Count - 1)
            _biliIndex++;
        await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildLoading());
        await PlayCurrentAsync();
    }

    private async Task PrevVideoAsync()
    {
        if (WebView.CoreWebView2 == null) return;
        if (_biliIndex <= 0)
        {
            await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildToast("已经是第一个视频了"));
            return;
        }
        _biliIndex--;
        await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildLoading());
        await PlayCurrentAsync();
    }

    private async Task RandomVideoAsync()
    {
        if (WebView.CoreWebView2 == null) return;
        var list = await BilibiliService.GetPopularAsync(Random.Shared.Next(1, 6));
        if (list.Count == 0)
        {
            await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildToast("随机获取失败，请稍后重试"));
            return;
        }
        var v = list[Random.Shared.Next(list.Count)];
        await PrefetchAsync(v);
        _biliQueue.Insert(Math.Min(_biliIndex + 1, _biliQueue.Count), v);
        if (_biliIndex < _biliQueue.Count - 1) _biliIndex++;
        await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildLoading());
        await PlayCurrentAsync();
    }

    private async void StartBiliLogin()
    {
        if (WebView.CoreWebView2 == null) return;
        _waitingBiliLogin = true;
        WebView.CoreWebView2.Navigate("https://passport.bilibili.com/login");

        // 轮询登录状态：登录成功后自动回到视频页
        for (var i = 0; i < 240 && _waitingBiliLogin; i++)
        {
            await Task.Delay(1500);
            await SyncBiliCookiesAsync();
            if (await BilibiliService.IsLoggedInAsync())
            {
                _waitingBiliLogin = false;
                ShowVideoFeed();
                return;
            }
        }
        _waitingBiliLogin = false;
    }

    private async void WebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(e.TryGetWebMessageAsString());
            var type = doc.RootElement.GetProperty("type").GetString();

            // 每次写操作前重新同步内置浏览器（WebView2）里的 B 站登录 Cookie，
            // 保证点赞/收藏/评论/关注/投币的 csrf（bili_jct）与登录态实时一致
            if (type is "like" or "coin" or "collect" or "follow" or "send_comment")
                await SyncBiliCookiesAsync();

            switch (type)
            {
                case "next":
                    await NextVideoAsync();
                    break;
                case "prev":
                    await PrevVideoAsync();
                    break;
                case "random":
                    await RandomVideoAsync();
                    break;
                case "video_error":
                    // 直连播放地址失效：应用内提示后自动跳到下一个视频
                    await NextVideoAsync();
                    break;
                case "login":
                    StartBiliLogin();
                    break;
                case "like":
                {
                    var aid = GetAid(doc.RootElement);
                    if (aid <= 0) break;
                    _biliLiked.TryGetValue(aid, out var liked);
                    var (ok, msg) = await BilibiliService.LikeAsync(aid, !liked);
                    if (ok)
                    {
                        _biliLiked[aid] = !liked;
                        _biliLikeCache[aid] = !liked;
                        var v = _biliQueue.FirstOrDefault(x => x.Aid == aid);
                        await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildLike(!liked, (v?.Like ?? 0) + (liked ? 0 : 1)));
                    }
                    await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildToast(msg));
                    break;
                }
                case "coin":
                {
                    var aid = GetAid(doc.RootElement);
                    if (aid <= 0) break;
                    if (_biliCoined.Contains(aid))
                    {
                        await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildToast("该视频已投过币"));
                        break;
                    }
                    var (cok, cmsg) = await BilibiliService.CoinAsync(aid);
                    if (cok)
                    {
                        _biliCoined.Add(aid);
                        await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildCoin(true));
                    }
                    await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildToast(cmsg));
                    break;
                }
                case "collect":
                {
                    var aid = GetAid(doc.RootElement);
                    if (aid <= 0) break;
                    _biliCollected.TryGetValue(aid, out var collected);
                    var (ok, msg) = await BilibiliService.CollectAsync(aid, !collected);
                    if (ok)
                    {
                        _biliCollected[aid] = !collected;
                        await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildCollect(!collected));
                    }
                    await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildToast(msg));
                    break;
                }
                case "follow":
                {
                    var uid = GetAid(doc.RootElement, "uid");
                    if (uid <= 0) break;
                    _biliFollowed.TryGetValue(uid, out var followed);
                    var (ok, msg) = await BilibiliService.FollowAsync(uid, !followed);
                    if (ok)
                    {
                        _biliFollowed[uid] = !followed;
                        _biliFollowCache[uid] = !followed ? 1 : 0;
                        await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildFollow(!followed));
                    }
                    await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildToast(msg));
                    break;
                }
                case "comments":
                {
                    var aid = GetAid(doc.RootElement);
                    if (aid <= 0) break;
                    var list = await BilibiliService.GetCommentsAsync(aid);
                    await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildComments(list));
                    break;
                }
                case "send_comment":
                {
                    var aid = GetAid(doc.RootElement);
                    if (aid <= 0) break;
                    var message = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                    if (string.IsNullOrWhiteSpace(message)) break;
                    var (ok, msg) = await BilibiliService.SendCommentAsync(aid, message.Trim());
                    await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildToast(msg));
                    if (ok)
                    {
                        var list = await BilibiliService.GetCommentsAsync(aid);
                        await WebView.CoreWebView2.ExecuteScriptAsync(BiliFeed.BuildComments(list));
                    }
                    break;
                }
                case "open_author":
                {
                    var uid = GetAid(doc.RootElement, "uid");
                    if (uid > 0) OpenInApp($"https://space.bilibili.com/{uid}");
                    break;
                }
            }
        }
        catch
        {
            // 忽略异常消息
        }
    }

    private static long GetAid(System.Text.Json.JsonElement doc, string name = "aid")
        => doc.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;
}
