using LenjExchange.Domain.Ledger;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Transfers;

namespace LenjExchange.Application.Reporting;

public enum StatusFilter
{
    /// <summary>All ledger entries: posted, reversed originals and their reversal entries.</summary>
    AllPosted = 0,
    /// <summary>Hides reversed originals and their reversal entries (they net to zero, so balances are unchanged).</summary>
    ExcludeReversed = 1
}

public sealed record StatementQuery(
    Guid AccountId,
    DateRange Range,
    CurrencyCode? Currency = null,
    OperationType? OperationType = null,
    StatusFilter Status = StatusFilter.AllPosted);

public sealed record StatementRow(
    string EntryNumber,
    DateTime DateUtc,
    string ShamsiDate,
    string Description,
    CurrencyCode Currency,
    decimal Debit,
    decimal Credit,
    decimal Balance,
    OperationType OperationType,
    JournalStatus Status,
    Guid JournalEntryId);

public sealed record StatementCurrencySection(
    CurrencyCode Currency,
    decimal Opening,
    decimal TotalDebit,
    decimal TotalCredit,
    decimal Closing,
    IReadOnlyList<StatementRow> Rows)
{
    public static string Side(decimal balance) => balance > 0 ? "بدهکار" : balance < 0 ? "بستانکار" : "—";
}

public sealed record StatementReport(
    Guid AccountId,
    string AccountCode,
    string AccountName,
    DateRange Range,
    IReadOnlyList<StatementCurrencySection> Sections,
    DateTime GeneratedAtUtc);

public sealed record CustomerSummaryRow(
    Guid CustomerId,
    string Code,
    string FullName,
    CurrencyCode Currency,
    int TransactionCount,
    decimal TotalReceived,
    decimal TotalPaid,
    decimal Balance,
    DateTime? LastTransactionUtc)
{
    public string Side => StatementCurrencySection.Side(Balance);
}

public sealed record AllCustomersReport(DateRange Range, IReadOnlyList<CustomerSummaryRow> Rows, DateTime GeneratedAtUtc)
{
    /// <summary>Totals per currency — never summed across currencies.</summary>
    public IReadOnlyList<(CurrencyCode Currency, decimal Received, decimal Paid, decimal Balance)> Totals =>
        Rows.GroupBy(r => r.Currency).OrderBy(g => g.Key)
            .Select(g => (g.Key, g.Sum(r => r.TotalReceived), g.Sum(r => r.TotalPaid), g.Sum(r => r.Balance))).ToList();
}

public sealed record PartyBalanceRow(Guid AccountId, string AccountName, Guid? PartyId, CurrencyCode Currency,
    decimal PeriodDebit, decimal PeriodCredit, decimal Balance);

public sealed record PartyReport(AccountCategory Category, DateRange Range, IReadOnlyList<PartyBalanceRow> Rows);

public sealed record OpenCaseRow(Guid CaseId, string CaseNumber, string CustomerName, TransferStatus Status, CurrencyCode Currency,
    decimal Target, decimal Committed, decimal DeliveredToAgents, decimal InTransit, decimal ReceivedInChina, decimal Unsettled,
    IReadOnlyList<string> Issues, DateTime CreatedAtUtc);

public sealed record MoneyByCurrency(CurrencyCode Currency, decimal Amount)
{
    public string Formatted => new Money(Amount, Currency).Format();
}

public sealed record NamedBalances(string Name, IReadOnlyList<MoneyByCurrency> Balances);

public sealed record RecentTransaction(string EntryNumber, DateTime DateUtc, string Description, OperationType Type, JournalStatus Status);

public sealed record DashboardSummary(
    int CustomerCount,
    IReadOnlyList<MoneyByCurrency> TodayReceipts,
    IReadOnlyList<MoneyByCurrency> TodayPayments,
    int OpenCases,
    int SettledCases,
    int DiscrepancyCases,
    IReadOnlyList<NamedBalances> ExchangerOutstanding,
    IReadOnlyList<NamedBalances> AgentUsdHeld,
    IReadOnlyList<NamedBalances> RecipientReceipts,
    IReadOnlyList<RecentTransaction> LatestTransactions);
