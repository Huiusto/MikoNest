using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using MiNest.Models;
using MiNest.Services;

namespace MiNest.Pages;

/// <summary>
/// 启动页（仿 Starward）：整页由内嵌 HTML（WebView2）渲染。
/// 官方壁纸（含 .webm 动态壁纸）由 Chromium 直接播放，因此动态壁纸真正会动；
/// 页面上没有整屏蒙版，左侧靠近边缘浮现「上一张 / 圆点 / 下一张 / 保存壁纸 / 暂停动态」按钮组。
/// 每款游戏的「开始游戏 / 游戏启动中」各自独立计时，游戏进程退出后自动恢复。
/// </summary>
public partial class LaunchPage : UserControl
{
    private readonly MainWindow _main;
    private string _selectedKey = "genshin";
    private string? _locatedDir;
    private readonly HashSet<string> _runningGames = new();
    private List<LauncherBackgroundService.BackgroundItem> _bgItems = new();
    private int _bgIndex;
    private bool _useCustom;
    private bool _paused;
    private bool _preparing;
    private LauncherBackgroundService.PreDownloadInfo? _preDownload;
    private InstallUi? _install;
    private CancellationTokenSource? _installCts;
    private GameInstallService.PauseController? _installPause;
    private bool _installStopRequested;
    private DateTime _lastInstallPush = DateTime.MinValue;
    private string? _installError;
    private bool _webReady;
    private readonly string _rootDir = AppPaths.DataDir;

    /// <summary>启动页按钮区域显示的安装状态，进度变化不再经过独立窗口。</summary>
    private sealed class InstallUi
    {
        public string Key = "";
        public string Name = "";
        public string Dest = "";
        public string Language = "zh-cn";
        public string Stage = "正在获取官方安装清单…";
        public long Done;
        public long Total;
        public long InstallDone;
        public long InstallTotal;
        public double Speed;
        public bool Paused;
        public bool Stopping;
        public bool Completed;
    }

    public LaunchPage(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Loaded += OnLoaded;
        LauncherBackgroundService.BackgroundReady += OnBackgroundReady;
        Unloaded += (_, _) => LauncherBackgroundService.BackgroundReady -= OnBackgroundReady;
    }

    /// <summary>后台预加载完某款游戏的动态壁纸：若正是当前游戏，刷新列表让切换立即生效</summary>
    private void OnBackgroundReady(string key)
    {
        if (!IsLoaded) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (key != _selectedKey) return;
            var cached = LauncherBackgroundService.GetCached(key);
            if (cached == null) return;
            _bgItems = cached.Items;
            if (_bgIndex >= _bgItems.Count) _bgIndex = Math.Max(0, _bgItems.Count - 1);
            PushState();
        }));
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_webReady) { PushState(); return; }
        await InitWebAsync();
    }

    // ---------- WebView2 初始化 ----------

    private async Task InitWebAsync()
    {
        try
        {
            Directory.CreateDirectory(_rootDir);
            PrepareHtmlFile();

            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.DataDir, "webview2-launch"));
            await Web.EnsureCoreWebView2Async(env);

            var s = Web.CoreWebView2.Settings;
            s.AreDevToolsEnabled = false;
            s.AreDefaultContextMenusEnabled = false;
            s.IsStatusBarEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = false;
            s.IsZoomControlEnabled = false;
            Web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 11, 13, 18);

            Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "minest.local", _rootDir, CoreWebView2HostResourceAccessKind.Allow);
            Web.CoreWebView2.WebMessageReceived += OnWebMessage;
            Web.CoreWebView2.NavigationCompleted += async (_, __) =>
            {
                _webReady = true;
                SelectFirstOrderedGame();
                LoadBackgrounds(_selectedKey);
                PushState();
                RefreshPreDownload();
                await EnsureCurrentVideoAsync();
                // 其余游戏与其余动态壁纸在后台全部预加载，切换游戏时不再等待
                _ = LauncherBackgroundService.PrefetchAllAsync();
            };
            Web.CoreWebView2.Navigate("https://minest.local/launch/launch.html");
        }
        catch (Exception ex)
        {
            MessageBox.Show("启动页初始化失败：" + ex.Message, "MikoNest", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>把内置 launch.html 与兜底壁纸复制到缓存目录（虚拟域名 minest.local 指向该目录）</summary>
    private void PrepareHtmlFile()
    {
        var launchDir = Path.Combine(_rootDir, "launch");
        var assetDir = Path.Combine(launchDir, "assets");
        Directory.CreateDirectory(assetDir);

        CopyResource("Assets/Launch/launch.html", Path.Combine(launchDir, "launch.html"));

        foreach (var name in new[] { "background.jpg", "bg_genshin.jpg", "bg_StarRail.jpg", "bg_zzz.jpg", "bg_bh3.jpg" })
        {
            var target = Path.Combine(assetDir, name);
            if (!File.Exists(target)) CopyResource("Assets/" + name, target);
        }
    }

    private static void CopyResource(string resourcePath, string targetPath)
    {
        try
        {
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/" + resourcePath))?.Stream;
            if (stream == null) return;
            using var fs = File.Create(targetPath);
            stream.CopyTo(fs);
        }
        catch
        {
            // 资源缺失时忽略，页面仍有自带兜底样式
        }
    }

    // ---------- 状态推送 ----------

    /// <summary>启动页首页固定显示用户排序里的第一款游戏。</summary>
    private void SelectFirstOrderedGame()
    {
        var first = OrderedGames().FirstOrDefault();
        if (first != null) _selectedKey = first.Key;
    }

    /// <summary>按用户在「全部游戏」下拉里设定的顺序排列，新加入的游戏补在末尾。</summary>
    private static List<GameDef> OrderedGames()
    {
        var result = new List<GameDef>();
        var remaining = GameLocator.Games.ToList();
        var order = SettingsService.Settings.LaunchGameOrder ?? new List<string>();
        foreach (var key in order)
        {
            var g = remaining.FirstOrDefault(x => x.Key == key);
            if (g == null) continue;
            result.Add(g);
            remaining.Remove(g);
        }
        result.AddRange(remaining);
        return result;
    }

    private static bool IsLocated(string key)
    {
        var game = GameLocator.Get(key);
        if (SettingsService.Settings.GamePaths.TryGetValue(key, out var p) &&
            Directory.Exists(p) && GameLocator.FindExe(p, game) != null)
            return true;

        var detected = GameLocator.DetectInstallDir(game);
        return detected != null && GameLocator.FindExe(detected, game) != null;
    }

    private void PushState()
    {
        if (!_webReady) return;

        var games = OrderedGames().Select(g => new
        {
            key = g.Key,
            name = g.Name,
            located = IsLocated(g.Key),
            running = _runningGames.Contains(g.Key),
        }).ToList();

        // 自定义壁纸作为第一张排在左侧切换条里，与官方壁纸一起切换
        var customPath = HasCustomBackground(_selectedKey)
            ? SettingsService.Settings.GameBackgrounds[_selectedKey]
            : null;
        var items = new List<object>();
        if (customPath != null)
        {
            items.Add(new
            {
                id = "custom",
                image = ToUrl(customPath),
                video = (string?)null,
                custom = true,
            });
        }
        foreach (var i in _bgItems)
        {
            items.Add(new { id = i.Id, image = ToUrl(i.ImagePath), video = ToUrl(i.VideoPath), custom = false });
        }

        var hasCustom = customPath != null;
        var index = hasCustom && _useCustom ? 0 : (hasCustom ? _bgIndex + 1 : _bgIndex);

        var state = new
        {
            games,
            selected = _selectedKey,
            backgrounds = items,
            index,
            paused = _paused,
            custom = hasCustom && _useCustom,
            preparing = _preparing,
            accent = AccentHex(),
            systemAccent = SystemAccentHex(),
            preDownload = _preDownload == null || !IsLocated(_selectedKey) ? null : new
            {
                version = _preDownload.Version,
                size = FormatSize(_preDownload.SizeBytes),
                kind = _preDownload.IsPreDownload ? "预下载" : "新版本",
                text = _preDownload.IsPreDownload
                    ? $"预下载 {_preDownload.Version} · {FormatSize(_preDownload.SizeBytes)}"
                    : $"发现新版本 {_preDownload.Version}（本地 {_preDownload.LocalVersion}），点击更新",
            },
            install = _install != null && _install.Key == _selectedKey ? InstallPayload(_install) : null,
            hint = BuildHint(),
        };

        var json = JsonSerializer.Serialize(state);
        _ = Web.CoreWebView2.ExecuteScriptAsync($"window.miNestInit({json});");
    }

    /// <summary>当前主题的强调色（十六进制），供启动页按钮 / 面板跟随软件配色</summary>
    private static string AccentHex()
    {
        var brush = Application.Current.TryFindResource("Accent") as SolidColorBrush;
        var c = brush?.Color ?? Color.FromRgb(0xD9, 0x4F, 0x5C);
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    /// <summary>Windows 系统强调色；安装进度、安装详情等安装相关配色跟它走，读不到时回退软件强调色。</summary>
    private static string SystemAccentHex()
    {
        try
        {
            // Windows 11：AccentColor 为 AABBGGRR；Windows 10 也可能只有 ColorizationColor（AARRGGBB）。
            var accent = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null);
            if (accent is int a)
            {
                var r = (byte)(a & 0xFF);
                var g = (byte)((a >> 8) & 0xFF);
                var b = (byte)((a >> 16) & 0xFF);
                if (r + g + b > 24) return $"#{r:X2}{g:X2}{b:X2}";
            }

            var colorization = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "ColorizationColor", null);
            if (colorization is int c)
            {
                var r = (byte)((c >> 16) & 0xFF);
                var g = (byte)((c >> 8) & 0xFF);
                var b = (byte)(c & 0xFF);
                if (r + g + b > 24) return $"#{r:X2}{g:X2}{b:X2}";
            }
        }
        catch
        {
            // 注册表不可读时回退
        }
        return AccentHex();
    }

    private void PushPatch(string json)
    {
        if (!_webReady) return;
        _ = Web.CoreWebView2.ExecuteScriptAsync($"window.miNestUpdate({json});");
    }

    private void Toast(string text)
    {
        if (!_webReady) return;
        var json = JsonSerializer.Serialize(text);
        _ = Web.CoreWebView2.ExecuteScriptAsync($"window.miNestToast({json});");
    }

    private string BuildHint()
    {
        var g = GameLocator.Get(_selectedKey);
        _locatedDir = ResolveDir();
        if (_install != null && _install.Key == _selectedKey)
        {
            if (_install.Completed) return "安装完成，游戏文件已写入，可以直接开始游戏。";
            if (_install.Stopping) return "正在停止安装并清理本次写入的文件…";
            if (_install.Paused) return "安装已暂停。点「继续安装」从当前位置继续，不会重新下载。";
            return "正在安装游戏。鼠标靠近主按钮查看下载详情；点主按钮可暂停安装。";
        }
        if (!string.IsNullOrWhiteSpace(_installError)) return _installError!;
        if (_runningGames.Contains(_selectedKey)) return "游戏运行中，可直接开始其它游戏；游戏退出后此按钮自动恢复。";
        if (_locatedDir == null) return "点击「安装游戏」会弹出安装窗口，确认路径与语音后由软件内直接下载安装，不再跳转官方启动器。";
        var exe = GameLocator.FindExe(_locatedDir, g);
        return exe == null
            ? "该目录中没有游戏主程序，点击「安装游戏」会重新选择安装位置；也可以点「定位游戏」换到正确目录"
            : "已定位：" + _locatedDir;
    }

    /// <summary>本地文件转成虚拟域名 URL（minest.local 映射到数据目录）</summary>
    private string? ToUrl(string? absPath)
    {
        if (string.IsNullOrWhiteSpace(absPath)) return null;
        var full = Path.GetFullPath(absPath).Replace('\\', '/');
        var root = Path.GetFullPath(_rootDir).Replace('\\', '/').TrimEnd('/');
        if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return "https://minest.local" + full.Substring(root.Length);
        return null;
    }

    // ---------- 壁纸列表 ----------

    /// <summary>读取当前游戏官方背景列表；官方一版给几张，左侧就有几个圆点</summary>
    private void LoadBackgrounds(string key)
    {
        var cached = LauncherBackgroundService.GetCached(key);
        _bgItems = cached?.Items ?? new List<LauncherBackgroundService.BackgroundItem>();
        _bgIndex = SettingsService.Settings.GameBackgroundIndex.TryGetValue(key, out var saved)
            ? Math.Clamp(saved, 0, Math.Max(0, _bgItems.Count - 1))
            : 0;
        _useCustom = HasCustomBackground(key);
        _paused = false;
        _ = RefreshOfficialAsync(key);
    }

    private async Task RefreshOfficialAsync(string key)
    {
        var meta = await LauncherBackgroundService.RefreshAsync(key);
        if (meta == null || key != _selectedKey) return;

        var oldIndex = _bgIndex;
        _bgItems = meta.Items;
        _bgIndex = SettingsService.Settings.GameBackgroundIndex.TryGetValue(key, out var saved)
            ? Math.Clamp(saved, 0, Math.Max(0, _bgItems.Count - 1))
            : Math.Clamp(oldIndex, 0, Math.Max(0, _bgItems.Count - 1));
        PushState();
    }

    private static bool HasCustomBackground(string key)
        => SettingsService.Settings.GameBackgrounds.TryGetValue(key, out var p) && File.Exists(p);

    private async Task SelectBackgroundAsync(int index, bool save = true)
    {
        if (index < 0) return;

        // 左侧第一颗圆点 = 用户自定义壁纸
        var hasCustom = HasCustomBackground(_selectedKey);
        if (hasCustom && index == 0)
        {
            _useCustom = true;
            _paused = false;
            PushState();
            return;
        }
        if (hasCustom) index -= 1;
        if (index >= _bgItems.Count) return;

        _useCustom = false;
        SettingsService.Settings.GameBackgroundIndex[_selectedKey] = index;
        SettingsService.Save();

        _bgIndex = index;
        _paused = false;

        // 动态壁纸体积大，切到这张时才下载
        var item = index >= 0 && index < _bgItems.Count ? _bgItems[index] : null;
        if (item != null && item.VideoPath == null && !string.IsNullOrWhiteSpace(item.VideoUrl))
        {
            _preparing = true;
            PushState();
            await LauncherBackgroundService.EnsureVideoAsync(_selectedKey, item.Id);
            _preparing = false;
        }
        PushState();
    }

    private async Task StepBackgroundAsync(int delta)
    {
        var hasCustom = HasCustomBackground(_selectedKey);
        var total = _bgItems.Count + (hasCustom ? 1 : 0);
        if (total <= 0) return;
        var cur = hasCustom ? (_useCustom ? 0 : _bgIndex + 1) : _bgIndex;
        var next = ((cur + delta) % total + total) % total;
        await SelectBackgroundAsync(next);
    }

    private async Task EnsureCurrentVideoAsync()
    {
        if (_useCustom) return;
        var item = _bgIndex >= 0 && _bgIndex < _bgItems.Count ? _bgItems[_bgIndex] : null;
        if (item == null || item.VideoPath != null || string.IsNullOrWhiteSpace(item.VideoUrl)) return;
        _preparing = true;
        PushState();
        await LauncherBackgroundService.EnsureVideoAsync(_selectedKey, item.Id);
        _preparing = false;
        PushState();
    }

    // ---------- 页面消息 ----------

    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string type;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            type = doc.RootElement.GetProperty("type").GetString() ?? "";
        }
        catch
        {
            return;
        }

        try
        {
            switch (type)
            {
                case "ready":
                    PushState();
                    break;
                case "selectGame":
                    {
                        var key = ReadString(e.WebMessageAsJson, "key");
                        if (!string.IsNullOrEmpty(key) && key != _selectedKey)
                        {
                            _selectedKey = key;
                            _installError = null;
                            LoadBackgrounds(key);
                            PushState();
                            RefreshPreDownload();
                        }
                        break;
                    }
                case "bg":
                    await SelectBackgroundAsync((int)ReadNumber(e.WebMessageAsJson, "index"));
                    break;
                case "bgNext":
                    await StepBackgroundAsync(1);
                    break;
                case "bgPrev":
                    await StepBackgroundAsync(-1);
                    break;
                case "pauseToggle":
                    _paused = !_paused;
                    PushPatch($"{{\"paused\":{(_paused ? "true" : "false")}}}");
                    break;
                case "saveBg":
                    SaveCurrentBackground();
                    break;
                case "changeBg":
                    ChangeCustomBackground();
                    break;
                case "resetBg":
                    SettingsService.Settings.GameBackgrounds.Remove(_selectedKey);
                    SettingsService.Settings.CustomBackground = null;
                    SettingsService.Save();
                    LoadBackgrounds(_selectedKey);
                    PushState();
                    break;
                case "start":
                    await StartGameAsync();
                    break;
                case "installToggle":
                    ToggleInstallPause();
                    break;
                case "installStop":
                    StopInstallAndCleanup();
                    break;
                case "gameSettings":
                    {
                        var g = GameLocator.Get(_selectedKey);
                        var installActive = _install != null && _install.Key == _selectedKey
                            && !_install.Completed && !_install.Stopping;
                        new MiNest.Windows.GameSettingsWindow(
                            _selectedKey, g, ResolveDir(), installActive, StopInstallAndCleanup)
                        {
                            Owner = Window.GetWindow(this),
                        }.ShowDialog();
                        PushState();
                        break;
                    }
                case "gameOrder":
                    {
                        var keys = new List<string>();
                        using (var doc = JsonDocument.Parse(e.WebMessageAsJson))
                        {
                            if (doc.RootElement.TryGetProperty("order", out var arr) &&
                                arr.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var item in arr.EnumerateArray())
                                {
                                    if (item.ValueKind != JsonValueKind.String) continue;
                                    var key = item.GetString();
                                    if (key != null && GameLocator.Games.Any(g => g.Key == key) &&
                                        !keys.Contains(key))
                                        keys.Add(key);
                                }
                            }
                        }
                        if (keys.Count > 0)
                        {
                            SettingsService.Settings.LaunchGameOrder = keys;
                            SettingsService.Save();

                            // 首页始终展示排序后的第一款游戏。
                            var first = OrderedGames().FirstOrDefault();
                            if (first != null && first.Key != _selectedKey)
                            {
                                _selectedKey = first.Key;
                                _installError = null;
                                LoadBackgrounds(_selectedKey);
                            }
                            PushState();
                            RefreshPreDownload();
                        }
                        break;
                    }
                case "locate":
                    PickFolder();
                    break;
                case "install":
                    await InstallGameAsync();
                    break;
                case "predownload":
                    ShowPreDownloadDialog();
                    break;
                case "mhyy":
                    _main.OpenInApp("https://mhyy.mihoyo.com/");
                    break;
                case "reserve":
                    {
                        var url = ReadString(e.WebMessageAsJson, "url");
                        if (!string.IsNullOrEmpty(url)) _main.OpenInApp(url);
                        break;
                    }
            }
        }
        catch (Exception ex)
        {
            Toast("操作失败：" + ex.Message);
        }
    }

    private static string? ReadString(string json, string prop)
        => JsonDocument.Parse(json).RootElement.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static double ReadNumber(string json, string prop)
        => JsonDocument.Parse(json).RootElement.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble() : 0;

    // ---------- 壁纸保存 / 自定义 ----------

    private void SaveCurrentBackground()
    {
        if (_useCustom)
        {
            Toast("当前是自定义壁纸，本身就是本地图片，无需再保存");
            return;
        }
        var item = _bgIndex >= 0 && _bgIndex < _bgItems.Count ? _bgItems[_bgIndex] : null;
        var src = item?.VideoPath ?? item?.ImagePath;
        if (src == null || !File.Exists(src))
        {
            Toast("当前壁纸还没有下载完成，无法保存");
            return;
        }
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "MikoNest 壁纸");
            Directory.CreateDirectory(dir);
            var name = GameLocator.Get(_selectedKey).Name + "_" + Path.GetFileName(src);
            File.Copy(src, Path.Combine(dir, name), true);
            Toast("壁纸已保存到 图片\\MikoNest 壁纸");
        }
        catch (Exception ex)
        {
            Toast("保存失败：" + ex.Message);
        }
    }

    private void ChangeCustomBackground()
    {
        using var dlg = new System.Windows.Forms.OpenFileDialog
        {
            Title = $"选择「{GameLocator.Get(_selectedKey).Name}」启动页背景图",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif|所有文件|*.*",
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        try
        {
            var bgDir = LauncherBackgroundService.CacheDir;
            Directory.CreateDirectory(bgDir);
            var ext = Path.GetExtension(dlg.FileName);
            var target = Path.Combine(bgDir, $"custom_{_selectedKey}{ext}");
            File.Copy(dlg.FileName, target, true);
            SettingsService.Settings.GameBackgrounds[_selectedKey] = target;
            SettingsService.Save();
            LoadBackgrounds(_selectedKey);
            PushState();
        }
        catch (Exception ex)
        {
            Toast("设置背景失败：" + ex.Message);
        }
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

    private void PickFolder()
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = $"请选择「{GameLocator.Get(_selectedKey).Name}」的安装目录（包含 {string.Join(" / ", GameLocator.Get(_selectedKey).ExeNames)} 的文件夹）",
            UseDescriptionForTitle = true,
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        SettingsService.Settings.GamePaths[_selectedKey] = dlg.SelectedPath;
        SettingsService.Save();
        PushState();
    }

    private async Task StartGameAsync()
    {
        if (_install != null && _install.Key == _selectedKey && !_install.Completed)
        {
            ToggleInstallPause();
            return;
        }

        var g = GameLocator.Get(_selectedKey);
        _locatedDir = ResolveDir();
        var exe = _locatedDir != null ? GameLocator.FindExe(_locatedDir, g) : null;

        if (exe == null)
        {
            // 按钮显示「安装游戏」时，即使旧目录仍存在也必须进入安装流程，不能只提示定位失败。
            await InstallGameAsync();
            return;
        }
        if (_runningGames.Contains(_selectedKey)) return;

        // 打开 / 启动游戏时自动检查 MikoNest 自身更新；用户选择更新时安装器会接管并退出旧进程。
        var owner = Window.GetWindow(this);
        if (owner != null && await UpdatePromptService.CheckAndPromptAsync(owner))
            return;

        var key = _selectedKey;
        _runningGames.Add(key);
        PushState();

        var workDir = Path.GetDirectoryName(exe) ?? _locatedDir;
        var started = false;
        try
        {
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = workDir, UseShellExecute = false });
            started = true;
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = workDir, UseShellExecute = true });
                started = true;
            }
            catch (Exception ex)
            {
                _runningGames.Remove(key);
                PushState();
                Toast("启动失败：" + ex.Message);
                return;
            }
        }
        if (!started) { _runningGames.Remove(key); PushState(); return; }

        // 启动 3 秒后最小化窗口
        await Task.Delay(3000);
        var win = Window.GetWindow(this);
        if (win != null) win.WindowState = WindowState.Minimized;

        // 「游戏启动中」一直显示，直到该游戏所有进程退出（游戏本体/启动器换进程也能正确判定）
        _ = TrackGameExitAsync(key, Path.GetFileNameWithoutExtension(exe));
    }

    /// <summary>轮询进程名：等到进程出现（最多 60s），再等到连续消失后恢复按钮</summary>
    private async Task TrackGameExitAsync(string key, string processName)
    {
        var appeared = false;
        var missingChecks = 0;

        for (var i = 0; i < 30 && !appeared; i++)
        {
            await Task.Delay(500);
            if (Process.GetProcessesByName(processName).Length > 0) appeared = true;
        }

        while (true)
        {
            await Task.Delay(2000);
            var alive = Process.GetProcessesByName(processName).Length > 0;
            if (alive) { missingChecks = 0; continue; }
            missingChecks++;
            if (missingChecks >= 3)
            {
                _runningGames.Remove(key);
                PushState();
                return;
            }
        }
    }

    private async Task InstallGameAsync()
    {
        if (_install != null)
        {
            Toast($"正在安装「{_install.Name}」，请先完成或停止当前安装。");
            return;
        }

        var g = GameLocator.Get(_selectedKey);
        string ver = "";
        long size = 0;
        var build = await LauncherBackgroundService.GetChunkBuildAsync(_selectedKey, "zh-cn");
        if (build != null)
        {
            ver = build.Version;
            size = build.TotalUncompressedBytes;
        }
        else
        {
            (ver, size) = await LauncherBackgroundService.GetLatestPackageAsync(_selectedKey);
        }

        var dlg = new MiNest.Windows.InstallGameDialog(g, DefaultGamesRoot(), false, ver, size)
        {
            Owner = Window.GetWindow(this),
        };
        if (dlg.ShowDialog() != true) return;
        BeginInstall(g, dlg.SelectedDir, dlg.SelectedLanguage);
    }

    private void ShowPreDownloadDialog()
    {
        if (_install != null)
        {
            Toast($"正在安装「{_install.Name}」，请先完成或停止当前安装。");
            return;
        }
        if (_preDownload == null) return;
        var g = GameLocator.Get(_selectedKey);
        var dlg = new MiNest.Windows.InstallGameDialog(
            g, ResolveDir() ?? DefaultGamesRoot(), _preDownload.IsPreDownload, _preDownload.Version, _preDownload.SizeBytes)
        {
            Owner = Window.GetWindow(this),
        };
        if (dlg.ShowDialog() != true) return;
        BeginInstall(g, dlg.SelectedDir, dlg.SelectedLanguage);
        RefreshPreDownload();
    }

    // ---------- 启动页内嵌安装 ----------

    private void BeginInstall(GameDef game, string dest, string language)
    {
        _installError = null;
        _installStopRequested = false;
        _installCts?.Dispose();
        _installCts = new CancellationTokenSource();
        _installPause = new GameInstallService.PauseController();

        var job = new InstallUi
        {
            Key = game.Key,
            Name = game.Name,
            Dest = dest,
            Language = language,
            Stage = "正在获取官方安装清单…",
        };
        _install = job;
        _selectedKey = game.Key;
        PushState();
        Toast($"已开始安装 {game.Name}，进度会显示在主按钮上");
        _ = RunInstallAsync(job, _installCts, _installPause);
    }

    private async Task RunInstallAsync(
        InstallUi job, CancellationTokenSource cts, GameInstallService.PauseController pause)
    {
        var progress = new Progress<GameInstallService.Progress>(p =>
        {
            if (_install != job) return;
            job.Stage = p.Stage;
            job.Done = p.Done;
            job.Total = p.Total;
            job.InstallDone = p.InstallDone;
            job.InstallTotal = p.InstallTotal;
            job.Speed = p.BytesPerSecond;
            PushInstallProgress();
        });

        try
        {
            var finalDir = await GameInstallService.InstallAsync(
                job.Key, job.Dest, job.Language, progress, cts.Token, pause);

            if (_install != job) return;
            job.Completed = true;
            job.Paused = false;
            job.Stopping = false;
            job.Dest = finalDir;
            job.Stage = "安装完成，游戏文件已写入。";
            if (job.Total > 0) job.Done = job.Total;
            if (job.InstallTotal > 0) job.InstallDone = job.InstallTotal;
            _locatedDir = ResolveDir();
            PushState();
            Toast($"{job.Name} 已安装完成，可以直接开始游戏");

            await Task.Delay(1200);
            if (_install == job)
            {
                _install = null;
                PushState();
            }
            RefreshPreDownload();
        }
        catch (OperationCanceledException)
        {
            if (_install != job) return;
            _install = null;
            _installError = _installStopRequested ? "安装已停止，本次写入的文件和临时分片已清理。" : null;
            if (_installStopRequested)
            {
                ForgetGamePathIfNotInstalled(job.Key);
                _locatedDir = null;
            }
            PushState();
            if (_installStopRequested) Toast("已停止安装，并清理本次写入的文件");
        }
        catch (Exception ex)
        {
            if (_install != job) return;
            _install = null;
            _installError = $"安装失败：{ex.Message} 可再次点击「安装游戏」重试。";
            PushState();
            Toast("安装失败：" + ex.Message);
        }
    }

    /// <summary>停止安装并清理后，如果目录里已经没有主程序，就忘掉这条安装路径，避免主按钮误显示「开始游戏」。</summary>
    private static void ForgetGamePathIfNotInstalled(string key)
    {
        if (!SettingsService.Settings.GamePaths.TryGetValue(key, out var dir)) return;
        var game = GameLocator.Get(key);
        var valid = !string.IsNullOrWhiteSpace(dir) &&
                    Directory.Exists(dir) &&
                    GameLocator.FindExe(dir, game) != null;
        if (valid) return;

        SettingsService.Settings.GamePaths.Remove(key);
        SettingsService.Save();
    }

    private void ToggleInstallPause()
    {
        var job = _install;
        if (job == null || job.Completed || job.Stopping) return;
        if (job.Paused)
        {
            _installPause?.Resume();
            job.Paused = false;
            job.Stage = "正在继续安装…";
            Toast("已继续安装");
        }
        else
        {
            _installPause?.Pause();
            job.Paused = true;
            job.Stage = "安装已暂停，点击继续安装。";
            Toast("已暂停安装，已下载的部分会保留");
        }
        PushState();
    }

    private bool StopInstallAndCleanup()
    {
        var job = _install;
        if (job == null || job.Completed || job.Stopping) return false;
        _installStopRequested = true;
        job.Stopping = true;
        job.Paused = false;
        job.Stage = "正在停止安装，并清理本次写入的文件…";
        _installPause?.Resume();
        _installCts?.Cancel();
        PushState();
        return true;
    }

    private object InstallPayload(InstallUi job)
    {
        var pct = job.Total > 0 ? Math.Clamp(job.Done * 100d / job.Total, 0, 100) : 0;
        var remaining = Math.Max(0, job.Total - job.Done);
        var eta = job.Speed > 1 && remaining > 0
            ? FormatRemaining(remaining / job.Speed)
            : job.Completed ? "00:00:00" : "计算中";
        var title = job.Completed ? "安装完成"
            : job.Stopping ? "正在停止"
            : job.Paused ? "已暂停"
            : "下载中";
        return new
        {
            key = job.Key,
            name = job.Name,
            title,
            percent = Math.Round(pct, 1),
            percentText = $"{pct:0.0}%",
            paused = job.Paused,
            stopping = job.Stopping,
            completed = job.Completed,
            stage = job.Stage,
            doneText = FormatSize(job.Done),
            totalText = FormatSize(job.Total),
            installDoneText = FormatSize(job.InstallDone),
            installTotalText = FormatSize(job.InstallTotal),
            installPercent = job.InstallTotal > 0
                ? Math.Round(job.InstallDone * 100d / job.InstallTotal, 1)
                : 0,
            speedText = job.Speed > 1 ? FormatSize((long)job.Speed) + "/s" : "—",
            etaText = eta,
            dir = job.Dest,
        };
    }

    private void PushInstallProgress()
    {
        if (!_webReady || _install == null) return;
        var now = DateTime.UtcNow;
        if ((now - _lastInstallPush).TotalMilliseconds < 350) return;
        _lastInstallPush = now;
        var payload = JsonSerializer.Serialize(new
        {
            install = _install.Key == _selectedKey ? InstallPayload(_install) : null,
            hint = BuildHint(),
        });
        PushPatch(payload);
    }

    private static string FormatRemaining(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return "计算中";
        var total = (long)Math.Min(seconds, 99 * 3600 + 59 * 60 + 59);
        return $"{total / 3600:00}:{total % 3600 / 60:00}:{total % 60:00}";
    }

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
            "MikoNest", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private static string FormatSize(long bytes)
    {
        const double gb = 1024d * 1024 * 1024;
        return bytes >= gb ? $"{bytes / gb:0.00} GB" : $"{bytes / (1024d * 1024):0} MB";
    }

    /// <summary>新版本 / 预下载信息在后台刷新，有则推给页面</summary>
    public async void RefreshPreDownload()
    {
        var key = _selectedKey;
        try
        {
            _preDownload = await LauncherBackgroundService.GetUpdateStatusAsync(key, ResolveDir());
        }
        catch
        {
            _preDownload = null;
        }
        if (key == _selectedKey) PushState();
    }
}
