using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LenjExchange.Application.Security;
using LenjExchange.Desktop.Services;
using LenjExchange.Domain.Common;

namespace LenjExchange.Desktop.ViewModels;

/// <summary>Login, or first-run setup that forces creating the administrator (no built-in default password).</summary>
public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly UserService _users;
    private readonly NavigationService _nav;

    [ObservableProperty] private bool _isFirstRun;
    [ObservableProperty] private string _userName = "";
    [ObservableProperty] private string _displayName = "";

    public string Password { get; set; } = "";
    public string ConfirmPassword { get; set; } = "";

    public LoginViewModel(UserService users, NavigationService nav)
    {
        _users = users;
        _nav = nav;
    }

    public override string Title => IsFirstRun ? "راه‌اندازی اولیه — ایجاد حساب مدیر" : "ورود به سامانه";

    public string PasswordHint => $"حداقل {UserService.MinPasswordLength} کاراکتر، شامل حرف و عدد.";

    public override async Task LoadAsync()
    {
        IsFirstRun = await _users.IsFirstRunAsync();
        OnPropertyChanged(nameof(Title));
    }

    [RelayCommand]
    private async Task Submit()
    {
        var ok = await RunAsync(async () =>
        {
            if (IsFirstRun)
            {
                if (Password != ConfirmPassword) throw new DomainException("password_confirm", "تکرار رمز عبور مطابقت ندارد.");
                await _users.CreateInitialAdminAsync(UserName, string.IsNullOrWhiteSpace(DisplayName) ? UserName : DisplayName, Password);
            }
            else if (await _users.LoginAsync(UserName, Password) is null)
                throw new DomainException("login_failed", "نام کاربری یا رمز عبور نادرست است.");
        });
        if (ok) await _nav.SignedInAsync();
    }
}
