using System.Windows;
using System.Windows.Controls;
using LenjExchange.Desktop.ViewModels;

namespace LenjExchange.Desktop.Views;

public partial class LoginView : UserControl
{
    public LoginView() => InitializeComponent();

    // PasswordBox.Password is intentionally not bindable; forward it to the view model.
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm) vm.Password = Password.Password;
    }

    private void OnConfirmChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm) vm.ConfirmPassword = Confirm.Password;
    }
}
