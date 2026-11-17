using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MiNest.Models;
using MiNest.Services;

namespace MiNest.Pages;

/// <summary>
/// 抽卡记录（学习 HoYo.Gacha）：本地持久化库 + 多账号/多游戏管理 + UIGF/SRGF/CSV 导入导出 + 统计卡片 + 卡池筛选。
/// 拉取 / 导入的记录都会合并进本地库（gacha_records.json），重启不丢失。
/// </summary>
public partial class GachaPage : UserControl
{
    private readonly List<GachaItem> _items = new();
    private string _gameKey = "genshin";
    private string? _baseUrl;
    private bool _subscribed;

    private static readonly string[] PoolFilters = { "全部", "角色", "武器", "常驻", "新手", "其他" };

    // 抽卡记录仅支持这四款游戏（UIGF 通用）
    private static readonly Dictionary<string, string> GameBiz = new()
    {
        ["genshin"] = "hk4e_cn",
        ["starrail"] = "hkrpg_cn",
        ["zzz"] = "nap_cn",
        ["bh3"] = "bh3_cn",
    };

    public GachaPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ComboGame.ItemsSource = GameLocator.Games.Select(g => g.Name).ToList();
        ComboGame.SelectedIndex = 0;
        ComboPool.ItemsSource = PoolFilters;
        ComboPool.SelectedIndex = 0;
        RefreshUidList();
        if (!_subscribed)
        {
            _subscribed = true;
            AccountService.Changed += () => Dispatcher.Invoke(async () => await RefreshBoundGamesAsync());
        }
        _ = RefreshBoundGamesAsync();
        TxtStatus.Text = "提示：可先登录游戏并在游戏内打开一次「抽卡记录」页面后点「自动获取」；也可登录米游社账号后点「从登录账号获取」。拉取/导入的记录会持久化到本地库，重启不丢失。";
    }

    /// <summary>UID 下拉：全部 + 本地库该游戏下所有 UID</summary>
    private void RefreshUidList()
    {
        var prev = ComboUid.SelectedItem as string;
        var uids = GachaStore.GetUids(_gameKey);
        var list = new List<string> { "全部" };
        list.AddRange(uids);
        ComboUid.ItemsSource = list;
        ComboUid.SelectedIndex = Math.Max(0, list.IndexOf(prev ?? "全部"));
    }

    /// <summary>按当前筛选（游戏 + UID + 卡池）从本地库载入并刷新列表/统计</summary>
    private void ReloadFromStore(bool resetStatus = true)
    {
        var uid = ComboUid.SelectedItem as string;
        var uidFilter = string.IsNullOrEmpty(uid) || uid == "全部" ? null : uid;
        var all = GachaStore.Query(uidFilter, _gameKey);
        _items.Clear();
        _items.AddRange(all);
        TxtLocalTotal.Text = $"{GachaStore.Query(null, _gameKey).Count} 条";
        ApplyFilter();
        if (all.Count == 0 && resetStatus)
            TxtStatus.Text = "本地库暂无记录。点「自动获取」或「从登录账号获取」拉取，或「导入 UIGF/SRGF」导入已有记录。";
    }

    private void ComboUid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComboUid.SelectedIndex < 0) return;
        ReloadFromStore();
    }

    /// <summary>登录账号后自动识别该账号绑定的米哈游游戏</summary>
    private async Task RefreshBoundGamesAsync()
    {
        var acc = AccountService.Active;
        if (acc == null)
        {
            TxtStatus.Text = "未登录米游社账号。登录后此处会自动识别该账号绑定的游戏，可点「从登录账号获取」直接拉取抽卡记录。";
            BtnFromAccount.IsEnabled = false;
            return;
        }

        // 缺 stoken 时角色/签到接口一律返回登录失效，提前给出明确引导
        if (!acc.Cookie.Contains("stoken", StringComparison.OrdinalIgnoreCase))
        {
            BtnFromAccount.IsEnabled = false;
            TxtStatus.Text = $"已登录：{acc.DisplayName}，但登录态缺少 stoken（角色识别 / 抽卡 / 签到接口必需）。" +
                             "请到「账号」页用「扫码登录」或「Cookie 登录」重新登录后，此功能自动恢复。";
            return;
        }

        var bound = new List<string>();
        foreach (var g in GameLocator.Games)
        {
            if (!GameBiz.ContainsKey(g.Key)) continue;
            var (roles, _) = await SignService.GetRolesAsync(acc.Cookie, GameBiz[g.Key]);
            if (roles.Count > 0) bound.Add(g.Name);
        }

        var idx = bound.Count > 0
            ? Math.Max(0, GameLocator.Games.ToList().FindIndex(x => bound.Contains(x.Name)))
            : ComboGame.SelectedIndex;
        if (idx >= 0 && idx < ComboGame.Items.Count)
            ComboGame.SelectedIndex = idx;

        BtnFromAccount.IsEnabled = true;
        if (bound.Count == 0)
            TxtStatus.Text = $"已登录：{acc.DisplayName}。该账号暂未绑定任何可查询的米哈游游戏角色。";
        else
            TxtStatus.Text = $"已登录：{acc.DisplayName}。识别到绑定游戏：{string.Join("、", bound)}，可点「从登录账号获取」直接拉取。";
    }

    private async void BtnFromAccount_Click(object sender, RoutedEventArgs e)
    {
        var acc = AccountService.Active;
        if (acc == null)
        {
            TxtStatus.Text = "未登录米游社账号，请先到「账号」页登录。";
            return;
        }
        if (!GameBiz.TryGetValue(_gameKey, out var biz))
        {
            TxtStatus.Text = "该游戏暂不支持账号直连获取。";
            return;
        }
        if (!acc.Cookie.Contains("stoken", StringComparison.OrdinalIgnoreCase))
        {
            TxtStatus.Text = "当前账号登录态缺少 stoken，抽卡接口无法使用。请到「账号」页用「扫码登录 / Cookie 登录」重新登录。";
            return;
        }

        var (roles, err) = await SignService.GetRolesAsync(acc.Cookie, biz);
        if (err != null)
        {
            TxtStatus.Text = $"获取角色失败：{err}";
            return;
        }
        if (roles.Count == 0)
        {
            TxtStatus.Text = $"当前账号在「{GameLocator.Get(_gameKey).Name}」下未绑定角色。";
            return;
        }

        var role = roles[0];
        TxtStatus.Text = $"正在为 {role.Nickname}（{role.Uid}）生成 authkey…";
        try
        {
            var (ok, msg, url) = await GachaService.GenAuthKeyAsync(acc.Cookie, biz, role.Region, role.Uid);
            if (!ok || url == null)
            {
                TxtStatus.Text = msg.Contains("-502") || msg.Contains("Something went wrong")
                    ? "从登录账号获取暂不可用：米哈游对当前网络环境的 genAuthKey 接口做了风控（-502）。" +
                      "请改用「自动获取」：先在游戏内打开一次「抽卡记录」页面（authkey 仅约 5 分钟有效），再回本页点「自动获取」。"
                    : $"authkey 生成失败：{msg}";
                return;
            }
            await FetchAsync(url);
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"获取失败：{ex.Message}";
        }
    }

    private void ComboGame_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComboGame.SelectedIndex < 0) return;
        _gameKey = GameLocator.Games[ComboGame.SelectedIndex].Key;
        _baseUrl = null;
        RefreshUidList();
        ComboPool.SelectedIndex = 0;
        ReloadFromStore();
    }

    private void ComboPool_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComboPool.SelectedIndex < 0) return;
        ApplyFilter();
    }

    /// <summary>按卡池分类筛选并刷新列表/统计</summary>
    private void ApplyFilter()
    {
        var filter = ComboPool.SelectedItem as string ?? "全部";
        var filtered = filter == "全部"
            ? _items.ToList()
            : _items.Where(x => PoolCategory(x) == filter).ToList();
        Grid.ItemsSource = filtered;
        UpdateStats(filtered.Count);
        if (filter != "全部")
            TxtStatus.Text = $"已筛选：{filter}（{filtered.Count} 条 / 共 {_items.Count} 条）";
    }

    /// <summary>依据卡池名称判断分类（flyingmouse-format 同款口径）</summary>
    private string PoolCategory(GachaItem it)
    {
        var p = it.PoolName ?? "";
        if (p.Contains("新手")) return "新手";
        if (p.Contains("武器") || p.Contains("精准")) return "武器";
        if (p.Contains("角色") || p.Contains("限定") || p.Contains("UP")) return "角色";
        if (p.Contains("常驻") || p.Contains("标配")) return "常驻";
        return "其他";
    }

    private string? ResolveDir()
    {
        var g = GameLocator.Get(_gameKey);
        if (SettingsService.Settings.GamePaths.TryGetValue(_gameKey, out var p))
        {
            // 配置路径可能指错游戏（早期版本把原神目录填给了绝区零），校验数据目录
            if (Directory.Exists(Path.Combine(p, g.DataDir)) ||
                (g.Key == "genshin" && Directory.Exists(Path.Combine(p, "GenshinImpact_Data"))))
                return p;
        }

        var detected = GameLocator.DetectInstallDir(g);
        if (detected != null)
        {
            SettingsService.Settings.GamePaths[_gameKey] = detected;
            SettingsService.Save();
        }
        return detected;
    }

    private async void BtnAutoFetch_Click(object sender, RoutedEventArgs e)
    {
        var dir = ResolveDir();
        if (dir == null)
        {
            TxtStatus.Text = "未定位该游戏，请先点击「选择游戏目录」指定游戏安装目录。";
            return;
        }

        var g = GameLocator.Get(_gameKey);
        var rawUrl = GameLocator.FindGachaUrl(dir, g);
        if (string.IsNullOrEmpty(rawUrl))
        {
            TxtStatus.Text = "未在缓存中找到抽卡链接。请先在游戏内打开一次「抽卡记录」页面后重试，\n" +
                             "或点击「粘贴链接」手动输入（搜索目录：" + Path.Combine(dir, g.DataDir, "webCaches") + "）。";
            return;
        }
        await FetchAsync(GachaService.ToApiUrl(rawUrl, _gameKey));
    }

    private async void BtnPickFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = $"选择「{GameLocator.Get(_gameKey).Name}」安装目录（含 {GameLocator.Get(_gameKey).DataDir} 的文件夹）",
            UseDescriptionForTitle = true,
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        SettingsService.Settings.GamePaths[_gameKey] = dlg.SelectedPath;
        SettingsService.Save();

        var g = GameLocator.Get(_gameKey);
        var rawUrl = GameLocator.FindGachaUrl(dlg.SelectedPath, g);
        if (string.IsNullOrEmpty(rawUrl))
        {
            TxtStatus.Text = "该目录缓存中未找到抽卡链接，请先在游戏内打开「抽卡记录」页面后重试，或手动粘贴链接。";
            return;
        }
        await FetchAsync(GachaService.ToApiUrl(rawUrl, _gameKey));
    }

    private async void BtnPasteUrl_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new InputDialog("粘贴抽卡链接",
            "粘贴从游戏缓存或浏览器开发者工具中复制的 getGachaLog 链接（应包含 authkey 参数）：");
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Result))
            await FetchAsync(dlg.Result.Trim());
    }

    /// <summary>拉取 → 合并进本地库 → 刷新展示</summary>
    private async Task FetchAsync(string url)
    {
        _baseUrl = url;
        TxtStatus.Text = "正在拉取抽卡记录…";
        try
        {
            var (ok, msg, items) = await GachaService.FetchAllAsync(url, _gameKey);
            foreach (var it in items)
            {
                it.GameKey = _gameKey;
                it.PoolName = GachaService.PoolName(_gameKey, it.GachaType);
            }

            if (!ok)
            {
                TxtStatus.Text = $"拉取中断：{msg}（已拉取部分也会合并进本地库）";
            }

            var added = GachaStore.Merge(items);
            // 拉取后 UID 下拉可能新增（首次拉取该账号时），刷新并默认选中该 UID
            RefreshUidList();
            ReloadFromStore(resetStatus: false);

            var uid = items.FirstOrDefault()?.Uid;
            if (uid != null)
            {
                var idx = ComboUid.Items.IndexOf(uid);
                if (idx > 0) ComboUid.SelectedIndex = idx;
            }

            if (ok)
                TxtStatus.Text = $"拉取完成：{msg}；本地库新增 {added} 条（该游戏本地库共 {GachaStore.Query(null, _gameKey).Count} 条）。";
            // 失败时保留上面的"拉取中断"提示
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"拉取失败：{ex.Message}";
        }
    }

    /// <summary>导入 UIGF / SRGF 文件到本地库</summary>
    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.OpenFileDialog
        {
            Title = "导入抽卡记录（UIGF / SRGF）",
            Filter = "抽卡记录文件|*.json;*.json.gz|所有文件|*.*",
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        var (ok, msg, total, added) = GachaStore.ImportFile(dlg.FileName, _gameKey);
        if (!ok)
        {
            TxtStatus.Text = msg;
            return;
        }
        RefreshUidList();
        ReloadFromStore();
        TxtStatus.Text = $"{msg}，新增 {added} 条（导入目标：{GameLocator.Get(_gameKey).Name}）。";
    }

    private void BtnExportUigf_Click(object sender, RoutedEventArgs e)
    {
        ExportCore("UIGF", "*.json", "uigf", (path, uid, key) => GachaStore.ExportUigf(uid, key, path));
    }

    private void BtnExportSrgf_Click(object sender, RoutedEventArgs e)
    {
        ExportCore("SRGF", "*.json", "srgf", (path, uid, key) => GachaStore.ExportSrgf(uid, key, path));
    }

    private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
    {
        ExportCore("CSV", "*.csv", "csv", (path, uid, key) => GachaStore.ExportCsv(uid, key, path));
    }

    private void ExportCore(string fmt, string filter, string ext,
        Action<string, string, string> writer)
    {
        var uid = ComboUid.SelectedItem as string;
        if (string.IsNullOrEmpty(uid) || uid == "全部")
        {
            // 全部时按当前游戏导出所有 UID 的记录；文件名不含具体 UID
            var all = GachaStore.Query(null, _gameKey);
            if (all.Count == 0)
            {
                TxtStatus.Text = "没有可导出的记录";
                return;
            }
            using var dlg = new System.Windows.Forms.SaveFileDialog
            {
                Title = $"导出 {fmt} 抽卡记录",
                Filter = $"{fmt} 文件|{filter}",
                FileName = $"mi-Nest_{_gameKey}_{fmt.ToLower()}.{ext}",
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            var exportUid = all[0].Uid;
            writer(dlg.FileName, exportUid, _gameKey);
            TxtStatus.Text = $"已导出：{dlg.FileName}（共 {all.Count} 条，UID {exportUid}）";
            return;
        }

        var list = GachaStore.Query(uid, _gameKey);
        if (list.Count == 0)
        {
            TxtStatus.Text = "该 UID 下没有可导出的记录";
            return;
        }
        using var dlg2 = new System.Windows.Forms.SaveFileDialog
        {
            Title = $"导出 {fmt} 抽卡记录",
            Filter = $"{fmt} 文件|{filter}",
            FileName = $"mi-Nest_{_gameKey}_{uid}_{fmt.ToLower()}.{ext}",
        };
        if (dlg2.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        writer(dlg2.FileName, uid, _gameKey);
        TxtStatus.Text = $"已导出：{dlg2.FileName}（共 {list.Count} 条，UID {uid}）";
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        // 仅清空当前展示（不删本地库）；如确需删除本地库数据请手动处理 gacha_records.json
        _items.Clear();
        Grid.ItemsSource = null;
        UpdateStats();
        TxtStatus.Text = "已清空当前展示（本地库数据保留，重新进入页面会自动载入）。";
    }

    /// <summary>刷新顶部统计卡片（总抽数 / 五星 / 四星 / 三星 / 五星率 / 距上次五星）</summary>
    private void UpdateStats(int? total = null)
    {
        var filter = ComboPool.SelectedItem as string ?? "全部";
        var list = filter == "全部"
            ? _items.ToList()
            : _items.Where(x => PoolCategory(x) == filter).ToList();
        var n = total ?? list.Count;
        if (n == 0)
        {
            BuildStatCards(0, 0, 0, 0, 0, 0);
            return;
        }

        long five = list.Count(x => x.RankType == "5");
        long four = list.Count(x => x.RankType == "4");
        long three = list.Count(x => x.RankType == "3");

        // 当前未出五星抽数：列表按时间倒序（最新在前），从最新向前数到最近一个五星
        long pity = 0;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].RankType == "5") break;
            pity++;
        }

        var rate = n > 0 ? (double)five / n : 0;
        BuildStatCards(n, five, four, three, rate, pity);
    }

    private void BuildStatCards(long total, long five, long four, long three, double rate, long pity)
    {
        StatsPanel.Children.Clear();

        void Card(string title, string value, string sub, Brush color)
        {
            var border = new Border
            {
                Width = 122,
                Height = 82,
                Margin = new Thickness(0, 0, 12, 0),
                Background = (Brush)FindResource("CardBg"),
                BorderBrush = (Brush)FindResource("CardBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
            };
            // 左 4px 色条（星级色 / 主题色）+ 右侧内容
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var bar = new Border
            {
                Width = 5,
                Background = color,
                CornerRadius = new CornerRadius(5, 0, 0, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 8, 0, 8),
            };
            var sp = new StackPanel { Margin = new Thickness(12, 9, 8, 8) };
            sp.Children.Add(new TextBlock { Text = title, FontSize = 11.5, Foreground = color, Opacity = 0.9 });
            sp.Children.Add(new TextBlock
            {
                Text = value, FontSize = 21, FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("TextPrimary"), Margin = new Thickness(0, 1, 0, 0),
            });
            if (!string.IsNullOrEmpty(sub))
                sp.Children.Add(new TextBlock { Text = sub, FontSize = 10.5, Foreground = color, Opacity = 0.9, Margin = new Thickness(0, 1, 0, 0) });
            System.Windows.Controls.Grid.SetColumn(bar, 0);
            System.Windows.Controls.Grid.SetColumn(sp, 1);
            grid.Children.Add(bar);
            grid.Children.Add(sp);
            border.Child = grid;
            StatsPanel.Children.Add(border);
        }

        var fiveBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xB6, 0x5C));
        var fourBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0x8C, 0xEF));
        var threeBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x93, 0xA8));
        Card("总抽数", total.ToString("N0"), "", (Brush)FindResource("TextPrimary"));
        Card("五星", five.ToString("N0"), five == 0 ? "" : $"{rate:P1}", fiveBrush);
        Card("四星", four.ToString("N0"), "", fourBrush);
        Card("三星", three.ToString("N0"), "", threeBrush);
        Card("距上次五星", $"{pity:N0} 抽", pity >= 89 ? "已到保底范围" : "", fiveBrush);
    }
}
