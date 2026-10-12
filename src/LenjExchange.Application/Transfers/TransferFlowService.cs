using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Common;
using LenjExchange.Application.Ledger;
using LenjExchange.Application.Numbering;
using LenjExchange.Domain.Common;
using LenjExchange.Domain.Ledger;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Security;
using LenjExchange.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace LenjExchange.Application.Transfers;

public sealed record CommitmentInput(Guid TransferCaseId, Guid ExchangerId, DateTime DateUtc, decimal PaidAmount, CurrencyCode PaidCurrency,
    ExchangeRate? Rate, CurrencyCode CommittedCurrency = CurrencyCode.USD, Guid? FundingBankId = null, string? Notes = null);

public sealed record DeliveryInput(Guid ExchangerCommitmentId, Guid AgentId, DateTime DateUtc, decimal AmountUsd,
    string? ExternalTrackingNo = null, string? Notes = null);

public sealed record ChinaReceiptInput(Guid TransferCaseId, Guid AgentId, Guid FinalRecipientId, DateTime DateUtc, decimal SettledUsd,
    CurrencyCode PaidCurrency = CurrencyCode.USD, ExchangeRate? Rate = null, string? TrackingNo = null, string? Notes = null);

/// <summary>Turkey/China leg: exchanger commitments, deliveries to agents (settlements) and receipts in China.</summary>
public sealed class TransferFlowService
{
    public const string CommitmentSource = nameof(ExchangerCommitment);
    public const string DeliverySource = nameof(AgentDelivery);
    public const string ChinaSource = nameof(ChinaReceipt);

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly NumberingService _numbering;
    private readonly LedgerService _ledger;
    private readonly AccountDirectory _accounts;
    private readonly TransferCaseService _cases;
    private readonly AuditService _audit;
    private readonly UnitOfWork _uow;

    public TransferFlowService(IAppDbContext db, ICurrentUser user, IClock clock, NumberingService numbering, LedgerService ledger,
        AccountDirectory accounts, TransferCaseService cases, AuditService audit, UnitOfWork uow)
    {
        _db = db;
        _user = user;
        _clock = clock;
        _numbering = numbering;
        _ledger = ledger;
        _accounts = accounts;
        _cases = cases;
        _audit = audit;
        _uow = uow;
    }

    /// <summary>
    /// Iranian funds paid to the Turkish exchanger, which commits to deliver USD.
    /// Dr Exchanger (IRT) / Cr Bank|Cash (IRT); FX legs convert the exchanger's IRT receivable into USD.
    /// </summary>
    public Task<ExchangerCommitment> CreateCommitmentAsync(CommitmentInput input, CancellationToken ct = default)
    {
        _user.Demand(Permission.ManageExchangerDocuments);
        return _uow.ExecuteAsync(async () =>
        {
            var tc = await _db.TransferCases.FirstOrDefaultAsync(c => c.Id == input.TransferCaseId, ct) ?? throw new NotFoundException("پرونده");
            EnsureOpen(tc);
            var exchanger = await _db.Exchangers.FirstOrDefaultAsync(e => e.Id == input.ExchangerId, ct) ?? throw new NotFoundException("صرافی");
            var commitment = new ExchangerCommitment(tc.Id, exchanger.Id, input.DateUtc, Money.Of(input.PaidAmount, input.PaidCurrency),
                input.Rate, input.CommittedCurrency, input.FundingBankId, input.Notes);
            commitment.StampCreated(_user.UserName, _clock.UtcNow);
            _db.ExchangerCommitments.Add(commitment);

            var exAcc = await _accounts.ForExchangerAsync(exchanger, ct);
            var source = input.FundingBankId is { } bid
                ? await _accounts.ForBankAsync(await _db.Banks.FirstOrDefaultAsync(b => b.Id == bid, ct) ?? throw new NotFoundException("بانک"), ct)
                : await _accounts.SystemAsync(AccountCategory.IranCash, ct);

            var entry = await _ledger.NewEntryAsync(commitment.DateUtc, OperationType.ExchangerCommitment,
                $"پرداخت به {exchanger.Name} و تعهد {commitment.Committed} - پرونده {tc.CaseNumber}", tc.Id, CommitmentSource, commitment.Id, ct);
            entry.UseHistoricRates(commitment.Rate);
            entry.Debit(exAcc.Id, commitment.Paid, "پرداخت ریالی به صرافی").Credit(source.Id, commitment.Paid, $"پرداخت به {exchanger.Name}");
            if (commitment.PaidCurrency != commitment.CommittedCurrency)
            {
                var fx = await _accounts.SystemAsync(AccountCategory.FxConversion, ct);
                var rateText = commitment.Rate?.Describe() ?? "";
                entry.Credit(exAcc.Id, commitment.Paid, $"تبدیل ({rateText})")
                    .Debit(fx.Id, commitment.Paid, rateText)
                    .Credit(fx.Id, commitment.Committed, rateText)
                    .Debit(exAcc.Id, commitment.Committed, $"تعهد تحویل {commitment.Committed}");
            }
            await _ledger.PostAsync(entry, ct);
            commitment.Post(entry.Id);
            _audit.Record("commitment.post", CommitmentSource, commitment.Id, commitment.Committed.Format());
            await _cases.RecalculateAsync(tc.Id, ct);
            return commitment;
        }, ct);
    }

    public Task MarkReadyForDeliveryAsync(Guid commitmentId, CancellationToken ct = default)
    {
        _user.Demand(Permission.ManageExchangerDocuments);
        return _uow.ExecuteAsync(async () =>
        {
            var c = await _db.ExchangerCommitments.FirstOrDefaultAsync(x => x.Id == commitmentId, ct) ?? throw new NotFoundException("تعهد");
            c.MarkReadyForDelivery();
            _audit.Record("commitment.ready", CommitmentSource, c.Id);
            await _cases.RecalculateAsync(c.TransferCaseId, ct);
        }, ct);
    }

    /// <summary>Registers an exchanger → agent USD delivery (pending approval). Over-delivery beyond the commitment is rejected.</summary>
    public Task<AgentDelivery> RegisterDeliveryAsync(DeliveryInput input, CancellationToken ct = default)
    {
        _user.Demand(Permission.ManageExchangerDocuments);
        return _uow.ExecuteAsync(async () =>
        {
            var c = await _db.ExchangerCommitments.FirstOrDefaultAsync(x => x.Id == input.ExchangerCommitmentId, ct) ?? throw new NotFoundException("تعهد");
            if (c.Status != DocumentStatus.Posted) throw new DomainException("commitment_status", "تعهد صرافی قطعی نیست.");
            if (c.CommittedCurrency != CurrencyCode.USD) throw new DomainException("agent_currency", "تحویل به نماینده فقط برای تعهد دلاری ممکن است.");
            var tc = await _db.TransferCases.FirstAsync(x => x.Id == c.TransferCaseId, ct);
            EnsureOpen(tc);
            if (!await _db.Agents.AnyAsync(a => a.Id == input.AgentId, ct)) throw new NotFoundException("نماینده");

            var existing = await _db.AgentDeliveries.Where(d => d.ExchangerCommitmentId == c.Id && !d.IsReversed && d.Approval != ApprovalStatus.Rejected).ToListAsync(ct);
            var used = existing.Aggregate(Money.Zero(CurrencyCode.USD), (a, d) => a + d.Money);
            var amount = Money.Of(input.AmountUsd, CurrencyCode.USD);
            if (used + amount > c.Committed)
                throw new DomainException("delivery_exceeds", $"مجموع تحویل ({used + amount}) از تعهد صرافی ({c.Committed}) بیشتر است.");

            var tracking = await _numbering.NextAsync(DocumentKinds.Delivery, input.DateUtc, ct);
            var d = new AgentDelivery(c.TransferCaseId, c.Id, c.ExchangerId, input.AgentId, input.DateUtc, amount, tracking, input.ExternalTrackingNo, input.Notes);
            d.StampCreated(_user.UserName, _clock.UtcNow);
            _db.AgentDeliveries.Add(d);
            _audit.Record("delivery.register", DeliverySource, d.Id, $"{tracking} {amount}");
            return d;
        }, ct);
    }

    /// <summary>Approval posts Dr Agent funds (USD) / Cr Exchanger (USD) — a (partial) settlement of the commitment.</summary>
    public Task<AgentDelivery> ApproveDeliveryAsync(Guid deliveryId, CancellationToken ct = default)
    {
        _user.Demand(Permission.ApproveDeliveries);
        return _uow.ExecuteAsync(async () =>
        {
            var d = await _db.AgentDeliveries.FirstOrDefaultAsync(x => x.Id == deliveryId, ct) ?? throw new NotFoundException("تحویل");
            var exchanger = await _db.Exchangers.FirstAsync(e => e.Id == d.ExchangerId, ct);
            var agent = await _db.Agents.FirstAsync(a => a.Id == d.AgentId, ct);
            var tc = await _db.TransferCases.FirstAsync(x => x.Id == d.TransferCaseId, ct);
            var entry = await _ledger.NewEntryAsync(d.DateUtc, OperationType.AgentDelivery,
                $"تحویل {d.Money} از {exchanger.Name} به {agent.Name} ({d.TrackingNo}) - پرونده {tc.CaseNumber}", d.TransferCaseId, DeliverySource, d.Id, ct);
            entry.Debit((await _accounts.ForAgentAsync(agent, ct)).Id, d.Money, $"دریافت از {exchanger.Name}")
                 .Credit((await _accounts.ForExchangerAsync(exchanger, ct)).Id, d.Money, $"تحویل به {agent.Name}");
            await _ledger.PostAsync(entry, ct);
            d.Approve(_user.UserName, _clock.UtcNow, entry.Id);
            _audit.Record("delivery.approve", DeliverySource, d.Id);
            await _cases.RecalculateAsync(d.TransferCaseId, ct);
            return d;
        }, ct);
    }

    public Task RejectDeliveryAsync(Guid deliveryId, CancellationToken ct = default)
    {
        _user.Demand(Permission.ApproveDeliveries);
        return _uow.ExecuteAsync(async () =>
        {
            var d = await _db.AgentDeliveries.FirstOrDefaultAsync(x => x.Id == deliveryId, ct) ?? throw new NotFoundException("تحویل");
            d.Reject(_user.UserName, _clock.UtcNow);
            _audit.Record("delivery.reject", DeliverySource, d.Id);
            await _cases.RecalculateAsync(d.TransferCaseId, ct);
        }, ct);
    }

    /// <summary>
    /// Registers a China receipt: the agent has sent the USD (Dr In-transit / Cr Agent). Approval later confirms
    /// receipt by the final recipient. Paying in CNY/USDT requires the agreed USD/CNY or USD/USDT rate.
    /// </summary>
    public Task<ChinaReceipt> RegisterChinaReceiptAsync(ChinaReceiptInput input, CancellationToken ct = default)
    {
        _user.Demand(Permission.ManageExchangerDocuments);
        return _uow.ExecuteAsync(async () =>
        {
            var tc = await _db.TransferCases.FirstOrDefaultAsync(c => c.Id == input.TransferCaseId, ct) ?? throw new NotFoundException("پرونده");
            EnsureOpen(tc);
            var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == input.AgentId, ct) ?? throw new NotFoundException("نماینده");
            var recipient = await _db.FinalRecipients.FirstOrDefaultAsync(r => r.Id == input.FinalRecipientId, ct) ?? throw new NotFoundException("گیرنده");
            if (!recipient.Accepts(input.PaidCurrency))
                throw new DomainException("recipient_currency", $"گیرنده «{recipient.Name}» ارز {input.PaidCurrency} را نمی‌پذیرد.");

            var agentAcc = await _accounts.ForAgentAsync(agent, ct);
            var held = (await _ledger.BalancesAsync(agentAcc.Id, null, ct)).FirstOrDefault(b => b.Currency == CurrencyCode.USD)?.Balance ?? 0m;
            var amount = Money.Of(input.SettledUsd, CurrencyCode.USD);
            if (amount.Amount > held)
                throw new DomainException("agent_insufficient", $"موجودی دلار نزد {agent.Name} ({Money.Of(held, CurrencyCode.USD)}) کافی نیست.");

            var cr = new ChinaReceipt(tc.Id, agent.Id, recipient.Id, input.DateUtc, amount, input.PaidCurrency, input.Rate, input.TrackingNo, input.Notes);
            cr.StampCreated(_user.UserName, _clock.UtcNow);
            _db.ChinaReceipts.Add(cr);

            var transit = await _accounts.SystemAsync(AccountCategory.InTransitToChina, ct);
            var entry = await _ledger.NewEntryAsync(cr.DateUtc, OperationType.ChinaDispatch,
                $"ارسال {amount} توسط {agent.Name} برای {recipient.Name} - پرونده {tc.CaseNumber}", tc.Id, ChinaSource, cr.Id, ct);
            entry.Debit(transit.Id, amount, $"در مسیر چین ({recipient.Name})").Credit(agentAcc.Id, amount, "ارسال به چین");
            await _ledger.PostAsync(entry, ct);
            cr.AttachDispatch(entry.Id);
            _audit.Record("china.register", ChinaSource, cr.Id, cr.Paid.Format());
            await _cases.RecalculateAsync(tc.Id, ct);
            return cr;
        }, ct);
    }

    /// <summary>Approval: Dr Recipient (paid currency) / Cr In-transit (USD), with FX legs when paid in CNY/USDT.</summary>
    public Task<ChinaReceipt> ApproveChinaReceiptAsync(Guid chinaReceiptId, CancellationToken ct = default)
    {
        _user.Demand(Permission.ApproveChinaReceipts);
        return _uow.ExecuteAsync(async () =>
        {
            var cr = await _db.ChinaReceipts.FirstOrDefaultAsync(x => x.Id == chinaReceiptId, ct) ?? throw new NotFoundException("دریافت چین");
            var recipient = await _db.FinalRecipients.FirstAsync(r => r.Id == cr.FinalRecipientId, ct);
            var tc = await _db.TransferCases.FirstAsync(x => x.Id == cr.TransferCaseId, ct);
            var transit = await _accounts.SystemAsync(AccountCategory.InTransitToChina, ct);
            var recAcc = await _accounts.ForRecipientAsync(recipient, ct);

            var entry = await _ledger.NewEntryAsync(cr.DateUtc, OperationType.ChinaReceipt,
                $"دریافت {cr.Paid} توسط {recipient.Name} - پرونده {tc.CaseNumber}", tc.Id, ChinaSource, cr.Id, ct);
            entry.UseHistoricRates(cr.Rate);
            entry.Credit(transit.Id, cr.SettledUsd, "تحویل در چین");
            if (cr.PaidCurrency == CurrencyCode.USD)
                entry.Debit(recAcc.Id, cr.Paid, "دریافت دلار");
            else
            {
                var fx = await _accounts.SystemAsync(AccountCategory.FxConversion, ct);
                var rateText = cr.Rate!.Describe();
                entry.Debit(fx.Id, cr.SettledUsd, rateText)
                     .Credit(fx.Id, cr.Paid, rateText)
                     .Debit(recAcc.Id, cr.Paid, $"دریافت {cr.PaidCurrency} ({rateText})");
            }
            await _ledger.PostAsync(entry, ct);
            cr.Approve(_user.UserName, _clock.UtcNow, entry.Id);
            _audit.Record("china.approve", ChinaSource, cr.Id);
            await _cases.RecalculateAsync(tc.Id, ct);
            return cr;
        }, ct);
    }

    /// <summary>Rejection returns the funds to the agent by reversing the dispatch entry.</summary>
    public Task RejectChinaReceiptAsync(Guid chinaReceiptId, string reason, CancellationToken ct = default)
    {
        _user.Demand(Permission.ApproveChinaReceipts);
        return _uow.ExecuteAsync(async () =>
        {
            var cr = await _db.ChinaReceipts.FirstOrDefaultAsync(x => x.Id == chinaReceiptId, ct) ?? throw new NotFoundException("دریافت چین");
            cr.Reject(_user.UserName, _clock.UtcNow);
            if (cr.DispatchJournalEntryId is { } je)
                await _ledger.ReverseInternalAsync(je, $"رد دریافت چین: {reason}", null, ct);
            _audit.Record("china.reject", ChinaSource, cr.Id, reason);
            await _cases.RecalculateAsync(cr.TransferCaseId, ct);
        }, ct);
    }

    /// <summary>Computes the paid amount for a China receipt, e.g. 1,000 USD at 1 USD = 7.20 CNY → 7,200 CNY.</summary>
    public static Money ComputePaidAmount(decimal settledUsd, CurrencyCode paidCurrency, ExchangeRate? rate) =>
        paidCurrency == CurrencyCode.USD
            ? Money.Of(settledUsd, CurrencyCode.USD)
            : (rate ?? throw new DomainException("rate_required", "نرخ توافقی لازم است.")).Convert(Money.Of(settledUsd, CurrencyCode.USD), paidCurrency);

    private static void EnsureOpen(TransferCase tc)
    {
        if (tc.IsCancelled) throw new DomainException("case_cancelled", "پرونده ابطال شده است.");
        if (tc.Status == TransferStatus.Settled) throw new DomainException("case_settled", "پرونده تسویه شده است.");
    }
}
