using System.Windows;

namespace MiNest.Pages;

public partial class InputDialog : Window
{
    public string Result { get; private set; } = "";

    public InputDialog(string title, string prompt, string initial = "")
    {
        InitializeComponent();
        Title = title;
        Prompt.Text = prompt;
        InputBox.Text = initial;
        InputBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = InputBox.Text;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
