using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MiNest.Avalonia.Theme;

namespace MiNest.Avalonia.Views;

/// <summary>轻量模态对话框：Avalonia 没有 MessageBox，这里做一个统一风格的小窗。</summary>
public static class MessageDialog
{
    /// <summary>显示一条提示，只有「知道了」。</summary>
    public static Task Show(Window? owner, string title, string message) =>
        ShowCore(owner, title, message, "知道了", null);

    /// <summary>显示确认框，返回是否点了确认。</summary>
    public static Task<bool> Confirm(Window? owner, string title, string message,
        string okText = "确定", string cancelText = "取消") =>
        ShowCore(owner, title, message, okText, cancelText);

    private static async Task<bool> ShowCore(Window? owner, string title, string message,
        string okText, string? cancelText)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = owner != null
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen,
            Background = (IBrush?)Application.Current?.Resources["CardBg"] ?? Brushes.White,
            ShowInTaskbar = false,
        };

        var body = new StackPanel { Spacing = 14, Margin = new Thickness(24, 22, 24, 18) };
        body.Children.Add(new TextBlock
        {
            Text = title, FontSize = 17, FontWeight = FontWeight.Bold,
            Foreground = (IBrush?)Application.Current?.Resources["TextPrimary"] ?? Brushes.Black,
        });
        body.Children.Add(new TextBlock
        {
            Text = message, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, LineHeight = 20,
            Foreground = (IBrush?)Application.Current?.Resources["TextSecondary"] ?? Brushes.DimGray,
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var result = new TaskCompletionSource<bool>();

        if (cancelText != null)
        {
            var cancel = new Button { Content = cancelText, Classes = { "ghost" }, Height = 34, Padding = new Thickness(18, 0) };
            cancel.Click += (_, _) => { result.TrySetResult(false); dialog.Close(); };
            buttons.Children.Add(cancel);
        }

        var ok = new Button { Content = okText, Classes = { "primary" }, Height = 34, Padding = new Thickness(22, 0) };
        ok.Click += (_, _) => { result.TrySetResult(true); dialog.Close(); };
        buttons.Children.Add(ok);
        body.Children.Add(buttons);

        dialog.Content = body;
        dialog.Closed += (_, _) => result.TrySetResult(false);

        if (owner != null) await dialog.ShowDialog(owner);
        else dialog.Show();
        return await result.Task;
    }
}
