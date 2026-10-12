using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Common;
using LenjExchange.Application.Numbering;
using LenjExchange.Domain.Ledger;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Security;
using LenjExchange.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace LenjExchange.Application.Transfers;

public sealed record TimelineItem(DateTime DateUtc, string Kind, string Title, string? Amount, string Status, Guid DocumentId);

public sealed record TransferCaseDetail(
    TransferCase Case,
    string CustomerName,
    TransferCaseSnapshot Snapshot,
    IReadOnlyList<CustomerReceipt> Receipts,
    IReadOnlyList<ExchangerCommitment> Commitments,
    IReadOnlyList<AgentDelivery> Deliveries,
    IReadOnlyList<ChinaReceipt> ChinaReceipts,
    IReadOnlyList<JournalEntry> JournalEntries,
    IReadOnlyList<TimelineItem> Timeline);

public sealed class TransferCaseService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly NumberingService _numbering;
    private readonly AuditService _audit;
    private readonly UnitOfWork _uow;

    public TransferCaseService(IAppDbContext db, ICurrentUser user, IClock clock, NumberingService numbering, AuditService audit, UnitOfWork uow)
    {
        _db = db;
        _user = user;
        _clock = clock;
        _numbering = numbering;
        _audit = audit;
        _uow = uow;
    }

    public Task<TransferCase> CreateAsync(Guid customerId, Guid? recipientId, CurrencyCode targetCurrency, string? notes = null, CancellationToken ct = default)
    {
        _user.Demand(Permission.ManageCases);
        return CreateInternalAsync(customerId, recipientId, targetCurrency, notes, ct);
    }

    internal Task<TransferCase> CreateInternalAsync(Guid customerId, Guid? recipientId, CurrencyCode targetCurrency, string? notes, CancellationToken ct) =>
        _uow.ExecuteAsync(async () =>
        {
            if (!await _db.Customers.AnyAsync(c => c.Id == customerId && !c.IsDeleted, ct))
                throw new NotFoundException("مشتری");
            var number = await _numbering.NextAsync(DocumentKinds.Case, _clock.UtcNow, ct);
            var tc = new TransferCase(number, customerId, recipientId, targetCurrency, notes);
            tc.StampCreated(_user.UserName, _clock.UtcNow);
            _db.TransferCases.Add(tc);
            _audit.Record("case.create", nameof(TransferCase), tc.Id, number);
            return tc;
        }, ct);

    /// <summary>Re-derives the case status from its documents. Called after every posting.</summary>
    public async Task<TransferCaseSnapshot> RecalculateAsync(Guid caseId, CancellationToken ct = default)
    {
        var tc = _db.TransferCases.Local.FirstOrDefault(c => c.Id == caseId)
                 ?? await _db.TransferCases.FirstOrDefaultAsync(c => c.Id == caseId, ct)
                 ?? throw new NotFoundException("پرونده");
        var (r, c, d, ch) = await LoadDocsAsync(caseId, tracking: true, ct);
        var before = tc.Status;
        var snap = tc.Recalculate(r, c, d, ch, _clock.UtcNow);
        if (before != snap.Status)
            _audit.Record("case.status", nameof(TransferCase), tc.Id, $"{before} → {snap.Status}");
        await _db.SaveChangesAsync(ct);
        return snap;
    }

    public async Task<TransferCaseDetail> GetDetailAsync(Guid caseId, CancellationToken ct = default)
    {
        var tc = await _db.TransferCases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == caseId, ct) ?? throw new NotFoundException("پرونده");
        var customer = await _db.Customers.AsNoTracking().Where(c => c.Id == tc.CustomerId).Select(c => c.FullName).FirstOrDefaultAsync(ct) ?? "";
        var (r, c, d, ch) = await LoadDocsAsync(caseId, tracking: false, ct);
        var snap = TransferStatusCalculator.Calculate(tc, r, c, d, ch);
        var entries = await _db.JournalEntries.AsNoTracking().Include(e => e.Lines)
            .Where(e => e.TransferCaseId == caseId).OrderBy(e => e.DateUtc).ThenBy(e => e.CreatedAtUtc).ToListAsync(ct);

        var timeline = new List<TimelineItem>();
        timeline.AddRange(r.Select(x => new TimelineItem(x.DateUtc, "receipt", $"رسید {x.ReceiptNumber ?? "(پیش‌نویس)"}", x.Total.Format(), TransferStatusText.Persian(x.Status), x.Id)));
        timeline.AddRange(c.Select(x => new TimelineItem(x.DateUtc, "commitment", "تعهد صرافی", x.Committed.Format(), TransferStatusText.Persian(x.Status), x.Id)));
        timeline.AddRange(d.Select(x => new TimelineItem(x.DateUtc, "delivery", $"تحویل به نماینده ({x.TrackingNo})", x.Money.Format(), x.IsReversed ? "برگشت‌خورده" : TransferStatusText.Persian(x.Approval), x.Id)));
        timeline.AddRange(ch.Select(x => new TimelineItem(x.DateUtc, "china", "دریافت در چین", x.Paid.Format(), x.IsReversed ? "برگشت‌خورده" : TransferStatusText.Persian(x.Approval), x.Id)));

        return new TransferCaseDetail(tc, customer, snap, r, c, d, ch, entries, timeline.OrderBy(t => t.DateUtc).ToList());
    }

    public async Task<IReadOnlyList<TransferCase>> ListAsync(TransferStatus? status = null, Guid? customerId = null, CancellationToken ct = default)
    {
        var q = _db.TransferCases.AsNoTracking();
        if (status is { } s) q = q.Where(c => c.Status == s);
        if (customerId is { } cid) q = q.Where(c => c.CustomerId == cid);
        return await q.OrderByDescending(c => c.CreatedAtUtc).Take(2000).ToListAsync(ct);
    }

    public Task CancelAsync(Guid caseId, string reason, CancellationToken ct = default)
    {
        _user.Demand(Permission.ReverseEntries);
        return _uow.ExecuteAsync(async () =>
        {
            var tc = await _db.TransferCases.FirstOrDefaultAsync(c => c.Id == caseId, ct) ?? throw new NotFoundException("پرونده");
            var hasLiveEntries = await _db.JournalEntries.AnyAsync(e => e.TransferCaseId == caseId && e.Status == JournalStatus.Posted && e.ReversalOfEntryId == null, ct);
            if (hasLiveEntries)
                throw new Domain.Common.DomainException("case_has_entries", "پیش از ابطال پرونده، اسناد قطعی آن باید برگشت داده شوند.");
            tc.Cancel(reason);
            _audit.Record("case.cancel", nameof(TransferCase), tc.Id, reason);
            await RecalculateAsync(caseId, ct);
        }, ct);
    }

    public Task FlagDiscrepancyAsync(Guid caseId, string note, CancellationToken ct = default)
    {
        _user.Demand(Permission.ManageCases);
        return _uow.ExecuteAsync(async () =>
        {
            var tc = await _db.TransferCases.FirstOrDefaultAsync(c => c.Id == caseId, ct) ?? throw new NotFoundException("پرونده");
            tc.FlagDiscrepancy(note);
            _audit.Record("case.discrepancy", nameof(TransferCase), tc.Id, note);
            await RecalculateAsync(caseId, ct);
        }, ct);
    }

    private async Task<(List<CustomerReceipt>, List<ExchangerCommitment>, List<AgentDelivery>, List<ChinaReceipt>)> LoadDocsAsync(Guid caseId, bool tracking, CancellationToken ct)
    {
        IQueryable<T> Q<T>(DbSet<T> set) where T : class => tracking ? set : set.AsNoTracking();
        var receipts = await Q(_db.CustomerReceipts).Include(x => x.Deposits).Where(x => x.TransferCaseId == caseId).ToListAsync(ct);
        var commitments = await Q(_db.ExchangerCommitments).Where(x => x.TransferCaseId == caseId).ToListAsync(ct);
        var deliveries = await Q(_db.AgentDeliveries).Where(x => x.TransferCaseId == caseId).ToListAsync(ct);
        var china = await Q(_db.ChinaReceipts).Where(x => x.TransferCaseId == caseId).ToListAsync(ct);
        return (receipts, commitments, deliveries, china);
    }
}
