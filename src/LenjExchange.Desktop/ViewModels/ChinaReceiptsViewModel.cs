using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LenjExchange.Application.Abstractions;
using LenjExchange.Application.MasterData;
using LenjExchange.Application.Rates;
using LenjExchange.Application.Transfers;
using LenjExchange.Desktop.Services;
using LenjExchange.Domain.Common;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Parties;
using LenjExchange.Domain.Security;
using LenjExchange.Domain.Shamsi;
using LenjExchange.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace LenjExchange.Desktop.ViewModels;

public sealed record ChinaRow(Guid Id, string CaseNumber, string Agent, string Recipient, string Date, string SettledUsd, string Paid, string Rate,
    string? TrackingNo, string Approval);

/// <summary>Money arriving at the final recipient in China (USD, CNY or USDT), settled against USD held by an agent.</summary>
public sealed partial class ChinaReceiptsViewModel : ViewModelBase
{
    private readonly MasterDataService _master;
    private readonly TransferFlowService _flow;
    private readonly ReversalService _reversals;
    private readonly RateService _rates;
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IDialogService _dialogs;
    private List<FinalRecipient> _recipientEntities = new();

    [ObservableProperty] private IReadOnlyList<Lookup> _cases = Array.Empty<Lookup>();
    [ObservableProperty] private IReadOnlyList<Lookup> _agents = Array.Empty<Lookup>();
    [ObservableProperty] private IReadOnlyList<Lookup> _recipients = Array.Empty<Lookup>();
    [ObservableProperty] private IReadOnlyList<CurrencyCode> _recipientCurrencies = Array.Empty<CurrencyCode>();
    [ObservableProperty] private ChinaRow? _selected;

    [ObservableProperty] private Lookup? _formCase;
    [ObservableProperty] private Lookup? _formAgent;
    [ObservableProperty] private Lookup? _formRecipient;
    [ObservableProperty] private string _formDate = "";
    [ObservableProperty] private string _formSettledUsd = "";
    [ObservableProperty] private CurrencyCode _formPaidCurrency = CurrencyCode.USD;
    [ObservableProperty] private string _formRate = "";
    [ObservableProperty] private string _formTrackingNo = "";
    [ObservableProperty] private string _formNotes = "";
    [ObservableProperty] private string _formPaidPreview = "";

    [ObservableProperty] private bool _recipientUsd;
    [ObservableProperty] private bool _recipientCny;
    [ObservableProperty] private bool _recipientUsdt;

    public ObservableCollection<ChinaRow> Items { get; } = new();
    public bool CanApprove => _user.Has(Permission.ApproveChinaReceipts);
    public bool CanReverse => _user.Has(Permission.ReverseEntries);
    public bool CanManageMasterData => _user.Has(Permission.ManageMasterData);
    public bool NeedsRate => FormPaidCurrency != CurrencyCode.USD;

    public ChinaReceiptsViewModel(MasterDataService master, TransferFlowService flow, ReversalService reversals, RateService rates,
        IAppDbContext db, ICurrentUser user, IClock clock, IDialogService dialogs)
    {
        _master = master;
        _flow = flow;
        _reversals = reversals;
        _rates = rates;
        _db = db;
        _user = user;
        _clock = clock;
        _dialogs = dialogs;
    }

    public override string Title => "دریافت در چین";

    public override Task LoadAsync() => RunAsync(async () =>
    {
        Agents = (await _master.AgentsAsync()).Where(a => a.IsActive).Select(a => new Lookup(a.Id, a.Name)).ToList();
        _recipientEntities = (await _master.RecipientsAsync()).Where(r => r.IsActive).ToList();
        Recipients = _recipientEntities.Select(r => new Lookup(r.Id, $"{r.Name} - {r.Country}")).ToList();
        var customers = await _db.Customers.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.FullName);
        Cases = (await _db.TransferCases.AsNoTracking()
                .Where(c => !c.IsCancelled && c.Status != TransferStatus.Settled && c.Status != TransferStatus.Draft)
                .OrderByDescending(c => c.CreatedAtUtc).ToListAsync())
            .Select(c => new Lookup(c.Id, $"{c.CaseNumber} - {customers.GetValueOrDefault(c.CustomerId, "")}")).ToList();
        FormDate = ShamsiDate.Format(_clock.LocalToday());
        FormRecipient ??= Recipients.FirstOrDefault();
        await LoadListAsync();
    });

    private async Task LoadListAsync()
    {
        var list = await _db.ChinaReceipts.AsNoTracking().OrderByDescending(c => c.DateUtc).Take(1000).ToListAsync();
        var caseIds = list.Select(c => c.TransferCaseId).Distinct().ToList();
        var cases = await _db.TransferCases.AsNoTracking().Where(c => caseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.CaseNumber);
        var agents = Agents.ToDictionary(a => a.Id, a => a.Name);
        var recipients = _recipientEntities.ToDictionary(r => r.Id, r => r.Name);
        Items.Clear();
        foreach (var c in list)
            Items.Add(new ChinaRow(c.Id, cases.GetValueOrDefault(c.TransferCaseId, ""), agents.GetValueOrDefault(c.AgentId, ""),
                recipients.GetValueOrDefault(c.FinalRecipientId, ""), Display.DateTime(c.DateUtc), c.SettledUsd.Format(), c.Paid.Format(),
                c.Rate?.Describe() ?? "—", c.TrackingNo, c.IsReversed ? "برگشت‌خورده" : TransferStatusText.Persian(c.Approval)));
    }

    partial void OnFormRecipientChanged(Lookup? value)
    {
        var r = _recipientEntities.FirstOrDefault(x => x.Id == value?.Id);
        RecipientCurrencies = r?.AcceptedCurrencies ?? Array.Empty<CurrencyCode>();
        RecipientUsd = r?.Accepts(CurrencyCode.USD) ?? false;
        RecipientCny = r?.Accepts(CurrencyCode.CNY) ?? false;
        RecipientUsdt = r?.Accepts(CurrencyCode.USDT) ?? false;
        if (!RecipientCurrencies.Contains(FormPaidCurrency)) FormPaidCurrency = RecipientCurrencies.FirstOrDefault(CurrencyCode.USD);
    }

    partial void OnFormPaidCurrencyChanged(CurrencyCode value)
    {
        OnPropertyChanged(nameof(NeedsRate));
        UpdatePreview();
    }

    partial void OnFormSettledUsdChanged(string value) => UpdatePreview();
    partial void OnFormRateChanged(string value) => UpdatePreview();

    private ExchangeRate? BuildRate(bool strict)
    {
        if (!NeedsRate) return null;
        if (strict) return new ExchangeRate(CurrencyCode.USD, FormPaidCurrency, ParseAmount(FormRate, "نرخ"));
        return Domain.Text.DigitNormalizer.TryParseDecimal(FormRate, out var r) && r > 0 ? new ExchangeRate(CurrencyCode.USD, FormPaidCurrency, r) : null;
    }

    private void UpdatePreview()
    {
        try
        {
            if (!Domain.Text.DigitNormalizer.TryParseDecimal(FormSettledUsd, out var usd)) { FormPaidPreview = ""; return; }
            var rate = BuildRate(strict: false);
            FormPaidPreview = NeedsRate && rate is null ? "" : "مبلغ پرداختی به گیرنده: " + TransferFlowService.ComputePaidAmount(usd, FormPaidCurrency, rate).Format();
        }
        catch (DomainException ex)
        {
            FormPaidPreview = ex.Message;
        }
    }

    [RelayCommand]
    private Task LoadLatestRate() => RunAsync(async () =>
    {
        if (!NeedsRate) return;
        var r = await _rates.LatestAsync(CurrencyCode.USD, FormPaidCurrency);
        if (r is null) { StatusMessage = $"نرخ USD/{FormPaidCurrency} ثبت نشده است."; return; }
        FormRate = r.Rate.ToString("0.########");
    });

    [RelayCommand]
    private async Task Register()
    {
        var ok = await RunAsync(async () =>
        {
            if (FormCase is null) throw new DomainException("input_case", "پرونده را انتخاب کنید.");
            if (FormAgent is null) throw new DomainException("input_agent", "نماینده ارسال‌کننده را انتخاب کنید.");
            if (FormRecipient is null) throw new DomainException("input_recipient", "گیرنده نهایی را انتخاب کنید.");
            var date = _clock.ToUtc(ParseShamsi(FormDate, "تاریخ").Add(_clock.ToLocal(_clock.UtcNow).TimeOfDay));
            await _flow.RegisterChinaReceiptAsync(new ChinaReceiptInput(FormCase.Id, FormAgent.Id, FormRecipient.Id, date,
                ParseAmount(FormSettledUsd, "مبلغ دلاری تسویه"), FormPaidCurrency, BuildRate(strict: true),
                string.IsNullOrWhiteSpace(FormTrackingNo) ? null : FormTrackingNo.Trim(), string.IsNullOrWhiteSpace(FormNotes) ? null : FormNotes));
        }, "دریافت در چین ثبت شد (در مسیر؛ در انتظار تأیید).");
        if (!ok) return;
        var msg = StatusMessage;
        FormSettledUsd = FormRate = FormTrackingNo = FormNotes = "";
        await RunAsync(LoadListAsync);
        StatusMessage = msg;
    }

    [RelayCommand]
    private async Task Approve()
    {
        if (Selected is not { } s) return;
        if (!_dialogs.Confirm($"دریافت {s.Paid} توسط «{s.Recipient}» تأیید شود؟")) return;
        if (await RunAsync(() => _flow.ApproveChinaReceiptAsync(s.Id), "دریافت در چین تأیید شد.")) await RunAsync(LoadListAsync);
    }

    [RelayCommand]
    private async Task Reject()
    {
        if (Selected is not { } s) return;
        var reason = _dialogs.Prompt("دلیل رد:", "رد دریافت در چین");
        if (string.IsNullOrWhiteSpace(reason)) return;
        if (await RunAsync(() => _flow.RejectChinaReceiptAsync(s.Id, reason), "دریافت رد شد و سند ارسال برگشت داده شد.")) await RunAsync(LoadListAsync);
    }

    [RelayCommand]
    private async Task Reverse()
    {
        if (Selected is not { } s) return;
        var reason = _dialogs.Prompt("دلیل برگشت دریافت تأییدشده:", "برگشت سند");
        if (string.IsNullOrWhiteSpace(reason)) return;
        if (await RunAsync(() => _reversals.ReverseChinaReceiptAsync(s.Id, reason), "سند برگشتی ثبت شد.")) await RunAsync(LoadListAsync);
    }

    [RelayCommand]
    private async Task SaveRecipientCurrencies()
    {
        if (FormRecipient is null) return;
        var list = new List<CurrencyCode>();
        if (RecipientUsd) list.Add(CurrencyCode.USD);
        if (RecipientCny) list.Add(CurrencyCode.CNY);
        if (RecipientUsdt) list.Add(CurrencyCode.USDT);
        var id = FormRecipient.Id;
        if (await RunAsync(() => _master.UpdateRecipientCurrenciesAsync(id, list), "ارزهای قابل دریافت گیرنده ذخیره شد."))
        {
            await LoadAsync();
            FormRecipient = Recipients.FirstOrDefault(r => r.Id == id);
        }
    }

    [RelayCommand]
    private async Task AddRecipient()
    {
        var name = _dialogs.Prompt("نام گیرنده نهایی جدید (در چین):", "افزودن گیرنده");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (await RunAsync(() => _master.AddRecipientAsync(name.Trim(), "چین", new[] { CurrencyCode.USD }))) await LoadAsync();
    }
}
