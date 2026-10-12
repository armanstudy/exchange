using System.Windows;

namespace LenjExchange.Desktop.Views;

public partial class PromptWindow : Window
{
    public PromptWindow(string message, string title, string initial)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        Input.Text = initial;
        Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
    }

    public string Value => Input.Text;

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
