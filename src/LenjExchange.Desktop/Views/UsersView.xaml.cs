using System.Windows;
using System.Windows.Controls;
using LenjExchange.Desktop.ViewModels;

namespace LenjExchange.Desktop.Views;

/// <summary>PasswordBox.Password is not bindable by design; values are forwarded to the view model on change.</summary>
public partial class UsersView : UserControl
{
    public UsersView() => InitializeComponent();

    private UsersViewModel? Vm => DataContext as UsersViewModel;

    private void OnNewPassword(object sender, RoutedEventArgs e) { if (Vm is { } vm) vm.NewPassword = NewPassword.Password; }
    private void OnCurrentPassword(object sender, RoutedEventArgs e) { if (Vm is { } vm) vm.CurrentPassword = CurrentPassword.Password; }
    private void OnResetPassword(object sender, RoutedEventArgs e) { if (Vm is { } vm) vm.ResetPassword = ResetPassword.Password; }
}
