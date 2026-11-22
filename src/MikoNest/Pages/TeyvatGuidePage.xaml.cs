using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MiNest.Services;

namespace MiNest.Pages;

public partial class TeyvatGuidePage : UserControl
{
    private readonly MainWindow _owner;

    public TeyvatGuidePage(MainWindow owner)
    {
        InitializeComponent();
        _owner = owner;
        Loaded += (_, _) => LoadNotePreview();
    }

    private void OpenGitHub_Click(object sender, RoutedEventArgs e)
        => _owner.OpenInApp("https://github.com/BTMuli/TeyvatGuide");

    private void GotoNote_Click(object sender, MouseButtonEventArgs e) => _owner.ShowPage("实时便笺");
    private void GotoTask_Click(object sender, MouseButtonEventArgs e) => _owner.ShowPage("任务/签到");
    private void GotoGacha_Click(object sender, MouseButtonEventArgs e) => _owner.ShowPage("抽卡记录");
    private void GotoAccount_Click(object sender, MouseButtonEventArgs e) => _owner.ShowPage("账号");

    private async void LoadNotePreview()
    {
        NotePreview.ItemsSource = await DailyNoteService.GetPreviewItemsAsync();
    }
}
