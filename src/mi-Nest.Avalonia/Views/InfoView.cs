using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MiNest.Avalonia.Views;

/// <summary>账号 / 任务 / 米哈游相关：跨平台版先提供信息与入口，登录能力沿用桌面端。</summary>
public static class InfoView
{
    public static Control Account() => Page("账号",
        "跨平台版暂不内置网页登录。桌面版已登录的账号与角色信息保存在同一数据目录，抽卡记录可直接查看。",
        new[] { ("米游社通行证", "https://user.mihoyo.com/") });

    public static Control Tasks() => Page("任务 / 签到",
        "每日签到需要米游社登录态，跨平台版暂未接入；可在手机端米游社或桌面版完成签到。",
        new[] { ("米游社签到", "https://act.mihoyo.com/bbs/event/signin/hk4e/index.html") });

    public static Control Mhy() => Page("米哈游相关",
        "官方社区与常用工具入口。",
        new[]
        {
            ("米游社", "https://www.miyoushe.com/"),
            ("原神官网", "https://ys.mihoyo.com/"),
            ("星穹铁道", "https://sr.mihoyo.com/"),
            ("绝区零", "https://zzz.mihoyo.com/"),
            ("崩坏3", "https://bh3.mihoyo.com/"),
            ("HOYO-MiX", "https://www.miyoushe.com/dby/accountCenter/postList?id=310621322"),
        });

    private static Control Page(string title, string desc, (string Label, string Url)[] links)
    {
        var stack = new StackPanel { Spacing = 14, Margin = new Thickness(22, 18, 22, 22) };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold });
        stack.Children.Add(new TextBlock
        {
            Text = desc, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#8A94A6")),
        });

        var wrap = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 10, LineSpacing = 10 };
        foreach (var (label, url) in links)
        {
            var btn = new Button { Content = label, Classes = { "ghost" }, Height = 36, Padding = new Thickness(16, 0) };
            btn.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } };
            wrap.Children.Add(btn);
        }
        stack.Children.Add(wrap);

        return new Border
        {
            CornerRadius = new CornerRadius(18),
            Margin = new Thickness(18, 6, 18, 18),
            Background = (IBrush)Application.Current!.Resources["CardBg"]!,
            BorderBrush = (IBrush)Application.Current!.Resources["CardBorder"]!,
            BorderThickness = new Thickness(1),
            Child = stack,
        };
    }
}
