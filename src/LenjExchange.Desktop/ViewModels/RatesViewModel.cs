using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Rates;
using LenjExchange.Desktop.Services;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Security;
using LenjExchange.Domain.Shamsi;

namespace LenjExchange.Desktop.ViewModels;

public sealed record RateRow(Guid Id, string Date, string Pair, string Rate, string Text, string? Notes, string CreatedBy);

/// <summary>
/// Daily reference rates. They only pre-fill forms: every document stores its own rate snapshot, so editing a
/// rate here never changes documents already registered.
/// </summary>
public sealed partial class RatesViewModel : ViewModelBase
{
    private readonly RateService _rates;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IDialogService _dialogs;

    [ObservableProperty] private string _date = "";
    [ObservableProperty] private CurrencyCode _baseCurrency = CurrencyCode.USD;
    [ObservableProperty] private CurrencyCode _quoteCurrency = CurrencyCode.IRT;
    [ObservableProperty] private string _rate = "";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private RateRow? _selected;

    public ObservableCollection<RateRow> Items { get; } = new();
    public bool CanManage => _user.Has(Permission.ManageRates);

    public RatesViewModel(RateService rates, ICurrentUser user, IClock clock, IDialogService dialogs)
    {
        _rates = rates;
        _user = user;
        _clock = clock;
        _dialogs = dialogs;
    }

    public override string Title => "نرخ‌های روزانه";
    public string Preview => $"۱ {BaseCurrency} = {Rate} {QuoteCurrency}";

    partial void OnBaseCurrencyChanged(CurrencyCode value) => OnPropertyChanged(nameof(Preview));
    partial void OnQuoteCurrencyChanged(CurrencyCode value) => OnPropertyChanged(nameof(Preview));
    partial void OnRateChanged(string value) => OnPropertyChanged(nameof(Preview));

    public override Task LoadAsync() => RunAsync(async () =>
    {
        Date = ShamsiDate.Format(_clock.LocalToday());
        Items.Clear();
        foreach (var r in await _rates.ListAsync())
            Items.Add(new RateRow(r.Id, Display.Date(r.EffectiveDateUtc), $"{r.BaseCurrency}/{r.QuoteCurrency}",
                AmountFormatter.Format(r.Rate, r.QuoteCurrency), r.ToExchangeRate().Describe(), r.Notes, r.CreatedBy ?? ""));
    });

    [RelayCommand]
    private async Task Save()
    {
        var ok = await RunAsync(async () =>
        {
            var date = _clock.ToUtc(ParseShamsi(Date, "تاریخ"));
            await _rates.SetDailyRateAsync(date, new ExchangeRate(BaseCurrency, QuoteCurrency, ParseAmount(Rate, "نرخ")),
                string.IsNullOrWhiteSpace(Notes) ? null : Notes);
        });
        if (!ok) return;
        Rate = Notes = "";
        await LoadAsync();
        StatusMessage = "نرخ ثبت شد.";
    }

    [RelayCommand]
    private async Task Edit()
    {
        if (Selected is not { } s) return;
        var text = _dialogs.Prompt($"نرخ جدید برای {s.Pair} در {s.Date} (اسناد ثبت‌شده تغییر نمی‌کنند):", "ویرایش نرخ");
        if (text is null) return;
        if (await RunAsync(() => _rates.UpdateDailyRateAsync(s.Id, ParseAmount(text, "نرخ"), s.Notes))) await LoadAsync();
    }
}
