using System.Windows;
using LenjExchange.Desktop.ViewModels;

namespace LenjExchange.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
