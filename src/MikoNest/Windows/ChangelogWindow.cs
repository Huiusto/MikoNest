using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MiNest.Windows;

/// <summary>设置页「更新日志」窗口：按版本倒序展示从首个公开版到当前版本的变化。</summary>
public sealed class ChangelogWindow : Window
{
    private sealed record Entry(string Version, string Date, string[] Items);

    private static readonly Entry[] Entries =
    {
        new("0.1.40", "2026-11-25", new[]
        {
            "「账号」页管理账号列表新增账号头像，无头像时显示昵称首字占位。",
            "更新日志内容精简为简要说明。",
        }),
        new("0.1.39", "2026-11-25", new[]
        {
            "更新弹窗开始更新后不再自动最小化。",
            "关闭米哈云游后，启动页「全部游戏 / 当前游戏」按钮向左补位。",
        }),
        new("0.1.38", "2026-11-25", new[]
        {
            "修复换票嵌套 token 被当成 JSON 原文写入 Cookie 的问题。",
            "角色拉取日志透出原始 retcode 与凭证齐全状态。",
        }),
        new("0.1.37", "2026-11-25", new[]
        {
            "调整扫码登录凭证解析，减少登录失败误报。",
            "短信验证码登录复用同一套凭证解析逻辑。",
        }),
        new("0.1.36", "2026-11-25", new[]
        {
            "换票改用通行证 V2 接口，旧链路作为兜底。",
            "修复刚登录账号误报凭证过期、昵称占位显示。",
        }),
        new("0.1.35", "2026-11-25", new[]
        {
            "账号列表正确显示通行证昵称与米游社 UID。",
            "合并两条角色获取链路，减少凭证过期误判。",
        }),
        new("0.1.34", "2026-11-25", new[]
        {
            "修复登录后账号昵称占位显示。",
            "修复任务 / 签到接口提示不可用。",
        }),
        new("0.1.33", "2026-11-25", new[]
        {
            "预约游戏 / 米哈云游开关互不影响其他按钮位置。",
            "修复账号名称误显示成游戏角色名与游戏 UID。",
            "调整全新安装后的默认设置。",
        }),
        new("0.1.32", "2026-11-25", new[]
        {
            "修复扫码 / 短信登录提示 Cookie 无效。",
            "启动页新增预约游戏、米哈云游显隐开关。",
        }),
        new("0.1.31", "2026-11-25", new[]
        {
            "全部游戏下拉在游戏名前显示对应角色头像图标。",
        }),
        new("0.1.30", "2026-11-25", new[]
        {
            "任务 / 签到页账号状态改用圆点加文字。",
            "登录失效时统一提示到「账号」页重新登录。",
        }),
        new("0.1.28", "2026-11-22", new[]
        {
            "更名 MikoNest，更换图标与品牌字标。",
            "数据目录自动迁移，继承原有设置、账号与抽卡记录。",
        }),
        new("0.1.27", "2026-11-22", new[]
        {
            "修正武器池与常驻池保底条的范围与配色。",
        }),
        new("0.1.26", "2026-11-21", new[]
        {
            "设置页卡片高度收紧并保持等高。",
            "检查更新按钮显示下载进度。",
            "缩小「发现新版本」更新弹窗。",
        }),
        new("0.1.25", "2026-11-21", new[]
        {
            "修复旧目录找不到主程序时无法进入安装。",
        }),
        new("0.1.24", "2026-11-21", new[]
        {
            "「获取游戏」更名为「安装游戏」。",
            "启动时自动检查更新并弹出更新窗口。",
            "设置页卡片改为等高布局。",
        }),
        new("0.1.23", "2026-11-21", new[]
        {
            "游戏设置新增卸载游戏；修复停止安装后主按钮误显示开始游戏。",
            "修复更新弹窗关闭按钮显示不全。",
        }),
        new("0.1.22", "2026-11-21", new[]
        {
            "停止安装并清理移到游戏设置。",
            "安装进度配色跟随系统强调色。",
            "全部游戏下拉支持自定义排序。",
        }),
        new("0.1.21", "2026-11-21", new[]
        {
            "安装时预取启动页动态壁纸。",
            "安装进度直接显示在开始游戏按钮上。",
            "同一版本更新只提示一次。",
        }),
        new("0.1.20", "2026-11-20", new[]
        {
            "游戏设置改为图标按钮并精简面板。",
            "安装改为软件内逐文件安装，可中止并清理。",
            "开始游戏前自动检查更新。",
        }),
        new("0.1.19", "2026-11-20", new[]
        {
            "「实时便签」改为「更新日志」。",
            "游戏设置支持查看路径、定位、日志、卸载与安装更新。",
            "优化抽卡记录 UID 下拉与保底条，限定记录加 UP 标记。",
        }),
        new("0.1.18", "2026-11-20", new[]
        {
            "修复扫码登录换取 cookie_token 失败。",
            "修复从启动页点社区仍停留启动页。",
        }),
        new("0.1.17", "2026-11-20", new[]
        {
            "修复扫码后缺凭证导致任务签到不可用。",
            "抽卡记录更新后保持当前 UID；修正 4★ 保底条。",
        }),
        new("0.1.16", "2026-11-20", new[]
        {
            "后台预加载官方壁纸，换版本自动清理旧壁纸。",
            "修复关闭到托盘后再次启动黑屏。",
        }),
        new("0.1.15", "2026-11-20", new[]
        {
            "签到图形验证码在软件内完成并自动重试。",
            "修复一键更新无法覆盖旧版本；重做 macOS 界面。",
        }),
        new("0.1.13", "2026-11-20", new[]
        {
            "新增跨平台 UI 与 macOS 安装包。",
            "接入各游戏抽卡接口，新增保底条与 UP 标记。",
            "语言抽屉与跟随系统、13 套配色。",
        }),
        new("0.1.12", "2026-11-19", new[] { "版本迭代与稳定性修复。" }),
        new("0.1.11", "2026-11-19", new[] { "版本迭代与界面细节优化。" }),
        new("0.1.10", "2026-11-19", new[] { "版本迭代与稳定性修复。" }),
        new("0.1.9", "2026-11-19", new[] { "版本迭代与稳定性修复。" }),
        new("0.1.8", "2026-11-19", new[] { "版本迭代与稳定性修复。" }),
        new("0.1.7", "2026-11-19", new[]
        {
            "接入官方壁纸接口，支持动态壁纸。",
            "获取游戏改为安装弹窗；修复窗口还原与按钮裁切问题。",
        }),
        new("0.1.5", "2026-11-17", new[]
        {
            "首个公开版本：启动游戏、社区、抽卡记录、签到、账号与设置。",
            "任务 / 签到页支持多游戏签到、米游币任务与实时日志。",
            "新增胡桃红配色与多游戏运行状态。",
        }),
    };

    public ChangelogWindow()
    {
        Title = "更新日志 · MikoNest";
        Width = 640;
        Height = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = (Brush)Application.Current.Resources["PageBg"];

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock
        {
            Text = "更新日志  (≧▽≦)",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
        });
        header.Children.Add(new TextBlock
        {
            Text = "MikoNest 从首个公开版本到 0.1.40 的更新与修复记录",
            FontSize = 12,
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
        });
        root.Children.Add(header);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var list = new StackPanel();
        scroll.Content = list;
        Loaded += (_, _) => scroll.ScrollToTop();

        foreach (var e in Entries)
        {
            var card = new Border
            {
                Background = (Brush)Application.Current.Resources["CardBg"],
                BorderBrush = (Brush)Application.Current.Resources["CardBorder"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 13, 16, 14),
                Margin = new Thickness(0, 0, 0, 10),
            };
            var stack = new StackPanel();
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(new TextBlock
            {
                Text = $"v{e.Version}",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = $"  {e.Date}",
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(4, 0, 0, 2),
                Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            });
            stack.Children.Add(titleRow);

            foreach (var item in e.Items)
            {
                var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock
                {
                    Text = "•",
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["TextSecondary"],
                });
                var txt = new TextBlock
                {
                    Text = item,
                    FontSize = 12.5,
                    LineHeight = 19,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)Application.Current.Resources["TextSecondary"],
                };
                Grid.SetColumn(txt, 1);
                row.Children.Add(txt);
                stack.Children.Add(row);
            }

            card.Child = stack;
            list.Children.Add(card);
        }

        Content = root;
    }
}

