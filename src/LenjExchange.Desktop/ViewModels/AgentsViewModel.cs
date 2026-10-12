using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LenjExchange.Application.Abstractions;
using LenjExchange.Application.MasterData;
using LenjExchange.Application.Reporting;
using LenjExchange.Application.Transfers;
using LenjExchange.Desktop.Services;
using LenjExchange.Domain.Ledger;
using LenjExchange.Domain.Security;
using LenjExchange.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace LenjExchange.Desktop.ViewModels;

public sealed record DeliveryRow(Guid Id, string TrackingNo, string? ExternalNo, string CaseNumber, string Exchanger, string Agent, string Date,
    string Amount, string Approval, ApprovalStatus ApprovalValue, bool IsReversed);

/// <summary>Turkish agents (آیدین، چاتای …): USD they hold and the exchanger deliveries awaiting their confirmation.</summary>
public sealed partial class AgentsViewModel : ViewModelBase
{
    private readonly MasterDataService _master;
    private readonly TransferFlowService _flow;
    private readonly ReversalService _reversals;
    private readonly ReportService _reports;
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IDialogService _dialogs;

    [ObservableProperty] private bool _pendingOnly = true;
    [ObservableProperty] private DeliveryRow? _selected;

    public ObservableCollection<BalanceLine> Balances { get; } = new();
    public ObservableCollection<DeliveryRow> Items { get; } = new();
    public bool CanApprove => _user.Has(Permission.ApproveDeliveries);
    public bool CanReverse => _user.Has(Permission.ReverseEntries);
    public bool CanManageMasterData => _user.Has(Permission.ManageMasterData);

    public AgentsViewModel(MasterDataService master, TransferFlowService flow, ReversalService reversals, ReportService reports,
        IAppDbContext db, ICurrentUser user, IClock clock, IDialogService dialogs)
    {
        _master = master;
        _flow = flow;
        _reversals = reversals;
        _reports = reports;
        _db = db;
        _user = user;
        _clock = clock;
        _dialogs = dialogs;
    }

    public override string Title => "نمایندگان ترکیه — دلار نزد نماینده";

    public override Task LoadAsync() => RunAsync(async () =>
    {
        var agents = await _master.AgentsAsync();
        var report = await _reports.PartyBalancesAsync(AccountCategory.AgentFunds, DateRanges.For(_clock, DatePreset.All));
        Balances.Clear();
        foreach (var a in agents)
        {
            var rows = report.Rows.Where(r => r.PartyId == a.Id && r.Balance != 0).ToList();
            Balances.Add(new BalanceLine(a.Name, rows.Count == 0 ? "0" : string.Join("  |  ", rows.Select(r => Display.Money(r.Balance, r.Currency)))));
        }

        var q = _db.AgentDeliveries.AsNoTracking();
        if (PendingOnly) q = q.Where(d => d.Approval == ApprovalStatus.Pending && !d.IsReversed);
        var list = await q.OrderByDescending(d => d.DateUtc).Take(1000).ToListAsync();
        var caseIds = list.Select(d => d.TransferCaseId).Distinct().ToList();
        var cases = await _db.TransferCases.AsNoTracking().Where(c => caseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.CaseNumber);
        var exchangers = (await _master.ExchangersAsync()).ToDictionary(e => e.Id, e => e.Name);
        var agentNames = agents.ToDictionary(a => a.Id, a => a.Name);
        Items.Clear();
        foreach (var d in list)
            Items.Add(new DeliveryRow(d.Id, d.TrackingNo, d.ExternalTrackingNo, cases.GetValueOrDefault(d.TransferCaseId, ""),
                exchangers.GetValueOrDefault(d.ExchangerId, ""), agentNames.GetValueOrDefault(d.AgentId, ""), Display.DateTime(d.DateUtc),
                d.Money.Format(), d.IsReversed ? "برگشت‌خورده" : TransferStatusText.Persian(d.Approval), d.Approval, d.IsReversed));
    });

    partial void OnPendingOnlyChanged(bool value) => _ = LoadAsync();

    [RelayCommand]
    private async Task Approve()
    {
        if (Selected is not { } s) return;
        if (!_dialogs.Confirm($"دریافت {s.Amount} توسط «{s.Agent}» (کد {s.TrackingNo}) تأیید شود؟")) return;
        if (await RunAsync(() => _flow.ApproveDeliveryAsync(s.Id))) { await LoadAsync(); StatusMessage = "تحویل تأیید و در دفاتر ثبت شد."; }
    }

    [RelayCommand]
    private async Task Reject()
    {
        if (Selected is not { } s) return;
        if (!_dialogs.Confirm($"تحویل {s.TrackingNo} رد شود؟")) return;
        if (await RunAsync(() => _flow.RejectDeliveryAsync(s.Id))) { await LoadAsync(); StatusMessage = "تحویل رد شد."; }
    }

    [RelayCommand]
    private async Task Reverse()
    {
        if (Selected is not { } s) return;
        var reason = _dialogs.Prompt("دلیل برگشت تحویل تأییدشده:", "برگشت سند");
        if (string.IsNullOrWhiteSpace(reason)) return;
        if (await RunAsync(() => _reversals.ReverseDeliveryAsync(s.Id, reason))) { await LoadAsync(); StatusMessage = "سند برگشتی ثبت شد."; }
    }

    [RelayCommand]
    private async Task AddAgent()
    {
        var name = _dialogs.Prompt("نام نماینده جدید:", "افزودن نماینده");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (await RunAsync(() => _master.AddAgentAsync(name.Trim()))) await LoadAsync();
    }

    [RelayCommand] private Task Refresh() => LoadAsync();
}
