using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Transfers;
using LenjExchange.Desktop.Services;
using LenjExchange.Domain.Ledger;
using LenjExchange.Domain.Security;
using LenjExchange.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace LenjExchange.Desktop.ViewModels;

public sealed record CaseRow(Guid Id, string CaseNumber, string Customer, string Status, string Target, string Created, bool Discrepancy);
public sealed record TimelineRow(string Date, string Kind, string Title, string? Amount, string Status, Guid DocumentId, string KindKey);
public sealed record JournalRow(string EntryNumber, string Date, string Type, string Status, string Description, string Lines);

public sealed partial class CasesViewModel : ViewModelBase
{
    private readonly TransferCaseService _cases;
    private readonly ReversalService _reversals;
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IDialogService _dialogs;

    [ObservableProperty] private Option<TransferStatus?>? _statusFilter;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private CaseRow? _selected;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private IReadOnlyList<string> _issues = Array.Empty<string>();
    [ObservableProperty] private TimelineRow? _selectedEvent;

    public ObservableCollection<CaseRow> Items { get; } = new();
    public ObservableCollection<TimelineRow> Timeline { get; } = new();
    public ObservableCollection<JournalRow> Journal { get; } = new();

    public IReadOnlyList<Option<TransferStatus?>> StatusOptions { get; } =
        new[] { new Option<TransferStatus?>(null, "همه وضعیت‌ها") }
            .Concat(Enum.GetValues<TransferStatus>().Select(s => new Option<TransferStatus?>(s, TransferStatusText.Persian(s)))).ToList();

    public bool CanReverse => _user.Has(Permission.ReverseEntries);

    public CasesViewModel(TransferCaseService cases, ReversalService reversals, IAppDbContext db, ICurrentUser user, IDialogService dialogs)
    {
        _cases = cases;
        _reversals = reversals;
        _db = db;
        _user = user;
        _dialogs = dialogs;
        _statusFilter = StatusOptions[0];
    }

    public override string Title => "پرونده‌های انتقال";

    public override Task LoadAsync() => RunAsync(async () =>
    {
        var list = await _cases.ListAsync(StatusFilter?.Value);
        var names = await _db.Customers.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.FullName);
        var term = Search.Trim();
        Items.Clear();
        foreach (var c in list)
        {
            var name = names.GetValueOrDefault(c.CustomerId, "");
            if (term.Length > 0 && !c.CaseNumber.Contains(term, StringComparison.OrdinalIgnoreCase) && !name.Contains(term)) continue;
            Items.Add(new CaseRow(c.Id, c.CaseNumber, name, TransferStatusText.Persian(c.Status), c.TargetCurrency.ToString(),
                Display.Date(c.CreatedAtUtc), c.DiscrepancyFlag));
        }
    });

    partial void OnSelectedChanged(CaseRow? value) => _ = LoadDetailAsync();

    private Task LoadDetailAsync() => RunAsync(async () =>
    {
        Timeline.Clear();
        Journal.Clear();
        Summary = "";
        Issues = Array.Empty<string>();
        if (Selected is null) return;
        var d = await _cases.GetDetailAsync(Selected.Id);
        var s = d.Snapshot;
        Summary = $"مشتری: {d.CustomerName}   |   وضعیت: {TransferStatusText.Persian(s.Status)}\n" +
                  $"مبلغ هدف: {s.Target.Format()}   |   تعهد صرافی: {s.Committed.Format()}   |   تحویل به نماینده: {s.DeliveredToAgents.Format()}\n" +
                  $"در مسیر چین: {s.InTransit.Format()}   |   دریافت در چین: {s.ReceivedInChina.Format()}   |   مانده تسویه‌نشده: {s.Unsettled.Format()}";
        Issues = s.Issues;
        foreach (var t in d.Timeline)
            Timeline.Add(new TimelineRow(Display.DateTime(t.DateUtc), KindText(t.Kind), t.Title, t.Amount, t.Status, t.DocumentId, t.Kind));
        foreach (var e in d.JournalEntries)
            Journal.Add(new JournalRow(e.EntryNumber ?? "—", Display.DateTime(e.DateUtc), LedgerText.Persian(e.OperationType), LedgerText.Persian(e.Status),
                e.Description, string.Join("\n", e.Lines.Select(l => $"{(l.Debit > 0 ? "بد" : "بس")} {Display.Money(l.Debit > 0 ? l.Debit : l.Credit, l.Currency)} — {l.Description}"))));
    });

    private static string KindText(string kind) => kind switch
    {
        "receipt" => "رسید مشتری",
        "commitment" => "تعهد صرافی",
        "delivery" => "تحویل به نماینده",
        "china" => "دریافت در چین",
        _ => kind
    };

    [RelayCommand] private Task Find() => LoadAsync();
    [RelayCommand] private Task RefreshDetail() => LoadDetailAsync();

    [RelayCommand]
    private async Task ReverseEvent()
    {
        if (SelectedEvent is not { } ev) { ErrorMessage = "یک رویداد از خط زمانی را انتخاب کنید."; return; }
        var reason = _dialogs.Prompt($"دلیل برگشت «{ev.Title}» را بنویسید. یک سند برگشتی معکوس ثبت می‌شود و سند اصلی حذف نمی‌شود.", "برگشت سند");
        if (string.IsNullOrWhiteSpace(reason)) return;
        var ok = await RunAsync(() => ev.KindKey switch
        {
            "receipt" => _reversals.ReverseReceiptAsync(ev.DocumentId, reason),
            "commitment" => _reversals.ReverseCommitmentAsync(ev.DocumentId, reason),
            "delivery" => _reversals.ReverseDeliveryAsync(ev.DocumentId, reason),
            "china" => _reversals.ReverseChinaReceiptAsync(ev.DocumentId, reason),
            _ => Task.CompletedTask
        }, "سند برگشتی ثبت شد.");
        if (ok) await LoadDetailAsync();
    }

    [RelayCommand]
    private async Task FlagDiscrepancy()
    {
        if (Selected is not { } c) return;
        var note = _dialogs.Prompt("شرح مغایرت:", "علامت‌گذاری مغایرت");
        if (string.IsNullOrWhiteSpace(note)) return;
        if (await RunAsync(() => _cases.FlagDiscrepancyAsync(c.Id, note), "پرونده دارای مغایرت علامت‌گذاری شد.")) await LoadDetailAsync();
    }

    [RelayCommand]
    private async Task Cancel()
    {
        if (Selected is not { } c) return;
        var reason = _dialogs.Prompt("دلیل ابطال پرونده (اسناد قطعی باید قبلاً برگشت داده شده باشند):", "ابطال پرونده");
        if (string.IsNullOrWhiteSpace(reason)) return;
        if (await RunAsync(() => _cases.CancelAsync(c.Id, reason), "پرونده ابطال شد.")) await LoadAsync();
    }
}
