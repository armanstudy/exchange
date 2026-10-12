using LenjExchange.Application.Ledger;
using LenjExchange.Application.Reporting;
using LenjExchange.Application.Transfers;
using LenjExchange.Domain.Ledger;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Transfers;
using LenjExchange.Tests.Support;

namespace LenjExchange.Tests.Integration;

/// <summary>The six acceptance scenarios from the specification, end-to-end through services, EF Core and SQLite.</summary>
public class AcceptanceScenarioTests
{
    private static async Task<decimal> BalanceAsync(TestApp app, AccountCategory cat, Guid? linked, CurrencyCode cur)
    {
        var acc = linked is { } id
            ? await app.Get<AccountDirectory>().FindLinkedAsync(id, cat)
            : await app.Get<AccountDirectory>().SystemAsync(cat);
        var b = await app.Get<LedgerService>().BalancesAsync(acc!.Id);
        return b.FirstOrDefault(x => x.Currency == cur)?.Balance ?? 0m;
    }

    [Fact]
    public async Task Scenario1_full_flow_settles_case()
    {
        await using var app = await TestApp.CreateAsync();
        var (customer, receipt) = await Flow.ReceiveAsync(app, 100_000_000m, Flow.UsdIrt100k, "علی احمدی");

        Assert.Equal(1_000m, receipt.TargetAmount);
        Assert.Equal(CurrencyCode.USD, receipt.TargetCurrency);
        Assert.Matches(@"^YI1405\d{2}\d{3,}LE$", receipt.ReceiptNumber!);

        var commitment = await Flow.CommitAsync(app, receipt);
        Assert.Equal(1_000m, commitment.CommittedAmount);
        await Flow.DeliverAsync(app, commitment, 1_000m, "آیدین");
        await Flow.ChinaAsync(app, receipt.TransferCaseId, 1_000m);

        app.NewScope();
        var detail = await app.Get<TransferCaseService>().GetDetailAsync(receipt.TransferCaseId);
        Assert.Equal(TransferStatus.Settled, detail.Case.Status);
        Assert.Equal(TransferStatus.Settled, detail.Snapshot.Status);
        Assert.Equal(0m, detail.Snapshot.Unsettled.Amount);
        Assert.Equal(0m, detail.Snapshot.ExchangerOutstanding.Amount);

        var exchanger = await app.ExchangerAsync();
        var agent = await app.AgentAsync();
        var mohammad = await app.MohammadAsync();
        Assert.Equal(0m, await BalanceAsync(app, AccountCategory.Exchanger, exchanger.Id, CurrencyCode.USD));
        Assert.Equal(0m, await BalanceAsync(app, AccountCategory.AgentFunds, agent.Id, CurrencyCode.USD));
        Assert.Equal(0m, await BalanceAsync(app, AccountCategory.InTransitToChina, null, CurrencyCode.USD));
        Assert.Equal(1_000m, await BalanceAsync(app, AccountCategory.Recipient, mohammad.Id, CurrencyCode.USD));
        Assert.Equal(-1_000m, await BalanceAsync(app, AccountCategory.Customer, customer.Id, CurrencyCode.USD)); // credit: owed to customer
        Assert.Equal(0m, await BalanceAsync(app, AccountCategory.Customer, customer.Id, CurrencyCode.IRT));
        Assert.Equal(0m, await BalanceAsync(app, AccountCategory.FxConversion, null, CurrencyCode.IRT));
        Assert.Equal(0m, await BalanceAsync(app, AccountCategory.FxConversion, null, CurrencyCode.USD));
        Assert.All(detail.JournalEntries, e => Assert.True(e.IsBalanced));
    }

    [Fact]
    public async Task Scenario2_partial_china_receipt_leaves_400_unsettled()
    {
        await using var app = await TestApp.CreateAsync();
        var (_, receipt) = await Flow.ReceiveAsync(app);
        var c = await Flow.CommitAsync(app, receipt);
        await Flow.DeliverAsync(app, c, 1_000m);
        await Flow.ChinaAsync(app, receipt.TransferCaseId, 600m);

        app.NewScope();
        var detail = await app.Get<TransferCaseService>().GetDetailAsync(receipt.TransferCaseId);
        Assert.Equal(TransferStatus.PartiallyReceivedInChina, detail.Case.Status);
        Assert.Equal(400m, detail.Snapshot.Unsettled.Amount);
        Assert.Equal(600m, detail.Snapshot.ReceivedInChina.Amount);
        var agent = await app.AgentAsync();
        Assert.Equal(400m, await BalanceAsync(app, AccountCategory.AgentFunds, agent.Id, CurrencyCode.USD));

        var open = await app.Get<ReportService>().OpenCasesAsync();
        var row = Assert.Single(open);
        Assert.Equal(400m, row.Unsettled);
        Assert.Equal(TransferStatus.PartiallyReceivedInChina, row.Status);
    }

    [Fact]
    public async Task Scenario3_china_pays_cny_at_agreed_rate()
    {
        await using var app = await TestApp.CreateAsync();
        var (_, receipt) = await Flow.ReceiveAsync(app);
        var c = await Flow.CommitAsync(app, receipt);
        await Flow.DeliverAsync(app, c, 1_000m);
        var usdCny = new ExchangeRate(CurrencyCode.USD, CurrencyCode.CNY, 7.15m);

        Assert.Equal(Money.Of(7_150m, CurrencyCode.CNY), TransferFlowService.ComputePaidAmount(1_000m, CurrencyCode.CNY, usdCny));
        var cr = await Flow.ChinaAsync(app, receipt.TransferCaseId, 1_000m, CurrencyCode.CNY, usdCny);
        Assert.Equal(7_150m, cr.PaidAmount);
        Assert.Equal(CurrencyCode.CNY, cr.PaidCurrency);

        app.NewScope();
        var detail = await app.Get<TransferCaseService>().GetDetailAsync(receipt.TransferCaseId);
        Assert.Equal(TransferStatus.Settled, detail.Case.Status);
        var mohammad = await app.MohammadAsync();
        Assert.Equal(7_150m, await BalanceAsync(app, AccountCategory.Recipient, mohammad.Id, CurrencyCode.CNY));
        Assert.Equal(0m, await BalanceAsync(app, AccountCategory.Recipient, mohammad.Id, CurrencyCode.USD));
        Assert.Equal(-7_150m, await BalanceAsync(app, AccountCategory.FxConversion, null, CurrencyCode.CNY));
        Assert.Equal(1_000m, await BalanceAsync(app, AccountCategory.FxConversion, null, CurrencyCode.USD));
        var china = detail.JournalEntries.Single(e => e.OperationType == OperationType.ChinaReceipt);
        Assert.True(china.IsBalanced);
    }

    [Fact]
    public async Task Scenario4_multiple_deposits_reconcile_to_receipt()
    {
        await using var app = await TestApp.CreateAsync();
        var customer = await app.NewCustomerAsync("رضا کریمی");
        var mohammad = await app.MohammadAsync();
        var now = app.Clock.UtcNow;
        var deposits = new[]
        {
            new DepositInput("بانک ملت", now, 40_000_000m, CurrencyCode.IRT, "111111"),
            new DepositInput("بانک ملی", now, 35_000_000m, CurrencyCode.IRT, "222222"),
            new DepositInput("بانک ملت", now, 250_000_000m, CurrencyCode.IRR, "333333"), // 25,000,000 IRT
        };
        var svc = app.Get<ReceiptService>();
        var check = await svc.CheckAsync(100_000_000m, CurrencyCode.IRT, deposits);
        Assert.False(check.HasMismatch);
        Assert.Equal(100_000_000m, check.DepositsTotal.Amount);

        var receipt = await svc.RegisterAsync(new ReceiptInput(customer.Id, now, 100_000_000m, CurrencyCode.IRT, CurrencyCode.USD,
            Flow.UsdIrt100k, mohammad.Id, deposits));
        app.NewScope();
        var loaded = await app.Get<ReceiptService>().GetAsync(receipt.Id);
        Assert.Equal(3, loaded.Deposits.Count);
        Assert.Equal(loaded.Total, loaded.DepositsTotal());
        Assert.False(loaded.HasDepositMismatch);

        var mellat = (await app.Get<Application.MasterData.MasterDataService>().BanksAsync()).Single(b => b.Name == "بانک ملت");
        Assert.Equal(65_000_000m, await BalanceAsync(app, AccountCategory.Bank, mellat.Id, CurrencyCode.IRT));
    }

    [Fact]
    public async Task Scenario5_monthly_statement_opening_plus_movement_equals_closing()
    {
        await using var app = await TestApp.CreateAsync();
        var customer = await app.NewCustomerAsync("سارا رضایی");
        // Shahrivar 1405 (before the period) and Mehr 1405 (the period).
        await Flow.ReceiveAsync(app, 50_000_000m, customer: customer, dateUtc: app.Shamsi(1405, 6, 25));
        await Flow.ReceiveAsync(app, 20_000_000m, customer: customer, dateUtc: app.Shamsi(1405, 7, 1, 0)); // first minute-hour of the month
        await Flow.ReceiveAsync(app, 30_000_000m, customer: customer, dateUtc: app.Shamsi(1405, 7, 15));
        await app.Get<ReceiptService>().RegisterCustomerPaymentAsync(customer.Id, Money.Of(100m, CurrencyCode.USD), app.Shamsi(1405, 7, 30, 23), null, "استرداد جزئی");
        await Flow.ReceiveAsync(app, 10_000_000m, customer: customer, dateUtc: app.Shamsi(1405, 8, 1, 0)); // after the period

        var range = DateRanges.ShamsiMonth(app.Clock, 1405, 7);
        var st = await app.Get<ReportService>().CustomerStatementAsync(customer.Id, range);
        var usd = st.Sections.Single(s => s.Currency == CurrencyCode.USD);
        Assert.Equal(-500m, usd.Opening);
        Assert.Equal(100m, usd.TotalDebit);
        Assert.Equal(500m, usd.TotalCredit);
        Assert.Equal(usd.Opening + usd.TotalDebit - usd.TotalCredit, usd.Closing);
        Assert.Equal(-900m, usd.Closing);
        Assert.Equal(usd.Closing, usd.Rows[^1].Balance);

        var irt = st.Sections.Single(s => s.Currency == CurrencyCode.IRT);
        Assert.Equal(0m, irt.Opening);
        Assert.Equal(50_000_000m, irt.TotalDebit);
        Assert.Equal(50_000_000m, irt.TotalCredit);
        Assert.Equal(0m, irt.Closing);

        // Next month's opening equals this month's closing.
        var next = await app.Get<ReportService>().CustomerStatementAsync(customer.Id, DateRanges.ShamsiMonth(app.Clock, 1405, 8), CurrencyCode.USD);
        Assert.Equal(usd.Closing, next.Sections.Single().Opening);
    }

    [Fact]
    public async Task Scenario6_all_customers_custom_range_independent_currency_balances()
    {
        await using var app = await TestApp.CreateAsync();
        var a = await app.NewCustomerAsync("مشتری الف");
        var b = await app.NewCustomerAsync("مشتری ب");
        var mohammad = await app.MohammadAsync();
        await Flow.ReceiveAsync(app, 100_000_000m, customer: a, dateUtc: app.Shamsi(1405, 7, 5));
        // Customer B pays in IRT and keeps the balance in IRT (no conversion), plus a USD receipt.
        await app.Get<ReceiptService>().RegisterAsync(new ReceiptInput(b.Id, app.Shamsi(1405, 7, 6), 70_000_000m, CurrencyCode.IRT,
            CurrencyCode.IRT, null, null));
        await app.Get<ReceiptService>().RegisterAsync(new ReceiptInput(b.Id, app.Shamsi(1405, 7, 8), 250m, CurrencyCode.USD,
            CurrencyCode.USD, null, mohammad.Id));
        await Flow.ReceiveAsync(app, 10_000_000m, customer: a, dateUtc: app.Shamsi(1405, 7, 20)); // outside range

        var range = DateRanges.For(app.Clock, DatePreset.Custom, LenjExchange.Domain.Shamsi.ShamsiDate.ToGregorian(1405, 7, 1),
            LenjExchange.Domain.Shamsi.ShamsiDate.ToGregorian(1405, 7, 10));
        var report = await app.Get<ReportService>().AllCustomersAsync(range);

        var aUsd = report.Rows.Single(r => r.CustomerId == a.Id && r.Currency == CurrencyCode.USD);
        Assert.Equal(-1_000m, aUsd.Balance);
        Assert.Equal("بستانکار", aUsd.Side);
        Assert.Equal(1, aUsd.TransactionCount);
        var bIrt = report.Rows.Single(r => r.CustomerId == b.Id && r.Currency == CurrencyCode.IRT);
        Assert.Equal(-70_000_000m, bIrt.Balance);
        Assert.Equal(70_000_000m, bIrt.TotalReceived);
        var bUsd = report.Rows.Single(r => r.CustomerId == b.Id && r.Currency == CurrencyCode.USD);
        Assert.Equal(-250m, bUsd.Balance);

        var totals = report.Totals.ToDictionary(t => t.Currency);
        Assert.Equal(-1_250m, totals[CurrencyCode.USD].Balance);
        Assert.Equal(-70_000_000m, totals[CurrencyCode.IRT].Balance);
        Assert.DoesNotContain(report.Rows, r => r.Currency == CurrencyCode.CNY);
    }
}
