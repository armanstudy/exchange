using LenjExchange.Application.Common;
using LenjExchange.Application.Ledger;
using LenjExchange.Application.Transfers;
using LenjExchange.Domain.Common;
using LenjExchange.Domain.Ledger;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Security;
using LenjExchange.Domain.Transfers;
using LenjExchange.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace LenjExchange.Tests.Integration;

public class TransferFlowTests
{
    private static async Task<decimal> Bal(TestApp app, AccountCategory cat, Guid? linked, CurrencyCode cur)
    {
        var acc = linked is { } id ? await app.Get<AccountDirectory>().FindLinkedAsync(id, cat) : await app.Get<AccountDirectory>().SystemAsync(cat);
        return (await app.Get<LedgerService>().BalancesAsync(acc!.Id)).FirstOrDefault(b => b.Currency == cur)?.Balance ?? 0m;
    }

    private static async Task<TransferStatus> Status(TestApp app, Guid caseId) =>
        (await app.Get<TransferCaseService>().GetDetailAsync(caseId)).Case.Status;

    [Fact]
    public async Task Exchanger_commitment_moves_irt_and_creates_usd_receivable()
    {
        await using var app = await TestApp.CreateAsync();
        var (_, r) = await Flow.ReceiveAsync(app);
        var c = await Flow.CommitAsync(app, r);
        var ex = await app.ExchangerAsync();
        Assert.Equal(DocumentStatus.Posted, c.Status);
        Assert.Equal(1_000m, await Bal(app, AccountCategory.Exchanger, ex.Id, CurrencyCode.USD));
        Assert.Equal(0m, await Bal(app, AccountCategory.Exchanger, ex.Id, CurrencyCode.IRT));
        Assert.Equal(-100_000_000m, await Bal(app, AccountCategory.IranCash, null, CurrencyCode.IRT)); // paid from Iran cash
        Assert.Equal(TransferStatus.AwaitingTurkishExchanger, await Status(app, r.TransferCaseId));

        await app.Get<TransferFlowService>().MarkReadyForDeliveryAsync(c.Id);
        Assert.Equal(TransferStatus.ReadyForUsdDelivery, await Status(app, r.TransferCaseId));
    }

    [Fact]
    public async Task Exchanger_rate_differing_from_customer_rate_leaves_fx_residual()
    {
        await using var app = await TestApp.CreateAsync();
        var (_, r) = await Flow.ReceiveAsync(app); // customer: 1 USD = 100,000 IRT
        var c = await Flow.CommitAsync(app, r, 100_000_000m, new ExchangeRate(CurrencyCode.USD, CurrencyCode.IRT, 98_000m));
        Assert.Equal(1_020.41m, c.CommittedAmount);
        Assert.Equal(-20.41m, await Bal(app, AccountCategory.FxConversion, null, CurrencyCode.USD)); // credit = FX gain in USD
        Assert.Equal(0m, await Bal(app, AccountCategory.FxConversion, null, CurrencyCode.IRT));
    }

    [Fact]
    public async Task Delivery_to_agent_requires_approval_and_cannot_exceed_commitment()
    {
        await using var app = await TestApp.CreateAsync();
        var (_, r) = await Flow.ReceiveAsync(app);
        var c = await Flow.CommitAsync(app, r);
        var agent = await app.AgentAsync();
        var flow = app.Get<TransferFlowService>();
        var d = await flow.RegisterDeliveryAsync(new DeliveryInput(c.Id, agent.Id, app.Clock.UtcNow, 700m, "EXT-99"));
        Assert.Equal(ApprovalStatus.Pending, d.Approval);
        Assert.Matches(@"^AD140507\d{4}$", d.TrackingNo);
        Assert.Equal("EXT-99", d.ExternalTrackingNo);
        Assert.Equal(0m, await Bal(app, AccountCategory.AgentFunds, agent.Id, CurrencyCode.USD));

        var ex = await Assert.ThrowsAsync<DomainException>(() => flow.RegisterDeliveryAsync(new DeliveryInput(c.Id, agent.Id, app.Clock.UtcNow, 301m)));
        Assert.Equal("delivery_exceeds", ex.Code);

        await flow.ApproveDeliveryAsync(d.Id);
        Assert.Equal(700m, await Bal(app, AccountCategory.AgentFunds, agent.Id, CurrencyCode.USD));
        Assert.Equal(TransferStatus.DeliveredToAgent, await Status(app, r.TransferCaseId));
        var detail = await app.Get<TransferCaseService>().GetDetailAsync(r.TransferCaseId);
        Assert.Equal(300m, detail.Snapshot.ExchangerOutstanding.Amount);
        await Assert.ThrowsAsync<DomainException>(() => flow.ApproveDeliveryAsync(d.Id));
    }

    [Fact]
    public async Task Partial_settlements_from_two_deliveries_then_full_settlement()
    {
        await using var app = await TestApp.CreateAsync();
        var (_, r) = await Flow.ReceiveAsync(app);
        var c = await Flow.CommitAsync(app, r);
        await Flow.DeliverAsync(app, c, 400m);
        await Flow.ChinaAsync(app, r.TransferCaseId, 400m);
        Assert.Equal(TransferStatus.PartiallyReceivedInChina, await Status(app, r.TransferCaseId));

        await Flow.DeliverAsync(app, c, 600m, "چاتای");
        await Flow.ChinaAsync(app, r.TransferCaseId, 600m, agentName: "چاتای");
        app.NewScope();
        var detail = await app.Get<TransferCaseService>().GetDetailAsync(r.TransferCaseId);
        Assert.Equal(TransferStatus.Settled, detail.Case.Status);
        Assert.Equal(2, detail.Deliveries.Count);
        Assert.Equal(2, detail.ChinaReceipts.Count);
        Assert.Equal(1_000m, detail.Snapshot.ReceivedInChina.Amount);
        Assert.True(detail.Timeline.Count >= 6);
        await Assert.ThrowsAsync<DomainException>(() => Flow.ChinaAsync(app, r.TransferCaseId, 1m));
    }

    [Fact]
    public async Task Full_china_receipt_without_full_exchanger_settlement_is_not_settled()
    {
        await using var app = await TestApp.CreateAsync();
        var (c1, r1) = await Flow.ReceiveAsync(app, 50_000_000m);
        var commit = await Flow.CommitAsync(app, r1, 100_000_000m); // exchanger committed 1,000 for a 500 case
        await Flow.DeliverAsync(app, commit, 500m);
        await Flow.ChinaAsync(app, r1.TransferCaseId, 500m);
        Assert.Equal(TransferStatus.FullyReceivedInChina, await Status(app, r1.TransferCaseId));
    }

    [Fact]
    public async Task China_receipt_pending_is_in_transit_and_rejection_returns_funds_to_agent()
    {
        await using var app = await TestApp.CreateAsync();
        var (_, r) = await Flow.ReceiveAsync(app);
        var c = await Flow.CommitAsync(app, r);
        await Flow.DeliverAsync(app, c, 1_000m);
        var agent = await app.AgentAsync();
        var cr = await Flow.ChinaAsync(app, r.TransferCaseId, 1_000m, approve: false);
        Assert.Equal(TransferStatus.InTransitToChina, await Status(app, r.TransferCaseId));
        Assert.Equal(1_000m, await Bal(app, AccountCategory.InTransitToChina, null, CurrencyCode.USD));
        Assert.Equal(0m, await Bal(app, AccountCategory.AgentFunds, agent.Id, CurrencyCode.USD));
        var ex = await Assert.ThrowsAsync<DomainException>(() => Flow.ChinaAsync(app, r.TransferCaseId, 1m, approve: false));
        Assert.Equal("agent_insufficient", ex.Code);

        await app.Get<TransferFlowService>().RejectChinaReceiptAsync(cr.Id, "مبلغ نرسید");
        Assert.Equal(1_000m, await Bal(app, AccountCategory.AgentFunds, agent.Id, CurrencyCode.USD));
        Assert.Equal(0m, await Bal(app, AccountCategory.InTransitToChina, null, CurrencyCode.USD));
        Assert.Equal(TransferStatus.DeliveredToAgent, await Status(app, r.TransferCaseId));
    }

    [Fact]
    public async Task Reversal_keeps_history_restores_balances_and_status()
    {
        await using var app = await TestApp.CreateAsync();
        var (customer, r) = await Flow.ReceiveAsync(app);
        var c = await Flow.CommitAsync(app, r);
        var d = await Flow.DeliverAsync(app, c, 1_000m);
        var cr = await Flow.ChinaAsync(app, r.TransferCaseId, 1_000m);
        Assert.Equal(TransferStatus.Settled, await Status(app, r.TransferCaseId));

        var rev = app.Get<ReversalService>();
        await Assert.ThrowsAsync<DomainException>(() => rev.ReverseCommitmentAsync(c.Id, "x")); // dependents first
        var revEntry = await rev.ReverseChinaReceiptAsync(cr.Id, "ثبت اشتباه");
        Assert.Equal(OperationType.Reversal, revEntry.OperationType);
        Assert.Equal(TransferStatus.DeliveredToAgent, await Status(app, r.TransferCaseId));
        var agent = await app.AgentAsync();
        Assert.Equal(1_000m, await Bal(app, AccountCategory.AgentFunds, agent.Id, CurrencyCode.USD));

        await rev.ReverseDeliveryAsync(d.Id, "اشتباه");
        await rev.ReverseCommitmentAsync(c.Id, "اشتباه");
        await rev.ReverseReceiptAsync(r.Id, "اشتباه");
        Assert.Equal(TransferStatus.Draft, await Status(app, r.TransferCaseId));
        Assert.Equal(0m, await Bal(app, AccountCategory.Customer, customer.Id, CurrencyCode.USD));

        app.NewScope();
        var db = app.Get<LenjExchange.Application.Abstractions.IAppDbContext>();
        var entries = await db.JournalEntries.Where(e => e.TransferCaseId == r.TransferCaseId).ToListAsync();
        var originals = entries.Where(e => e.ReversalOfEntryId == null).ToList();
        var reversals = entries.Where(e => e.ReversalOfEntryId != null).ToList();
        Assert.Equal(originals.Count, reversals.Count);
        Assert.All(originals, o =>
        {
            Assert.Equal(JournalStatus.Reversed, o.Status);
            Assert.NotNull(o.ReversedByEntryId);
            Assert.Equal("admin", o.ReversedBy);
            Assert.NotNull(o.ReversedAtUtc);
            Assert.Contains(reversals, x => x.Id == o.ReversedByEntryId && x.ReversalOfEntryId == o.Id);
        });
        var receipt = await db.CustomerReceipts.SingleAsync(x => x.Id == r.Id);
        Assert.Equal(DocumentStatus.Reversed, receipt.Status);
        Assert.Equal("YI140507001LE", receipt.ReceiptNumber); // number retained
        Assert.True(await db.AuditLog.AnyAsync(a => a.Operation == "journal.reverse"));

        await app.Get<TransferCaseService>().CancelAsync(r.TransferCaseId, "انصراف مشتری");
        Assert.Equal(TransferStatus.Cancelled, await Status(app, r.TransferCaseId));
    }

    [Fact]
    public async Task Balances_are_computed_only_from_posted_entries()
    {
        await using var app = await TestApp.CreateAsync();
        var c = await app.NewCustomerAsync();
        var mohammad = await app.MohammadAsync();
        var svc = app.Get<ReceiptService>();
        var draft = await svc.CreateDraftAsync(new ReceiptInput(c.Id, app.Clock.UtcNow, 500m, CurrencyCode.USD, CurrencyCode.USD, null, mohammad.Id));
        var acc = await app.Get<AccountDirectory>().FindLinkedAsync(c.Id, AccountCategory.Customer);
        Assert.Empty(await app.Get<LedgerService>().BalancesAsync(acc!.Id));
        await svc.FinalizeAsync(draft.Id);
        await Flow.ReceiveAsync(app, 30_000_000m, customer: c);
        await svc.RegisterCustomerPaymentAsync(c.Id, Money.Of(200m, CurrencyCode.USD), app.Clock.UtcNow, null, "پرداخت به مشتری");

        var bal = await app.Get<LedgerService>().BalancesAsync(acc.Id);
        var usd = bal.Single(b => b.Currency == CurrencyCode.USD);
        Assert.Equal(200m, usd.Debit);
        Assert.Equal(800m, usd.Credit);
        Assert.Equal(-600m, usd.Balance);
        Assert.Equal("بستانکار", usd.Side);
        Assert.Equal(0m, bal.Single(b => b.Currency == CurrencyCode.IRT).Balance);

        // Global check: every currency sums to zero across the whole ledger.
        var db = app.Get<LenjExchange.Application.Abstractions.IAppDbContext>();
        var lines = await db.JournalLines.ToListAsync();
        Assert.All(lines.GroupBy(l => l.Currency), g => Assert.Equal(0m, g.Sum(l => l.Debit - l.Credit)));
    }

    [Fact]
    public async Task Operator_cannot_reverse_or_manage_users()
    {
        await using var app = await TestApp.CreateAsync();
        var (_, r) = await Flow.ReceiveAsync(app);
        await app.SignInAsync(Role.Operator);
        Assert.Equal(Role.Operator, app.Session.User!.Role);
        await Assert.ThrowsAsync<PermissionDeniedException>(() => app.Get<ReversalService>().ReverseReceiptAsync(r.Id, "x"));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => app.Get<LedgerService>().ReverseAsync(r.JournalEntryId!.Value, "x"));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => app.Get<LenjExchange.Application.Security.UserService>().ListAsync());
        await Assert.ThrowsAsync<PermissionDeniedException>(() => app.Get<LenjExchange.Application.Settings.SettingsService>().SaveAccountingAsync(new()));
        // Operators can still register receipts.
        var (_, r2) = await Flow.ReceiveAsync(app, 1_000_000m, customerName: "مشتری اپراتور");
        Assert.Equal(DocumentStatus.Posted, r2.Status);
    }
}
