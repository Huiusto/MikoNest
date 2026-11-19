using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MiNest.Services;
using MiNest.Windows;

namespace MiNest.Pages;

/// <summary>设置：界面配色 / 应用信息 / 关于 / 开发者首页</summary>
public partial class SettingsPage : UserControl
{
    private readonly MainWindow? _main;
    private bool _navInit;
    private bool _opacityInit;

    public SettingsPage(MainWindow? main = null)
    {
        InitializeComponent();
        _main = main;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildThemePicker();
        TxtVersion.Text = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.11";
        TxtDataDir.Text = AppPaths.DataDir;
        // 初始化同步设置值时不触发写入事件（避免误存）
        _navInit = true;
        var disabled = SettingsService.Settings.DisabledNavItems;
        ChkNavLaunch.IsChecked = !disabled.Contains("启动");
        ChkNavCommunity.IsChecked = !disabled.Contains("社区");
        ChkNavGacha.IsChecked = !disabled.Contains("抽卡记录");
        ChkNavTask.IsChecked = !disabled.Contains("任务/签到");
        ChkNavNote.IsChecked = !disabled.Contains("实时便笺");
        ChkNavLinks.IsChecked = !disabled.Contains("米哈游相关");
        ChkAutoStart.IsChecked = SettingsService.Settings.AutoStartOnBoot;
        ChkBringFront.IsChecked = SettingsService.Settings.BringFrontOnGameExit;
        ChkHutaoTheme.IsChecked = SettingsService.Settings.Theme == "HutaoRed";
        ChkHutaoBackdrop.IsChecked = SettingsService.Settings.HutaoBackdrop;
        TglAccountNav.IsChecked = !SettingsService.Settings.HideAccountNav;
        if (SettingsService.Settings.CloseBehavior == "Tray") RdoCloseTray.IsChecked = true;
        else RdoCloseExit.IsChecked = true;
        _navInit = false;
        RefreshNavManager();

        _opacityInit = true;
        SldOpacity.Value = Math.Clamp(SettingsService.Settings.CardOpacity, 0, 100);
        TxtOpacityVal.Text = $"{(int)SldOpacity.Value}%";
        _opacityInit = false;

        SldAppBgVeil.Value = Math.Clamp(SettingsService.Settings.CustomAppBackgroundVeil, 0, 100);
        TxtAppBgVeil.Text = $"{(int)SldAppBgVeil.Value}%";
        UpdateAppBgText();
    }

    private void UpdateAppBgText()
    {
        var path = SettingsService.Settings.CustomAppBackground;
        TxtAppBgName.Text = !string.IsNullOrWhiteSpace(path) && File.Exists(path)
            ? $"当前：{Path.GetFileName(path)}"
            : "当前：未设置（使用主题默认背景）";
    }

    // ---------- 隐藏设置：自定义软件背景图 ----------

    private void BtnPickAppBg_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择软件背景图",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif|所有文件|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        SettingsService.Settings.CustomAppBackground = dlg.FileName;
        SettingsService.Save();
        _main?.ApplyBackdrop();
        UpdateAppBgText();
    }

    private void BtnClearAppBg_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.Settings.CustomAppBackground = null;
        SettingsService.Save();
        _main?.ApplyBackdrop();
        UpdateAppBgText();
    }

    private void AppBgVeil_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtAppBgVeil == null) return;
        var v = (int)Math.Round(e.NewValue);
        TxtAppBgVeil.Text = $"{v}%";
        SettingsService.Settings.CustomAppBackgroundVeil = v;
        SettingsService.Save();
        _main?.ApplyBackdrop();
    }

    /// <summary>最上方滑动条：实时调整卡片 / 框框背景不透明度（0-100）</summary>
    private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_opacityInit || TxtOpacityVal == null) return;
        var v = (int)Math.Round(e.NewValue);
        TxtOpacityVal.Text = $"{v}%";
        ThemeService.SetCardOpacity(v);
    }

    /// <summary>设置页内打开实时便笺（原左侧导航项已移入设置）</summary>
    private void BtnDailyNote_Click(object sender, RoutedEventArgs e)
        => _main?.ShowPage("实时便笺");

    /// <summary>隐藏设置：左侧内置项目栏显示开关（默认全部显示）</summary>
    private void NavVisibility_Changed(object sender, RoutedEventArgs e)
    {
        if (_navInit) return;
        if (sender is System.Windows.Controls.CheckBox cb && cb.Tag is string key)
            _main?.SetNavVisible(key, cb.IsChecked == true);
    }

    /// <summary>「更新」上方的开关：显示 / 隐藏左侧「账号」按钮</summary>
    private void AccountNav_Changed(object sender, RoutedEventArgs e)
    {
        if (_navInit) return;
        SettingsService.Settings.HideAccountNav = TglAccountNav.IsChecked != true;
        SettingsService.Save();
        _main?.ApplyAccountNavVisibility();
    }

    // ---------- 管理项目栏：排列左侧导航顺序 ----------

    private void RefreshNavManager()
    {
        NavManagerPanel.Children.Clear();
        if (_main == null) return;
        var order = _main.GetNavOrderKeys();
        var s = SettingsService.Settings;
        for (var i = 0; i < order.Count; i++)
        {
            var key = order[i];
            var name = key.StartsWith("pin:")
                ? s.PinnedLinks.FirstOrDefault(p => $"pin:{p.Key}" == key)?.Name ?? "固定链接"
                : LocalizationService.Get(LocalizationService.NavKey(key));

            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameTxt = new TextBlock
            {
                Text = name,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextPrimary"),
            };
            row.Children.Add(nameTxt);

            var up = MakeArrowBtn("↑", i > 0);
            up.Click += (_, _) => MoveNav(key, -1);
            Grid.SetColumn(up, 1);
            row.Children.Add(up);

            var down = MakeArrowBtn("↓", i < order.Count - 1);
            down.Click += (_, _) => MoveNav(key, +1);
            Grid.SetColumn(down, 2);
            row.Children.Add(down);

            if (key.StartsWith("pin:"))
            {
                var remove = new Button
                {
                    Content = "✕",
                    Width = 28,
                    Height = 24,
                    Padding = new Thickness(0),
                    Margin = new Thickness(6, 0, 0, 0),
                    FontSize = 12,
                    ToolTip = "移除固定",
                    Cursor = System.Windows.Input.Cursors.Hand,
                };
                remove.Click += (_, _) => RemovePin(key);
                Grid.SetColumn(remove, 3);
                row.Children.Add(remove);
            }
            NavManagerPanel.Children.Add(row);
        }
    }

    private Button MakeArrowBtn(string arrow, bool enabled)
    {
        var b = new Button
        {
            Content = arrow,
            Width = 28,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 13,
            IsEnabled = enabled,
            Cursor = enabled ? System.Windows.Input.Cursors.Hand : null,
        };
        return b;
    }

    private void MoveNav(string key, int delta)
    {
        if (_main == null) return;
        _main.MoveNavItem(key, delta);
        RefreshNavManager();
    }

    private void RemovePin(string key)
    {
        if (_main == null) return;
        var pinKey = key["pin:".Length..];
        _main.UnpinLink(pinKey);
        RefreshNavManager();
    }

    // ---------- mi-Nest 隐藏设置（密码 310621322） ----------

    private const string HiddenPwd = "310621322";

    private void BtnHidden_Click(object sender, RoutedEventArgs e)
    {
        HiddenPanel.Visibility = Visibility.Visible;
        TxtPwd.Focus();
        TxtPwdErr.Visibility = Visibility.Collapsed;
    }

    private void BtnPwdOk_Click(object sender, RoutedEventArgs e) => TryEnterHidden();

    private void TxtPwd_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) TryEnterHidden();
    }

    private void TryEnterHidden()
    {
        if (TxtPwd.Password != HiddenPwd)
        {
            TxtPwdErr.Text = "密码错误，请重试。";
            TxtPwdErr.Visibility = Visibility.Visible;
            TxtPwd.Clear();
            TxtPwd.Focus();
            return;
        }
        TxtPwdErr.Visibility = Visibility.Collapsed;
        HiddenSettings.Visibility = Visibility.Visible;
        TxtPwd.IsEnabled = false;
    }

    private void BuildThemePicker()
    {
        ThemePanel.Children.Clear();
        for (var i = 0; i < ThemeService.Themes.Length; i++)
        {
            var theme = ThemeService.Themes[i];
            var rb = new RadioButton
            {
                GroupName = "theme",
                Margin = new Thickness(0, 4, 14, 4),
                IsChecked = SettingsService.Settings.Theme == theme,
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalContentAlignment = VerticalAlignment.Center,
                Style = (Style)FindResource("ThemeSwatchRadio"),
            };
            rb.Checked += (_, _) =>
            {
                ThemeService.Apply(theme);
                RefreshThemeRings();
            };

            var swatch = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Background = (Brush)new BrushConverter().ConvertFromString(ThemeService.ThemeColors[i])!,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(2),
                Margin = new Thickness(-2, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(swatch);
            sp.Children.Add(new TextBlock
            {
                Text = ThemeService.ThemeNames[i],
                FontSize = 13,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextPrimary"),
            });
            rb.Content = sp;
            System.Windows.Automation.AutomationProperties.SetName(rb, ThemeService.ThemeNames[i]);
            ThemePanel.Children.Add(rb);
        }
        RefreshThemeRings();
    }

    /// <summary>选中的配色在色块外画一圈主题色描边，未选中则透明。</summary>
    private void RefreshThemeRings()
    {
        var accent = (Brush)FindResource("Accent");
        foreach (var child in ThemePanel.Children)
        {
            if (child is not RadioButton rb || rb.Content is not StackPanel sp) continue;
            if (sp.Children.Count == 0 || sp.Children[0] is not Border swatch) continue;
            swatch.BorderBrush = rb.IsChecked == true ? accent : Brushes.Transparent;
        }
    }

    private void BtnDevHome_Click(object sender, RoutedEventArgs e)
    {
        if (_main == null) return;
        _main.OpenInApp("https://www.miyoushe.com/dby/accountCenter/postList?id=310621322");
    }

    private void BtnRepoHome_Click(object sender, RoutedEventArgs e)
    {
        if (_main == null) return;
        _main.OpenInApp("https://github.com/Huiusto/mi-Nest");
    }

    private bool _checkingUpdate;

    private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_checkingUpdate) return;
        _checkingUpdate = true;

        var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0, 0);
        BtnUpdate.IsEnabled = false;
        BtnUpdate.Content = "检查中…";
        SetHint("");

        try
        {
            var (info, error) = await UpdateService.GetLatestAsync();
            if (info == null)
            {
                SetHint("检查失败：" + (error ?? "未知错误"));
                MessageBox.Show("检查更新失败：" + (error ?? "未知错误") +
                                "\n\n可稍后重试，或直接在 GitHub 的 Release 页手动下载。", "检查更新",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var latest = UpdateService.ParseTag(info.Version);
            if (latest != null && latest <= current)
            {
                SetHint("已是最新版本");
                MessageBox.Show($"当前已是最新版本 v{current.ToString(3)}。\n\n" +
                                $"最新发布：v{info.Version}（{info.ReleasePage}）", "检查更新",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrEmpty(info.AssetUrl))
            {
                SetHint($"发现 v{info.Version}（未附带安装包）");
                var r = MessageBox.Show($"发现新版本 v{info.Version}（当前 v{current.ToString(3)}），" +
                                        "但该 Release 没有附带安装包附件。\n\n是否打开下载页手动下载？", "发现新版本",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.OK) OpenReleasePage(info.ReleasePage);
                return;
            }

            var sizeText = info.AssetSize > 0 ? $"（{info.AssetSize / 1024d / 1024d:F1} MB）" : "";
            var ok = MessageBox.Show($"发现新版本 v{info.Version}（当前 v{current.ToString(3)}）。\n\n" +
                                     $"安装包：{info.AssetName} {sizeText}\n\n" +
                                     "点击「确定」开始下载，下载完成后会自动安装并重启软件。\n" +
                                     "（安装时会弹出 Windows 授权窗口，请点「是」）",
                "发现新版本", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (ok != MessageBoxResult.OK) return;

            BtnUpdate.Content = "下载 0%";
            var path = await UpdateService.DownloadAsync(info, (done, total) =>
            {
                if (total <= 0) return;
                var pct = (int)Math.Round(done * 100d / total);
                BtnUpdate.Content = $"下载 {pct}%";
            });

            BtnUpdate.Content = "安装中…";
            SetHint("安装包已下载，正在启动安装程序…");
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = BuildInstallerArgs(),
                UseShellExecute = true,
            });
            await System.Threading.Tasks.Task.Delay(1200);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            SetHint("更新失败：" + ex.Message);
            MessageBox.Show("更新失败：" + ex.Message + "\n\n可在 GitHub 的 Release 页手动下载安装包。", "更新",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _checkingUpdate = false;
            BtnUpdate.IsEnabled = true;
            BtnUpdate.Content = "更新";
        }
    }

    /// <summary>
    /// 静默安装参数：便携版就地覆盖当前目录，已安装版沿用原来的安装目录。
    /// /D 必须是最后一个参数，且不能带引号。
    /// </summary>
    private string BuildInstallerArgs()
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

    /// <summary>更新提示：有内容才占位，避免「更新」和「卸载」之间出现空白间隙。</summary>
    private void SetHint(string text)
    {
        TxtUpdateHint.Text = text;
        TxtUpdateHint.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OpenReleasePage(string url)
    {
        if (_main != null) _main.OpenInApp(url);
        else
        {
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { }
        }
    }

    private void BtnUninstall_Click(object sender, RoutedEventArgs e)
    {
        // 查找卸载程序：先当前目录，再从注册表卸载项读取
        var uninstall = Path.Combine(AppContext.BaseDirectory, "uninstall.exe");
        if (!File.Exists(uninstall))
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\mi-Nest")
                    ?? Registry.LocalMachine.OpenSubKey(
                    @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\mi-Nest");
                if (key?.GetValue("UninstallString") is string str && str.Length > 0)
                {
                    var p = str.Trim().Trim('"');
                    if (File.Exists(p)) uninstall = p;
                }
            }
            catch { }
        }
        if (!File.Exists(uninstall))
        {
            MessageBox.Show("未检测到安装版卸载程序（当前为便携/开发版运行，可直接删除程序目录）。", "卸载",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new UninstallDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        try
        {
            Process.Start(new ProcessStartInfo(uninstall) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动卸载程序失败：{ex.Message}", "卸载",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // 卸载程序已启动：稍等片刻后自动退出本程序，
        // 否则本程序占用安装目录里的 exe/dll，卸载时会删不掉、留下残留文件。
        ScheduleQuitAfterUninstall();
    }

    /// <summary>启动卸载程序后调用：短暂延时后自动退出本程序（留出卸载器完成初始化/提权的时间）。</summary>
    internal static void ScheduleQuitAfterUninstall()
    {
        var quit = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        quit.Tick += (_, _) =>
        {
            quit.Stop();
            Application.Current.Shutdown();
        };
        quit.Start();
    }

    private void BtnOpenDataDir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.DataDir}\"") { UseShellExecute = true });
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>更改数据目录：选择新目录 → 写入 settings.DataDirOverride → 提示重启生效。</summary>
    private void BtnChangeDataDir_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择新的数据目录（设置、账号、抽卡记录等将保存在该目录）",
            SelectedPath = AppPaths.DataDir,
            UseDescriptionForTitle = true,
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;
        var path = dialog.SelectedPath.Trim();
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法创建该目录：{ex.Message}", "更改数据目录",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SettingsService.Settings.DataDirOverride = path;
        SettingsService.Save();
        TxtDataDir.Text = path;
        MessageBox.Show(
            "数据目录已更改。重启 mi-Nest 后生效；旧的设置、账号、抽卡记录不会自动迁移，可手动复制到新目录。",
            "更改数据目录", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---------- 隐藏设置：胡桃主题配色 / 胡桃风格背景 ----------
    private void HutaoTheme_Changed(object sender, RoutedEventArgs e)
    {
        if (_navInit) return;
        var on = ChkHutaoTheme.IsChecked == true;
        ThemeService.Apply(on ? "HutaoRed" : "Light");
        BuildThemePicker(); // 同步顶部「界面配色」单选项的选中状态
    }

    private void HutaoBackdrop_Changed(object sender, RoutedEventArgs e)
    {
        if (_navInit) return;
        SettingsService.Settings.HutaoBackdrop = ChkHutaoBackdrop.IsChecked == true;
        SettingsService.Save();
        _main?.ApplyBackdrop();
    }

    // ---------- 通用设置：启动设置 / 关闭设置 ----------
    private const string RunKeyName = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "mi-Nest";

    private void ChkAutoStart_Changed(object sender, RoutedEventArgs e)
    {
        if (_navInit) return;
        var on = ChkAutoStart.IsChecked == true;
        SettingsService.Settings.AutoStartOnBoot = on;
        SettingsService.Save();
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyName);
            if (on) key.SetValue(RunValueName, "\"" + Environment.ProcessPath + "\"");
            else key.DeleteValue(RunValueName, false);
        }
        catch
        {
            // 注册表写入失败不阻断设置保存
        }
    }

    private void ChkBringFront_Changed(object sender, RoutedEventArgs e)
    {
        if (_navInit) return;
        SettingsService.Settings.BringFrontOnGameExit = ChkBringFront.IsChecked == true;
        SettingsService.Save();
        _main?.UpdateGameWatch();
    }

    private void CloseBehavior_Changed(object sender, RoutedEventArgs e)
    {
        if (_navInit) return;
        SettingsService.Settings.CloseBehavior = RdoCloseTray.IsChecked == true ? "Tray" : "Exit";
        SettingsService.Save();
        _main?.ApplyCloseBehavior();
    }
}




