using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MiNest.Services;

namespace MiNest.Pages;

public partial class DailyNotePage : UserControl
{
    private readonly MainWindow _main;
    private bool _loading;

    public DailyNotePage(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Loaded += async (_, _) => { LoadAccounts(); await RefreshAsync(); };
    }

    private void LoadAccounts()
    {
        CmbAccount.ItemsSource = null;
        CmbAccount.Items.Clear();
        var accounts = AccountService.All;
        if (accounts.Count == 0)
        {
            TxtHint.Text = "尚未登录账号，请先到「账号」页面登录";
            return;
        }
        CmbAccount.ItemsSource = accounts;
        CmbAccount.SelectedItem = accounts.FirstOrDefault(a => a.IsActive) ?? accounts[0];
        TxtHint.Text = "";
    }

    private void CmbAccount_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CmbAccount.SelectedItem == null) return;
        _ = RefreshAsync();
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private async System.Threading.Tasks.Task RefreshAsync()
    {
        if (CmbAccount.SelectedItem is not Models.Account acc) return;
        _loading = true;
        TxtHint.Text = "正在获取便笺…";
        NoteList.ItemsSource = null;

        var cookie = acc.Cookie;
        var cards = new List<NoteCard>();
        foreach (var game in DailyNoteService.Games)
        {
            var roles = await DailyNoteService.GetRolesAsync(cookie, game.Biz);
            foreach (var (uid, nickname, region, level) in roles)
            {
                var note = await DailyNoteService.GetNoteAsync(cookie, game, uid, region);
                cards.Add(new NoteCard(game, note, nickname, level));
            }
        }
        NoteList.ItemsSource = cards;
        TxtHint.Text = cards.Count == 0 ? "未查询到已绑定游戏角色" : $"共 {cards.Count} 个角色";
        _loading = false;
    }
}

/// <summary>便笺卡片视图模型</summary>
public class NoteCard
{
    public string GameName { get; }
    public int Current { get; }
    public int Max { get; }
    public string ResinText { get; }
    public string RecoveryText { get; }
    public string ExpeditionText { get; }
    public string DailyText { get; }
    public string Extra { get; }
    public string Extra2 { get; }
    public string NicknameLine { get; }
    public string StatusText { get; }
    public Brush StatusBrush { get; }
    public Brush BarBrush { get; }

    public NoteCard(DailyNoteService.GameBiz game, DailyNoteService.Note n, string nickname, string level)
    {
        GameName = game.Name;
        Current = n.Current;
        Max = n.Max;
        NicknameLine = string.IsNullOrEmpty(nickname) ? $"UID {n.Uid}（{n.Region}）" : $"{nickname} · UID {n.Uid}（Lv.{level}）";
        ResinText = $"{n.Current} / {n.Max}";

        if (!n.Ok)
        {
            StatusText = "获取失败";
            StatusBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x3D));
            BarBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            RecoveryText = n.Error;
            ExpeditionText = "";
            DailyText = "";
            Extra = "";
            Extra2 = "";
            return;
        }

        var full = n.Current >= n.Max && n.Max > 0;
        StatusText = full ? "已满" : "恢复中";
        StatusBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
        BarBrush = full
            ? new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A))
            : new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5));

        if (n.RecoverSeconds > 0)
        {
            var t = TimeSpan.FromSeconds(n.RecoverSeconds);
            RecoveryText = $"约 {t.Hours} 小时 {t.Minutes} 分钟回满";
        }
        else
        {
            RecoveryText = full ? "体力已满" : "恢复中";
        }

        ExpeditionText = n.ExpeditionMax > 0
            ? $"派遣 {n.ExpeditionNow} / {n.ExpeditionMax}"
            : "";
        DailyText = n.DailyDoneUnknown
            ? ""
            : n.DailyDone ? "✓ 每日任务已完成" : "✗ 每日任务未完成";
        Extra = n.Extra;
        Extra2 = n.Extra2;
    }
}
