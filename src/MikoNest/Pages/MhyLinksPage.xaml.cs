using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MiNest.Models;
using MiNest.Services;

namespace MiNest.Pages;

/// <summary>米哈游相关（官方 / 社区 / 第三方链接，全部软件内打开）：9 主入口 + 请愿 + 第三方工具子页（页内置顶）</summary>
public partial class MhyLinksPage : UserControl
{
    private readonly MainWindow _main;

    // 第三方工具：名称 / 一句话描述 / 链接（🔝 可置顶到本页最前面，不占用左侧项目栏）
    private static readonly (string Name, string Desc, string Url)[] ThirdPartyTools =
    {
        ("原神 SEELIE", "角色养成规划、体力安排", "https://seelie.me/"),
        ("星穹铁道 SEELIE", "角色养成规划、体力安排", "https://hsr.seelie.me/"),
        ("绝区零 SEELIE", "角色养成规划、体力安排", "https://zzz.seelie.me/"),
        ("椰羊 cocogoat", "圣遗物管理、成就扫描", "https://cocogoat.work"),
        ("KeqingMains", "原神角色强度研究（欧美硬核理论站）", "https://keqingmains.com/"),
        ("非小酋·原神", "抽卡记录统计", "https://feixiaoqiu.com"),
        ("非小酋·星铁", "抽卡记录统计", "https://feixiaoqiu.com/"),
        ("空荧酒馆地图", "玩家社区维护的地图工具", "https://yuanshen.site"),
        ("星穹智库", "跃迁记录、配队、遗器查询", "http://sr.appfeng.com"),
        ("原神伤害计算", "角色伤害模拟计算", "https://linchuangrong.github.io/GenshinCompute/"),
        ("原神中英日词典", "多语言名词查询", "https://genshin-dictionary.com/zh-CN"),
        ("米哈游版本文件列表", "原神、星铁、绝区零、崩坏3", "https://files.hk4e.com/hk4e/version-compare"),
    };

    public MhyLinksPage(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        BuildLinks();
        BuildTools();
        BtnPetition.Click += (_, _) => _main.OpenInApp("https://www.change.org/GenshinImpact");
    }

    private void BuildLinks()
    {
        var links = new (string name, string url)[]
        {
            ("AGC 平台", "https://agc.miyoushe.com/"),
            ("Anuneko", "https://anuneko.com"),
            ("人工桌面", "https://n0va.mihoyo.com/#/"),
            ("米游铺", "https://www.mihoyogift.com/"),
            ("HOYO-MiX（第三方）", "https://hoyomix.amarea.cn/"),
            ("米哈游支付中心", "https://pay.mihoyo.com/#/"),
            ("了解米哈游", "https://www.mihoyo.com/"),
            ("加入米哈游", "https://jobs.mihoyo.com/"),
            ("米哈游通行证", "https://user.miyoushe.com/index.html#/home/account-overview"),
        };
        var s = SettingsService.Settings;
        foreach (var (name, url) in links)
        {
            // 每个链接：主按钮铺满 + 右上角 📌 固定按钮
            var cell = new Grid();
            var main = new Button
            {
                Content = name,
                Tag = url,
                FontSize = 16,
                Margin = new Thickness(8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            main.Click += (_, _) => _main.OpenInApp(url);

            var pin = new Button
            {
                Content = "📌",
                FontSize = 14,
                Width = 34,
                Height = 30,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(6, 12, 14, 0),
                ToolTip = "固定到项目栏",
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            var existing = s.PinnedLinks.FirstOrDefault(p => p.Url == url);
            if (existing != null)
            {
                pin.Content = "📌✓";
                pin.Tag = existing.Key;
                pin.ToolTip = "已固定到项目栏，点击取消";
            }
            pin.Click += (_, _) => PinToggle(pin, name, url);

            cell.Children.Add(main);
            cell.Children.Add(pin);
            MainLinksGrid.Children.Add(cell);
        }
    }

    /// <summary>📌 固定 / 取消固定到左侧项目栏（固定后左侧导航直接出现，点击即打开）</summary>
    private void PinToggle(Button pin, string name, string url)
    {
        if (pin.Tag is string pinKey)
        {
            _main.UnpinLink(pinKey);
            pin.Tag = null;
            pin.Content = "📌";
            pin.ToolTip = "固定到项目栏";
            return;
        }
        // HOYO-MiX 固定到侧栏时使用短名
        if (url == "https://hoyomix.amarea.cn/") name = "HOYO-MiX";
        if (_main.PinLink(name, url))
        {
            var link = SettingsService.Settings.PinnedLinks.First(p => p.Url == url);
            pin.Tag = link.Key;
            pin.Content = "📌✓";
            pin.ToolTip = "已固定到项目栏，点击取消";
        }
    }

    // ---------- 第三方工具（直接展示在 9 个主入口下方） ----------

    private void BuildTools()
    {
        ToolsPanel.Children.Clear();
        var s = SettingsService.Settings;
        var pinned = s.ThirdPartyPinned ?? new System.Collections.Generic.List<string>();

        var sorted = ThirdPartyTools
            .OrderByDescending(t => pinned.Contains(t.Url))
            .ThenBy(t => Array.IndexOf(ThirdPartyTools, t))
            .ToList();

        foreach (var tool in sorted)
        {
            var cell = new Grid();
            var main = new Button
            {
                Tag = tool.Url,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Width = 248,
                Height = 58,
                Margin = new Thickness(6),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = System.Windows.Input.Cursors.Hand,
                Content = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = tool.Name,
                            FontWeight = FontWeights.SemiBold,
                            FontSize = 14,
                            TextAlignment = TextAlignment.Center,
                        },
                        new TextBlock
                        {
                            Text = tool.Desc,
                            FontSize = 11,
                            Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextSecondary"),
                            TextAlignment = TextAlignment.Center,
                            Margin = new Thickness(0, 3, 0, 0),
                        },
                    },
                },
            };
            main.Click += (_, _) => _main.OpenInApp(tool.Url);

            var pin = new Button
            {
                Content = "🔝",
                FontSize = 13,
                Width = 32,
                Height = 26,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 11, 10, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            var isPinned = pinned.Contains(tool.Url);
            pin.Content = isPinned ? "🔝✓" : "🔝";
            pin.ToolTip = isPinned ? "已置顶到本页最前面，点击取消" : "置顶到本页最前面";
            pin.Click += (_, _) => ToggleToolPin(pin, tool.Url);

            cell.Children.Add(main);
            cell.Children.Add(pin);
            ToolsPanel.Children.Add(cell);
        }
    }

    /// <summary>🔝 置顶 / 取消置顶（仅本页内排序，持久化到 settings，不占用左侧项目栏）</summary>
    private void ToggleToolPin(Button pin, string url)
    {
        var s = SettingsService.Settings;
        s.ThirdPartyPinned ??= new System.Collections.Generic.List<string>();
        if (s.ThirdPartyPinned.Contains(url))
            s.ThirdPartyPinned.Remove(url);
        else
            s.ThirdPartyPinned.Insert(0, url);
        SettingsService.Save();
        BuildTools();
    }
}
