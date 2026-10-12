using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LenjExchange.Application.Customers;
using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Reporting;
using LenjExchange.Desktop.Services;
using LenjExchange.Domain.Parties;

namespace LenjExchange.Desktop.ViewModels;

public sealed record CustomerRow(Guid Id, string Code, string FullName, string? Phone, string? Address, string? Notes, bool IsActive, string Balances)
{
    public string ActiveText => IsActive ? "فعال" : "غیرفعال";
}

public sealed partial class CustomersViewModel : ViewModelBase
{
    private readonly CustomerService _customers;
    private readonly ReportService _reports;
    private readonly IClock _clock;
    private readonly IDialogService _dialogs;
    private readonly NavigationService _nav;

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private bool _includeInactive;
    [ObservableProperty] private CustomerRow? _selected;

    [ObservableProperty] private Guid? _editingId;
    [ObservableProperty] private string _fullName = "";
    [ObservableProperty] private string _phone = "";
    [ObservableProperty] private string _address = "";
    [ObservableProperty] private string _notes = "";

    public ObservableCollection<CustomerRow> Items { get; } = new();

    public CustomersViewModel(CustomerService customers, ReportService reports, IClock clock, IDialogService dialogs, NavigationService nav)
    {
        _customers = customers;
        _reports = reports;
        _clock = clock;
        _dialogs = dialogs;
        _nav = nav;
    }

    public override string Title => "مشتریان";
    public string FormTitle => EditingId is null ? "مشتری جدید" : "ویرایش مشتری";

    partial void OnEditingIdChanged(Guid? value) => OnPropertyChanged(nameof(FormTitle));

    partial void OnSelectedChanged(CustomerRow? value)
    {
        if (value is null) return;
        EditingId = value.Id;
        FullName = value.FullName;
        Phone = value.Phone ?? "";
        Address = value.Address ?? "";
        Notes = value.Notes ?? "";
    }

    public override Task LoadAsync() => RunAsync(async () =>
    {
        var list = await _customers.SearchAsync(Search, IncludeInactive);
        var balances = await _reports.AllCustomersAsync(DateRanges.For(_clock, DatePreset.All), includeZero: false);
        var byCustomer = balances.Rows.GroupBy(r => r.CustomerId)
            .ToDictionary(g => g.Key, g => string.Join("  |  ", g.Select(r => Display.Signed(r.Balance, r.Currency) + " " + r.Currency)));
        Items.Clear();
        foreach (var c in list)
            Items.Add(new CustomerRow(c.Id, c.Code, c.FullName, c.Phone, c.Address, c.Notes, c.IsActive,
                byCustomer.TryGetValue(c.Id, out var b) ? b : "—"));
    });

    [RelayCommand] private Task Find() => LoadAsync();

    [RelayCommand]
    private void New()
    {
        Selected = null;
        EditingId = null;
        FullName = Phone = Address = Notes = "";
    }

    [RelayCommand]
    private async Task Save()
    {
        var input = new CustomerInput(FullName, Blank(Phone), Blank(Address), Blank(Notes));
        Customer? saved = null;
        var ok = await RunAsync(async () =>
            saved = EditingId is { } id ? await _customers.UpdateAsync(id, input) : await _customers.CreateAsync(input));
        if (!ok) return;
        var message = $"مشتری «{saved!.FullName}» با کد {saved.Code} ذخیره شد.";
        await LoadAsync();
        StatusMessage = message;
        EditingId = saved.Id;
    }

    [RelayCommand]
    private async Task ToggleActive()
    {
        if (Selected is not { } s) return;
        if (await RunAsync(() => _customers.SetActiveAsync(s.Id, !s.IsActive))) await LoadAsync();
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (Selected is not { } s) return;
        if (!_dialogs.Confirm($"مشتری «{s.FullName}» حذف (غیرفعال و پنهان) شود؟ سوابق مالی او حفظ می‌شود.")) return;
        if (await RunAsync(() => _customers.SoftDeleteAsync(s.Id))) { New(); await LoadAsync(); }
    }

    [RelayCommand]
    private Task Statement() =>
        Selected is { } s ? _nav.NavigateAsync<ReportsViewModel>(vm => { vm.PreselectCustomer(s.Id); return Task.CompletedTask; }) : Task.CompletedTask;

    [RelayCommand]
    private Task NewReceipt() =>
        Selected is { } s ? _nav.NavigateAsync<NewReceiptViewModel>(vm => { vm.PreselectCustomer(s.Id); return Task.CompletedTask; }) : Task.CompletedTask;

    private static string? Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
