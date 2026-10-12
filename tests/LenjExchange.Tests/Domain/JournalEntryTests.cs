using LenjExchange.Domain.Common;
using LenjExchange.Domain.Ledger;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Transfers;

namespace LenjExchange.Tests.Domain;

public class JournalEntryTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc);
    private readonly Guid _a = Guid.NewGuid(), _b = Guid.NewGuid(), _fx = Guid.NewGuid();

    private JournalEntry NewEntry() => new(Now, OperationType.Adjustment, "test", "tester", Now, baseCurrency: CurrencyCode.USD);

    [Fact]
    public void Unbalanced_entry_cannot_be_posted()
    {
        var e = NewEntry().Debit(_a, Money.Of(100, CurrencyCode.USD)).Credit(_b, Money.Of(99, CurrencyCode.USD));
        Assert.False(e.IsBalanced);
        var ex = Assert.Throws<DomainException>(() => e.Post("JV1", "u", Now));
        Assert.Equal("entry_unbalanced", ex.Code);
        Assert.Equal(JournalStatus.Draft, e.Status);
    }

    [Fact]
    public void Each_currency_must_balance_independently()
    {
        // Totals "match" numerically across currencies but each currency is unbalanced → rejected.
        var e = NewEntry().Debit(_a, Money.Of(100, CurrencyCode.USD)).Credit(_b, Money.Of(100, CurrencyCode.USDT));
        Assert.Throws<DomainException>(() => e.EnsureBalanced());
    }

    [Fact]
    public void Cross_currency_entry_balances_through_fx_account()
    {
        var e = NewEntry()
            .Debit(_a, Money.Of(100_000_000, CurrencyCode.IRT))
            .Credit(_fx, Money.Of(100_000_000, CurrencyCode.IRT))
            .Debit(_fx, Money.Of(1_000, CurrencyCode.USD))
            .Credit(_b, Money.Of(1_000, CurrencyCode.USD));
        Assert.True(e.IsBalanced);
        e.Post("JV1", "u", Now);
        Assert.Equal(JournalStatus.Posted, e.Status);
    }

    [Fact]
    public void Line_must_have_exactly_one_side()
    {
        var e = NewEntry();
        Assert.Throws<DomainException>(() => e.AddLine(_a, CurrencyCode.USD, 10, 10));
        Assert.Throws<DomainException>(() => e.AddLine(_a, CurrencyCode.USD, 0, 0));
        Assert.Throws<DomainException>(() => e.AddLine(_a, CurrencyCode.USD, -1, 0));
    }

    [Fact]
    public void Posted_entry_is_immutable()
    {
        var e = NewEntry().Debit(_a, Money.Of(1, CurrencyCode.USD)).Credit(_b, Money.Of(1, CurrencyCode.USD));
        e.Post("JV1", "u", Now);
        Assert.Throws<DomainException>(() => e.Debit(_a, Money.Of(1, CurrencyCode.USD)));
        Assert.Throws<DomainException>(() => e.Post("JV2", "u", Now));
    }

    [Fact]
    public void Reversal_mirrors_lines_and_links_both_entries()
    {
        var e = NewEntry().Debit(_a, Money.Of(5, CurrencyCode.USD)).Credit(_b, Money.Of(5, CurrencyCode.USD));
        Assert.Throws<DomainException>(() => e.CreateReversal("x", "u", Now, Now)); // drafts cannot be reversed
        e.Post("JV1", "u", Now);
        var rev = e.CreateReversal("اشتباه در مبلغ", "admin", Now.AddDays(1), Now.AddDays(1));
        Assert.Equal(e.Id, rev.ReversalOfEntryId);
        Assert.Equal(5, rev.Lines.Single(l => l.AccountId == _a).Credit);
        Assert.Equal(5, rev.Lines.Single(l => l.AccountId == _b).Debit);
        rev.Post("JV2", "admin", Now.AddDays(1));
        e.MarkReversed(rev, "admin", Now.AddDays(1));
        Assert.Equal(JournalStatus.Reversed, e.Status);
        Assert.Equal(rev.Id, e.ReversedByEntryId);
        Assert.Equal("اشتباه در مبلغ", e.ReversalReason);
        Assert.Throws<DomainException>(() => e.CreateReversal("again", "u", Now, Now));
    }

    [Fact]
    public void Base_equivalent_uses_historic_rate_snapshot()
    {
        var rate = new ExchangeRate(CurrencyCode.USD, CurrencyCode.IRT, 100_000m);
        var e = NewEntry().UseHistoricRates(rate)
            .Debit(_a, Money.Of(100_000_000, CurrencyCode.IRT))
            .Credit(_b, Money.Of(100_000_000, CurrencyCode.IRT))
            .Debit(_a, Money.Of(50, CurrencyCode.USD))
            .Credit(_b, Money.Of(50, CurrencyCode.CNY));
        Assert.Equal(1_000m, e.Lines[0].BaseAmount);
        Assert.Equal(0.00001m, e.Lines[0].BaseRate);
        Assert.Equal(50m, e.Lines[2].BaseAmount);
        Assert.Null(e.Lines[3].BaseAmount); // no USD/CNY rate on this document
    }

    [Fact]
    public void Status_calculator_requires_posted_receipt()
    {
        var tc = new TransferCase("TC1", Guid.NewGuid(), null, CurrencyCode.USD, null);
        var s = TransferStatusCalculator.Calculate(tc, Array.Empty<CustomerReceipt>(), Array.Empty<ExchangerCommitment>(),
            Array.Empty<AgentDelivery>(), Array.Empty<ChinaReceipt>());
        Assert.Equal(TransferStatus.Draft, s.Status);
        tc.Cancel("لغو");
        Assert.Equal(TransferStatus.Cancelled, TransferStatusCalculator.Calculate(tc, Array.Empty<CustomerReceipt>(),
            Array.Empty<ExchangerCommitment>(), Array.Empty<AgentDelivery>(), Array.Empty<ChinaReceipt>()).Status);
    }
}
