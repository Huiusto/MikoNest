using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using MiNest.Models;
using MiNest.Services;

namespace MiNest.Pages;

/// <summary>抽卡记录：按卡池分列（Starward 祈愿记录 / 跃迁记录 / 调频记录同款口径）</summary>
public partial class GachaPage : UserControl
{
    private static readonly string[] GameKeys = { "genshin", "starrail", "zzz", "bh3" };
    private static readonly string[] GameLabels = { "原神", "星穹铁道", "绝区零", "崩坏3" };

    private readonly List<ColumnUi> _columns = new();
    private readonly List<BeyondColUi> _beyondColumns = new();
    private bool _loading;
    private string _gameKey = "genshin";
    private bool _beyondMode;

    /// <summary>千星奇域记录单独存库，避免与普通祈愿统计互相污染</summary>
    private const string BeyondStoreKey = "genshin_beyond";

    private sealed class BeyondColUi
    {
        public GachaPoolDef Pool = new("", "");
        public bool ShowFour = true;
        public bool Activity;
        public Border Root = new();
        public Border Card = new();
        public StackPanel ListPanel = new();
        public Button TabFive = new();
        public Button TabFour = new();
        public Button TabThree = new();
        public List<RowVm> FiveRows = new();
        public List<RowVm> FourRows = new();
        public List<RowVm> ThreeRows = new();
        public int Total;
        public int Pending;
        public TextBlock EmptyHint = new();
    }

    private sealed class RowVm
    {
        public GachaItem Item = new();
        public int Pity;
    }

    private sealed class ColumnUi
    {
        public GachaPoolDef Pool = new("", "");
        public bool ShowFive = true;
        public Border Root = new();
        public StackPanel ListPanel = new();
        public Button TabFive = new();
        public Button TabFour = new();
        public Border Card = new();
        public List<RowVm> FiveRows = new();
        public List<RowVm> FourRows = new();
        public int Total;
        public int Pending;
        public TextBlock EmptyHint = new();
    }

    public GachaPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ComboGame.Items.Count == 0)
        {
            _loading = true;
            foreach (var label in GameLabels) ComboGame.Items.Add(label);
            ComboGame.SelectedIndex = 0;
            _loading = false;
        }
        ChkShow4.IsChecked = true;
        ChkShowIcon.IsChecked = true;
        BtnBeyond.Visibility = _gameKey == "genshin" ? Visibility.Visible : Visibility.Collapsed;
        TxtSettingsInfo.Text = $"数据目录：{AppPaths.DataDir}";
        RefreshAll();
    }

    // ================= 顶部筛选 =================

    private void ComboGame_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var idx = Math.Max(0, ComboGame.SelectedIndex);
        _gameKey = GameKeys[idx];
        _beyondMode = false;
        BtnBeyond.Content = "千星奇域颂愿";
        BtnBeyond.Visibility = _gameKey == "genshin" ? Visibility.Visible : Visibility.Collapsed;
        TxtTitle.Text = _gameKey switch
        {
            "genshin" => "祈愿记录",
            "starrail" => "跃迁记录",
            "zzz" => "调频记录",
            _ => "补给记录",
        };
        ApplyModeLayout();
        RefreshAll();
    }

    private void BtnBeyond_Click(object sender, RoutedEventArgs e)
    {
        _beyondMode = !_beyondMode;
        BtnBeyond.Content = _beyondMode ? "返回祈愿记录" : "千星奇域颂愿";
        ApplyModeLayout();
        RefreshAll();
    }

    private void ApplyModeLayout()
    {
        BannerScroll.Visibility = _beyondMode ? Visibility.Collapsed : Visibility.Visible;
        BeyondScroll.Visibility = _beyondMode ? Visibility.Visible : Visibility.Collapsed;
        TxtTitle.Text = _beyondMode
            ? "千星奇域颂愿"
            : _gameKey switch
            {
                "genshin" => "祈愿记录",
                "starrail" => "跃迁记录",
                "zzz" => "调频记录",
                _ => "补给记录",
            };
    }

    private void ComboUid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        RefreshBanners();
    }

    private void RefreshAll()
    {
        RefreshUidCombo();
        RefreshBanners();
    }

    private string? SelectedUid
    {
        get
        {
            var v = ComboUid.SelectedItem as string;
            if (string.IsNullOrEmpty(v) || v.StartsWith("全部")) return null;
            return v;
        }
    }

    private string StoreKey => _beyondMode ? BeyondStoreKey : _gameKey;

    private void RefreshUidCombo()
    {
        var uids = GachaStore.GetUids(StoreKey);
        _loading = true;
        var keep = SelectedUid;
        ComboUid.Items.Clear();
        if (uids.Count == 0)
        {
            ComboUid.Items.Add("全部");
            ComboUid.SelectedIndex = 0;
        }
        else
        {
            ComboUid.Items.Add("全部 UID");
            foreach (var u in uids) ComboUid.Items.Add(u);
            var idx = keep != null ? uids.IndexOf(keep) + 1 : 1;
            ComboUid.SelectedIndex = Math.Clamp(idx, 0, uids.Count);
        }
        _loading = false;
    }

    private void RefreshBanners()
    {
        if (_beyondMode)
        {
            RefreshBeyondBanners();
            return;
        }
        var uid = SelectedUid;
        var all = GachaStore.Query(uid, _gameKey);
        var last = all.Count > 0 ? all.Max(x => x.Time) : null;
        TxtSubtitle.Text = $"本地库 {all.Count} 条" + (last != null ? $" · 最近更新 {last}" : "");
        if (all.Count == 0)
            TxtStatus.Text = "本地库还是空的：先在游戏内打开一次「抽卡记录」页面，再回来点「更新记录」；也可以从「▾ → 导入 UIGF / SRGF 文件」导入已有记录。";

        BannerPanel.Children.Clear();
        _columns.Clear();

        var pools = GachaService.Pools.TryGetValue(_gameKey, out var p)
            ? p
            : new[] { new GachaPoolDef("", "卡池") };

        foreach (var pool in pools)
        {
            var items = all.Where(x => x.GachaType == pool.Code).ToList();
            // 新手池只有真的有记录时才显示，避免一排空列
            if (pool.Code is "100" or "2" && items.Count == 0) continue;
            var col = BuildColumn(pool, items);
            _columns.Add(col);
            BannerPanel.Children.Add(col.Root);
        }

        ApplyColumnWidth();
    }

    // ================= 列构建 =================

    private static readonly Dictionary<string, string> RankColor = new()
    {
        ["5"] = "#D9A24A",
        ["4"] = "#A47BD0",
        ["3"] = "#7C93B0",
    };

    private static readonly Dictionary<string, string> RankSoft = new()
    {
        ["5"] = "#26D9A24A",
        ["4"] = "#1FA47BD0",
        ["3"] = "#147C93B0",
    };

    private static Brush Brush(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private ColumnUi BuildColumn(GachaPoolDef pool, List<GachaItem> items)
    {
        var col = new ColumnUi { Pool = pool };
        var accentHex = _gameKey switch
        {
            "genshin" => "#56B3D6",
            "starrail" => "#7C86D9",
            "zzz" => "#E0A93B",
            _ => "#C77DDB",
        };

        var asc = items.OrderBy(x => x.Time).ThenBy(x => x.Id).ToList();
        var five = new List<RowVm>();
        var four = new List<RowVm>();
        var c5 = 0;
        var c4 = 0;
        foreach (var it in asc)
        {
            c5++;
            c4++;
            if (it.RankType == "5")
            {
                five.Add(new RowVm { Item = it, Pity = c5 });
                c5 = 0;
                c4 = 0;
            }
            else if (it.RankType == "4")
            {
                four.Add(new RowVm { Item = it, Pity = c4 });
                c4 = 0;
            }
        }
        col.Pending = c5;
        col.FiveRows = five.OrderByDescending(r => r.Item.Time).ThenByDescending(r => r.Item.Id).ToList();
        col.FourRows = four.OrderByDescending(r => r.Item.Time).ThenByDescending(r => r.Item.Id).ToList();

        var total = items.Count;
        col.Total = total;
        var cFive = items.Count(x => x.RankType == "5");
        var cFour = items.Count(x => x.RankType == "4");
        var cThree = items.Count(x => x.RankType == "3");
        var avgFive = five.Count > 0 ? five.Average(r => r.Pity) : 0d;
        var start = asc.FirstOrDefault()?.Time;
        var end = asc.LastOrDefault()?.Time;

        string Pct(int n) => total > 0 ? $"{n * 100.0 / total:0.00}%" : "0.00%";
        static string Short(string? t)
        {
            if (string.IsNullOrEmpty(t)) return "—";
            var s = t.Replace('-', '/');
            return s.Length >= 16 ? s[..16] : s;
        }

        var root = new Border
        {
            Width = 262,
            Margin = new Thickness(0, 0, 10, 0),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 12, 14, 12),
        };
        col.Card = root;
        root.SetResourceReference(Border.BackgroundProperty, "CardBg");
        root.SetResourceReference(Border.BorderBrushProperty, "CardBorder");

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // 顶部细色条（区分卡池）
        var accent = new Border
        {
            Height = 3,
            CornerRadius = new CornerRadius(2),
            Background = Brush(accentHex),
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 10),
        };
        grid.Children.Add(accent);

        // 标题 + 总数
        var headGrid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var nameBlock = new TextBlock
        {
            Text = pool.Name,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var totalBlock = new TextBlock
        {
            Text = total.ToString(),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            Margin = new Thickness(8, 0, 0, 0),
        };
        Grid.SetColumn(totalBlock, 1);
        headGrid.Children.Add(nameBlock);
        headGrid.Children.Add(totalBlock);
        Grid.SetRow(headGrid, 1);
        grid.Children.Add(headGrid);

        // 时间区间 + 统计
        var statPanel = new StackPanel { Margin = new Thickness(0, 2, 0, 10) };
        statPanel.Children.Add(new TextBlock
        {
            Text = $"{Short(start)} - {Short(end)}",
            FontSize = 10,
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            Margin = new Thickness(0, 0, 0, 8),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        statPanel.Children.Add(StatRowHost("5★ 平均", five.Count > 0 ? avgFive.ToString("0.00") : "—", "#D9A24A"));
        statPanel.Children.Add(StatRowHost("5★ 统计", $"{cFive}  [{Pct(cFive)}]", "#D9A24A"));
        statPanel.Children.Add(StatRowHost("4★ 统计", $"{cFour}  [{Pct(cFour)}]", "#A47BD0"));
        statPanel.Children.Add(StatRowHost("3★ 统计", $"{cThree}  [{Pct(cThree)}]", "#7C93B0"));
        Grid.SetRow(statPanel, 2);
        grid.Children.Add(statPanel);

        // 5★ / 4★ 分段切换
        var tabs = new Grid { Height = 28, Margin = new Thickness(0, 0, 0, 8) };
        tabs.ColumnDefinitions.Add(new ColumnDefinition());
        tabs.ColumnDefinitions.Add(new ColumnDefinition());
        var pillBg = new Border
        {
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
        };
        pillBg.SetResourceReference(Border.BackgroundProperty, "InputBg");
        pillBg.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
        Grid.SetColumnSpan(pillBg, 2);
        tabs.Children.Add(pillBg);

        col.TabFive = MakePillButton("5★");
        col.TabFive.Margin = new Thickness(2);
        col.TabFive.Click += (_, _) => SwitchTab(col, true);
        Grid.SetColumn(col.TabFive, 0);

        col.TabFour = MakePillButton("4★");
        col.TabFour.Margin = new Thickness(2);
        col.TabFour.Click += (_, _) => SwitchTab(col, false);
        Grid.SetColumn(col.TabFour, 1);

        tabs.Children.Add(col.TabFive);
        tabs.Children.Add(col.TabFour);
        Grid.SetRow(tabs, 3);
        grid.Children.Add(tabs);

        // 记录列表（占满剩余高度）
        var listScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, -6, 0),
            Padding = new Thickness(0, 0, 6, 0),
        };
        listScroll.Content = col.ListPanel;
        var listHost = new Grid { MinHeight = 180, Margin = new Thickness(0, 0, 0, 0) };
        listHost.Children.Add(listScroll);
        Grid.SetRow(listHost, 4);
        grid.Children.Add(listHost);

        // 空态提示
        col.EmptyHint = new TextBlock
        {
            Text = "暂无记录",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 30, 0, 0),
        };
        listHost.Children.Add(col.EmptyHint);

        root.Child = grid;
        col.Root = root;

        SwitchTab(col, true);
        return col;
    }

    private Border StatRowHost(string label, string value, string colorHex)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var l = new TextBlock
        {
            Text = label,
            FontSize = 11.5,
            Foreground = Brush(colorHex),
            FontWeight = FontWeights.SemiBold,
        };
        var v = new TextBlock
        {
            Text = value,
            FontSize = 11.5,
            Foreground = Brush(colorHex),
            FontWeight = FontWeights.SemiBold,
        };
        Grid.SetColumn(v, 1);
        g.Children.Add(l);
        g.Children.Add(v);
        return new Border { Child = g };
    }

    private void BannerScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyColumnWidth();
    }

    private void ApplyColumnWidth()
    {
        if (_columns.Count == 0) return;
        var avail = BannerScroll.ActualWidth - 4;
        if (avail <= 0) return;
        var gap = 10.0;
        var w = (avail - gap * (_columns.Count - 1)) / _columns.Count;
        w = Math.Clamp(w, 208, 340);
        foreach (var c in _columns) c.Card.Width = w;
    }

    // ================= 千星奇域颂愿 =================

    private void RefreshBeyondBanners()
    {
        var uid = SelectedUid;
        var all = GachaStore.Query(uid, BeyondStoreKey);
        var last = all.Count > 0 ? all.Max(x => x.Time) : null;
        TxtSubtitle.Text = $"本地库 {all.Count} 条" + (last != null ? $" · 最近更新 {last}" : "");
        if (all.Count == 0)
            TxtStatus.Text = "还没有颂愿记录：先在游戏内打开一次「千星奇域 - 颂愿记录」页面，再回来点「更新记录」。";
        else
            TxtStatus.Text = $"颂愿记录已载入：{all.Count} 条（数据来源：游戏缓存 / 粘贴链接）";

        BeyondPanel.Children.Clear();
        _beyondColumns.Clear();

        foreach (var pool in GachaService.BeyondPools)
        {
            var items = all.Where(x => x.GachaType == pool.Code).ToList();
            var col = BuildBeyondColumn(pool, items);
            _beyondColumns.Add(col);
            BeyondPanel.Children.Add(col.Root);
        }
        ApplyBeyondWidth();
    }

    private static readonly Dictionary<string, string> BeyondRankColor = new()
    {
        ["5"] = "#E8A33D",
        ["4"] = "#C864E0",
        ["3"] = "#5EAAED",
        ["2"] = "#3BA272",
    };

    private static readonly Dictionary<string, string> BeyondRankSoft = new()
    {
        ["5"] = "#2EE8A33D",
        ["4"] = "#26C864E0",
        ["3"] = "#205EAAED",
        ["2"] = "#1A3BA272",
    };

    private BeyondColUi BuildBeyondColumn(GachaPoolDef pool, List<GachaItem> items)
    {
        var col = new BeyondColUi { Pool = pool, Activity = pool.Code == "2000" };
        var accentHex = col.Activity ? "#E8A33D" : "#C864E0";
        var asc = items.OrderBy(x => x.Time).ThenBy(x => x.Id).ToList();
        var five = new List<RowVm>();
        var four = new List<RowVm>();
        var three = new List<RowVm>();
        var c4 = 0;
        var c5 = 0;
        foreach (var it in asc)
        {
            c4++;
            c5++;
            if (it.RankType == "5")
            {
                five.Add(new RowVm { Item = it, Pity = c5 });
                c5 = 0;
                c4 = 0;
            }
            else if (it.RankType == "4")
            {
                four.Add(new RowVm { Item = it, Pity = c4 });
                c4 = 0;
            }
            else if (it.RankType == "3")
            {
                three.Add(new RowVm { Item = it, Pity = c4 });
            }
        }
        col.Pending = col.Activity ? c5 : c4;
        col.FiveRows = five.OrderByDescending(r => r.Item.Time).ThenByDescending(r => r.Item.Id).ToList();
        col.FourRows = four.OrderByDescending(r => r.Item.Time).ThenByDescending(r => r.Item.Id).ToList();
        col.ThreeRows = three.OrderByDescending(r => r.Item.Time).ThenByDescending(r => r.Item.Id).ToList();

        var total = items.Count;
        col.Total = total;
        var cFive2 = items.Count(x => x.RankType == "5");
        var cFour = items.Count(x => x.RankType == "4");
        var cThree = items.Count(x => x.RankType == "3");
        var cTwo = items.Count(x => x.RankType == "2");
        var avgFour = four.Count > 0 ? four.Average(r => r.Pity) : 0d;
        var avgFive = five.Count > 0 ? five.Average(r => r.Pity) : 0d;
        var start = asc.FirstOrDefault()?.Time;
        var end = asc.LastOrDefault()?.Time;

        string Pct(int n) => total > 0 ? $"{n * 100.0 / total:0.00}%" : "0.00%";
        static string Short(string? t)
        {
            if (string.IsNullOrEmpty(t)) return "—";
            var s = t.Replace('-', '/');
            return s.Length >= 16 ? s[..16] : s;
        }

        var root = new Border
        {
            Width = 460,
            Margin = new Thickness(0, 0, 10, 0),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 14, 16, 14),
        };
        col.Card = root;
        root.SetResourceReference(Border.BackgroundProperty, "CardBg");
        root.SetResourceReference(Border.BorderBrushProperty, "CardBorder");

        var grid = new Grid();
        for (var i = 0; i < 5; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions[4].Height = new GridLength(1, GridUnitType.Star);

        var accent = new Border
        {
            Height = 3,
            CornerRadius = new CornerRadius(2),
            Background = Brush(accentHex),
            Opacity = 0.8,
            Margin = new Thickness(0, 0, 0, 10),
        };
        grid.Children.Add(accent);

        var headGrid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var nameBlock = new TextBlock
        {
            Text = pool.Name,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var totalBlock = new TextBlock
        {
            Text = total.ToString(),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            Margin = new Thickness(8, 0, 0, 0),
        };
        Grid.SetColumn(totalBlock, 1);
        headGrid.Children.Add(nameBlock);
        headGrid.Children.Add(totalBlock);
        Grid.SetRow(headGrid, 1);
        grid.Children.Add(headGrid);

        var statPanel = new StackPanel { Margin = new Thickness(0, 3, 0, 12) };
        statPanel.Children.Add(new TextBlock
        {
            Text = $"{Short(start)} - {Short(end)}",
            FontSize = 10.5,
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            Margin = new Thickness(0, 0, 0, 8),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (col.Activity)
        {
            statPanel.Children.Add(StatRowHost("5★ 平均", five.Count > 0 ? avgFive.ToString("0.00") : "—", "#E8A33D"));
            statPanel.Children.Add(StatRowHost("5★ 统计", $"{cFive2}  [{Pct(cFive2)}]", "#E8A33D"));
            statPanel.Children.Add(StatRowHost("4★ 统计", $"{cFour}  [{Pct(cFour)}]", "#C864E0"));
            statPanel.Children.Add(StatRowHost("3★ 统计", $"{cThree}  [{Pct(cThree)}]", "#5EAAED"));
        }
        else
        {
            statPanel.Children.Add(StatRowHost("4★ 平均", four.Count > 0 ? avgFour.ToString("0.00") : "—", "#C864E0"));
            statPanel.Children.Add(StatRowHost("4★ 统计", $"{cFour}  [{Pct(cFour)}]", "#C864E0"));
            statPanel.Children.Add(StatRowHost("3★ 统计", $"{cThree}  [{Pct(cThree)}]", "#5EAAED"));
            statPanel.Children.Add(StatRowHost("2★ 统计", $"{cTwo}  [{Pct(cTwo)}]", "#3BA272"));
        }
        Grid.SetRow(statPanel, 2);
        grid.Children.Add(statPanel);

        var tabs = new Grid { Height = 30, Margin = new Thickness(0, 0, 0, 9) };
        tabs.ColumnDefinitions.Add(new ColumnDefinition());
        tabs.ColumnDefinitions.Add(new ColumnDefinition());
        var pillBg = new Border { CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1) };
        pillBg.SetResourceReference(Border.BackgroundProperty, "InputBg");
        pillBg.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
        Grid.SetColumnSpan(pillBg, 2);
        tabs.Children.Add(pillBg);

        col.TabFive = MakePillButton(col.Activity ? "5★" : "4★");
        col.TabFive.Margin = new Thickness(2);
        col.TabFive.Click += (_, _) => SwitchBeyondTab(col, true);
        Grid.SetColumn(col.TabFive, 0);

        col.TabFour = MakePillButton(col.Activity ? "4★" : "3★");
        col.TabFour.Margin = new Thickness(2);
        col.TabFour.Click += (_, _) => SwitchBeyondTab(col, false);
        Grid.SetColumn(col.TabFour, 1);

        tabs.Children.Add(col.TabFive);
        tabs.Children.Add(col.TabFour);
        Grid.SetRow(tabs, 3);
        grid.Children.Add(tabs);

        var listScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, -6, 0),
            Padding = new Thickness(0, 0, 6, 0),
        };
        listScroll.Content = col.ListPanel;
        var listHost = new Grid { MinHeight = 220 };
        listHost.Children.Add(listScroll);
        Grid.SetRow(listHost, 4);
        grid.Children.Add(listHost);

        col.EmptyHint = new TextBlock
        {
            Text = "暂无记录",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 30, 0, 0),
        };
        listHost.Children.Add(col.EmptyHint);

        root.Child = grid;
        col.Root = root;
        SwitchBeyondTab(col, true);
        return col;
    }

    private static string ColorForFour(int pity)
        => pity >= 10 ? "#C0453F" : pity >= 8 ? "#D08A3E" : "#3F9B6B";

    private void SwitchBeyondTab(BeyondColUi col, bool first)
    {
        col.ShowFour = first;
        var accent = (Brush)Application.Current.Resources["Accent"];
        var secondary = (Brush)Application.Current.Resources["TextSecondary"];
        var cardBg = (Brush)Application.Current.Resources["CardBg"];

        col.TabFive.Background = first ? cardBg : Brushes.Transparent;
        col.TabFour.Background = first ? Brushes.Transparent : cardBg;
        col.TabFive.Foreground = first ? accent : secondary;
        col.TabFour.Foreground = first ? secondary : accent;
        col.TabFive.FontWeight = first ? FontWeights.SemiBold : FontWeights.Normal;
        col.TabFour.FontWeight = first ? FontWeights.Normal : FontWeights.SemiBold;
        RenderBeyondRows(col);
    }

    private void RenderBeyondRows(BeyondColUi col)
    {
        col.ListPanel.Children.Clear();
        var rows = col.Activity
            ? (col.ShowFour ? col.FiveRows : col.FourRows)
            : (col.ShowFour ? col.FourRows : col.ThreeRows);
        var starLabel = col.Activity
            ? (col.ShowFour ? "5★" : "4★")
            : (col.ShowFour ? "4★" : "3★");
        col.EmptyHint.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        col.EmptyHint.Text = $"暂无 {starLabel} 记录";

        // 已垫（常驻：距上次 4★；活动：距上次 5★）
        var pityRow = new Grid { Margin = new Thickness(0, 0, 0, 5), Height = 30 };
        pityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var bg = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = Brush(col.Activity ? ColorFor(col.Pending) : ColorForFour(col.Pending)),
            Opacity = 0.92,
        };
        Grid.SetColumnSpan(bg, 2);
        pityRow.Children.Add(bg);
        var label = new TextBlock
        {
            Text = "已垫",
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#FFFFFF"),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var val = new TextBlock
        {
            Text = col.Pending.ToString(),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#FFFFFF"),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(val, 1);
        pityRow.Children.Add(label);
        pityRow.Children.Add(val);
        col.ListPanel.Children.Add(pityRow);

        foreach (var row in rows)
            col.ListPanel.Children.Add(BuildBeyondRow(row));
    }

    private Border BuildBeyondRow(RowVm row)
    {
        var rank = row.Item.RankType;
        var rankColor = BeyondRankColor.TryGetValue(rank, out var rc) ? rc : "#7C93B0";

        var g = new Grid { Margin = new Thickness(0, 0, 0, 4), Height = 32 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var rowBg = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = Brush(BeyondRankSoft.TryGetValue(rank, out var soft) ? soft : "#00000000"),
        };
        Grid.SetColumnSpan(rowBg, 3);
        g.Children.Add(rowBg);

        if (ChkShowIcon.IsChecked == true)
        {
            var name = row.Item.Name;
            var badge = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(6, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new LinearGradientBrush(
                    (Color)ColorConverter.ConvertFromString(rankColor),
                    (Color)ColorConverter.ConvertFromString("#FFFFFF"),
                    new Point(0, 0), new Point(0, 1)),
                Opacity = 0.92,
                ToolTip = name,
            };
            badge.Child = new TextBlock
            {
                Text = name.Length > 0 ? name[..1] : "?",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Brush("#FFFFFF"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            g.Children.Add(badge);
        }

        var nameBlock = new TextBlock
        {
            Text = row.Item.Name,
            FontSize = 12.5,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = $"{row.Item.Name} · {row.Item.Time}",
            Margin = new Thickness(ChkShowIcon.IsChecked == true ? 0 : 10, 0, 6, 0),
        };
        Grid.SetColumn(nameBlock, 1);
        g.Children.Add(nameBlock);

        var pityBlock = new TextBlock
        {
            Text = row.Pity.ToString(),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush(rank == "5" ? ColorFor(row.Pity) : ColorForFour(row.Pity)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        Grid.SetColumn(pityBlock, 2);
        g.Children.Add(pityBlock);

        return new Border { Child = g, Background = Brushes.Transparent };
    }

    private void BeyondScroll_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyBeyondWidth();

    private void ApplyBeyondWidth()
    {
        if (_beyondColumns.Count == 0) return;
        var avail = BeyondScroll.ActualWidth - 4;
        if (avail <= 0) return;
        var gap = 10.0;
        var w = (avail - gap * (_beyondColumns.Count - 1)) / _beyondColumns.Count;
        w = Math.Clamp(w, 300, 620);
        foreach (var c in _beyondColumns) c.Card.Width = w;
    }

    private Button MakePillButton(string text)
    {
        var b = new Button
        {
            Content = text,
            FontSize = 12,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0, 4, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        AutomationProperties.SetName(b, text);
        b.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextSecondary");
        return b;
    }

    private void SwitchTab(ColumnUi col, bool five)
    {
        col.ShowFive = five;
        var accent = (Brush)Application.Current.Resources["Accent"];
        var secondary = (Brush)Application.Current.Resources["TextSecondary"];
        var cardBg = (Brush)Application.Current.Resources["CardBg"];

        col.TabFive.Background = five ? cardBg : Brushes.Transparent;
        col.TabFour.Background = five ? Brushes.Transparent : cardBg;
        col.TabFive.Foreground = five ? accent : secondary;
        col.TabFour.Foreground = five ? secondary : accent;
        col.TabFive.FontWeight = five ? FontWeights.SemiBold : FontWeights.Normal;
        col.TabFour.FontWeight = five ? FontWeights.Normal : FontWeights.SemiBold;
        col.TabFour.Visibility = ChkShow4.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        RenderRows(col);
    }

    private void ShowOption_Changed(object sender, RoutedEventArgs e)
    {
        foreach (var col in _columns)
        {
            col.TabFour.Visibility = ChkShow4.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            if (ChkShow4.IsChecked != true && !col.ShowFive) SwitchTab(col, true);
            else RenderRows(col);
        }
    }

    private void RenderRows(ColumnUi col)
    {
        col.ListPanel.Children.Clear();
        var rows = col.ShowFive ? col.FiveRows : col.FourRows;
        col.EmptyHint.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        col.EmptyHint.Text = col.ShowFive && col.Pending > 0 ? "暂无 5★ 记录" : "暂无记录";

        if (col.ShowFive && col.Total > 0)
        {
            var pityRow = new Grid { Margin = new Thickness(0, 0, 0, 6), Height = 28 };
            pityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var bg = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = Brush(ColorFor(col.Pending)),
                Opacity = 0.9,
            };
            Grid.SetColumnSpan(bg, 2);
            pityRow.Children.Add(bg);

            var label = new TextBlock
            {
                Text = "已垫",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#FFFFFF"),
                Margin = new Thickness(9, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var val = new TextBlock
            {
                Text = col.Pending.ToString(),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#FFFFFF"),
                Margin = new Thickness(0, 0, 9, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(val, 1);
            pityRow.Children.Add(label);
            pityRow.Children.Add(val);
            col.ListPanel.Children.Add(pityRow);
        }

        foreach (var row in rows)
            col.ListPanel.Children.Add(BuildRow(row));
    }

    /// <summary>保底色阶：<74 绿、74-89 橙、>=90 红（对应原神/星铁 5★ 保底区间）</summary>
    private static string ColorFor(int pity)
        => pity >= 90 ? "#C0453F" : pity >= 74 ? "#D08A3E" : "#3F9B6B";

    private Border BuildRow(RowVm row)
    {
        var rank = row.Item.RankType;
        var rankColor = RankColor.TryGetValue(rank, out var rc) ? rc : "#7C93B0";

        var g = new Grid { Margin = new Thickness(0, 0, 0, 4), Height = 32 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var rowBg = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = Brush(RankSoft.TryGetValue(rank, out var soft) ? soft : "#00000000"),
        };
        Grid.SetColumnSpan(rowBg, 3);
        g.Children.Add(rowBg);

        if (ChkShowIcon.IsChecked == true)
        {
            var name = row.Item.Name;
            var badge = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(7),
                Margin = new Thickness(5, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new LinearGradientBrush(
                    (Color)ColorConverter.ConvertFromString(rankColor),
                    (Color)ColorConverter.ConvertFromString("#FFFFFF"),
                    new Point(0, 0), new Point(0, 1)),
                Opacity = 0.92,
                ToolTip = name,
            };
            badge.Child = new TextBlock
            {
                Text = name.Length > 0 ? name[..1] : "?",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = Brush("#FFFFFF"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            g.Children.Add(badge);
        }

        var nameBlock = new TextBlock
        {
            Text = row.Item.Name,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = $"{row.Item.Name} · {row.Item.Time}",
            Margin = new Thickness(ChkShowIcon.IsChecked == true ? 0 : 8, 0, 6, 0),
        };
        Grid.SetColumn(nameBlock, 1);
        g.Children.Add(nameBlock);

        var pityBlock = new TextBlock
        {
            Text = row.Pity.ToString(),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush(ColorFor(row.Pity)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 0),
        };
        Grid.SetColumn(pityBlock, 2);
        g.Children.Add(pityBlock);

        return new Border { Child = g, Background = Brushes.Transparent };
    }

    // ================= 拉取 / 导入导出 =================

    private GameDef GameDef => GameLocator.Get(_gameKey);

    private string? GameDirectory()
    {
        var saved = SettingsService.Settings.GamePaths.TryGetValue(_gameKey, out var p) ? p : null;
        if (!string.IsNullOrEmpty(saved) && Directory.Exists(saved)) return saved;
        return GameLocator.DetectInstallDir(GameDef);
    }

    private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
    {
        var dir = GameDirectory();
        if (dir == null)
        {
            SetStatus("未找到游戏安装目录：请在「▾ - 选择游戏安装目录」里指定游戏根目录");
            BtnMore.ContextMenu!.IsOpen = true;
            return;
        }

        if (_beyondMode)
        {
            string? beyondErr;
            var beyondUrl = GameLocator.FindBeyondUrl(dir, GameDef, out beyondErr);
            if (string.IsNullOrEmpty(beyondUrl))
            {
                SetStatus(string.IsNullOrEmpty(beyondErr)
                    ? "游戏缓存里还没有颂愿链接：请先在游戏内打开一次「千星奇域 - 颂愿记录」页面，再回来点「更新记录」"
                    : beyondErr);
                return;
            }
            await FetchAsync(GachaService.ToBeyondApiUrl(beyondUrl));
            return;
        }

        string? err;
        var url = GameLocator.FindGachaUrl(dir, GameDef, out err);
        if (string.IsNullOrEmpty(url))
        {
            SetStatus(string.IsNullOrEmpty(err)
                ? "游戏缓存中还没有抽卡链接：请先启动游戏，在游戏内打开一次「抽卡记录」页面，再回来点「更新记录」"
                : err);
            return;
        }
        await FetchAsync(GachaService.ToApiUrl(url, _gameKey));
    }

    private async Task FetchAsync(string apiUrl)
    {
        BtnUpdate.IsEnabled = false;
        BtnUpdate.Content = "更新中…";
        var storeKey = _beyondMode ? BeyondStoreKey : _gameKey;
        try
        {
            var (ok, msg, items) = _beyondMode
                ? await GachaService.FetchBeyondAsync(apiUrl)
                : await GachaService.FetchAllAsync(apiUrl, _gameKey);
            if (!ok || items.Count == 0)
            {
                SetStatus(items.Count == 0 ? msg : $"{msg}（已保留本地库记录）");
                return;
            }
            foreach (var it in items)
            {
                it.GameKey = storeKey;
                it.PoolName = _beyondMode
                    ? GachaService.BeyondPoolName(it.GachaType)
                    : GachaService.PoolName(_gameKey, it.GachaType);
            }
            var added = GachaStore.Merge(items);
            RefreshAll();
            SetStatus(added > 0
                ? $"更新完成：拉到 {items.Count} 条，新增 {added} 条"
                : $"更新完成：拉到 {items.Count} 条，没有新记录");
        }
        catch (Exception ex)
        {
            SetStatus("更新失败：" + ex.Message);
        }
        finally
        {
            BtnUpdate.IsEnabled = true;
            BtnUpdate.Content = "更新记录";
        }
    }

    private void MenuUpdateCache_Click(object sender, RoutedEventArgs e) => BtnUpdate_Click(sender, e);

    private async void MenuUpdateAccount_Click(object sender, RoutedEventArgs e)
    {
        var acc = AccountService.Active;
        if (acc == null || string.IsNullOrEmpty(acc.Cookie))
        {
            SetStatus("没有已登录账号：请先在左侧「账号」页登录米游社账号");
            return;
        }
        var biz = _gameKey switch
        {
            "genshin" => "hk4e_cn",
            "starrail" => "hkrpg_cn",
            "zzz" => "nap_cn",
            _ => "bh3_cn",
        };
        BtnUpdate.IsEnabled = false;
        BtnUpdate.Content = "获取中…";
        try
        {
            var (roles, err) = await SignService.GetRolesAsync(acc.Cookie, biz);
            if (roles.Count == 0)
            {
                SetStatus($"该账号下没有{GameLabels[Array.IndexOf(GameKeys, _gameKey)]}角色：{err}");
                return;
            }
            var role = roles[0];
            var (ok, msg, url) = await GachaService.GenAuthKeyAsync(acc.Cookie, biz, role.Region, role.Uid);
            if (!ok || url == null)
            {
                SetStatus($"从账号生成 authkey 失败：{msg}。可改用游戏缓存方式（先在游戏内打开抽卡记录页）");
                return;
            }
            await FetchAsync(url);
        }
        finally
        {
            BtnUpdate.IsEnabled = true;
            BtnUpdate.Content = "更新记录";
        }
    }

    private async void MenuPasteUrl_Click(object sender, RoutedEventArgs e)
    {
        var text = Prompt("粘贴抽卡记录链接", "把游戏里「抽卡记录」页面的 URL（或带 authkey 的链接）粘贴进来：");
        if (string.IsNullOrWhiteSpace(text)) return;
        var clean = text.Trim();
        if (GachaService.IsBeyondUrl(clean))
        {
            if (!_beyondMode)
            {
                _beyondMode = true;
                BtnBeyond.Content = "返回祈愿记录";
                ApplyModeLayout();
                RefreshAll();
            }
            await FetchAsync(GachaService.ToBeyondApiUrl(clean));
            return;
        }
        await FetchAsync(GachaService.ToApiUrl(clean, _gameKey));
    }

    private void MenuPickFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = $"选择 {GameLabels[Array.IndexOf(GameKeys, _gameKey)]} 的安装目录",
        };
        var dir = GameDirectory();
        if (dir != null) dlg.InitialDirectory = dir;
        if (dlg.ShowDialog() != true) return;
        SettingsService.Settings.GamePaths[_gameKey] = dlg.FolderName;
        SettingsService.Save();
        SetStatus("已保存游戏目录：" + dlg.FolderName);
    }

    private void MenuImport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入 UIGF / SRGF 文件",
            Filter = "抽卡记录 (*.json)|*.json|所有文件 (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        var (ok, msg, total, added) = GachaStore.ImportFile(dlg.FileName, _gameKey);
        if (!ok)
        {
            SetStatus(msg);
            return;
        }
        foreach (var it in GachaStore.Query(null, _gameKey).Where(x => string.IsNullOrEmpty(x.PoolName)))
            it.PoolName = GachaService.PoolName(_gameKey, it.GachaType);
        RefreshAll();
        SetStatus($"{msg}，新增 {added} 条");
    }

    private void ExportUigf_Click(object sender, RoutedEventArgs e) => Export("uigf");
    private void ExportSrgf_Click(object sender, RoutedEventArgs e) => Export("srgf");
    private void ExportCsv_Click(object sender, RoutedEventArgs e) => Export("csv");

    private void Export(string kind)
    {
        var uid = SelectedUid;
        if (uid == null)
        {
            var uids = GachaStore.GetUids(_gameKey);
            if (uids.Count != 1)
            {
                SetStatus("导出前请先在右上角选择一个具体 UID");
                return;
            }
            uid = uids[0];
        }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出抽卡记录",
            FileName = $"mi-Nest_{_gameKey}_{uid}_{kind.ToUpperInvariant()}",
            Filter = kind switch
            {
                "uigf" => "UIGF 文件 (*.json)|*.json",
                "srgf" => "SRGF 文件 (*.json)|*.json",
                _ => "CSV 文件 (*.csv)|*.csv",
            },
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            switch (kind)
            {
                case "uigf": GachaStore.ExportUigf(uid, _gameKey, dlg.FileName); break;
                case "srgf": GachaStore.ExportSrgf(uid, _gameKey, dlg.FileName); break;
                default: GachaStore.ExportCsv(uid, _gameKey, dlg.FileName); break;
            }
            SetStatus("已导出：" + dlg.FileName);
        }
        catch (Exception ex)
        {
            SetStatus("导出失败：" + ex.Message);
        }
    }

    private void ClearUid_Click(object sender, RoutedEventArgs e)
    {
        var uid = SelectedUid;
        if (uid == null)
        {
            SetStatus("清空前请先选择一个具体 UID");
            return;
        }
        if (MessageBox.Show($"确定清空 UID {uid} 的本地抽卡记录？此操作不可恢复。", "mi-Nest",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        GachaStore.Clear(uid, StoreKey);
        RefreshAll();
        SetStatus($"已清空 UID {uid} 的本地记录");
    }

    private void OpenDataDir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppPaths.DataDir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            SetStatus("打开目录失败：" + ex.Message);
        }
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        PanelSettings.Visibility = PanelSettings.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void BtnMore_Click(object sender, RoutedEventArgs e)
    {
        var btn = (Button)sender;
        var menu = btn.ContextMenu!;
        menu.PlacementTarget = btn;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void SetStatus(string text) => TxtStatus.Text = $"{DateTime.Now:HH:mm:ss}  {text}";

    /// <summary>简易文本输入对话框（粘贴链接用）</summary>
    private static string? Prompt(string title, string hint)
    {
        var win = new Window
        {
            Title = title,
            Width = 560,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)Application.Current.Resources["CardBg"],
            Owner = Application.Current.MainWindow,
        };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = hint,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
        });
        var box = new TextBox { Margin = new Thickness(0, 10, 0, 12), Height = 30 };
        panel.Children.Add(box);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        string? result = null;
        var okBtn = new Button { Content = "确定", Width = 84, Height = 30, IsDefault = true };
        okBtn.Click += (_, _) => { result = box.Text; win.Close(); };
        var cancelBtn = new Button { Content = "取消", Width = 84, Height = 30, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        buttons.Children.Add(okBtn);
        buttons.Children.Add(cancelBtn);
        panel.Children.Add(buttons);
        win.Content = panel;
        win.ShowDialog();
        return result;
    }
}
