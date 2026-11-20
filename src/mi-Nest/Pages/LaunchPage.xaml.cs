using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
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
    private bool _webReady;
    private readonly string _rootDir = AppPaths.DataDir;

    public LaunchPage(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Loaded += OnLoaded;
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
                AutoSelectLocatedGame();
                LoadBackgrounds(_selectedKey);
                PushState();
                RefreshPreDownload();
                await EnsureCurrentVideoAsync();
            };
            Web.CoreWebView2.Navigate("https://minest.local/launch/launch.html");
        }
        catch (Exception ex)
        {
            MessageBox.Show("启动页初始化失败：" + ex.Message, "mi-Nest", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    private void AutoSelectLocatedGame()
    {
        var located = GameLocator.Games
            .Where(g => IsLocated(g.Key))
            .Select(g => g.Key)
            .ToList();
        if (located.Count > 0 && !located.Contains(_selectedKey))
            _selectedKey = located[0];
    }

    private static bool IsLocated(string key)
    {
        if (SettingsService.Settings.GamePaths.TryGetValue(key, out var p) && Directory.Exists(p)) return true;
        return GameLocator.DetectInstallDir(GameLocator.Get(key)) != null;
    }

    private void PushState()
    {
        if (!_webReady) return;

        var games = GameLocator.Games.Select(g => new
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
            preDownload = _preDownload == null ? null : new
            {
                text = $"预下载 {_preDownload.Version} · {FormatSize(_preDownload.SizeBytes)}",
            },
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
        if (_runningGames.Contains(_selectedKey)) return "游戏运行中，可直接开始其它游戏；游戏退出后此按钮自动恢复。";
        if (_locatedDir == null) return "点击「获取游戏」会弹出安装窗口（安装路径 / 语言 / 磁盘空间），确认后由官方启动器完成下载安装。";
        var exe = GameLocator.FindExe(_locatedDir, g);
        return exe == null
            ? $"已定位目录，但未找到主程序（{string.Join(" / ", g.ExeNames)}）"
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
                            LoadBackgrounds(key);
                            PushState();
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
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "mi-Nest 壁纸");
            Directory.CreateDirectory(dir);
            var name = GameLocator.Get(_selectedKey).Name + "_" + Path.GetFileName(src);
            File.Copy(src, Path.Combine(dir, name), true);
            Toast("壁纸已保存到 图片\\mi-Nest 壁纸");
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
        var g = GameLocator.Get(_selectedKey);
        _locatedDir = ResolveDir();
        var exe = _locatedDir != null ? GameLocator.FindExe(_locatedDir, g) : null;

        if (exe == null)
        {
            if (_locatedDir == null) { await InstallGameAsync(); }
            else Toast("未在目录中找到游戏主程序，请点击「定位游戏」重新选择");
            return;
        }
        if (_runningGames.Contains(_selectedKey)) return;

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
        var g = GameLocator.Get(_selectedKey);
        var (ver, size) = await LauncherBackgroundService.GetLatestPackageAsync(_selectedKey);
        var dlg = new MiNest.Windows.InstallGameDialog(g, DefaultGamesRoot(), false, ver, size)
        {
            Owner = Window.GetWindow(this),
        };
        if (dlg.ShowDialog() != true) return;
        OpenOfficialLauncher(g);
    }

    private void ShowPreDownloadDialog()
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

    /// <summary>预下载信息在后台刷新，有则推给页面</summary>
    public async void RefreshPreDownload()
    {
        var key = _selectedKey;
        _preDownload = await LauncherBackgroundService.GetPreDownloadAsync(key);
        if (key == _selectedKey) PushState();
    }
}
