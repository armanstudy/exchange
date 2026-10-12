using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Common;
using LenjExchange.Application.Ledger;
using LenjExchange.Domain.Ledger;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Security;
using LenjExchange.Domain.Shamsi;
using LenjExchange.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace LenjExchange.Application.Reporting;

public sealed class ReportService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly LedgerService _ledger;

    public ReportService(IAppDbContext db, ICurrentUser user, IClock clock, LedgerService ledger)
    {
        _db = db;
        _user = user;
        _clock = clock;
        _ledger = ledger;
    }

    public async Task<StatementReport> CustomerStatementAsync(Guid customerId, DateRange range, CurrencyCode? currency = null,
        OperationType? type = null, StatusFilter status = StatusFilter.AllPosted, CancellationToken ct = default)
    {
        var acc = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.LinkedEntityId == customerId && a.Category == AccountCategory.Customer, ct)
                  ?? throw new NotFoundException("حساب مشتری");
        return await StatementAsync(new StatementQuery(acc.Id, range, currency, type, status), ct);
    }

    /// <summary>
    /// Account statement. Opening balance = all posted lines before the period; each currency is computed
    /// independently: closing = opening + Σdebit − Σcredit.
    /// </summary>
    public async Task<StatementReport> StatementAsync(StatementQuery q, CancellationToken ct = default)
    {
        _user.Demand(Permission.ViewReports);
        var acc = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == q.AccountId, ct) ?? throw new NotFoundException("حساب");
        var rows = await _ledger.PostedLines()
            .Where(x => x.Line.AccountId == acc.Id && x.Entry.DateUtc < q.Range.ToUtcExclusive)
            .ToListAsync(ct);
        if (q.Currency is { } cur) rows = rows.Where(r => r.Line.Currency == cur).ToList();
        if (q.Status == StatusFilter.ExcludeReversed)
            rows = rows.Where(r => r.Entry.Status != JournalStatus.Reversed && r.Entry.ReversalOfEntryId == null).ToList();

        var sections = new List<StatementCurrencySection>();
        foreach (var g in rows.GroupBy(r => r.Line.Currency).OrderBy(g => g.Key))
        {
            var opening = g.Where(r => r.Entry.DateUtc < q.Range.FromUtc).Sum(r => r.Line.Debit - r.Line.Credit);
            var period = g.Where(r => r.Entry.DateUtc >= q.Range.FromUtc)
                .Where(r => q.OperationType is null || r.Entry.OperationType == q.OperationType)
                .OrderBy(r => r.Entry.DateUtc).ThenBy(r => r.Entry.PostedAtUtc).ThenBy(r => r.Line.LineNo).ToList();
            // With an operation-type filter, running balance covers only the filtered rows (documented behaviour).
            var running = opening;
            var list = new List<StatementRow>();
            foreach (var r in period)
            {
                running += r.Line.Debit - r.Line.Credit;
                list.Add(new StatementRow(r.Entry.EntryNumber ?? "", r.Entry.DateUtc, ShamsiDate.Format(_clock.ToLocal(r.Entry.DateUtc)),
                    r.Line.Description is { Length: > 0 } d ? $"{r.Entry.Description} — {d}" : r.Entry.Description,
                    g.Key, r.Line.Debit, r.Line.Credit, running, r.Entry.OperationType, r.Entry.Status, r.Entry.Id));
            }
            var td = list.Sum(r => r.Debit);
            var tc = list.Sum(r => r.Credit);
            if (opening == 0 && list.Count == 0) continue;
            sections.Add(new StatementCurrencySection(g.Key, opening, td, tc, opening + td - tc, list));
        }
        return new StatementReport(acc.Id, acc.Code, acc.Name, q.Range, sections, _clock.UtcNow);
    }

    /// <summary>All customers, one row per customer per currency; balances as of the end of the range.</summary>
    public async Task<AllCustomersReport> AllCustomersAsync(DateRange range, CurrencyCode? currency = null, bool includeZero = false, CancellationToken ct = default)
    {
        _user.Demand(Permission.ViewReports);
        var customers = await _db.Customers.AsNoTracking().Where(c => !c.IsDeleted).ToListAsync(ct);
        var accounts = await _db.Accounts.AsNoTracking().Where(a => a.Category == AccountCategory.Customer).ToListAsync(ct);
        var accByCustomer = accounts.Where(a => a.LinkedEntityId != null).ToDictionary(a => a.LinkedEntityId!.Value);
        var accIds = accounts.Select(a => a.Id).ToList();
        var lines = await _ledger.PostedLines()
            .Where(x => accIds.Contains(x.Line.AccountId) && x.Entry.DateUtc < range.ToUtcExclusive)
            .ToListAsync(ct);
        if (currency is { } cur) lines = lines.Where(l => l.Line.Currency == cur).ToList();
        var byAccount = lines.ToLookup(l => l.Line.AccountId);

        var rows = new List<CustomerSummaryRow>();
        foreach (var c in customers.OrderBy(c => c.Code))
        {
            if (!accByCustomer.TryGetValue(c.Id, out var acc)) continue;
            foreach (var g in byAccount[acc.Id].GroupBy(l => l.Line.Currency).OrderBy(g => g.Key))
            {
                var inPeriod = g.Where(l => l.Entry.DateUtc >= range.FromUtc).ToList();
                var balance = g.Sum(l => l.Line.Debit - l.Line.Credit);
                if (!includeZero && inPeriod.Count == 0 && balance == 0) continue;
                rows.Add(new CustomerSummaryRow(c.Id, c.Code, c.FullName, g.Key,
                    inPeriod.Select(l => l.Entry.Id).Distinct().Count(),
                    inPeriod.Sum(l => l.Line.Credit),
                    inPeriod.Sum(l => l.Line.Debit),
                    balance,
                    g.Max(l => (DateTime?)l.Entry.DateUtc)));
            }
        }
        return new AllCustomersReport(range, rows, _clock.UtcNow);
    }

    /// <summary>Exchanger, agent, recipient (Mohammad) or any category balances with period movements.</summary>
    public async Task<PartyReport> PartyBalancesAsync(AccountCategory category, DateRange range, CancellationToken ct = default)
    {
        _user.Demand(Permission.ViewReports);
        var accounts = await _db.Accounts.AsNoTracking().Where(a => a.Category == category).ToListAsync(ct);
        var ids = accounts.Select(a => a.Id).ToList();
        var lines = await _ledger.PostedLines().Where(x => ids.Contains(x.Line.AccountId) && x.Entry.DateUtc < range.ToUtcExclusive).ToListAsync(ct);
        var rows = new List<PartyBalanceRow>();
        foreach (var a in accounts.OrderBy(a => a.Name))
            foreach (var g in lines.Where(l => l.Line.AccountId == a.Id).GroupBy(l => l.Line.Currency).OrderBy(g => g.Key))
            {
                var p = g.Where(l => l.Entry.DateUtc >= range.FromUtc).ToList();
                rows.Add(new PartyBalanceRow(a.Id, a.Name, a.LinkedEntityId, g.Key, p.Sum(l => l.Line.Debit), p.Sum(l => l.Line.Credit),
                    g.Sum(l => l.Line.Debit - l.Line.Credit)));
            }
        return new PartyReport(category, range, rows);
    }

    /// <summary>In-transit and unsettled cases (everything not draft/settled/cancelled), with amounts per stage.</summary>
    public async Task<IReadOnlyList<OpenCaseRow>> OpenCasesAsync(CancellationToken ct = default)
    {
        _user.Demand(Permission.ViewReports);
        var cases = await _db.TransferCases.AsNoTracking()
            .Where(c => c.Status != TransferStatus.Settled && c.Status != TransferStatus.Cancelled && c.Status != TransferStatus.Draft)
            .ToListAsync(ct);
        var ids = cases.Select(c => c.Id).ToList();
        var customers = await _db.Customers.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.FullName, ct);
        var receipts = (await _db.CustomerReceipts.AsNoTracking().Include(r => r.Deposits).Where(r => ids.Contains(r.TransferCaseId)).ToListAsync(ct)).ToLookup(r => r.TransferCaseId);
        var commits = (await _db.ExchangerCommitments.AsNoTracking().Where(r => ids.Contains(r.TransferCaseId)).ToListAsync(ct)).ToLookup(r => r.TransferCaseId);
        var deliveries = (await _db.AgentDeliveries.AsNoTracking().Where(r => ids.Contains(r.TransferCaseId)).ToListAsync(ct)).ToLookup(r => r.TransferCaseId);
        var china = (await _db.ChinaReceipts.AsNoTracking().Where(r => ids.Contains(r.TransferCaseId)).ToListAsync(ct)).ToLookup(r => r.TransferCaseId);

        return cases.Select(c =>
        {
            var s = TransferStatusCalculator.Calculate(c, receipts[c.Id], commits[c.Id], deliveries[c.Id], china[c.Id]);
            return new OpenCaseRow(c.Id, c.CaseNumber, customers.GetValueOrDefault(c.CustomerId, ""), s.Status, c.TargetCurrency,
                s.Target.Amount, s.Committed.Amount, s.DeliveredToAgents.Amount, s.InTransit.Amount, s.ReceivedInChina.Amount,
                s.Unsettled.Amount, s.Issues, c.CreatedAtUtc);
        }).OrderBy(r => r.CreatedAtUtc).ToList();
    }

    public async Task<DashboardSummary> DashboardAsync(CancellationToken ct = default)
    {
        _user.Demand(Permission.ViewDashboard);
        var today = DateRanges.For(_clock, DatePreset.Today);
        var customerCount = await _db.Customers.CountAsync(c => !c.IsDeleted, ct);

        var custAccIds = await _db.Accounts.Where(a => a.Category == AccountCategory.Customer).Select(a => a.Id).ToListAsync(ct);
        var todayLines = await _ledger.PostedLines()
            .Where(x => custAccIds.Contains(x.Line.AccountId) && x.Entry.DateUtc >= today.FromUtc && x.Entry.DateUtc < today.ToUtcExclusive)
            .ToListAsync(ct);
        // Taken from the receipts themselves: the customer account also carries conversion legs.
        var todayReceipts = (await _db.CustomerReceipts.AsNoTracking()
                .Where(r => r.Status == DocumentStatus.Posted && r.DateUtc >= today.FromUtc && r.DateUtc < today.ToUtcExclusive)
                .Select(r => new { r.Currency, r.Amount }).ToListAsync(ct))
            .GroupBy(r => r.Currency).Select(g => new MoneyByCurrency(g.Key, g.Sum(r => r.Amount))).OrderBy(m => m.Currency).ToList();
        var todayPayments = todayLines.Where(l => l.Entry.OperationType == OperationType.CustomerPayment)
            .GroupBy(l => l.Line.Currency).Select(g => new MoneyByCurrency(g.Key, g.Sum(l => l.Line.Debit))).OrderBy(m => m.Currency).ToList();

        var statuses = await _db.TransferCases.AsNoTracking().Select(c => c.Status).ToListAsync(ct);
        var open = statuses.Count(s => s is not (TransferStatus.Settled or TransferStatus.Cancelled or TransferStatus.Draft));
        var settled = statuses.Count(s => s == TransferStatus.Settled);
        var disc = statuses.Count(s => s == TransferStatus.Discrepancy);

        async Task<IReadOnlyList<NamedBalances>> Balances(AccountCategory cat, Func<IEnumerable<PostedLine>, IEnumerable<MoneyByCurrency>> project)
        {
            var accs = await _db.Accounts.AsNoTracking().Where(a => a.Category == cat).ToListAsync(ct);
            var ids = accs.Select(a => a.Id).ToList();
            var lines = (await _ledger.PostedLines().Where(x => ids.Contains(x.Line.AccountId)).ToListAsync(ct)).ToLookup(l => l.Line.AccountId);
            return accs.OrderBy(a => a.Name).Select(a => new NamedBalances(a.Name, project(lines[a.Id]).Where(m => m.Amount != 0).ToList())).ToList();
        }

        var exch = await Balances(AccountCategory.Exchanger, ls => ls.GroupBy(l => l.Line.Currency).Select(g => new MoneyByCurrency(g.Key, g.Sum(l => l.Line.Debit - l.Line.Credit))));
        var agents = await Balances(AccountCategory.AgentFunds, ls => ls.Where(l => l.Line.Currency == CurrencyCode.USD).GroupBy(l => l.Line.Currency).Select(g => new MoneyByCurrency(g.Key, g.Sum(l => l.Line.Debit - l.Line.Credit))));
        var recips = await Balances(AccountCategory.Recipient, ls => ls.GroupBy(l => l.Line.Currency).Select(g => new MoneyByCurrency(g.Key, g.Sum(l => l.Line.Debit - l.Line.Credit))));

        var latest = await _db.JournalEntries.AsNoTracking().Where(e => e.Status != JournalStatus.Draft)
            .OrderByDescending(e => e.PostedAtUtc).Take(15)
            .Select(e => new RecentTransaction(e.EntryNumber ?? "", e.DateUtc, e.Description, e.OperationType, e.Status)).ToListAsync(ct);

        return new DashboardSummary(customerCount, todayReceipts, todayPayments, open, settled, disc, exch, agents, recips, latest);
    }
}
