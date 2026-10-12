using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Common;
using LenjExchange.Application.Ledger;
using LenjExchange.Domain.Common;
using LenjExchange.Domain.Ledger;
using LenjExchange.Domain.Security;
using LenjExchange.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace LenjExchange.Application.Transfers;

/// <summary>
/// Corrections are never edits: the posted entry stays, a mirror reversal entry is posted (date, reason, user and the
/// link original ↔ reversal are stored), and the source document is marked reversed. Requires ReverseEntries.
/// </summary>
public sealed class ReversalService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly LedgerService _ledger;
    private readonly TransferCaseService _cases;
    private readonly AuditService _audit;
    private readonly UnitOfWork _uow;

    public ReversalService(IAppDbContext db, ICurrentUser user, LedgerService ledger, TransferCaseService cases, AuditService audit, UnitOfWork uow)
    {
        _db = db;
        _user = user;
        _ledger = ledger;
        _cases = cases;
        _audit = audit;
        _uow = uow;
    }

    public Task<JournalEntry> ReverseReceiptAsync(Guid receiptId, string reason, CancellationToken ct = default)
    {
        _user.Demand(Permission.ReverseEntries);
        return _uow.ExecuteAsync(async () =>
        {
            var r = await _db.CustomerReceipts.FirstOrDefaultAsync(x => x.Id == receiptId, ct) ?? throw new NotFoundException("رسید");
            var dependents = await _db.ExchangerCommitments.AnyAsync(c => c.TransferCaseId == r.TransferCaseId && c.Status == DocumentStatus.Posted, ct);
            if (dependents)
                throw new DomainException("reverse_dependents", "ابتدا تعهد صرافی این پرونده را برگشت بزنید.");
            var rev = await _ledger.ReverseAsync(r.JournalEntryId ?? throw new DomainException("no_journal", "رسید سند حسابداری ندارد."), reason, null, ct);
            r.MarkReversed();
            _audit.Record("receipt.reverse", nameof(CustomerReceipt), r.Id, reason);
            await _cases.RecalculateAsync(r.TransferCaseId, ct);
            return rev;
        }, ct);
    }

    public Task<JournalEntry> ReverseCommitmentAsync(Guid commitmentId, string reason, CancellationToken ct = default)
    {
        _user.Demand(Permission.ReverseEntries);
        return _uow.ExecuteAsync(async () =>
        {
            var c = await _db.ExchangerCommitments.FirstOrDefaultAsync(x => x.Id == commitmentId, ct) ?? throw new NotFoundException("تعهد");
            if (await _db.AgentDeliveries.AnyAsync(d => d.ExchangerCommitmentId == c.Id && d.Approval == ApprovalStatus.Approved && !d.IsReversed, ct))
                throw new DomainException("reverse_dependents", "ابتدا تحویل‌های تأییدشدهٔ این تعهد را برگشت بزنید.");
            var rev = await _ledger.ReverseAsync(c.JournalEntryId!.Value, reason, null, ct);
            c.MarkReversed();
            _audit.Record("commitment.reverse", nameof(ExchangerCommitment), c.Id, reason);
            await _cases.RecalculateAsync(c.TransferCaseId, ct);
            return rev;
        }, ct);
    }

    public Task<JournalEntry> ReverseDeliveryAsync(Guid deliveryId, string reason, CancellationToken ct = default)
    {
        _user.Demand(Permission.ReverseEntries);
        return _uow.ExecuteAsync(async () =>
        {
            var d = await _db.AgentDeliveries.FirstOrDefaultAsync(x => x.Id == deliveryId, ct) ?? throw new NotFoundException("تحویل");
            if (d.JournalEntryId is null) throw new DomainException("no_journal", "تحویل تأیید نشده و سندی برای برگشت ندارد.");
            var rev = await _ledger.ReverseAsync(d.JournalEntryId.Value, reason, null, ct);
            d.MarkReversed();
            _audit.Record("delivery.reverse", nameof(AgentDelivery), d.Id, reason);
            await _cases.RecalculateAsync(d.TransferCaseId, ct);
            return rev;
        }, ct);
    }

    /// <summary>Reverses a China receipt: the approval entry (if any) and the dispatch entry.</summary>
    public Task<JournalEntry> ReverseChinaReceiptAsync(Guid chinaReceiptId, string reason, CancellationToken ct = default)
    {
        _user.Demand(Permission.ReverseEntries);
        return _uow.ExecuteAsync(async () =>
        {
            var cr = await _db.ChinaReceipts.FirstOrDefaultAsync(x => x.Id == chinaReceiptId, ct) ?? throw new NotFoundException("دریافت چین");
            if (cr.IsReversed) throw new DomainException("already_reversed", "این دریافت قبلاً برگشت خورده است.");
            JournalEntry? last = null;
            if (cr.ReceiptJournalEntryId is { } a) last = await _ledger.ReverseAsync(a, reason, null, ct);
            if (cr.DispatchJournalEntryId is { } d && cr.Approval != ApprovalStatus.Rejected) last = await _ledger.ReverseAsync(d, reason, null, ct);
            cr.MarkReversed();
            _audit.Record("china.reverse", nameof(ChinaReceipt), cr.Id, reason);
            await _cases.RecalculateAsync(cr.TransferCaseId, ct);
            return last ?? throw new DomainException("no_journal", "سندی برای برگشت یافت نشد.");
        }, ct);
    }
}
