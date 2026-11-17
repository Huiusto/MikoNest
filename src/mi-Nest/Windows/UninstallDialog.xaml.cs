using System.Windows;
using System.Windows.Input;

namespace MiNest.Windows;

/// <summary>胡桃风格的卸载确认弹窗（点击“卸载”返回 true）</summary>
public partial class UninstallDialog : Window
{
    public UninstallDialog()
    {
        InitializeComponent();
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

    private void BtnUninstall_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
