using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using MiNest.Models;
using MiNest.Services;

namespace MiNest.Windows;

/// <summary>
/// 仿官方 HoYoPlay「选择安装路径」弹窗：安装路径 / 语言 / 所需空间 / 自动创建子文件夹 / 开始安装。
/// 安装与预下载都由 MikoNest 读取官方散文件清单后逐文件写入，不调用官方启动器。
/// </summary>
public partial class InstallGameDialog : Window
{
    private readonly GameDef _game;
    private readonly bool _preDownload;
    private string _baseDir;

    /// <summary>用户确认后的安装根目录（已按「自动创建子文件夹」拼好）</summary>
    public string SelectedDir { get; private set; } = "";
    public string SelectedLanguage { get; private set; } = "zh-cn";

    public InstallGameDialog(GameDef game, string? defaultDir, bool preDownload, string version, long needBytes)
    {
        InitializeComponent();
        _game = game;
        _preDownload = preDownload;
        _baseDir = string.IsNullOrWhiteSpace(defaultDir)
            ? Path.Combine(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)) ?? "C:\\", "miHoYo", "miHoYo Launcher", "games")
            : defaultDir!;

        TxtTitle.Text = preDownload ? $"预下载 · {game.Name}" : $"安装 · {game.Name}";
        TxtGameInfo.Text = string.IsNullOrWhiteSpace(version)
            ? game.Name
            : $"{game.Name}  {version}{(preDownload ? "（预下载）" : "")}";
        TxtTip.Text = preDownload
            ? "将按官方清单把预下载文件逐块写入指定目录，不下载整包、不再解压，可随时停止。"
            : "下载与写入都由 MikoNest 在软件内逐文件完成，不下载整包、不再解压，可随时停止并清理。";
        BtnInstall.Content = preDownload ? "开始预下载" : "开始安装";

        // 更新已有安装时直接复用原目录，不再自动套一层游戏子文件夹。
        if (!string.IsNullOrWhiteSpace(defaultDir) && Directory.Exists(defaultDir)
            && GameLocator.FindExe(defaultDir, game) != null)
            ChkSubFolder.IsChecked = false;

        UpdatePathText();
        UpdateSpaceText(needBytes);
    }

    private string FolderName => string.IsNullOrWhiteSpace(_game.DataDir) ? _game.Key : _game.DataDir;

    private void UpdatePathText()
    {
        if (TxtPath == null || _game == null) return;   // XAML 加载期 CheckBox 触发的事件
        var sub = ChkSubFolder.IsChecked == true ? Path.Combine(_baseDir, FolderName) : _baseDir;
        TxtPath.Text = sub;
        SelectedDir = sub;
    }

    private void UpdateSpaceText(long needBytes)
    {
        if (TxtSpace == null) return;
        long available = 0;
        try
        {
            var root = Path.GetPathRoot(_baseDir);
            if (!string.IsNullOrEmpty(root))
                available = new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            // 忽略
        }

        var need = needBytes > 0 ? FormatSize(needBytes) : "未知";
        TxtSpace.Text = $"所需空间：{need}    可用空间：{(available > 0 ? FormatSize(available) : "未知")}";
        TxtSpace.Foreground = needBytes > 0 && available > 0 && available < needBytes
            ? System.Windows.Media.Brushes.Firebrick
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x5A, 0x51, 0x48));
    }

    private static string FormatSize(long bytes)
    {
        const double gb = 1024d * 1024 * 1024;
        return bytes >= gb ? $"{bytes / gb:0.00} GB" : $"{bytes / (1024d * 1024):0} MB";
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = $"选择「{_game.Name}」的安装位置",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_baseDir) ? _baseDir : "",
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        _baseDir = dlg.SelectedPath;
        UpdatePathText();
        UpdateSpaceText(0);
    }

    private void ChkSubFolder_Changed(object sender, RoutedEventArgs e) => UpdatePathText();

    private void BtnInstall_Click(object sender, RoutedEventArgs e)
    {
        SelectedLanguage = RdoEn.IsChecked == true ? "en-us"
            : RdoJa.IsChecked == true ? "ja-jp"
            : RdoKo.IsChecked == true ? "ko-kr" : "zh-cn";
        SelectedDir = ChkSubFolder.IsChecked == true ? Path.Combine(_baseDir, FolderName) : _baseDir;
        DialogResult = true;
        Close();
    }
}
