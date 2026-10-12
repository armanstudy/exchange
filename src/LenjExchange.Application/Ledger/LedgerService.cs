using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Common;
using LenjExchange.Application.Numbering;
using LenjExchange.Application.Settings;
using LenjExchange.Domain.Common;
using LenjExchange.Domain.Ledger;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace LenjExchange.Application.Ledger;

public sealed record CurrencyBalance(CurrencyCode Currency, decimal Debit, decimal Credit)
{
    /// <summary>Debit − Credit. Positive = بدهکار, negative = بستانکار.</summary>
    public decimal Balance => Debit - Credit;
    public string Side => Balance > 0 ? "بدهکار" : Balance < 0 ? "بستانکار" : "—";
}

public sealed class LedgerService
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _user;
    private readonly NumberingService _numbering;
    private readonly SettingsService _settings;
    private readonly AuditService _audit;
    private readonly UnitOfWork _uow;

    public LedgerService(IAppDbContext db, IClock clock, ICurrentUser user, NumberingService numbering,
        SettingsService settings, AuditService audit, UnitOfWork uow)
    {
        _db = db;
        _clock = clock;
        _user = user;
        _numbering = numbering;
        _settings = settings;
        _audit = audit;
        _uow = uow;
    }

    public async Task<JournalEntry> NewEntryAsync(DateTime dateUtc, OperationType type, string description,
        Guid? caseId = null, string? sourceType = null, Guid? sourceId = null, CancellationToken ct = default)
    {
        var acc = await _settings.GetAccountingAsync(ct);
        return new JournalEntry(dateUtc, type, description, _user.UserName, _clock.UtcNow, caseId, sourceType, sourceId, acc.BaseCurrency);
    }

    /// <summary>Validates per-currency balance and posts atomically. Unbalanced entries are rejected.</summary>
    public Task<JournalEntry> PostAsync(JournalEntry entry, CancellationToken ct = default) =>
        _uow.ExecuteAsync(async () =>
        {
            entry.EnsureBalanced();
            var accountIds = entry.Lines.Select(l => l.AccountId).Distinct().ToList();
            var known = _db.Accounts.Local.Where(a => accountIds.Contains(a.Id)).Select(a => a.Id).ToHashSet();
            var missing = accountIds.Where(id => !known.Contains(id)).ToList();
            if (missing.Count > 0)
            {
                var count = await _db.Accounts.CountAsync(a => missing.Contains(a.Id), ct);
                if (count != missing.Count) throw new DomainException("account_missing", "حساب سطر سند یافت نشد.");
            }
            var number = await _numbering.NextAsync(DocumentKinds.Journal, entry.DateUtc, ct);
            entry.Post(number, _user.UserName, _clock.UtcNow);
            _db.JournalEntries.Add(entry);
            _audit.Record("journal.post", nameof(JournalEntry), entry.Id, $"{number} {entry.OperationType}");
            return entry;
        }, ct);

    /// <summary>
    /// Posts a mirror entry and marks the original Reversed. Requires <see cref="Permission.ReverseEntries"/>.
    /// Source-document side effects (marking receipts reversed etc.) are handled by <c>ReversalService</c>.
    /// </summary>
    public Task<JournalEntry> ReverseAsync(Guid entryId, string reason, DateTime? reversalDateUtc = null, CancellationToken ct = default)
    {
        _user.Demand(Permission.ReverseEntries);
        return ReverseInternalAsync(entryId, reason, reversalDateUtc, ct);
    }

    /// <summary>System-initiated reversal (e.g. rejecting an in-transit dispatch); caller enforces its own permission.</summary>
    internal Task<JournalEntry> ReverseInternalAsync(Guid entryId, string reason, DateTime? reversalDateUtc, CancellationToken ct) =>
        _uow.ExecuteAsync(async () =>
        {
            var original = await _db.JournalEntries.Include(e => e.Lines).FirstOrDefaultAsync(e => e.Id == entryId, ct)
                           ?? throw new NotFoundException("سند حسابداری");
            var reversal = original.CreateReversal(reason, _user.UserName, reversalDateUtc ?? _clock.UtcNow, _clock.UtcNow);
            var number = await _numbering.NextAsync(DocumentKinds.Journal, reversal.DateUtc, ct);
            reversal.Post(number, _user.UserName, _clock.UtcNow);
            _db.JournalEntries.Add(reversal);
            original.MarkReversed(reversal, _user.UserName, _clock.UtcNow);
            _audit.Record("journal.reverse", nameof(JournalEntry), original.Id, $"{original.EntryNumber} → {number}: {reason}");
            return reversal;
        }, ct);

    /// <summary>Balances per currency from posted (and reversed + their reversal) entries only.</summary>
    public async Task<IReadOnlyList<CurrencyBalance>> BalancesAsync(Guid accountId, DateTime? asOfUtcExclusive = null, CancellationToken ct = default)
    {
        var lines = await PostedLines()
            .Where(x => x.Line.AccountId == accountId && (asOfUtcExclusive == null || x.Entry.DateUtc < asOfUtcExclusive))
            .Select(x => x.Line)
            .ToListAsync(ct);
        return Aggregate(lines);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<CurrencyBalance>>> BalancesForAccountsAsync(
        IReadOnlyCollection<Guid> accountIds, DateTime? asOfUtcExclusive = null, CancellationToken ct = default)
    {
        var lines = await PostedLines()
            .Where(x => accountIds.Contains(x.Line.AccountId) && (asOfUtcExclusive == null || x.Entry.DateUtc < asOfUtcExclusive))
            .Select(x => x.Line)
            .ToListAsync(ct);
        return lines.GroupBy(l => l.AccountId).ToDictionary(g => g.Key, g => Aggregate(g));
    }

    internal IQueryable<PostedLine> PostedLines() =>
        from l in _db.JournalLines.AsNoTracking()
        join e in _db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
        where e.Status == JournalStatus.Posted || e.Status == JournalStatus.Reversed
        select new PostedLine { Line = l, Entry = e };

    // Decimal aggregation is done in memory: SQLite stores decimals as TEXT and cannot SUM them exactly.
    public static IReadOnlyList<CurrencyBalance> Aggregate(IEnumerable<JournalLine> lines) =>
        lines.GroupBy(l => l.Currency)
            .Select(g => new CurrencyBalance(g.Key, g.Sum(l => l.Debit), g.Sum(l => l.Credit)))
            .OrderBy(b => b.Currency)
            .ToList();
}

public sealed class PostedLine
{
    public JournalLine Line { get; init; } = null!;
    public JournalEntry Entry { get; init; } = null!;
}
