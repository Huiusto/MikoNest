using System;
using System.Collections.Generic;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using MiNest.Models;
using MiNest.Services;

namespace MiNest.Windows;

/// <summary>
/// 安全验证窗口：把米哈游官方的签到页面嵌进软件里。
/// 触发风控（1034 / is_risk）时，验证码会出现在这个窗口内，由玩家自己完成验证并签到，
/// 关闭后调用方自动重试接口签到，从而把整条任务链跑完。
/// </summary>
public partial class SignVerifyWindow : Window
{
    private readonly string _cookie;
    private readonly string _url;
    private bool _webReady;

    /// <summary>玩家点「我已完成验证」关闭窗口时为 true。</summary>
    public bool Verified { get; private set; }

    public SignVerifyWindow(SignService.GameCheckin game, string cookie, string accountLabel)
        : this(BuildSignUrl(game), cookie, accountLabel,
            $"米哈游检测到本次签到需要人机验证。请在下方的官方「{game.Name}」签到页完成验证并点击签到，完成后回到这里点「我已完成验证」。")
    {
    }

    public SignVerifyWindow(string url, string cookie, string accountLabel, string hint)
    {
        InitializeComponent();
        _cookie = cookie ?? "";
        _url = url;

        TxtAccount.Text = accountLabel;
        if (!string.IsNullOrWhiteSpace(hint)) TxtHint.Text = hint;
        Loaded += async (_, _) => await InitWebAsync();
    }

    /// <summary>官方签到页：act_id / host 直接复用签到接口里的配置</summary>
    public static string BuildSignUrl(SignService.GameCheckin game)
        => $"https://act.mihoyo.com/bbs/event/signin/{game.Host}/index.html" +
           $"?act_id={game.ActId}&mhy_auth_required=true&mhy_presentation_style=fullscreen&bbs_presentation_style=fullscreen";

    /// <summary>米游社社区打卡页（米游币「打卡」任务触发验证时使用）</summary>
    public static string BuildBbsUrl() => "https://www.miyoushe.com/ys/";

    private async System.Threading.Tasks.Task InitWebAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebView2Dir,
                new CoreWebView2EnvironmentOptions());
            await Web.EnsureCoreWebView2Async(env);
            _webReady = true;

            Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            Web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Web.CoreWebView2.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (!string.IsNullOrEmpty(e.Uri))
                    Web.CoreWebView2.Navigate(e.Uri);
            };

            await InjectCookiesAsync();
            Web.CoreWebView2.Navigate(_url);
        }
        catch (Exception ex)
        {
            LoadingMask.Visibility = Visibility.Visible;
            TxtLoadingHint.Text = "验证页面初始化失败（" + ex.Message + "）。\n请确认已安装 WebView2 Runtime，或到浏览器完成签到后重试。";
            TxtLoadingHint.TextWrapping = TextWrapping.Wrap;
            TxtLoadingHint.TextAlignment = TextAlignment.Center;
            TxtLoadingHint.MaxWidth = 520;
        }
    }

    /// <summary>把账号 Cookie 写进 WebView2，官方页面打开即已登录</summary>
    private async System.Threading.Tasks.Task InjectCookiesAsync()
    {
        if (!_webReady || string.IsNullOrEmpty(_cookie)) return;
        var manager = Web.CoreWebView2.CookieManager;

        var keys = new[]
        {
            "account_id", "stuid", "ltuid", "mid", "stoken", "ltoken", "cookie_token",
            "ltuid_v2", "account_id_v2", "account_mid_v2", "ltoken_v2", "cookie_token_v2", "ltmid_v2",
        };

        // 官方 act 页 / 米游社页分别使用 .mihoyo.com 与 .miyoushe.com 域
        foreach (var domain in new[] { ".mihoyo.com", ".miyoushe.com" })
        {
            foreach (var key in keys)
            {
                var value = SignService.CookieValue(_cookie, key);
                if (string.IsNullOrEmpty(value)) continue;
                try
                {
                    var c = manager.CreateCookie(key, value, domain, "/");
                    c.IsSecure = true;
                    manager.AddOrUpdateCookie(c);
                }
                catch { /* 单个字段失败不影响其他 */ }
            }
        }
        await System.Threading.Tasks.Task.CompletedTask;
    }

    private void BtnDone_Click(object sender, RoutedEventArgs e)
    {
        Verified = true;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
