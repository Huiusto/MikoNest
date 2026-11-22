using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MiNest.Models;
using MiNest.Services;

namespace MiNest.Windows;

/// <summary>
/// 软件内直接安装游戏：显示官方散文件下载与写入进度。
/// 安装过程中可以随时停止；停止后会清理本次已经写入的游戏文件与临时分片。
/// </summary>
public sealed class GameInstallWindow : Window
{
    private readonly GameDef _game;
    private readonly string _dest;
    private readonly string _language;
    private readonly CancellationTokenSource _cts = new();
    private readonly TextBlock _stage;
    private readonly TextBlock _detail;
    private readonly System.Windows.Controls.ProgressBar _bar;
    private readonly Button _stop;
    private readonly Button _close;
    private bool _running;
    private bool _stopping;

    public string? InstalledDir { get; private set; }

    public GameInstallWindow(GameDef game, string dest, string language)
    {
        _game = game;
        _dest = dest;
        _language = language;

        Title = $"安装 · {game.Name}";
        Width = 620;
        Height = 320;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = (Brush)Application.Current.Resources["PageBg"];

        var root = new Grid { Margin = new Thickness(26) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = $"正在安装 {game.Name}",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
        };
        root.Children.Add(title);

        _stage = new TextBlock
        {
            Text = "正在获取官方安装清单…",
            FontSize = 12.5,
            Margin = new Thickness(0, 9, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
        };
        Grid.SetRow(_stage, 1);
        root.Children.Add(_stage);

        var center = new StackPanel { Margin = new Thickness(0, 16, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        Grid.SetRow(center, 2);
        root.Children.Add(center);

        _bar = new System.Windows.Controls.ProgressBar
        {
            Height = 9,
            Minimum = 0,
            Maximum = 100,
        };
        center.Children.Add(_bar);

        _detail = new TextBlock
        {
            Text = _dest,
            FontSize = 11.5,
            Margin = new Thickness(0, 9, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18,
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
        };
        center.Children.Add(_detail);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        _stop = new Button
        {
            Content = "停止安装并清理",
            Padding = new Thickness(18, 7, 18, 7),
            Margin = new Thickness(0, 0, 10, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x45, 0x3F)),
        };
        _stop.Click += (_, _) => StopAndCleanup();
        buttons.Children.Add(_stop);

        _close = new Button
        {
            Content = "关闭",
            Padding = new Thickness(22, 7, 22, 7),
            Visibility = Visibility.Collapsed,
        };
        _close.Click += (_, _) => Close();
        buttons.Children.Add(_close);

        Content = root;
        Loaded += async (_, _) => await RunAsync();
        Closing += (_, e) =>
        {
            if (!_running) return;
            e.Cancel = true;
            StopAndCleanup();
        };
    }

    private void StopAndCleanup()
    {
        if (!_running)
        {
            Close();
            return;
        }
        if (_stopping) return;
        _stopping = true;
        _stop.IsEnabled = false;
        _stop.Content = "正在停止并清理…";
        _stage.Text = "正在停止安装，并清理本次已经写入的文件…";
        _cts.Cancel();
    }

    private async Task RunAsync()
    {
        _running = true;
        var progress = new Progress<GameInstallService.Progress>(p =>
        {
            var pct = p.Total > 0 ? Math.Clamp(p.Done * 100.0 / p.Total, 0, 100) : 0;
            _bar.Value = pct;
            if (_stopping) return;
            _stage.Text = p.Stage;
            var pctText = p.Total > 0 ? $"   ·   {pct:0.0}%" : "";
            _detail.Text = $"{Format(p.Done)} / {Format(p.Total)}{pctText}" +
                           (p.BytesPerSecond > 0 ? $"   ·   {Format((long)p.BytesPerSecond)}/s" : "") +
                           $"\n{_dest}";
        });

        try
        {
            InstalledDir = await GameInstallService.InstallAsync(_game.Key, _dest, _language, progress, _cts.Token);
            _bar.Value = 100;
            _stage.Text = "安装完成，游戏文件已写入。";
            _detail.Text = $"{InstalledDir}\n\n现在可以直接点「开始游戏」。";
            _running = false;
            _stop.Visibility = Visibility.Collapsed;
            _close.Visibility = Visibility.Visible;
            MessageBox.Show(this, $"{_game.Name} 已安装完成。\n\n{InstalledDir}", "MikoNest",
                MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            _running = false;
            _bar.Value = 0;
            _stage.Text = "已停止安装，本次写入的文件与临时分片已清理。";
            _detail.Text = _dest;
            _stop.Visibility = Visibility.Collapsed;
            _close.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _running = false;
            _bar.Value = 0;
            _stage.Text = "安装失败：" + ex.Message;
            _detail.Text = $"{_dest}\n\n已下载的文件会保留，再次安装可继续下载；也可以在「游戏设置」里卸载。";
            _stop.Visibility = Visibility.Collapsed;
            _close.Visibility = Visibility.Visible;
        }
    }

    private static string Format(long bytes)
    {
        const double gb = 1024d * 1024 * 1024;
        return bytes >= gb ? $"{bytes / gb:0.00} GB" : $"{bytes / (1024d * 1024):0} MB";
    }
}
