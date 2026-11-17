using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using MiNest.Services;
using Microsoft.Web.WebView2.Core;

namespace MiNest.Windows;

/// <summary>
/// 极验（aigis）滑块验证窗口。
/// 用法：SmsCaptchaWindow.ShowChallenge(owner, challenge) → 返回验证成功后拼好的 aigis 字符串；取消/失败返回 null。
/// </summary>
public partial class SmsCaptchaWindow : Window
{
    private readonly AccountService.SmsChallengeInfo _challenge;
    private readonly TaskCompletionSource<string?> _tcs = new();

    private SmsCaptchaWindow(AccountService.SmsChallengeInfo challenge)
    {
        InitializeComponent();
        _challenge = challenge;
        Loaded += OnLoaded;
    }

    public static Task<string?> ShowChallenge(Window owner, AccountService.SmsChallengeInfo challenge)
    {
        var w = new SmsCaptchaWindow(challenge)
        {
            Owner = owner,
        };
        w.Show();
        return w._tcs.Task;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await CaptchaWeb.EnsureCoreWebView2Async();
            CaptchaWeb.CoreWebView2.WebMessageReceived += OnWebMessage;
            CaptchaWeb.CoreWebView2.Settings.AreDevToolsEnabled = false;
            CaptchaWeb.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            CaptchaWeb.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

            var html = LoadGeetestHtml();
            CaptchaWeb.CoreWebView2.NavigateToString(html);
        }
        catch (Exception ex)
        {
            Finish(null);
            MessageBox.Show(this, $"极验组件初始化失败：{ex.Message}", "安全验证", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess) return;
        try
        {
            var json = JsonSerializer.Serialize(new { session_id = _challenge.SessionId, data = _challenge.DataJson });
            _ = CaptchaWeb.CoreWebView2.ExecuteScriptAsync($"window.hoYoNestInit({json});");
        }
        catch
        {
            Finish(null);
        }
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var raw = e.TryGetWebMessageAsString() ?? "";
        if (raw == "__closed__" || raw == "__error__")
        {
            Finish(null);
            return;
        }
        // 极验验证结果 JSON：{geetest_challenge, geetest_validate, geetest_seccode}（V3）
        // 或 {lot_number, captcha_output, pass_token, gen_time}（V4）
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                Finish(null);
                return;
            }
            var aigis = AccountService.BuildAigisString(_challenge.SessionId, raw);
            Finish(aigis);
        }
        catch
        {
            Finish(null);
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Finish(null);

    private void Finish(string? result)
    {
        if (_tcs.Task.IsCompleted) return;
        _tcs.TrySetResult(result);
        Dispatcher.BeginInvoke(() =>
        {
            if (IsLoaded) Close();
        });
    }

    private static string LoadGeetestHtml()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/mi-Nest;component/Assets/geetest.html");
            var info = Application.GetResourceStream(uri);
            if (info != null)
            {
                using var reader = new StreamReader(info.Stream);
                return reader.ReadToEnd();
            }
        }
        catch
        {
            // 资源缺失时使用内嵌兜底
        }
        return "<html><body style='background:#1b1d26;color:#e6e8ee;font-family:sans-serif;display:flex;align-items:center;justify-content:center;height:100vh'>安全验证组件缺失，请重新安装 mi-Nest。</body></html>";
    }
}
