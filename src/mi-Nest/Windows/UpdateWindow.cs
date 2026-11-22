using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using MiNest.Services;

namespace MiNest.Windows
{
    /// <summary>
    /// 「检查更新」弹窗：仿官方启动器样式，左侧角色插画，右侧版本信息与「取消 / 开始更新」。
    /// 下载完成后静默启动 NSIS 安装程序并退出当前进程。
    /// </summary>
    public sealed class UpdateWindow : Window
    {
        private static readonly Brush Pink = new SolidColorBrush(Color.FromRgb(0xFF, 0x8D, 0x8D));
        private static readonly Brush PinkDark = new SolidColorBrush(Color.FromRgb(0xF0, 0x6E, 0x74));
        private static readonly Brush PinkSoft = new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xF1));
        private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x3A, 0x2E, 0x30));
        private static readonly Brush InkSoft = new SolidColorBrush(Color.FromRgb(0x8A, 0x76, 0x78));

        private readonly Version _current;
        private readonly UpdateInfo _info;
        private readonly CancellationTokenSource _cts = new();

        private Button _btnCancel = null!;
        private Button _btnUpdate = null!;
        private TextBlock _status = null!;
        private System.Windows.Controls.ProgressBar _bar = null!;

        /// <summary>用户点击「开始更新」且安装程序已成功启动。</summary>
        public bool StartedInstall { get; private set; }

        public UpdateWindow(Version current, UpdateInfo info)
        {
            _current = current;
            _info = info;

            Title = "检查更新 · MikoNest";
            Width = 900;
            Height = 520;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            Content = BuildRoot();
        }

        private UIElement BuildRoot()
        {
            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = Pink,
                BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(20),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 34,
                    ShadowDepth = 8,
                    Direction = 270,
                    Opacity = 0.26,
                    Color = Color.FromRgb(0x6B, 0x3A, 0x3A),
                },
            };

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // ── 标题栏 ───────────────────────────────────────────────
            var titleBar = new Border
            {
                Background = Pink,
                CornerRadius = new CornerRadius(17, 17, 0, 0),
                Padding = new Thickness(24, 0, 12, 0),
            };
            titleBar.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2) return;
                try { DragMove(); } catch { }
            };

            var titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = "检查更新",
                FontSize = 21,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
            };
            titleRow.Children.Add(title);

            var close = new Button
            {
                Content = "×",
                Width = 40,
                Height = 40,
                Padding = new Thickness(0),
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "关闭",
            };
            close.Click += (_, _) => Close();
            Grid.SetColumn(close, 1);
            titleRow.Children.Add(close);

            titleBar.Child = titleRow;
            root.Children.Add(titleBar);

            // ── 主体：左角色 + 右信息 / 更新日志 / 操作 ─────────────────
            var body = new Grid { Margin = new Thickness(24, 14, 24, 18) };
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) });
            Grid.SetRow(body, 1);

            var art = new Image
            {
                Source = LoadArt(),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 18, 18, 0),
            };
            RenderOptions.SetBitmapScalingMode(art, BitmapScalingMode.HighQuality);
            body.Children.Add(art);

            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(right, 1);

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4, 0, 0, 0) };
            info.Children.Add(new TextBlock
            {
                Text = "发现新版本！",
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Foreground = PinkDark,
            });

            var versionLine = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            versionLine.Children.Add(new TextBlock
            {
                Text = $"当前版本为 v{_current.ToString(3)}",
                FontSize = 16,
                Foreground = InkSoft,
            });
            versionLine.Children.Add(new TextBlock
            {
                Text = $"最新版本为 v{_info.Version}",
                FontSize = 16,
                Foreground = InkSoft,
                Margin = new Thickness(0, 4, 0, 0),
            });
            info.Children.Add(versionLine);

            var repo = new TextBlock
            {
                Text = UpdateService.RepoReleases,
                FontSize = 13.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0x9A, 0x9C)),
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            info.Children.Add(repo);

            if (_info.AssetSize > 0 && !string.IsNullOrEmpty(_info.AssetName))
            {
                info.Children.Add(new TextBlock
                {
                    Text = $"安装包 {_info.AssetName} · {_info.AssetSize / 1024d / 1024d:F1} MB",
                    FontSize = 12.5,
                    Foreground = InkSoft,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 10, 0, 0),
                });
            }
            right.Children.Add(info);

            // ── 本次 Release 的更新日志 ───────────────────────────────
            var notesText = FormatReleaseNotes(_info.ReleaseNotes);
            var notesCard = new Border
            {
                Background = PinkSoft,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0xD8)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(13, 10, 13, 11),
                Margin = new Thickness(16, 0, 0, 0),
                Visibility = string.IsNullOrWhiteSpace(notesText) ? Visibility.Collapsed : Visibility.Visible,
            };
            var notesGrid = new Grid();
            notesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            notesGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            notesGrid.Children.Add(new TextBlock
            {
                Text = "更新内容",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = PinkDark,
                Margin = new Thickness(0, 0, 0, 6),
            });
            var notesScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            notesScroll.Content = new TextBlock
            {
                Text = notesText,
                FontSize = 12.2,
                LineHeight = 18.5,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Ink,
            };
            Grid.SetRow(notesScroll, 1);
            notesGrid.Children.Add(notesScroll);
            notesCard.Child = notesGrid;
            Grid.SetColumn(notesCard, 2);
            body.Children.Add(notesCard);

            var bottom = new StackPanel();
            _status = new TextBlock
            {
                Text = "",
                FontSize = 12.5,
                Foreground = InkSoft,
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
            };
            bottom.Children.Add(_status);

            _bar = new System.Windows.Controls.ProgressBar
            {
                Height = 6,
                Margin = new Thickness(0, 10, 0, 0),
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Visibility = Visibility.Collapsed,
                Foreground = Pink,
                Background = PinkSoft,
                BorderThickness = new Thickness(0),
            };
            bottom.Children.Add(_bar);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
            };

            _btnCancel = MakeButton("取消", false);
            _btnCancel.Click += (_, _) =>
            {
                _cts.Cancel();
                Close();
            };
            buttons.Children.Add(_btnCancel);

            _btnUpdate = MakeButton("开始更新", true);
            _btnUpdate.Margin = new Thickness(14, 0, 0, 0);
            _btnUpdate.Click += async (_, _) => await StartUpdateAsync();
            buttons.Children.Add(_btnUpdate);
            bottom.Children.Add(buttons);

            Grid.SetRow(bottom, 1);
            Grid.SetColumn(bottom, 1);
            Grid.SetColumnSpan(bottom, 2);

            body.Children.Add(right);
            body.Children.Add(bottom);
            root.Children.Add(body);

            card.Child = root;
            return card;
        }

        private static Button MakeButton(string text, bool primary)
        {
            var b = new Button
            {
                Content = text,
                MinWidth = 128,
                Height = 44,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(2),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            if (primary)
            {
                b.Background = Pink;
                b.Foreground = Brushes.White;
                b.BorderBrush = Pink;
            }
            else
            {
                b.Background = Brushes.White;
                b.Foreground = PinkDark;
                b.BorderBrush = Pink;
            }
            return b;
        }

        private static ImageSource? LoadArt()
        {
            try
            {
                return new BitmapImage(new Uri("pack://application:,,,/MikoNest;component/Assets/update_char.png"));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>把 GitHub Release 的 Markdown 正文转成弹窗可读的纯文本，限制长度避免异常内容撑爆布局。</summary>
        private static string FormatReleaseNotes(string? markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return "";
            var text = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

            // Release 正文通常还包含下载表、安装说明等；弹窗只展示「更新内容」这一节。
            var section = Regex.Match(text,
                @"(?ms)^[ \t]*#{1,6}[ \t]*(?:\*\*|__)?[ \t]*(?:更新内容|更新日志|更新说明|本次更新)[ \t]*(?:\*\*|__)?[ \t]*\n(.*?)(?=^[ \t]*#{1,6}[ \t]|\z)");
            if (section.Success && !string.IsNullOrWhiteSpace(section.Groups[1].Value))
                text = section.Groups[1].Value;

            text = Regex.Replace(text, @"```[\s\S]*?```", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"!\[[^\]]*\]\([^)]*\)", "");
            text = Regex.Replace(text, @"\[([^\]]+)\]\((https?://[^)]+)\)", "$1\n$2");
            text = Regex.Replace(text, @"`([^`\n]+)`", "$1");
            text = Regex.Replace(text, @"\*\*([^*\n]+)\*\*", "$1");
            text = Regex.Replace(text, @"__([^_\n]+)__", "$1");
            text = Regex.Replace(text, @"(?m)^\s{0,3}#{1,6}\s*", "");
            // 弹窗已经自带「更新内容」标题，去掉正文开头重复的同名标题。
            text = Regex.Replace(text, @"^(?:更新内容|更新日志|更新说明|版本更新|本次更新)\s*\n+", "");
            text = Regex.Replace(text, @"(?m)^\s*[-*+]\s+", "· ");
            text = Regex.Replace(text, @"(?m)^\s*>\s*", "");
            text = Regex.Replace(text, @"(?m)^\|?[-:\s|]{5,}\|?$", "");
            text = Regex.Replace(text, @"(?m)^\|(.+)\|$", m =>
                string.Join("  ", m.Groups[1].Value.Split('|').Select(x => x.Trim()).Where(x => x.Length > 0)));
            text = Regex.Replace(text, @"\n[ \t]+\n", "\n\n");
            text = Regex.Replace(text, @"\n{3,}", "\n\n");
            text = text.Trim();
            return text.Length > 5000 ? text.Substring(0, 5000) + "\n…" : text;
        }

        /// <summary>开始更新 1 秒后把弹窗最小化，下载与安装继续在后台进行。</summary>
        private async Task MinimizeAfterDelayAsync()
        {
            await Task.Delay(1000);
            if (IsVisible && WindowState != WindowState.Minimized)
                WindowState = WindowState.Minimized;
        }

        private void RestoreAfterError()
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
                Activate();
            }
        }

        private async Task StartUpdateAsync()
        {
            if (string.IsNullOrEmpty(_info.AssetUrl)) return;

            _btnUpdate.IsEnabled = false;
            _btnCancel.IsEnabled = false;
            _btnUpdate.Content = "下载 0%";
            _bar.Visibility = Visibility.Visible;
            _status.Visibility = Visibility.Visible;
            UpdateProgressService.Report(0);
            _ = MinimizeAfterDelayAsync();

            try
            {
                var path = await UpdateService.DownloadAsync(_info, (done, total) =>
                {
                    if (total <= 0) return;
                    var pct = Math.Min(100, (int)Math.Round(done * 100d / total));
                    _bar.Value = pct;
                    _btnUpdate.Content = $"下载 {pct}%";
                    _status.Text = $"正在下载安装包… {done / 1024d / 1024d:F1} / {total / 1024d / 1024d:F1} MB";
                    UpdateProgressService.Report(pct);
                }, _cts.Token);

                _bar.Value = 100;
                _btnUpdate.Content = "安装中…";
                _status.Text = "安装包已下载，正在启动安装程序…";
                UpdateProgressService.Report(100);

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        Arguments = UpdateService.BuildInstallerArgs(),
                        UseShellExecute = true,
                        Verb = "runas",
                    });
                }
                catch (System.ComponentModel.Win32Exception w32) when (w32.NativeErrorCode == 1223)
                {
                    // 用户在 UAC 窗口点了「否」
                    UpdateProgressService.Report(-1);
                    RestoreAfterError();
                    _status.Text = $"已取消管理员授权。安装包保存在：\n{path}";
                    _btnUpdate.Content = "重试更新";
                    _btnUpdate.IsEnabled = true;
                    _btnCancel.IsEnabled = true;
                    _bar.Visibility = Visibility.Collapsed;
                    return;
                }

                StartedInstall = true;
                await Task.Delay(1200);
                Application.Current.Shutdown();
            }
            catch (OperationCanceledException)
            {
                // 用户主动关闭弹窗，静默退出。
                UpdateProgressService.Report(-1);
            }
            catch (Exception ex)
            {
                UpdateProgressService.Report(-1);
                RestoreAfterError();
                _status.Visibility = Visibility.Visible;
                _status.Text = "更新失败：" + ex.Message;
                _btnUpdate.Content = "重试更新";
                _btnUpdate.IsEnabled = true;
                _btnCancel.IsEnabled = true;
                _bar.Visibility = Visibility.Collapsed;
            }
        }
    }
}
