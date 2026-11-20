using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MiNest.Models;
using MiNest.Services;

namespace MiNest.Avalonia.Views;

/// <summary>抽卡记录：四列卡池、概率统计、UP 标记与保底进度条（读取本地库）。</summary>
public class GachaView : UserControl
{
    private sealed record Pool(string Code, string Title, string[] Extras);

    private static readonly Dictionary<string, Pool[]> Pools = new()
    {
        ["genshin"] = new[]
        {
            new Pool("200", "常驻祈愿", Array.Empty<string>()),
            new Pool("301", "角色活动祈愿", new[] { "400" }),
            new Pool("302", "武器活动祈愿", Array.Empty<string>()),
            new Pool("500", "集录祈愿", Array.Empty<string>()),
            new Pool("100", "新手祈愿", Array.Empty<string>()),
        },
        ["starrail"] = new[]
        {
            new Pool("1", "群星跃迁", Array.Empty<string>()),
            new Pool("11", "角色活动跃迁", Array.Empty<string>()),
            new Pool("12", "光锥活动跃迁", new[] { "22" }),
            new Pool("21", "角色联动跃迁", Array.Empty<string>()),
            new Pool("2", "始发跃迁", Array.Empty<string>()),
        },
        ["zzz"] = new[]
        {
            new Pool("2", "独家频段", new[] { "102" }),
            new Pool("3", "音擎频段", new[] { "103" }),
            new Pool("1", "常驻频段", Array.Empty<string>()),
            new Pool("5", "邦布频段", Array.Empty<string>()),
        },
        ["bh3"] = new[]
        {
            new Pool("1", "家园补给", Array.Empty<string>()),
            new Pool("2", "精准补给", Array.Empty<string>()),
            new Pool("3", "扩充补给", Array.Empty<string>()),
        },
    };

    private static readonly HashSet<string> StandardFiveStar = new(StringComparer.OrdinalIgnoreCase)
    {
        "迪卢克", "莫娜", "七七", "刻晴", "提纳里", "迪希雅", "梦见月瑞希",
        "天空之刃", "天空之脊", "天空之翼", "天空之卷", "天空之傲",
        "风鹰剑", "阿莫斯之弓", "四风原典", "和璞鸢", "狼的末路",
    };

    private static readonly (string Key, string Name)[] Games =
    {
        ("genshin", "原神"), ("starrail", "星穹铁道"), ("zzz", "绝区零"), ("bh3", "崩坏3"),
    };

    private string _gameKey = "genshin";
    private string? _uid;
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly ComboBox _uidBox = new() { Width = 150, FontSize = 12 };
    private readonly TextBlock _subtitle = new() { FontSize = 11.5 };
    private readonly TextBlock _status = new() { FontSize = 11.5 };
    private readonly WrapPanel _columns = new() { Orientation = Orientation.Horizontal, ItemSpacing = 12, LineSpacing = 12 };

    public GachaView()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(22, 18, 22, 18) };

        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") , Margin = new Thickness(0, 0, 0, 12)};
        var left = new StackPanel { Spacing = 8 };
        left.Children.Add(_chips);
        _subtitle.Classes.Add("muted");
        left.Children.Add(_subtitle);
        toolbar.Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
        _uidBox.SelectionChanged += (_, _) => { _uid = _uidBox.SelectedItem as string; Render(); };
        right.Children.Add(_uidBox);
        var refresh = new Button { Content = "刷新", Classes = { "ghost" }, Height = 32, Padding = new Thickness(14, 0) };
        refresh.Click += (_, _) => Reload();
        right.Children.Add(refresh);
        Grid.SetColumn(right, 1);
        toolbar.Children.Add(right);

        root.Children.Add(toolbar);

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        scroll.Content = _columns;
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        _status.Classes.Add("muted");
        _status.Margin = new Thickness(0, 10, 0, 0);

        var host = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        host.Children.Add(root);
        var statusRow = new Border
        {
            Padding = new Thickness(22, 8, 22, 12),
            Child = _status,
        };
        Grid.SetRow(statusRow, 1);
        host.Children.Add(statusRow);
        Content = host;

        BuildChips();
        SelectGame("genshin");
    }

    private void BuildChips()
    {
        foreach (var (key, name) in Games)
        {
            var btn = new Button
            {
                Content = name,
                FontSize = 12,
                Height = 30,
                Padding = new Thickness(14, 0),
                CornerRadius = new CornerRadius(9),
                Tag = key,
                Background = new SolidColorBrush(Color.Parse("#0A000000")),
                BorderThickness = new Thickness(0),
            };
            btn.Click += (_, _) => SelectGame(key);
            _chips.Children.Add(btn);
        }
    }

    private void SelectGame(string key)
    {
        _gameKey = key;
        foreach (var child in _chips.Children.OfType<Button>())
        {
            var active = (string?)child.Tag == key;
            child.Background = active
                ? (IBrush?)Application.Current!.Resources["AccentSoft"]
                : new SolidColorBrush(Color.Parse("#0A000000"));
            child.FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal;
        }
        Reload();
    }

    private void Reload()
    {
        var uids = GachaStore.GetUids(_gameKey);
        _uidBox.ItemsSource = uids;
        if (uids.Count > 0) _uidBox.SelectedIndex = 0;
        _uid = uids.Count > 0 ? uids[0] : null;
        Render();
    }

    private void Render()
    {
        _columns.Children.Clear();
        var all = GachaStore.Query(_uid, _gameKey);
        var last = all.Count > 0 ? all.Max(x => x.Time) : null;
        _subtitle.Text = $"本地库 {all.Count} 条" + (last != null ? $" · 最近更新 {last}" : "");
        _status.Text = all.Count == 0
            ? "本地库还是空的：抽卡数据与 mi-Nest 桌面版共用同一数据目录，导入 UIGF / SRGF 后即可查看。"
            : $"共 {all.Count} 条记录 · 统计口径与 Starward 一致（5★ 平均按每条 5★ 的垫抽数计算）";

        var defs = Pools.TryGetValue(_gameKey, out var p) ? p : new[] { new Pool("", "卡池", Array.Empty<string>()) };
        foreach (var pool in defs)
        {
            var items = all.Where(x => x.GachaType == pool.Code || pool.Extras.Contains(x.GachaType)).ToList();
            if ((pool.Code is "100" or "2") && items.Count == 0) continue;
            _columns.Children.Add(BuildColumn(pool, items));
        }
    }

    private Control BuildColumn(Pool pool, List<GachaItem> items)
    {
        var card = new Border
        {
            Width = 268,
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(14, 14, 14, 16),
            Background = (IBrush)Application.Current!.Resources["CardBg"]!,
            BorderBrush = (IBrush)Application.Current!.Resources["CardBorder"]!,
            BorderThickness = new Thickness(1),
        };

        var stack = new StackPanel { Spacing = 6 };

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        head.Children.Add(new TextBlock { Text = pool.Title, FontSize = 14, FontWeight = FontWeight.SemiBold });
        var total = new TextBlock { Text = items.Count.ToString(), FontSize = 14, FontWeight = FontWeight.SemiBold };
        Grid.SetColumn(total, 1);
        head.Children.Add(total);
        stack.Children.Add(head);

        var range = items.Count > 0 ? $"{items.Min(x => x.Time)} — {items.Max(x => x.Time)}" : "—";
        var rangeBlock = new TextBlock { Text = range, FontSize = 10 };
        rangeBlock.Classes.Add("muted");
        stack.Children.Add(rangeBlock);

        var five = items.Where(x => x.RankType == "5").ToList();
        var four = items.Where(x => x.RankType == "4").ToList();
        var three = items.Where(x => x.RankType == "3").ToList();

        var avg = five.Count > 0
            ? five.Select(x => PityOf(items, x)).Average()
            : 0;

        stack.Children.Add(StatRow("5★ 平均", five.Count > 0 ? avg.ToString("0.00") : "—", "#C9971F"));
        stack.Children.Add(StatRow("5★ 统计", $"{five.Count} [{Percent(five.Count, items.Count)}]", "#C9971F"));
        stack.Children.Add(StatRow("4★ 统计", $"{four.Count} [{Percent(four.Count, items.Count)}]", "#9A6BD6"));
        stack.Children.Add(StatRow("3★ 统计", $"{three.Count} [{Percent(three.Count, items.Count)}]", "#6A7488"));

        stack.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 6), Background = (IBrush)Application.Current!.Resources["Divider"]! });

        var pending = PendingPity(items);
        var pendingBorder = new Border
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(9, 6),
            Background = new SolidColorBrush(Color.Parse(pending <= 70 ? "#3F9B6B" : "#C0453F")),
        };
        var pendingGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        pendingGrid.Children.Add(new TextBlock { Text = "已垫", FontSize = 11.5, Foreground = Brushes.White, FontWeight = FontWeight.SemiBold });
        var pv = new TextBlock { Text = pending.ToString(), FontSize = 11.5, Foreground = Brushes.White, FontWeight = FontWeight.SemiBold };
        Grid.SetColumn(pv, 1);
        pendingGrid.Children.Add(pv);
        pendingBorder.Child = pendingGrid;
        stack.Children.Add(pendingBorder);

        var list = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        if (five.Count == 0)
            list.Children.Add(new TextBlock { Text = "暂无 5★ 记录", FontSize = 11, Classes = { "muted" } });
        foreach (var item in five)
            list.Children.Add(BuildRow(item, items));

        stack.Children.Add(list);
        card.Child = stack;
        return card;
    }

    private static string Percent(int part, int total) => total == 0 ? "0.00%" : $"{(double)part * 100 / total:0.00}%";

    private Control StatRow(string label, string value, string color)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Foreground = new SolidColorBrush(Color.Parse("#8A94A6")) });
        var v = new TextBlock { Text = value, FontSize = 11.5, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse(color)) };
        Grid.SetColumn(v, 1);
        grid.Children.Add(v);
        return grid;
    }

    /// <summary>某条 5★ 记录抽到时已经垫了多少抽（同一卡池内按时间正序，从上一次 5★ 之后算起）</summary>
    private static int PityOf(List<GachaItem> pool, GachaItem target)
    {
        var ordered = pool
            .OrderBy(x => x.Time)
            .ThenBy(x => x.Id)
            .ToList();
        var index = ordered.FindIndex(x => x.Id == target.Id);
        if (index < 0) return 0;

        var count = 0;
        for (var i = index; i >= 0; i--)
        {
            count++;
            if (i < index && ordered[i].RankType == "5") return count - 1;
        }
        return count;
    }

    private static int PendingPity(List<GachaItem> pool)
    {
        var count = 0;
        foreach (var item in pool.OrderByDescending(x => x.Time).ThenByDescending(x => x.Id))
        {
            if (item.RankType == "5") return count;
            count++;
        }
        return count;
    }

    private Control BuildRow(GachaItem item, List<GachaItem> pool)
    {
        var pity = PityOf(pool, item);
        var isUp = _gameKey == "genshin"
                   && item.RankType == "5"
                   && item.GachaType is "301" or "400" or "302"
                   && !StandardFiveStar.Contains(item.Name.Trim());

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var badge = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(7),
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.Parse("#33C9A227")),
        };
        badge.Child = new TextBlock
        {
            Text = item.Name.Length > 0 ? item.Name[..1] : "?",
            FontSize = 11, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#B8860B")),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(badge);

        var namePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        namePanel.Children.Add(new TextBlock { Text = item.Name, FontSize = 12 });
        if (isUp)
        {
            var up = new Border
            {
                CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1),
                Background = new SolidColorBrush(Color.Parse("#14D9A93C")),
                BorderBrush = new SolidColorBrush(Color.Parse("#66D9A93C")), BorderThickness = new Thickness(0.8),
                VerticalAlignment = VerticalAlignment.Center,
            };
            up.Child = new TextBlock { Text = "UP!", FontSize = 9.5, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#D9A93C")) };
            namePanel.Children.Add(up);
        }
        Grid.SetColumn(namePanel, 1);
        row.Children.Add(namePanel);

        var value = new TextBlock
        {
            Text = pity.ToString(),
            FontSize = 12, FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse(pity <= 70 ? "#3F9B6B" : "#C0453F")),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
        };
        Grid.SetColumn(value, 2);
        row.Children.Add(value);

        var bar = new Grid { Height = 3, Margin = new Thickness(2, 3, 6, 7), ColumnDefinitions = new ColumnDefinitions($"{Math.Clamp(pity, 0, 90)}*,{90 - Math.Clamp(pity, 0, 90)}*") };
        var track = new Border { CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(Color.Parse("#22808080")) };
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        bar.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(1.5),
            Background = new SolidColorBrush(Color.Parse(pity <= 70 ? "#3F9B6B" : "#C0453F")),
        });

        var stack = new StackPanel();
        stack.Children.Add(row);
        stack.Children.Add(bar);
        return stack;
    }
}
