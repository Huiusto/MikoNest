using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MiNest.Models;
using MiNest.Services;
using QRCoder;

namespace MiNest.Pages;

/// <summary>账号列表条目（显示名 + 是否当前账号 + 登录态是否完整）</summary>
public sealed record AccountEntry(Account Acc, string Display, bool IsActive, bool HasStoken);

/// <summary>
/// 账号管理（独立导航，仿 TeyvatGuide 登录）：
/// 扫码 / 验证码 / Cookie / 网页登录 / 浏览器导入，支持多账号管理，单击账号即快速切换。
/// </summary>
public partial class AccountPage : UserControl
{
    private readonly MainWindow _main;
    private string? _qrTicket;
    private DispatcherTimer? _qrTimer;
    private bool _subscribed;
    private int _qr502Count;

    public AccountPage(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            _subscribed = true;
            AccountService.Changed += () => Dispatcher.Invoke(RefreshList);
        }
        RefreshList();
        if (MainWindow.PendingAccountNotice != null)
        {
            WebLoginStatus.Text = MainWindow.PendingAccountNotice;
            MainWindow.PendingAccountNotice = null;
        }
    }

    private void RefreshList()
    {
        var items = AccountService.All
            .Select(a => new AccountEntry(a, a.DisplayName, ReferenceEquals(a, AccountService.Active),
                a.Cookie.Contains("stoken", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        AccountList.ItemsSource = null;
        AccountList.ItemsSource = items;
        AccountList.SelectedItem = items.FirstOrDefault(x => x.IsActive);
        BtnSetActive.IsEnabled = AccountService.Active != null;
        BtnDelete.IsEnabled = AccountList.SelectedItem != null;
        TxtAccountCount.Text = items.Count > 0 ? $"{items.Count} 个账号" : "尚未添加账号";
        // 登录态不完整（缺 stoken）的账号无法使用抽卡记录 / 签到，提示用户重新登录
        TxtStokenHint.Visibility = items.Any(x => !x.HasStoken) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AccountList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        BtnDelete.IsEnabled = AccountList.SelectedItem != null;
        // 单击即快速切换当前账号
        if (AccountList.SelectedItem is AccountEntry entry && !ReferenceEquals(entry.Acc, AccountService.Active))
        {
            AccountService.SetActive(entry.Acc);
            RefreshList();
        }
    }

    private void BtnSetActive_Click(object sender, RoutedEventArgs e)
    {
        if (AccountList.SelectedItem is AccountEntry entry)
        {
            AccountService.SetActive(entry.Acc);
            RefreshList();
        }
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (AccountList.SelectedItem is AccountEntry entry)
        {
            AccountService.Remove(entry.Acc);
            RefreshList();
        }
    }

    // ---------- 添加账号共用 ----------

    private async Task<bool> AddAccountByCookieAsync(string cookie, string? source)
    {
        var (ok, nickname, uid, msg, refreshed) = await AccountService.ValidateCookieAsync(cookie);
        if (!ok)
        {
            (source switch
            {
                "扫码" => QrStatus,
                "验证码" => SmsStatus,
                "Cookie" => CookieStatus,
                _ => ImportStatus,
            }).Text = $"Cookie 无效：{msg}";
            return false;
        }

        if (!string.IsNullOrEmpty(refreshed)) cookie = refreshed;
        AccountService.AddOrUpdate(new Account { Nickname = nickname, Uid = uid, Cookie = cookie });
        (source switch
        {
            "扫码" => QrStatus,
            "验证码" => SmsStatus,
            "Cookie" => CookieStatus,
            _ => ImportStatus,
        }).Text = $"已添加账号：{nickname}（{uid}）";
        RefreshList();
        return true;
    }

    // ---------- 扫码登录 ----------

    private async void BtnQrStart_Click(object sender, RoutedEventArgs e)
    {
        StopQrTimer();
        _qr502Count = 0;
        QrImage.Source = null;
        QrStatus.Text = "正在生成二维码…";

        var (ok, msg, payload, ticket) = await AccountService.CreateQrAsync();
        if (!ok || string.IsNullOrEmpty(payload))
        {
            QrStatus.Text = $"二维码生成失败：{msg}";
            return;
        }

        _qrTicket = ticket;
        QrImage.Source = QrToBitmapSource(payload);
        QrStatus.Text = "请用「米哈游通行证」App 扫码，并在手机上确认";
        BtnQrStart.Content = "刷新二维码";

        _qrTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _qrTimer.Tick += async (_, _) => await PollQrAsync();
        _qrTimer.Start();
    }

    private async Task PollQrAsync()
    {
        if (_qrTicket == null) return;
        var (ok, msg, cookie) = await AccountService.PollQrAsync(_qrTicket);

        if (!ok)
        {
            // 服务端风控（-502）：不立刻放弃，继续轮询；连续多次失败后提示改用网页登录
            if (msg.Contains("-502"))
            {
                _qr502Count++;
                QrStatus.Text = _qr502Count < 10
                    ? $"服务繁忙（-502），正在重试（{_qr502Count}）…"
                    : "当前网络被米游社风控，请改用上方「网页扫码登录」";
                if (_qr502Count >= 10)
                {
                    StopQrTimer();
                    QrImage.Source = null;
                    BtnQrStart.Content = "开始扫码";
                }
                return;
            }

            if (msg.Contains("等待") || msg.Contains("已扫码"))
            {
                QrStatus.Text = msg;
                return;
            }
            StopQrTimer();
            QrStatus.Text = msg;
            if (msg.Contains("过期"))
                QrImage.Source = null;
            return;
        }

        StopQrTimer();
        if (!string.IsNullOrEmpty(cookie))
            await AddAccountByCookieAsync(cookie, "扫码");
        else
            QrStatus.Text = "登录成功但未获取到 Cookie，请重试";
    }

    private void BtnQrCancel_Click(object sender, RoutedEventArgs e)
    {
        StopQrTimer();
        _qrTicket = null;
        QrImage.Source = null;
        QrStatus.Text = "已取消";
        BtnQrStart.Content = "开始扫码";
    }

    private void StopQrTimer()
    {
        _qrTimer?.Stop();
        _qrTimer = null;
    }

    private static BitmapSource QrToBitmapSource(string payload)
    {
        using var gen = new QRCodeGenerator();
        var data = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        using var qr = new QRCode(data);
        using var bmp = qr.GetGraphic(8);
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.StreamSource = ms;
        bi.EndInit();
        return bi;
    }

    // ---------- 验证码登录 ----------

    private async void BtnSendCode_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtMobile.Text))
        {
            SmsStatus.Text = "请先输入手机号";
            return;
        }
        BtnSendCode.IsEnabled = false;
        try
        {
            var (ok, msg, challenge) = await AccountService.SendSmsCodeDetailedAsync(TxtMobile.Text.Trim());
            if (!ok && challenge != null)
            {
                SmsStatus.Text = "正在等待完成滑块验证…";
                var aigis = await MiNest.Windows.SmsCaptchaWindow.ShowChallenge(Window.GetWindow(this), challenge);
                if (string.IsNullOrEmpty(aigis))
                {
                    SmsStatus.Text = "已取消滑块验证，验证码未发送";
                    return;
                }
                var (ok2, msg2, _) = await AccountService.SendSmsCodeDetailedAsync(TxtMobile.Text.Trim(), aigis);
                SmsStatus.Text = msg2;
                return;
            }
            SmsStatus.Text = msg;
        }
        finally
        {
            BtnSendCode.IsEnabled = true;
        }
    }

    private async void BtnSmsLogin_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtMobile.Text) || string.IsNullOrWhiteSpace(TxtSmsCode.Text))
        {
            SmsStatus.Text = "请输入手机号和验证码";
            return;
        }
        BtnSmsLogin.IsEnabled = false;
        try
        {
            var (ok, msg, cookie, challenge) = await AccountService.LoginBySmsDetailedAsync(TxtMobile.Text.Trim(), TxtSmsCode.Text.Trim());
            if (!ok && challenge != null)
            {
                SmsStatus.Text = "正在等待完成滑块验证…";
                var aigis = await MiNest.Windows.SmsCaptchaWindow.ShowChallenge(Window.GetWindow(this), challenge);
                if (string.IsNullOrEmpty(aigis))
                {
                    SmsStatus.Text = "已取消滑块验证，登录未完成";
                    return;
                }
                var (ok2, msg2, cookie2, _) = await AccountService.LoginBySmsDetailedAsync(TxtMobile.Text.Trim(), TxtSmsCode.Text.Trim(), aigis);
                if (!ok2)
                {
                    SmsStatus.Text = msg2;
                    return;
                }
                if (!string.IsNullOrEmpty(cookie2))
                    await AddAccountByCookieAsync(cookie2, "验证码");
                return;
            }
            if (!ok)
            {
                SmsStatus.Text = msg;
                return;
            }
            if (!string.IsNullOrEmpty(cookie))
                await AddAccountByCookieAsync(cookie, "验证码");
        }
        finally
        {
            BtnSmsLogin.IsEnabled = true;
        }
    }

    // ---------- Cookie 登录 ----------

    private async void BtnCookieAdd_Click(object sender, RoutedEventArgs e)
    {
        var cookie = TxtCookie.Text.Trim();
        if (string.IsNullOrEmpty(cookie))
        {
            CookieStatus.Text = "请粘贴 Cookie";
            return;
        }
        BtnCookieAdd.IsEnabled = false;
        try
        {
            await AddAccountByCookieAsync(cookie, "Cookie");
        }
        finally
        {
            BtnCookieAdd.IsEnabled = true;
        }
    }

    // ---------- 网页扫码登录（推荐兜底） ----------

    private async void BtnWebLogin_Click(object sender, RoutedEventArgs e)
    {
        if (_main.WebView.CoreWebView2 == null)
        {
            WebLoginStatus.Text = "内置浏览器尚未就绪，请先进入一次「社区 / 视频」页面再试。";
            return;
        }
        BtnWebLogin.IsEnabled = false;
        try
        {
            WebLoginStatus.Text = "正在打开官方登录页，请在页面内扫码或验证码登录…";
            await _main.OpenMiyousheLoginAsync();
        }
        finally
        {
            BtnWebLogin.IsEnabled = true;
        }
    }

    // ---------- 浏览器导入 ----------

    private async void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        if (_main.WebView.CoreWebView2 == null)
        {
            ImportStatus.Text = "内置浏览器尚未就绪，请先进入一次「社区 / 视频」页面。";
            return;
        }
        BtnImport.IsEnabled = false;
        try
        {
            var (ok, msg, cookie) = await AccountService.ImportFromWebViewAsync(_main.WebView.CoreWebView2.CookieManager);
            if (!ok)
            {
                ImportStatus.Text = msg;
                return;
            }
            if (string.IsNullOrEmpty(cookie))
            {
                ImportStatus.Text = "未在浏览器中找到登录态，请先在「社区 / 视频」页登录米游社。";
                return;
            }
            var (vok, nickname, uid, vmsg, vrefreshed) = await AccountService.ValidateCookieAsync(cookie);
            if (!vok)
            {
                ImportStatus.Text = $"Cookie 无效：{vmsg}";
                return;
            }
            if (!string.IsNullOrEmpty(vrefreshed)) cookie = vrefreshed;
            AccountService.AddOrUpdate(new Account { Nickname = nickname, Uid = uid, Cookie = cookie });
            ImportStatus.Text = $"已导入账号：{nickname}（{uid}）";
            RefreshList();
        }
        finally
        {
            BtnImport.IsEnabled = true;
        }
    }
}
