using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LenjExchange.Application.Reporting;
using LenjExchange.Desktop.Services;
using LenjExchange.Domain.Ledger;

namespace LenjExchange.Desktop.ViewModels;

public sealed record BalanceLine(string Name, string Amounts);
public sealed record RecentRow(string EntryNumber, string Date, string Description, string Type, string Status);

public sealed partial class DashboardViewModel : ViewModelBase
{
    private readonly ReportService _reports;
    private readonly NavigationService _nav;

    [ObservableProperty] private int _customerCount;
    [ObservableProperty] private int _openCases;
    [ObservableProperty] private int _settledCases;
    [ObservableProperty] private int _discrepancyCases;
    [ObservableProperty] private string _todayReceipts = "—";
    [ObservableProperty] private string _todayPayments = "—";
    [ObservableProperty] private IReadOnlyList<BalanceLine> _exchangerOutstanding = Array.Empty<BalanceLine>();
    [ObservableProperty] private IReadOnlyList<BalanceLine> _agentUsdHeld = Array.Empty<BalanceLine>();
    [ObservableProperty] private IReadOnlyList<BalanceLine> _recipientReceipts = Array.Empty<BalanceLine>();
    [ObservableProperty] private IReadOnlyList<RecentRow> _latest = Array.Empty<RecentRow>();

    public DashboardViewModel(ReportService reports, NavigationService nav)
    {
        _reports = reports;
        _nav = nav;
    }

    public override string Title => "داشبورد";

    public override Task LoadAsync() => RunAsync(async () =>
    {
        var d = await _reports.DashboardAsync();
        CustomerCount = d.CustomerCount;
        OpenCases = d.OpenCases;
        SettledCases = d.SettledCases;
        DiscrepancyCases = d.DiscrepancyCases;
        TodayReceipts = Join(d.TodayReceipts);
        TodayPayments = Join(d.TodayPayments);
        ExchangerOutstanding = d.ExchangerOutstanding.Select(Line).ToList();
        AgentUsdHeld = d.AgentUsdHeld.Select(Line).ToList();
        RecipientReceipts = d.RecipientReceipts.Select(Line).ToList();
        Latest = d.LatestTransactions.Select(t => new RecentRow(t.EntryNumber, Display.DateTime(t.DateUtc), t.Description,
            LedgerText.Persian(t.Type), LedgerText.Persian(t.Status))).ToList();
    });

    private static string Join(IReadOnlyList<MoneyByCurrency> items) =>
        items.Count == 0 ? "—" : string.Join("  |  ", items.Select(i => i.Formatted));

    private static BalanceLine Line(NamedBalances b) => new(b.Name, Join(b.Balances));

    [RelayCommand] private Task Refresh() => LoadAsync();
    [RelayCommand] private Task NewReceipt() => _nav.NavigateAsync("receipt");
    [RelayCommand] private Task OpenCasesPage() => _nav.NavigateAsync("cases");
}
