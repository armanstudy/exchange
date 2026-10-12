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
using LenjExchange.Domain.Security;
using LenjExchange.Domain.Shamsi;
using LenjExchange.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace LenjExchange.Desktop.ViewModels;

public sealed record CommitmentRow(Guid Id, string CaseNumber, string Exchanger, string Date, string Paid, string Rate, string Committed,
    string Delivered, string Outstanding, bool Ready, string Status, Guid CaseId, decimal OutstandingUsd)
{
    public string ReadyText => Ready ? "آماده تحویل" : "در انتظار";
}

/// <summary>Turkish exchangers: Toman paid to them, the USD they committed, and USD settlements to agents.</summary>
public sealed partial class ExchangersViewModel : ViewModelBase
{
    private readonly MasterDataService _master;
    private readonly TransferFlowService _flow;
    private readonly ReversalService _reversals;
    private readonly RateService _rates;
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IDialogService _dialogs;

    [ObservableProperty] private IReadOnlyList<Lookup> _exchangers = Array.Empty<Lookup>();
    [ObservableProperty] private Lookup? _exchangerFilter;
    [ObservableProperty] private IReadOnlyList<Lookup> _openCases = Array.Empty<Lookup>();
    [ObservableProperty] private IReadOnlyList<Lookup> _agents = Array.Empty<Lookup>();
    [ObservableProperty] private IReadOnlyList<Lookup> _banks = Array.Empty<Lookup>();
    [ObservableProperty] private CommitmentRow? _selected;

    [ObservableProperty] private Lookup? _formCase;
    [ObservableProperty] private Lookup? _formExchanger;
    [ObservableProperty] private Lookup? _formBank;
    [ObservableProperty] private string _formDate = "";
    [ObservableProperty] private string _formPaid = "";
    [ObservableProperty] private CurrencyCode _formPaidCurrency = CurrencyCode.IRT;
    [ObservableProperty] private string _formRate = "";
    [ObservableProperty] private string _formNotes = "";
    [ObservableProperty] private string _formPreview = "";

    [ObservableProperty] private Lookup? _deliveryAgent;
    [ObservableProperty] private string _deliveryAmount = "";
    [ObservableProperty] private string _deliveryDate = "";
    [ObservableProperty] private string _deliveryExternalNo = "";

    public ObservableCollection<CommitmentRow> Items { get; } = new();
    public IReadOnlyList<CurrencyCode> IranianCurrencies { get; } = new[] { CurrencyCode.IRT, CurrencyCode.IRR };
    public bool CanManageMasterData => _user.Has(Permission.ManageMasterData);
    public bool CanReverse => _user.Has(Permission.ReverseEntries);

    public ExchangersViewModel(MasterDataService master, TransferFlowService flow, ReversalService reversals, RateService rates,
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

    public override string Title => "صرافی‌های ترکیه و تعهدات دلاری";

    public override Task LoadAsync() => RunAsync(async () =>
    {
        Exchangers = (await _master.ExchangersAsync()).Where(e => e.IsActive).Select(e => new Lookup(e.Id, e.Name)).ToList();
        Agents = (await _master.AgentsAsync()).Where(a => a.IsActive).Select(a => new Lookup(a.Id, a.Name)).ToList();
        Banks = (await _master.BanksAsync()).Where(b => b.IsActive).Select(b => new Lookup(b.Id, b.Name)).ToList();
        var customers = await _db.Customers.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.FullName);
        OpenCases = (await _db.TransferCases.AsNoTracking()
                .Where(c => !c.IsCancelled && c.Status != TransferStatus.Settled && c.Status != TransferStatus.Draft)
                .OrderByDescending(c => c.CreatedAtUtc).ToListAsync())
            .Select(c => new Lookup(c.Id, $"{c.CaseNumber} - {customers.GetValueOrDefault(c.CustomerId, "")}")).ToList();
        FormDate = DeliveryDate = ShamsiDate.Format(_clock.LocalToday());
        FormExchanger ??= Exchangers.FirstOrDefault();
        await LoadCommitmentsAsync();
    });

    partial void OnExchangerFilterChanged(Lookup? value) => _ = RunAsync(LoadCommitmentsAsync);
    partial void OnFormPaidChanged(string value) => UpdatePreview();
    partial void OnFormRateChanged(string value) => UpdatePreview();
    partial void OnFormPaidCurrencyChanged(CurrencyCode value) => UpdatePreview();

    partial void OnSelectedChanged(CommitmentRow? value)
    {
        if (value is not null && value.OutstandingUsd > 0) DeliveryAmount = value.OutstandingUsd.ToString("0.##");
    }

    private void UpdatePreview()
    {
        FormPreview = Domain.Text.DigitNormalizer.TryParseDecimal(FormPaid, out var paid) &&
                      Domain.Text.DigitNormalizer.TryParseDecimal(FormRate, out var rate) && rate > 0
            ? "تعهد دلاری: " + new ExchangeRate(CurrencyCode.USD, CurrencyCode.IRT, rate).Convert(new Money(paid, FormPaidCurrency), CurrencyCode.USD).Format()
            : "";
    }

    private async Task LoadCommitmentsAsync()
    {
        var q = _db.ExchangerCommitments.AsNoTracking();
        if (ExchangerFilter is { } f) q = q.Where(c => c.ExchangerId == f.Id);
        var list = await q.OrderByDescending(c => c.DateUtc).Take(1000).ToListAsync();
        var ids = list.Select(c => c.Id).ToList();
        var delivered = (await _db.AgentDeliveries.AsNoTracking().Where(d => ids.Contains(d.ExchangerCommitmentId)).ToListAsync())
            .Where(d => d.Counts || (d.Approval == ApprovalStatus.Pending && !d.IsReversed))
            .GroupBy(d => d.ExchangerCommitmentId).ToDictionary(g => g.Key, g => g.Sum(d => d.Amount));
        var caseIds = list.Select(c => c.TransferCaseId).Distinct().ToList();
        var cases = await _db.TransferCases.AsNoTracking().Where(c => caseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.CaseNumber);
        var names = Exchangers.ToDictionary(e => e.Id, e => e.Name);

        Items.Clear();
        foreach (var c in list)
        {
            var del = delivered.GetValueOrDefault(c.Id);
            var outstanding = c.Status == DocumentStatus.Posted ? c.CommittedAmount - del : 0;
            Items.Add(new CommitmentRow(c.Id, cases.GetValueOrDefault(c.TransferCaseId, ""), names.GetValueOrDefault(c.ExchangerId, "—"),
                Display.Date(c.DateUtc), c.Paid.Format(), c.Rate?.Describe() ?? "—", c.Committed.Format(),
                Display.Money(del, c.CommittedCurrency), Display.Money(outstanding, c.CommittedCurrency), c.ReadyForDelivery,
                TransferStatusText.Persian(c.Status), c.TransferCaseId, outstanding));
        }
    }

    [RelayCommand]
    private Task LoadLatestRate() => RunAsync(async () =>
    {
        var r = await _rates.LatestAsync(CurrencyCode.USD, CurrencyCode.IRT);
        if (r is null) { StatusMessage = "نرخ USD/IRT ثبت نشده است."; return; }
        FormRate = r.Rate.ToString("0.########");
    });

    [RelayCommand]
    private async Task CreateCommitment()
    {
        var ok = await RunAsync(async () =>
        {
            if (FormCase is null) throw new DomainException("input_case", "پرونده را انتخاب کنید.");
            if (FormExchanger is null) throw new DomainException("input_exchanger", "صرافی را انتخاب کنید.");
            var rate = new ExchangeRate(CurrencyCode.USD, CurrencyCode.IRT, ParseAmount(FormRate, "نرخ"));
            var date = _clock.ToUtc(ParseShamsi(FormDate, "تاریخ").Add(_clock.ToLocal(_clock.UtcNow).TimeOfDay));
            await _flow.CreateCommitmentAsync(new CommitmentInput(FormCase.Id, FormExchanger.Id, date, ParseAmount(FormPaid, "مبلغ پرداختی"),
                FormPaidCurrency, rate, CurrencyCode.USD, FormBank?.Id, string.IsNullOrWhiteSpace(FormNotes) ? null : FormNotes));
        }, "تعهد صرافی ثبت و سند حسابداری آن قطعی شد.");
        if (!ok) return;
        var msg = StatusMessage;
        FormPaid = FormRate = FormNotes = "";
        await RunAsync(LoadCommitmentsAsync);
        StatusMessage = msg;
    }

    [RelayCommand]
    private async Task MarkReady()
    {
        if (Selected is not { } s) return;
        if (await RunAsync(() => _flow.MarkReadyForDeliveryAsync(s.Id), "تعهد آماده تحویل دلار علامت خورد.")) await RunAsync(LoadCommitmentsAsync);
    }

    [RelayCommand]
    private async Task RegisterDelivery()
    {
        if (Selected is not { } s) { ErrorMessage = "ابتدا تعهد صرافی را از جدول انتخاب کنید."; return; }
        AgentDelivery? delivery = null;
        var ok = await RunAsync(async () =>
        {
            if (DeliveryAgent is null) throw new DomainException("input_agent", "نماینده دریافت‌کننده را انتخاب کنید.");
            var date = _clock.ToUtc(ParseShamsi(DeliveryDate, "تاریخ تحویل").Add(_clock.ToLocal(_clock.UtcNow).TimeOfDay));
            delivery = await _flow.RegisterDeliveryAsync(new DeliveryInput(s.Id, DeliveryAgent.Id, date, ParseAmount(DeliveryAmount, "مبلغ دلار"),
                string.IsNullOrWhiteSpace(DeliveryExternalNo) ? null : DeliveryExternalNo.Trim()));
        });
        if (!ok) return;
        await RunAsync(LoadCommitmentsAsync);
        StatusMessage = $"تحویل با کد رهگیری {delivery!.TrackingNo} ثبت شد و در انتظار تأیید نماینده است.";
    }

    [RelayCommand]
    private async Task Reverse()
    {
        if (Selected is not { } s) return;
        var reason = _dialogs.Prompt("دلیل برگشت تعهد صرافی:", "برگشت سند");
        if (string.IsNullOrWhiteSpace(reason)) return;
        if (await RunAsync(() => _reversals.ReverseCommitmentAsync(s.Id, reason), "سند برگشتی ثبت شد.")) await RunAsync(LoadCommitmentsAsync);
    }

    [RelayCommand]
    private async Task AddExchanger()
    {
        var name = _dialogs.Prompt("نام صرافی جدید:", "افزودن صرافی");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (await RunAsync(() => _master.AddExchangerAsync(name.Trim()), "صرافی افزوده شد.")) await LoadAsync();
    }

    [RelayCommand] private Task Refresh() => LoadAsync();
}
