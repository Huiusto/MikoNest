using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MiNest.Models;
using MiNest.Services;
using MiNest.Windows;

namespace MiNest.Pages;

/// <summary>
/// 任务 / 签到（能力与交互对齐 TeyvatGuide「实用脚本」页）：
/// 米游币每日任务、各游戏角色签到、多账号一键执行、滚动日志输出。
/// </summary>
public partial class TaskPage : UserControl
{
    private sealed class RoleRow
    {
        public required SignService.GameCheckin Game;
        public required SignRole Role;
        public required System.Windows.Controls.CheckBox Box;
        public required TextBlock StatusText;
        public required Border RowBorder;
        public bool Signed;
        public bool StateKnown;
    }

    private readonly MainWindow _main;
    private readonly ObservableCollection<SignLogEntry> _logs = new();
    private readonly List<RoleRow> _roleRows = new();
    private SignService.MissionSnapshot? _missions;
    private bool _subscribed;
    private bool _busy;
    private bool _suppressAccountChange;
    private int _signVersion;
    private int _missionVersion;

    private static readonly Dictionary<string, Color> GameColors = new()
    {
        ["genshin"] = Color.FromRgb(0x47, 0xC4, 0xF7),
        ["starrail"] = Color.FromRgb(0x8A, 0x6D, 0xF0),
        ["zzz"] = Color.FromRgb(0xFF, 0x5C, 0x4D),
        ["bh3"] = Color.FromRgb(0x4A, 0x8E, 0xF0),
        ["bh2"] = Color.FromRgb(0xFF, 0x9E, 0x3D),
        ["nxx"] = Color.FromRgb(0x5A, 0xC8, 0xFA),
    };

    /// <summary>游戏小图标（Assets/game 下），key 与 SignService.Games 一致；缺图回退色块。</summary>
    private static readonly Dictionary<string, string> GameIcons = new()
    {
        ["genshin"] = "game_genshin.png",
        ["starrail"] = "game_starrail.png",
        ["zzz"] = "game_zzz.png",
        ["bh3"] = "game_bh3.jpg",
        ["bh2"] = "game_bh2.jpg",
        ["nxx"] = "game_nxx.jpg",
        ["miliastra"] = "game_miliastra.png",
        ["bhyj"] = "game_bhyj.png",
    };

    private static System.Windows.Media.Imaging.BitmapImage? LoadGameIcon(string fileName)
    {
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri($"pack://application:,,,/MikoNest;component/Assets/game/{fileName}", UriKind.Absolute);
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.EndInit();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public TaskPage(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        LogList.ItemsSource = _logs;
        Loaded += OnLoaded;
    }

    // ==================== 生命周期 ====================

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            _subscribed = true;
            AccountService.Changed += () => Dispatcher.Invoke(() =>
            {
                RefreshAccountBar();
                _ = RefreshAllSafeAsync();
            });
        }
        RefreshAccountBar();
        _ = RefreshAllSafeAsync();
    }

    private void RefreshAccountBar()
    {
        var selected = CmbAccount.SelectedItem as Account;
        _suppressAccountChange = true;
        try
        {
            CmbAccount.ItemsSource = null;
            CmbAccount.ItemsSource = AccountService.All;
            CmbAccount.SelectedItem = AccountService.Active ?? selected;
        }
        finally
        {
            _suppressAccountChange = false;
        }

        var acc = AccountService.Active;
        if (acc == null)
        {
            TxtUid.Text = "";
            TxtAccountStatus.Foreground = BrushOf("TextSecondary");
            TxtAccountStatus.Text = "● 未添加账号";
            BannerStoken.Visibility = Visibility.Visible;
            TxtAccountHint.Text = "请到「账号」页添加并登录账号（扫码 / 验证码 / Cookie），登录后即可一键完成签到与米游币任务。";
            return;
        }

        TxtUid.Text = string.IsNullOrEmpty(acc.Uid) ? "" : $"UID {acc.Uid}";
        // stoken 是长期凭证，存在即视为已登录；cookie_token 缺失只影响部分接口，不再整体判“登录态不完整”
        if (!SignService.HasStoken(acc.Cookie))
        {
            TxtAccountStatus.Foreground = BrushOf("Warning");
            TxtAccountStatus.Text = "● 登录态不完整";
            BannerStoken.Visibility = Visibility.Visible;
            TxtAccountHint.Text = "⚠ 当前登录态缺少 stoken（签到与米游币任务必需）。请到「账号」页用「扫码登录 / Cookie 登录」重新登录。";
        }
        else
        {
            TxtAccountStatus.Foreground = BrushOf("Success");
            TxtAccountStatus.Text = "● 已登录";
            BannerStoken.Visibility = Visibility.Collapsed;
        }
    }

    private void CmbAccount_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAccountChange) return;
        if (CmbAccount.SelectedItem is Account acc && !ReferenceEquals(acc, AccountService.Active))
        {
            AccountService.SetActive(acc);
            _missions = null;
            _roleRows.Clear();
            MissionPanel.Children.Clear();
            SignPanel.Children.Clear();
            RefreshAccountBar();
            _ = RefreshAllSafeAsync();
        }
    }

    // ==================== 自适应布局 ====================

    private void TaskPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 窄窗口（默认 1290×700 且 150% 缩放）下改为上下堆叠，避免右栏与顶栏被裁切
        bool narrow = ActualWidth > 0 && ActualWidth < 1120;
        if (TxtSubTitle.Visibility == (narrow ? Visibility.Collapsed : Visibility.Visible)) return;

        TxtSubTitle.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        if (narrow)
        {
            BodyCol1.Width = new GridLength(1, GridUnitType.Star);
            BodyColGap.Width = new GridLength(0);
            BodyCol2.Width = new GridLength(0);
            BodyRow1.Height = new GridLength(1.45, GridUnitType.Star);
            BodyRow2.Height = new GridLength(1, GridUnitType.Star);
            Grid.SetRow(LogCard, 1);
            Grid.SetColumn(LogCard, 0);
            LogCard.Margin = new Thickness(0, 14, 0, 0);
            LogCard.MinWidth = 0;
        }
        else
        {
            BodyColGap.Width = new GridLength(14);
            BodyCol2.Width = new GridLength(1.02, GridUnitType.Star);
            BodyRow1.Height = new GridLength(1, GridUnitType.Star);
            BodyRow2.Height = new GridLength(0);
            Grid.SetRow(LogCard, 0);
            Grid.SetColumn(LogCard, 2);
            LogCard.Margin = new Thickness(0);
            LogCard.MinWidth = 320;
        }
    }

    // ==================== 刷新 ====================

    private async Task RefreshAllSafeAsync()
    {
        try
        {
            await RefreshMissionsAsync();
            await RefreshSignAsync();
        }
        catch (Exception ex)
        {
            Log($"刷新异常：{ex.Message}", "ERROR");
        }
    }

    private async Task RefreshMissionsAsync()
    {
        var acc = AccountService.Active;
        if (acc == null)
        {
            MissionPanel.Children.Clear();
            MissionPanel.Children.Add(TipText("请先在「账号」页登录账号"));
            TxtMissionTitle.Text = "米游币任务（0/0）";
            TxtCoin.Text = "持有米游币：0";
            return;
        }

        Log("[米游币任务]刷新任务状态");
        var version = ++_missionVersion;
        var (snap, error, loginExpired) = await SignService.GetMissionsAsync(acc.Cookie);
        if (version != _missionVersion || !ReferenceEquals(acc, AccountService.Active)) return;
        if (snap == null)
        {
            MissionPanel.Children.Clear();
            MissionPanel.Children.Add(TipText(error ?? "刷新失败", loginExpired ? "Warning" : "Danger"));
            Log($"[米游币任务]{(loginExpired ? "登录状态已失效" : error)}", "ERROR");
            return;
        }

        _missions = snap;
        TxtMissionTitle.Text = $"米游币任务（{snap.TodayPoints}/{snap.TotalPoints}）";
        TxtCoin.Text = $"持有米游币：{snap.UserPoints:N0}";
        MissionPanel.Children.Clear();
        if (snap.Items.Count == 0)
        {
            MissionPanel.Children.Add(TipText("未获取到任务列表"));
            return;
        }
        foreach (var item in snap.Items.OrderBy(i => i.Id))
            MissionPanel.Children.Add(BuildMissionRow(item));
        Log($"[米游币任务]任务数据合并完成（{snap.Items.Count} 项，今日 {snap.TodayPoints}/{snap.TotalPoints}）");
    }

    private async Task RefreshSignAsync()
    {
        _roleRows.Clear();
        SignPanel.Children.Clear();
        TxtSignCount.Text = "";

        var acc = AccountService.Active;
        if (acc == null)
        {
            SignPanel.Children.Add(TipText("请先在「账号」页登录账号"));
            return;
        }

        Log("[签到任务]刷新游戏账户");
        var version = ++_signVersion;
        var result = await SignService.GetAllRolesAsync(acc.Cookie);
        if (version != _signVersion || !ReferenceEquals(acc, AccountService.Active)) return;
        if (result.Error != null)
        {
            SignPanel.Children.Add(TipText(result.Error, result.LoginExpired ? "Warning" : "Danger"));
            Log($"[签到任务]{result.Error}", "ERROR");
            return;
        }
        if (result.Roles.Count == 0)
        {
            SignPanel.Children.Add(TipText("该账号未绑定任何游戏角色"));
            Log("[签到任务]未找到已绑定的游戏角色");
            return;
        }

        // 去重（同一 game_biz + uid 只保留一条），避免并发刷新造成重复行
        var roles = result.Roles
            .GroupBy(r => $"{r.GameBiz}|{r.Uid}")
            .Select(g => g.First())
            .ToList();

        TxtSignCount.Text = $"共 {roles.Count} 个角色";
        Log($"[签到任务]获取游戏账户成功，共 {roles.Count} 个");

        foreach (var role in roles)
        {
            var game = SignService.GetByBiz(role.GameBiz);
            if (game == null) continue;
            SignPanel.Children.Add(BuildRoleRow(game, role));
        }
        int known = 0;
        foreach (var row in _roleRows.ToList())
        {
            if (version != _signVersion) return;
            var state = await SignService.GetSignStateAsync(acc.Cookie, row.Game, row.Role);
            // 接口限频（-500004 / 请求过于频繁 / 操作频繁）：退避后重试一次
            if (state.Signed == null && !state.LoginExpired &&
                (state.Message.Contains("-500004") || state.Message.Contains("频繁")))
            {
                await Task.Delay(1200);
                if (version != _signVersion) return;
                state = await SignService.GetSignStateAsync(acc.Cookie, row.Game, row.Role);
            }
            if (state.LoginExpired)
            {
                SetRoleStatus(row, null, "登录失效");
                continue;
            }
            if (state.Signed == null)
            {
                SetRoleStatus(row, null, "查询失败");
                Log($"[签到任务]刷新{row.Game.Name}-{SignService.RegionLabel(row.Role)}-{row.Role.Uid}：{state.Message}", "WARN");
                await Task.Delay(400);
                continue;
            }
            known++;
            SetRoleStatus(row, state.Signed, state.Signed == true ? "已签到" : "未签到");
            await Task.Delay(280);
        }
        Log($"[签到任务]签到状态查询完成（{known}/{_roleRows.Count}）");
    }

    // ==================== 执行 ====================

    /// <summary>米游币任务：打卡 + 浏览/点赞/分享（仅当前账号，带界面刷新）</summary>
    private async Task RunMissionsForCurrentAsync()
    {
        var acc = AccountService.Active;
        if (acc == null)
        {
            Log("未添加账号，请先到「账号」页登录", "ERROR");
            return;
        }

        LogSeparator("米游币任务");
        Log("[米游币任务]开始执行任务");

        if (_missions == null) await RefreshMissionsAsync();

        var signMission = _missions?.Items.FirstOrDefault(i => i.Key == "continuous_sign");
        if (signMission == null)
        {
            Log("[米游币任务]未找到打卡任务，跳过打卡", "WARN");
        }
        else if (signMission.Done)
        {
            Log("[米游币任务]今日已打卡");
        }
        else
        {
            var res = await SignService.BbsCheckInAsync(acc.Cookie);
            foreach (var line in res.Log) Log($"[米游币任务]{line.Text}", line.Level);
            if (!res.Ok && res.Retcode == 1034)
            {
                Log("[米游币任务]打卡触发人机验证，正在打开官方验证页…", "WARN");
                var label = string.IsNullOrEmpty(acc.Uid) ? acc.DisplayName : $"UID {acc.Uid}";
                if (AskVerifyBbs(acc.Cookie, label))
                {
                    Log("[米游币任务]验证已完成，正在重试打卡…");
                    await Task.Delay(400);
                    var retry = await SignService.BbsCheckInAsync(acc.Cookie);
                    foreach (var line in retry.Log) Log($"[米游币任务]{line.Text}", line.Level);
                }
                else
                {
                    Log("[米游币任务]已取消人机验证，继续执行其他任务", "WARN");
                }
            }
        }

        // 浏览 / 点赞 / 分享类任务（任务未完成时才执行）
        var needsBrowse = _missions?.Items.Any(i =>
            !i.Done && (i.Key.Contains("view", StringComparison.OrdinalIgnoreCase) ||
                        i.Key.Contains("like", StringComparison.OrdinalIgnoreCase) ||
                        i.Key.Contains("share", StringComparison.OrdinalIgnoreCase))) == true;
        if (needsBrowse)
        {
            Log("[米游币任务]执行浏览/点赞/分享任务");
            foreach (var line in await SignService.DoCommunityActionsAsync(acc.Cookie))
                Log($"[米游币任务]{line.Text}", line.Level);
        }

        Log("[米游币任务]任务执行完毕，即将刷新任务状态");
        await Task.Delay(2000);
        _missions = null;
        await RefreshMissionsAsync();
        LogSeparator("米游币任务", false);
    }

    /// <summary>签到：当前账号界面上已勾选的角色</summary>
    private async Task RunSignForCurrentAsync(bool onlySelected = true)
    {
        var acc = AccountService.Active;
        if (acc == null)
        {
            Log("未添加账号，请先到「账号」页登录", "ERROR");
            return;
        }
        if (_roleRows.Count == 0)
        {
            Log("[签到任务]未检测到游戏账户，正在刷新");
            await RefreshSignAsync();
        }

        var targets = _roleRows.Where(r => !onlySelected || r.Box.IsChecked == true).ToList();
        if (targets.Count == 0)
        {
            Log("[签到任务]未选择任何账户", "WARN");
            return;
        }

        LogSeparator("签到任务");
        Log("[签到任务]执行签到");
        foreach (var row in targets)
        {
            if (row.Signed)
            {
                Log($"[签到任务]{row.Game.Name}-{SignService.RegionLabel(row.Role)}-{row.Role.Uid}已签到");
                continue;
            }
            var res = await SignService.SignRoleAsync(acc.Cookie, row.Game, row.Role);
            foreach (var line in res.Log) Log($"[签到任务]{line.Text}", line.Level);
            if (res.Ok)
            {
                row.Signed = true;
                row.StateKnown = true;
                SetRoleStatus(row, true, "已签到");
            }
            else if (res.Retcode == 1034 || res.Message.Contains("风险"))
            {
                // 触发风控：把官方验证页嵌进软件，让玩家自己过验证码，再自动重试
                SetRoleStatus(row, null, "需要人机验证");
                Log($"[签到任务]{row.Game.Name}-{SignService.RegionLabel(row.Role)}-{row.Role.Uid} 触发人机验证，正在打开官方验证页…", "WARN");

                var label = string.IsNullOrEmpty(acc.Uid) ? acc.DisplayName : $"UID {acc.Uid}";
                var passed = AskVerify(row.Game, acc.Cookie, label);
                if (passed)
                {
                    Log("[签到任务]验证已完成，正在重试签到…");
                    await Task.Delay(400);
                    var retry = await SignService.SignRoleAsync(acc.Cookie, row.Game, row.Role);
                    foreach (var line in retry.Log) Log($"[签到任务]{line.Text}", line.Level);
                    if (retry.Ok)
                    {
                        row.Signed = true;
                        row.StateKnown = true;
                        SetRoleStatus(row, true, "已签到（验证后）");
                    }
                    else
                    {
                        SetRoleStatus(row, null, "验证后仍未成功");
                    }
                }
                else
                {
                    SetRoleStatus(row, null, "已取消验证");
                    Log("[签到任务]已取消人机验证，剩余角色继续执行", "WARN");
                }
            }
            await Task.Delay(600);
        }
        Log("[签到任务]签到任务执行完毕");
        LogSeparator("签到任务", false);
    }

    /// <summary>打开内嵌官方页面的安全验证窗口，返回玩家是否点「我已完成验证」。</summary>
    private bool AskVerify(SignService.GameCheckin game, string cookie, string label)
    {
        var win = new SignVerifyWindow(game, cookie, label);
        var owner = Window.GetWindow(this);
        if (owner != null && !ReferenceEquals(owner, win)) win.Owner = owner;
        return win.ShowDialog() == true;
    }

    /// <summary>米游社社区打卡的验证窗口（官方打卡页）。</summary>
    private bool AskVerifyBbs(string cookie, string label)
    {
        var win = new SignVerifyWindow(SignVerifyWindow.BuildBbsUrl(), cookie, label,
            "本次社区打卡需要人机验证：请在下方官方页面完成验证并点击「打卡」，完成后回到这里点「我已完成验证」。");
        var owner = Window.GetWindow(this);
        if (owner != null) win.Owner = owner;
        return win.ShowDialog() == true;
    }

    // ==================== 一键执行 ====================

    private async Task RunCurrentAccountAsync()
    {
        LogSeparator("全量执行");
        var acc = AccountService.Active;
        Log($"[INFO]账号 {acc?.DisplayName ?? "未登录"} 执行开始");
        await RunMissionsForCurrentAsync();
        await RunSignForCurrentAsync(onlySelected: false);
        LogSeparator("全量执行", false);
    }

    private async Task RunAllAccountsAsync()
    {
        var accounts = AccountService.All.Where(a => !string.IsNullOrEmpty(a.Cookie)).ToList();
        if (accounts.Count == 0)
        {
            Log("没有任何账号，请先到「账号」页登录", "ERROR");
            return;
        }
        LogSeparator("全量执行");
        foreach (var acc in accounts)
        {
            Log($"[INFO]账号 UID:{acc.Uid}（{acc.Nickname}）执行开始");
            if (!SignService.HasStoken(acc.Cookie))
            {
                Log($"账号 {acc.DisplayName} 登录态不完整（缺少 stoken），请在「账号」页重新登录", "ERROR");
                continue;
            }
            if (ReferenceEquals(acc, AccountService.Active))
            {
                await RunMissionsForCurrentAsync();
                await RunSignForCurrentAsync(onlySelected: false);
            }
            else
            {
                await RunMissionsForCookieAsync(acc.Cookie);
                await SignAllRolesForCookieAsync(acc.Cookie);
            }
        }
        LogSeparator("全量执行", false);
        _missions = null;
        await RefreshAllSafeAsync();
    }

    /// <summary>无界面账号：米游币任务</summary>
    private async Task RunMissionsForCookieAsync(string cookie)
    {
        Log("[米游币任务]开始执行任务");
        var (snap, error, _) = await SignService.GetMissionsAsync(cookie);
        if (snap == null)
        {
            Log($"[米游币任务]{error}", "ERROR");
            return;
        }
        var signMission = snap.Items.FirstOrDefault(i => i.Key == "continuous_sign");
        if (signMission?.Done == true)
            Log("[米游币任务]今日已打卡");
        else
        {
            var res = await SignService.BbsCheckInAsync(cookie);
            foreach (var line in res.Log) Log($"[米游币任务]{line.Text}", line.Level);
        }
        if (snap.Items.Any(i => !i.Done && (i.Key.Contains("view") || i.Key.Contains("like") || i.Key.Contains("share"))))
        {
            foreach (var line in await SignService.DoCommunityActionsAsync(cookie))
                Log($"[米游币任务]{line.Text}", line.Level);
        }
    }

    /// <summary>无界面账号：全部角色签到</summary>
    private async Task SignAllRolesForCookieAsync(string cookie)
    {
        Log("[签到任务]执行签到");
        var roles = await SignService.GetAllRolesAsync(cookie);
        if (roles.Error != null)
        {
            Log($"[签到任务]{roles.Error}", "ERROR");
            return;
        }
        foreach (var role in roles.Roles)
        {
            var game = SignService.GetByBiz(role.GameBiz);
            if (game == null) continue;
            var state = await SignService.GetSignStateAsync(cookie, game, role);
            if (state.LoginExpired)
            {
                Log($"[签到任务]登录状态已失效，停止该账号", "ERROR");
                return;
            }
            if (state.Signed == true)
            {
                Log($"[签到任务]{game.Name}-{SignService.RegionLabel(role)}-{role.Uid}已签到");
                continue;
            }
            var res = await SignService.SignRoleAsync(cookie, game, role);
            foreach (var line in res.Log) Log($"[签到任务]{line.Text}", line.Level);
            await Task.Delay(600);
        }
    }

    // ==================== 界面行 ====================

    private FrameworkElement BuildMissionRow(SignService.MissionItem m)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 左侧：状态图标 + 名称
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock
        {
            Text = m.Done ? "✓" : "○",
            FontSize = 13,
            Width = 20,
            Foreground = m.Done ? BrushOf("Success") : BrushOf("TextSecondary"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        left.Children.Add(new TextBlock
        {
            Text = m.Name,
            FontSize = 12.5,
            Foreground = BrushOf("TextPrimary"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (m.Reward > 0)
        {
            left.Children.Add(new TextBlock
            {
                Text = $" - {m.Reward}米游币",
                FontSize = 11.5,
                Foreground = BrushOf("TextSecondary"),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        if (m.HasCycle)
        {
            left.Children.Add(new TextBlock
            {
                Text = $" - Day{m.CycleTimes}",
                FontSize = 11.5,
                Foreground = BrushOf("TextSecondary"),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        // 右侧：进度条 + 计数
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(BuildProgress(m.Process, m.Total));
        right.Children.Add(new TextBlock
        {
            Text = $"{m.Process}/{m.Total}",
            FontSize = 11.5,
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = BrushOf("TextSecondary"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        var border = new Border
        {
            Child = grid,
            Padding = new Thickness(2, 6, 2, 6),
            BorderBrush = BrushOf("CardBorder"),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        return border;
    }

    private FrameworkElement BuildProgress(int process, int total)
    {
        const double width = 76;
        var pct = total <= 0 ? 0 : Math.Clamp((double)process / total, 0, 1);
        var accent = BrushOf("Accent");
        var accentColor = accent is SolidColorBrush sb ? sb.Color : Color.FromRgb(0x3B, 0x82, 0xF6);
        var track = new Border
        {
            Width = width,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromArgb(0x26, accentColor.R, accentColor.G, accentColor.B)),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var fill = new Border
        {
            Width = Math.Max(0, width * pct),
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = accent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var host = new Grid { Width = width, Height = 6, VerticalAlignment = VerticalAlignment.Center };
        host.Children.Add(track);
        host.Children.Add(fill);
        return host;
    }

    private static TextBlock FallbackGlyph(string name, Color color) => new()
    {
        Text = string.IsNullOrEmpty(name) ? "?" : name[..1],
        FontSize = 14,
        FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush(color),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private FrameworkElement BuildRoleRow(SignService.GameCheckin game, SignRole role)
    {
        var color = GameColors.TryGetValue(game.Key, out var c) ? c : Color.FromRgb(0x3B, 0x82, 0xF6);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 勾选框
        var box = new System.Windows.Controls.CheckBox
        {
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        Grid.SetColumn(box, 0);
        grid.Children.Add(box);

        // 游戏图标（官方头像小图，圆角裁剪；缺图回退色块首字）
        var icon = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0x22, color.R, color.G, color.B)),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true,
        };
        if (GameIcons.TryGetValue(game.Key, out var iconFile))
        {
            var bmp = LoadGameIcon(iconFile);
            if (bmp != null)
            {
                icon.Child = new System.Windows.Controls.Image
                {
                    Source = bmp,
                    Stretch = Stretch.UniformToFill,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };
            }
            else
            {
                icon.Child = FallbackGlyph(game.Name, color);
            }
        }
        else
        {
            icon.Child = FallbackGlyph(game.Name, color);
        }
        Grid.SetColumn(icon, 1);
        grid.Children.Add(icon);

        // UID + 服务器
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var line1 = new StackPanel { Orientation = Orientation.Horizontal };
        line1.Children.Add(new TextBlock
        {
            Text = $"{game.Name}  {role.Uid}",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushOf("TextPrimary"),
        });
        if (!string.IsNullOrEmpty(role.Nickname))
        {
            line1.Children.Add(new TextBlock
            {
                Text = $"  {role.Nickname}",
                FontSize = 11.5,
                Foreground = BrushOf("TextSecondary"),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        texts.Children.Add(line1);
        texts.Children.Add(new TextBlock
        {
            Text = SignService.RegionLabel(role),
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = BrushOf("TextSecondary"),
        });
        Grid.SetColumn(texts, 2);
        grid.Children.Add(texts);

        // 状态
        var status = new TextBlock
        {
            Text = "查询中…",
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushOf("TextSecondary"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 2, 0),
        };
        Grid.SetColumn(status, 3);
        grid.Children.Add(status);

        var border = new Border
        {
            Child = grid,
            Padding = new Thickness(6, 8, 6, 8),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            BorderBrush = BrushOf("CardBorder"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Cursor = Cursors.Hand,
        };
        border.MouseEnter += (_, _) => border.Background = new SolidColorBrush(Color.FromArgb(0x14, color.R, color.G, color.B));
        border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
        border.MouseLeftButtonUp += (_, e) =>
        {
            if (IsInsideCheckBox(e.OriginalSource as DependencyObject)) return;
            box.IsChecked = box.IsChecked != true;
        };

        var row = new RoleRow
        {
            Game = game,
            Role = role,
            Box = box,
            StatusText = status,
            RowBorder = border,
        };
        box.Checked += (_, _) => { };
        _roleRows.Add(row);
        return border;
    }

    private static bool IsInsideCheckBox(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is System.Windows.Controls.CheckBox) return true;
            source = source is Visual || source is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }

    private void SetRoleStatus(RoleRow row, bool? signed, string text)
    {
        row.StateKnown = signed != null;
        row.Signed = signed == true;
        row.StatusText.Text = text;
        row.StatusText.Foreground = signed switch
        {
            true => BrushOf("Success"),
            false => BrushOf("TextSecondary"),
            _ => BrushOf("Warning"),
        };
    }

    private TextBlock TipText(string text, string colorKey = "TextSecondary") => new()
    {
        Text = text,
        FontSize = 12,
        Margin = new Thickness(2, 8, 2, 4),
        TextWrapping = TextWrapping.Wrap,
        Foreground = BrushOf(colorKey),
    };

    private Brush BrushOf(string key)
        => TryFindResource(key) as Brush ?? Brushes.Gray;

    // ==================== 日志 ====================

    private void AddLog(SignLogEntry entry)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AddLog(entry));
            return;
        }
        _logs.Add(entry);
        while (_logs.Count > 800) _logs.RemoveAt(0);
        TxtLogCount.Text = $"{_logs.Count} 条";
        if (LogList.Items.Count > 0)
            LogList.ScrollIntoView(LogList.Items[^1]);
    }

    private void Log(string text, string level = "INFO")
        => AddLog(new SignLogEntry { Text = text, Level = level });

    private void LogSeparator(string title, bool start = true)
        => Log(start
            ? $"// --------------------{title} START-------------------- //"
            : $"// --------------------{title} END---------------------- //");

    // ==================== 按钮 ====================

    private async void BtnRefreshAll_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true);
            LogSeparator("刷新");
            Log("[INFO]刷新任务与签到状态");
            await RestartRefreshAsync();
            LogSeparator("刷新", false);
        }
        catch (Exception ex)
        {
            Log($"刷新异常：{ex.Message}", "ERROR");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RestartRefreshAsync()
    {
        _missions = null;
        await RefreshMissionsAsync();
        await RefreshSignAsync();
    }

    private async void BtnRefreshMission_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true);
            await RefreshMissionsAsync();
        }
        catch (Exception ex)
        {
            Log($"刷新米游币任务异常：{ex.Message}", "ERROR");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void BtnRefreshSign_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true);
            await RefreshSignAsync();
        }
        catch (Exception ex)
        {
            Log($"刷新签到任务异常：{ex.Message}", "ERROR");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void BtnRunMission_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true);
            await RunMissionsForCurrentAsync();
        }
        catch (Exception ex)
        {
            Log($"米游币任务异常：{ex.Message}", "ERROR");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void BtnRunSign_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true);
            await RunSignForCurrentAsync();
        }
        catch (Exception ex)
        {
            Log($"签到任务异常：{ex.Message}", "ERROR");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void BtnRunCurrent_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true);
            await RunCurrentAccountAsync();
        }
        catch (Exception ex)
        {
            Log($"一键执行异常：{ex.Message}", "ERROR");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void BtnRunAll_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true);
            await RunAllAccountsAsync();
        }
        catch (Exception ex)
        {
            Log($"一键执行全部账号异常：{ex.Message}", "ERROR");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void BtnCheckInTest_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var acc = AccountService.Active;
            if (acc == null)
            {
                Log("未添加账号，请先到「账号」页登录", "ERROR");
                return;
            }
            SetBusy(true);
            LogSeparator("打卡测试");
            var res = await SignService.BbsCheckInAsync(acc.Cookie);
            foreach (var line in res.Log) Log($"[打卡测试]{line.Text}", line.Level);
            LogSeparator("打卡测试", false);
        }
        catch (Exception ex)
        {
            Log($"打卡测试异常：{ex.Message}", "ERROR");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e) => _logs.Clear();

    private void SetBusy(bool busy)
    {
        if (_busy && busy) return;
        _busy = busy;
        BtnRefreshAll.IsEnabled = !busy;
        BtnCheckInTest.IsEnabled = !busy;
        BtnRunCurrent.IsEnabled = !busy;
        BtnRunAll.IsEnabled = !busy;
        BtnRefreshMission.IsEnabled = !busy;
        BtnRunMission.IsEnabled = !busy;
        BtnRefreshSign.IsEnabled = !busy;
        BtnRunSign.IsEnabled = !busy;
        CmbAccount.IsEnabled = !busy;
    }
}


