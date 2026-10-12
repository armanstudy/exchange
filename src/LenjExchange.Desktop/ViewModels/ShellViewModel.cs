using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LenjExchange.Application.Abstractions;
using LenjExchange.Desktop.Services;
using LenjExchange.Domain.Security;

namespace LenjExchange.Desktop.ViewModels;

public sealed record NavItem(string Key, string Title, Permission? Required);

public sealed partial class ShellViewModel : ObservableObject
{
    private static readonly NavItem[] AllItems =
    {
        new("dashboard", "داشبورد", Permission.ViewDashboard),
        new("customers", "مشتریان", Permission.ManageCustomers),
        new("receipt", "ثبت رسید جدید", Permission.RegisterReceipts),
        new("cases", "پرونده‌های انتقال", Permission.ManageCases),
        new("exchangers", "صرافی‌های ترکیه", Permission.ManageExchangerDocuments),
        new("agents", "نمایندگان ترکیه", Permission.ManageExchangerDocuments),
        new("china", "دریافت در چین", Permission.ManageExchangerDocuments),
        new("rates", "نرخ‌ها", Permission.ViewReports),
        new("reports", "گزارش‌ها", Permission.ViewReports),
        new("attachments", "بایگانی پیوست‌ها", Permission.ManageAttachments),
        new("users", "کاربران", Permission.ManageUsers),
        new("settings", "تنظیمات", Permission.ManageSettings),
        new("backup", "پشتیبان‌گیری", Permission.CreateBackup),
    };

    [ObservableProperty] private ViewModelBase? _currentPage;
    [ObservableProperty] private bool _isSignedIn;
    [ObservableProperty] private string _userDisplay = "";
    [ObservableProperty] private string _selectedKey = "dashboard";

    public System.Collections.ObjectModel.ObservableCollection<NavItem> NavItems { get; } = new();

    internal NavigationService? Navigation { get; set; }

    public string AppTitle => "سامانه مالی لنج | LENJ Exchange Manager";

    public void OnSignedIn(CurrentUserSession session)
    {
        NavItems.Clear();
        foreach (var item in AllItems.Where(i => i.Required is null || session.Has(i.Required.Value)))
            NavItems.Add(item);
        UserDisplay = $"{session.User!.DisplayName} ({(session.User.Role == Role.Admin ? "مدیر" : "اپراتور")})";
        IsSignedIn = true;
        SelectedKey = "dashboard";
    }

    public void OnSignedOut()
    {
        NavItems.Clear();
        IsSignedIn = false;
        UserDisplay = "";
    }

    [RelayCommand]
    private async Task Navigate(string key)
    {
        SelectedKey = key;
        if (Navigation is not null) await Navigation.NavigateAsync(key);
    }

    [RelayCommand]
    private async Task SignOut()
    {
        if (Navigation is not null) await Navigation.SignOutAsync();
    }
}
