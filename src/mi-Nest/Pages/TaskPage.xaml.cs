using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MiNest.Models;
using MiNest.Services;

namespace MiNest.Pages;

/// <summary>任务/签到（仿 TeyvatGuide 实用脚本）：米游社 App 各板块签到福利 + 米游币每日任务 + 全账号一键</summary>
public partial class TaskPage : UserControl
{
    private readonly MainWindow _main;
    private readonly ObservableCollection<SignLogEntry> _logs = new();
    private bool _subscribed;

    public TaskPage(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        LogList.ItemsSource = _logs;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            _subscribed = true;
            AccountService.Changed += () => Dispatcher.Invoke(RefreshAccountBar);
        }
        RefreshAccountBar();
        _ = BuildGameRowsAsync();
    }

    private void RefreshAccountBar()
    {
        CmbAccount.ItemsSource = null;
        CmbAccount.ItemsSource = AccountService.All;
        CmbAccount.SelectedItem = AccountService.Active;
        var acc = AccountService.Active;
        if (acc == null)
        {
            TxtAccountStatus.Foreground = (Brush)FindResource("TextSecondary");
            TxtAccountStatus.Text = "● 未添加账号";
            BannerStoken.Visibility = Visibility.Visible;
            TxtAccountHint.Text = "请到「账号」页添加并登录账号（支持扫码 / 验证码 / Cookie），添加后即可一键完成签到与米游币任务。";
        }
        else if (!acc.Cookie.Contains("stoken", StringComparison.OrdinalIgnoreCase))
        {
            TxtAccountStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB2, 0x24));
            TxtAccountStatus.Text = "● 登录态缺少 stoken";
            BannerStoken.Visibility = Visibility.Visible;
            TxtAccountHint.Text = "⚠ 当前账号登录态缺少 stoken（签到 / 米游币任务必需）。请到「账号」页用「扫码登录 / Cookie 登录」重新登录。";
        }
        else
        {
            TxtAccountStatus.Foreground = (Brush)FindResource("Success");
            TxtAccountStatus.Text = "● 已登录";
            BannerStoken.Visibility = Visibility.Collapsed;
        }
    }

    private void CmbAccount_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbAccount.SelectedItem is Account acc && !ReferenceEquals(acc, AccountService.Active))
        {
            AccountService.SetActive(acc);
            _ = BuildGameRowsAsync();
        }
    }

    private void AddLog(SignLogEntry entry)
    {
        _logs.Add(entry);
        if (_logs.Count > 500) _logs.RemoveAt(0);
        TxtLogCount.Text = $"{_logs.Count} 条";
        if (LogList.Items.Count > 0)
            LogList.ScrollIntoView(LogList.Items[^1]);
    }

    /// <summary>构建游戏区的信息提示条（无账号 / 缺 stoken 等场景）</summary>
    private Border BuildInfoCard(string message, string colorKey)
    {
        var color = (Brush)FindResource(colorKey);
        var border = new Border
        {
            Background = (Brush)FindResource("CardBg"),
            BorderBrush = (Brush)FindResource("CardBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 4, 0, 0),
            MaxWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        border.Child = new TextBlock
        {
            Text = message,
            FontSize = 12.5,
            Foreground = color,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
        };
        return border;
    }

    /// <summary>各游戏的主题标识色</summary>
    private static readonly Dictionary<string, Color> GameColors = new()
    {
        ["genshin"] = Color.FromRgb(0x47, 0xC4, 0xF7),   // 原神 天蓝
        ["starrail"] = Color.FromRgb(0x8A, 0x6D, 0xF0),  // 星穹铁道 紫
        ["zzz"] = Color.FromRgb(0xFF, 0x5C, 0x4D),        // 绝区零 橙红
        ["bh3"] = Color.FromRgb(0x4A, 0x8E, 0xF0),        // 崩坏3 蓝
        ["bh2"] = Color.FromRgb(0xFF, 0x9E, 0x3D),        // 崩坏2 橙
        ["nxx"] = Color.FromRgb(0x5A, 0xC8, 0xFA),        // 未定事件簿 浅蓝
    };

    /// <summary>构建一行角色状态：左侧圆点 + 昵称 + UID，右侧状态徽章（浅色圆角底）</summary>
    private Grid BuildRoleRow(string nickname, string uid, string statusText, Brush statusColor)
    {
        var row = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = statusColor,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        left.Children.Add(new TextBlock
        {
            Text = nickname,
            FontSize = 12.5,
            Foreground = (Brush)FindResource("TextPrimary"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 130,
        });
        left.Children.Add(new TextBlock
        {
            Text = $"UID {uid}",
            FontSize = 10.5,
            Foreground = (Brush)FindResource("TextSecondary"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        });
        Grid.SetColumn(left, 0);
        row.Children.Add(left);

        // 状态徽章：浅色圆角底 + 状态文字
        var badgeColor = statusColor is SolidColorBrush badgeBrush ? badgeBrush.Color : System.Windows.Media.Colors.MediumPurple;
        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x1A, badgeColor.R, badgeColor.G, badgeColor.B)),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        badge.Child = new TextBlock
        {
            Text = statusText,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = statusColor,
        };
        Grid.SetColumn(badge, 1);
        row.Children.Add(badge);

        return row;
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        _logs.Clear();
    }

    // ---------- 游戏行 ----------

    private async Task BuildGameRowsAsync()
    {
        GameRowsPanel.Children.Clear();
        var acc = AccountService.Active;
        if (acc == null)
        {
            GameRowsPanel.Children.Add(BuildInfoCard("请先在「账号」页添加并登录一个米哈游账号，登录后此处会展示可签到的游戏与角色。", "TextSecondary"));
            return;
        }
        if (!acc.Cookie.Contains("stoken", StringComparison.OrdinalIgnoreCase))
        {
            GameRowsPanel.Children.Add(BuildInfoCard("当前账号登录态缺少 stoken，签到接口不可用。请到「账号」页用「扫码登录 / Cookie 登录」重新登录后，此处会自动恢复。", "Warning"));
            return;
        }

        foreach (var game in SignService.Games)
        {
            var gameColor = new SolidColorBrush(GameColors.TryGetValue(game.Key, out var c) ? c : Color.FromRgb(0x3B, 0x82, 0xF6));

            var card = new Border
            {
                Background = (Brush)FindResource("CardBg"),
                BorderBrush = (Brush)FindResource("CardBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(0, 0, 12, 12),
                MinWidth = 270,
                MaxWidth = 330,
            };
            // 悬浮高亮边框
            card.MouseEnter += (_, _) => card.BorderBrush = new SolidColorBrush(Color.FromArgb(0xB0, gameColor.Color.R, gameColor.Color.G, gameColor.Color.B));
            card.MouseLeave += (_, _) => card.BorderBrush = (Brush)FindResource("CardBorder");

            var sp = new StackPanel { Margin = new Thickness(14, 12, 14, 14) };

            // 顶部游戏标识色条
            sp.Children.Add(new Border
            {
                Height = 3,
                Background = gameColor,
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 0, 0, 10),
            });

            // 头部：游戏标识色点 + 名称 + 签到按钮
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleSp.Children.Add(new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = gameColor,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 9, 0),
            });
            titleSp.Children.Add(new TextBlock
            {
                Text = game.Name,
                FontSize = 14.5,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("TextPrimary"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(titleSp, 0);
            head.Children.Add(titleSp);

            var signBtn = new Button
            {
                Content = "签到",
                Tag = game.Key,
                Padding = new Thickness(13, 4, 13, 4),
                FontSize = 11.5,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)FindResource("AccentButton"),
            };
            signBtn.Click += SignOneGame_Click;
            Grid.SetColumn(signBtn, 2);
            head.Children.Add(signBtn);
            sp.Children.Add(head);

            card.Child = sp;
            GameRowsPanel.Children.Add(card);

            var (roles, err) = await SignService.GetRolesAsync(acc.Cookie, game.GameBiz);
            if (err != null)
            {
                sp.Children.Add(new TextBlock
                {
                    Text = err,
                    FontSize = 12,
                    Foreground = (Brush)FindResource("Warning"),
                    Margin = new Thickness(0, 8, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                });
                continue;
            }
            if (roles.Count == 0)
            {
                sp.Children.Add(new TextBlock
                {
                    Text = "未找到已绑定的角色",
                    FontSize = 12,
                    Foreground = (Brush)FindResource("TextSecondary"),
                    Margin = new Thickness(0, 8, 0, 0),
                });
                continue;
            }

            foreach (var role in roles)
            {
                var line = BuildRoleRow(role.Nickname, role.Uid, "查询中…", (Brush)FindResource("TextSecondary"));
                sp.Children.Add(line);
                try
                {
                    var status = await SignService.GetSignStatusAsync(acc.Cookie, game, role.Uid, role.Region);
                    if (status == null)
                    {
                        sp.Children.Remove(line);
                        sp.Children.Add(BuildRoleRow(role.Nickname, role.Uid, "登录失效", (Brush)FindResource("Danger")));
                    }
                    else
                    {
                        sp.Children.Remove(line);
                        sp.Children.Add(status == true
                            ? BuildRoleRow(role.Nickname, role.Uid, "今日已签到", (Brush)FindResource("Success"))
                            : BuildRoleRow(role.Nickname, role.Uid, "今日未签到", (Brush)FindResource("Warning")));
                    }
                }
                catch
                {
                    sp.Children.Remove(line);
                    sp.Children.Add(BuildRoleRow(role.Nickname, role.Uid, "查询失败", (Brush)FindResource("Danger")));
                }
            }
        }
    }

    private async void SignOneGame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string key) return;
        var acc = AccountService.Active;
        if (acc == null)
        {
            AddLog(new SignLogEntry { Text = "未添加账号", IsError = true });
            return;
        }
        btn.IsEnabled = false;
        try
        {
            var (_, _, log) = await SignService.SignGameAsync(acc.Cookie, SignService.Get(key));
            foreach (var line in log) AddLog(line);
        }
        finally
        {
            btn.IsEnabled = true;
            _ = BuildGameRowsAsync();
        }
    }

    private async void BtnSignGames_Click(object sender, RoutedEventArgs e)
    {
        var acc = AccountService.Active;
        if (acc == null)
        {
            AddLog(new SignLogEntry { Text = "未添加账号，请先到「账号」页登录", IsError = true });
            return;
        }
        if (!acc.Cookie.Contains("stoken", StringComparison.OrdinalIgnoreCase))
        {
            AddLog(new SignLogEntry { Text = "登录态缺少 stoken，无法签到。请到「账号」页重新扫码 / Cookie 登录", IsError = true });
            return;
        }
        SetBusy(true);
        try
        {
            foreach (var game in SignService.Games)
            {
                var (_, _, log) = await SignService.SignGameAsync(acc.Cookie, game);
                foreach (var line in log) AddLog(line);
            }
        }
        finally
        {
            SetBusy(false);
            _ = BuildGameRowsAsync();
        }
    }

    private async void BtnBbs_Click(object sender, RoutedEventArgs e)
    {
        var acc = AccountService.Active;
        if (acc == null)
        {
            AddLog(new SignLogEntry { Text = "未添加账号，请先到「账号」页登录", IsError = true });
            return;
        }
        SetBusy(true);
        try
        {
            var (_, _, log) = await SignService.MiyousheDailyAsync(acc.Cookie);
            foreach (var line in log) AddLog(line);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void BtnAllAccounts_Click(object sender, RoutedEventArgs e)
    {
        var accounts = AccountService.All.Where(a => !string.IsNullOrEmpty(a.Cookie)).ToList();
        if (accounts.Count == 0)
        {
            AddLog(new SignLogEntry { Text = "没有任何账号，请先到「账号」页登录", IsError = true });
            return;
        }
        if (accounts.Any(a => !a.Cookie.Contains("stoken", StringComparison.OrdinalIgnoreCase)))
        {
            AddLog(new SignLogEntry { Text = "存在登录态缺少 stoken 的账号，无法完成签到。请先到「账号」页重新登录这些账号", IsError = true });
            return;
        }
        SetBusy(true);
        try
        {
            foreach (var acc in accounts)
            {
                AddLog(new SignLogEntry { Text = $"===== 账号：{acc.DisplayName} =====" });
                var log = await SignService.SignAllForAccountAsync(acc.Cookie);
                foreach (var line in log) AddLog(line);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void BtnRefreshStatus_Click(object sender, RoutedEventArgs e)
        => _ = BuildGameRowsAsync();

    private void SetBusy(bool busy)
    {
        BtnSignGames.IsEnabled = !busy;
        BtnBbs.IsEnabled = !busy;
        BtnAllAccounts.IsEnabled = !busy;
        BtnRefreshStatus.IsEnabled = !busy;
    }
}
