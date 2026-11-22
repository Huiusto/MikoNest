using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MiNest.Models;
using MiNest.Services;

namespace MiNest.Windows;

/// <summary>启动页「游戏设置」：展示游戏基本信息、安装目录，并提供重新定位 / 安装管理。</summary>
public sealed class GameSettingsWindow : Window
{
    private readonly string _gameKey;
    private readonly GameDef _game;
    private readonly bool _installActive;
    private readonly Func<bool>? _stopInstall;
    private string? _dir;

    public GameSettingsWindow(
        string gameKey, GameDef game, string? dir,
        bool installActive = false, Func<bool>? stopInstall = null)
    {
        _gameKey = gameKey;
        _game = game;
        _dir = dir;
        _installActive = installActive;
        _stopInstall = stopInstall;

        Title = $"游戏设置 · {game.Name}";
        Width = 600;
        Height = 430;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = (Brush)Application.Current.Resources["PageBg"];

        Build();
    }

    private void Build()
    {
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        head.Children.Add(new TextBlock
        {
            Text = $"游戏设置 · {_game.Name}",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
        });
        head.Children.Add(new TextBlock
        {
            Text = "游戏基本信息、安装目录与安装 / 卸载管理。",
            FontSize = 12,
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
        });
        root.Children.Add(head);

        var card = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBg"],
            BorderBrush = (Brush)Application.Current.Resources["CardBorder"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18),
        };
        Grid.SetRow(card, 1);
        root.Children.Add(card);

        var info = new StackPanel();
        card.Child = info;

        info.Children.Add(Row("游戏", _game.Name));
        info.Children.Add(Row("主程序", string.Join(" / ", _game.ExeNames)));
        info.Children.Add(Row("安装目录", string.IsNullOrEmpty(_dir) ? "尚未定位" : _dir));
        long free = 0;
        try { if (!string.IsNullOrEmpty(_dir)) free = new DriveInfo(Path.GetPathRoot(_dir)!).AvailableFreeSpace; } catch { }
        info.Children.Add(Row("所在磁盘可用空间", free > 0 ? FormatSize(free) : "未知"));

        var buttons = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(Button("📂 打开安装目录", OpenInstallDir));
        buttons.Children.Add(Button("🎯 重新定位游戏", Relocate));
        buttons.Children.Add(Button("🗑 卸载游戏", Uninstall, danger: true));
        if (_installActive && _stopInstall != null)
            buttons.Children.Add(Button("停止安装并清理", StopInstall, danger: true));
        info.Children.Add(buttons);

        var close = new Button
        {
            Content = "关闭",
            Padding = new Thickness(22, 7, 22, 7),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        close.Click += (_, _) => Close();
        Grid.SetRow(close, 2);
        root.Children.Add(close);

        Content = root;
    }

    private FrameworkElement Row(string label, string value)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12.5,
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
        });
        var v = new TextBlock
        {
            Text = value,
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
        };
        Grid.SetColumn(v, 1);
        g.Children.Add(v);
        return g;
    }

    private Button Button(string text, Action action, bool danger = false)
    {
        var b = new Button
        {
            Content = text,
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 10, 10),
        };
        if (danger) b.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x45, 0x3F));
        b.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "MikoNest", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        return b;
    }

    private void OpenInstallDir()
    {
        if (string.IsNullOrEmpty(_dir) || !Directory.Exists(_dir))
        {
            MessageBox.Show(this, "尚未定位游戏安装目录，请先点「重新定位游戏」。", "MikoNest", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_dir}\"") { UseShellExecute = true });
    }

    private void Relocate()
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = $"选择「{_game.Name}」的安装目录（包含 exe 的那一层）",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_dir) ? _dir : "",
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        _dir = dlg.SelectedPath;
        SettingsService.Settings.GamePaths[_gameKey] = _dir;
        SettingsService.Save();
        MessageBox.Show(this, "已保存新的游戏目录。", "MikoNest", MessageBoxButton.OK, MessageBoxImage.Information);
        Content = null;
        Build();
    }

    private void Uninstall()
    {
        if (_installActive)
        {
            MessageBox.Show(this, "当前正在安装游戏，请先点「停止安装并清理」，再卸载。", "卸载游戏",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var installRoot = FindInstallRoot();
        if (installRoot == null)
        {
            MessageBox.Show(this, "尚未定位到包含游戏主程序的安装目录，无法卸载。请先点「重新定位游戏」。", "卸载游戏",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var fullPath = Path.GetFullPath(installRoot);
        var root = Path.GetPathRoot(fullPath);
        if (root != null &&
            string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "安装目录是磁盘根目录，为避免误删，已取消卸载。", "卸载游戏",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        foreach (var exe in _game.ExeNames)
        {
            var processName = Path.GetFileNameWithoutExtension(exe);
            if (Process.GetProcessesByName(processName).Length > 0)
            {
                MessageBox.Show(this, $"「{_game.Name}」正在运行，请先退出游戏再卸载。", "卸载游戏",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        var confirm = MessageBox.Show(
            this,
            $"确定要卸载「{_game.Name}」吗？\n\n将永久删除：\n{fullPath}\n\n删除后无法恢复。",
            "卸载游戏",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            Directory.Delete(fullPath, true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "卸载失败：" + ex.Message + "\n\n请关闭正在使用该目录的程序后重试。", "卸载游戏",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SettingsService.Settings.GamePaths.Remove(_gameKey);
        SettingsService.Save();
        _dir = null;
        MessageBox.Show(this, $"「{_game.Name}」已卸载，安装目录已删除。", "卸载游戏",
            MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }

    private string? FindInstallRoot()
    {
        if (string.IsNullOrWhiteSpace(_dir) || !Directory.Exists(_dir)) return null;
        if (GameLocator.FindExe(_dir, _game) != null) return _dir;

        try
        {
            foreach (var sub in Directory.GetDirectories(_dir))
            {
                if (GameLocator.FindExe(sub, _game) != null) return sub;
            }
        }
        catch
        {
            // 目录不可读时按“未定位”处理
        }
        return null;
    }

    private void StopInstall()
    {
        if (_stopInstall == null || !_stopInstall())
        {
            MessageBox.Show(this, "当前没有正在进行的安装。", "MikoNest", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Close();
    }

    private static string FormatSize(long bytes)
    {
        const double gb = 1024d * 1024 * 1024;
        return bytes >= gb ? $"{bytes / gb:0.0} GB" : $"{bytes / (1024d * 1024):0} MB";
    }
}
