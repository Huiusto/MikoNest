using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MiNest.Services;

namespace MiNest.Pages;

public partial class StarwardPage : UserControl
{
    private readonly MainWindow _owner;

    public StarwardPage(MainWindow owner)
    {
        InitializeComponent();
        _owner = owner;
        Loaded += (_, _) => LoadNotePreview();
    }

    private void OpenGitHub_Click(object sender, RoutedEventArgs e)
        => _owner.OpenInApp("https://github.com/Scighost/Starward");

    private void GotoGacha_Click(object sender, MouseButtonEventArgs e) => _owner.ShowPage("抽卡记录");
    private void GotoNote_Click(object sender, MouseButtonEventArgs e) => _owner.ShowPage("实时便笺");
    private void GotoLaunch_Click(object sender, MouseButtonEventArgs e) => _owner.ShowPage("启动");
    private void GotoSettings_Click(object sender, MouseButtonEventArgs e) => _owner.ShowPage("设置");

    private async void LoadNotePreview()
    {
        NotePreview.ItemsSource = await DailyNoteService.GetPreviewItemsAsync();
    }
}
