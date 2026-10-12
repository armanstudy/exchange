using LenjExchange.Application.Customers;
using LenjExchange.Application.Ledger;
using LenjExchange.Application.Transfers;
using LenjExchange.Domain.Common;
using LenjExchange.Domain.Ledger;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Transfers;
using LenjExchange.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace LenjExchange.Tests.Integration;

public class CustomerAndReceiptTests
{
    [Fact]
    public async Task Customer_creation_assigns_code_and_ledger_account()
    {
        await using var app = await TestApp.CreateAsync();
        var svc = app.Get<CustomerService>();
        var c1 = await svc.CreateAsync(new CustomerInput("علي احمدي", "۰۹۱۲۱۲۳۴۵۶۷", "تهران"));
        var c2 = await svc.CreateAsync(new CustomerInput("مریم موسوی"));
        Assert.Equal("C00001", c1.Code);
        Assert.Equal("C00002", c2.Code);
        Assert.Equal("علی احمدی", c1.FullName); // Arabic ي normalized
        Assert.Equal("09121234567", c1.Phone);
        Assert.True(c1.IsActive);
        Assert.Equal("admin", c1.CreatedBy);

        app.NewScope();
        var acc = await app.Get<AccountDirectory>().FindLinkedAsync(c1.Id, AccountCategory.Customer);
        Assert.NotNull(acc);
        Assert.Equal("2100-C00001", acc!.Code);

        var found = await app.Get<CustomerService>().SearchAsync("احمدی");
        Assert.Single(found);
        Assert.Single(await app.Get<CustomerService>().SearchAsync("۰۹۱۲۱۲"));
        await Assert.ThrowsAsync<DomainException>(() => app.Get<CustomerService>().CreateAsync(new CustomerInput("x", Code: "C00001")));

        await app.Get<CustomerService>().SoftDeleteAsync(c2.Id);
        Assert.Empty(await app.Get<CustomerService>().SearchAsync("موسوی", includeInactive: true));
        Assert.True(await app.Get<LenjExchange.Application.Abstractions.IAppDbContext>().Customers.AnyAsync(c => c.Id == c2.Id && c.IsDeleted));
    }

    [Fact]
    public async Task Receipt_numbers_are_sequential_unique_and_immutable()
    {
        await using var app = await TestApp.CreateAsync();
        var c = await app.NewCustomerAsync();
        var numbers = new List<string>();
        for (var i = 0; i < 5; i++)
            numbers.Add((await Flow.ReceiveAsync(app, 1_000_000m + i * 1_000m, customer: c)).Receipt.ReceiptNumber!);
        Assert.Equal(new[] { "YI140507001LE", "YI140507002LE", "YI140507003LE", "YI140507004LE", "YI140507005LE" }, numbers);
        Assert.Equal(numbers.Count, numbers.Distinct().Count());

        // Next Shamsi month restarts the monthly sequence.
        var aban = (await Flow.ReceiveAsync(app, 1_000_000m, customer: c, dateUtc: app.Shamsi(1405, 8, 2))).Receipt;
        Assert.Equal("YI140508001LE", aban.ReceiptNumber);

        var db = app.Get<LenjExchange.Application.Abstractions.IAppDbContext>();
        var r = await db.CustomerReceipts.Include(x => x.Deposits).FirstAsync(x => x.ReceiptNumber == numbers[0]);
        var ex = Assert.Throws<DomainException>(() => r.Finalize("YI999", "admin", DateTime.UtcNow, false));
        Assert.Equal("receipt_immutable", ex.Code);
        Assert.Throws<DomainException>(() => r.SetAmount(Money.Of(1, CurrencyCode.IRT), CurrencyCode.IRT, null));
        Assert.Throws<DomainException>(() => r.AddDeposit("b", null, DateTime.UtcNow, Money.Of(1, CurrencyCode.IRT), null, null, null));
    }

    [Fact]
    public async Task Failed_finalization_does_not_consume_a_receipt_number()
    {
        await using var app = await TestApp.CreateAsync();
        var c = await app.NewCustomerAsync();
        var svc = app.Get<ReceiptService>();
        var now = app.Clock.UtcNow;
        await Assert.ThrowsAsync<DomainException>(() => svc.RegisterAsync(new ReceiptInput(c.Id, now, 100m, CurrencyCode.IRT, CurrencyCode.IRT, null, null,
            new[] { new DepositInput("بانک ملت", now, 90m, CurrencyCode.IRT, "T1") })));
        var ok = await Flow.ReceiveAsync(app, 5_000_000m, customer: c);
        Assert.Equal("YI140507001LE", ok.Receipt.ReceiptNumber);
    }

    [Fact]
    public async Task Receipt_registration_posts_balanced_journal_entry()
    {
        await using var app = await TestApp.CreateAsync();
        var (customer, receipt) = await Flow.ReceiveAsync(app);
        app.NewScope();
        var db = app.Get<LenjExchange.Application.Abstractions.IAppDbContext>();
        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync(e => e.Id == receipt.JournalEntryId);
        Assert.Equal(JournalStatus.Posted, entry.Status);
        Assert.Equal(OperationType.CustomerReceipt, entry.OperationType);
        Assert.True(entry.IsBalanced);
        Assert.Equal(6, entry.Lines.Count);
        Assert.All(entry.ImbalanceByCurrency().Values, v => Assert.Equal(0m, v));
        Assert.Matches(@"^JV1405\d{6}$", entry.EntryNumber!);
        Assert.Equal("admin", entry.PostedBy);
        Assert.Equal(receipt.TransferCaseId, entry.TransferCaseId);
        // Base-currency (USD) equivalents from the historic receipt rate.
        Assert.All(entry.Lines, l => Assert.NotNull(l.BaseAmount));
        Assert.Equal(1_000m, entry.Lines.Where(l => l.Currency == CurrencyCode.IRT).Sum(l => Math.Abs(l.BaseAmount!.Value)) / 4);

        var tc = await db.TransferCases.SingleAsync(t => t.Id == receipt.TransferCaseId);
        Assert.Equal(TransferStatus.ReceivedInIran, tc.Status);
    }

    [Fact]
    public async Task Deposit_mismatch_is_detected_and_blocked_unless_accepted()
    {
        await using var app = await TestApp.CreateAsync();
        var c = await app.NewCustomerAsync();
        var svc = app.Get<ReceiptService>();
        var now = app.Clock.UtcNow;
        var deposits = new[]
        {
            new DepositInput("بانک ملت", now, 60_000_000m, CurrencyCode.IRT, "A1"),
            new DepositInput("بانک ملت", now, 39_000_000m, CurrencyCode.IRT, "A2"),
        };
        var check = await svc.CheckAsync(100_000_000m, CurrencyCode.IRT, deposits);
        Assert.True(check.HasMismatch);
        Assert.Equal(-1_000_000m, check.Difference.Amount);
        Assert.Contains("کمتر", check.MismatchMessage);

        var input = new ReceiptInput(c.Id, now, 100_000_000m, CurrencyCode.IRT, CurrencyCode.USD, Flow.UsdIrt100k, null, deposits);
        var ex = await Assert.ThrowsAsync<DomainException>(() => svc.RegisterAsync(input));
        Assert.Equal("deposit_mismatch", ex.Code);

        // Admin may accept: the shortfall is posted to the discrepancy account and the case is flagged.
        var r = await svc.RegisterAsync(input, acceptMismatch: true);
        Assert.True(r.MismatchAccepted);
        var disc = await app.Get<AccountDirectory>().SystemAsync(AccountCategory.Discrepancy);
        var bal = await app.Get<LedgerService>().BalancesAsync(disc.Id);
        Assert.Equal(1_000_000m, bal.Single(b => b.Currency == CurrencyCode.IRT).Balance);
        var detail = await app.Get<TransferCaseService>().GetDetailAsync(r.TransferCaseId);
        Assert.Equal(TransferStatus.Discrepancy, detail.Case.Status);
    }

    [Fact]
    public async Task Duplicate_deposits_detected_by_tracking_number_and_by_amount_bank_date()
    {
        await using var app = await TestApp.CreateAsync();
        var c = await app.NewCustomerAsync();
        var svc = app.Get<ReceiptService>();
        var now = app.Clock.UtcNow;
        await svc.RegisterAsync(new ReceiptInput(c.Id, now, 10_000_000m, CurrencyCode.IRT, CurrencyCode.IRT, null, null,
            new[] { new DepositInput("بانک ملت", now, 10_000_000m, CurrencyCode.IRT, "TRK-777") }));

        var sameTracking = new[] { new DepositInput("بانک ملی", now.AddHours(-30), 5_000_000m, CurrencyCode.IRT, "TRK-777") };
        var w1 = await svc.FindDuplicatesAsync(sameTracking, null);
        Assert.Contains(w1, w => w.Kind == DuplicateKind.SameTrackingNo && w.ExistingReceiptNumber == "YI140507001LE");

        var sameAmount = new[] { new DepositInput("بانک ملت", now.AddHours(1), 10_000_000m, CurrencyCode.IRT, "OTHER") };
        var w2 = await svc.FindDuplicatesAsync(sameAmount, null);
        Assert.Contains(w2, w => w.Kind == DuplicateKind.SameAmountBankDate);

        var withinInput = new[]
        {
            new DepositInput("بانک سپه", now, 1_000m, CurrencyCode.IRT, "DUP"),
            new DepositInput("بانک سپه", now, 2_000m, CurrencyCode.IRT, "DUP"),
        };
        Assert.Contains(await svc.FindDuplicatesAsync(withinInput, null), w => w.InputIndex == 1 && w.Kind == DuplicateKind.SameTrackingNo);

        var input = new ReceiptInput(c.Id, now, 5_000_000m, CurrencyCode.IRT, CurrencyCode.IRT, null, null, sameTracking);
        var ex = await Assert.ThrowsAsync<DuplicateDepositException>(() => svc.RegisterAsync(input));
        Assert.NotEmpty(ex.Warnings);
        var forced = await svc.RegisterAsync(input, acceptDuplicates: true);
        Assert.Equal(DocumentStatus.Posted, forced.Status);
        Assert.Empty(await svc.FindDuplicatesAsync(new[] { new DepositInput("بانک تجارت", now, 1m, CurrencyCode.IRT, "NEW-1") }, null));
    }

    [Fact]
    public async Task Receipt_requires_rate_for_cross_currency_and_validates_recipient_currency()
    {
        await using var app = await TestApp.CreateAsync();
        var c = await app.NewCustomerAsync();
        var mohammad = await app.MohammadAsync();
        var svc = app.Get<ReceiptService>();
        var now = app.Clock.UtcNow;
        var ex = await Assert.ThrowsAsync<DomainException>(() => svc.RegisterAsync(
            new ReceiptInput(c.Id, now, 100m, CurrencyCode.IRT, CurrencyCode.USD, null, null)));
        Assert.Equal("rate_required", ex.Code);
        var ex2 = await Assert.ThrowsAsync<DomainException>(() => svc.RegisterAsync(
            new ReceiptInput(c.Id, now, 100m, CurrencyCode.IRT, CurrencyCode.IRT, null, mohammad.Id)));
        Assert.Equal("recipient_currency", ex2.Code);

        // IRR receipt converted to an IRT commitment via the fixed factor, no rate needed.
        var r = await svc.RegisterAsync(new ReceiptInput(c.Id, now, 1_000_000m, CurrencyCode.IRR, CurrencyCode.IRT, null, null));
        Assert.Equal(100_000m, r.TargetAmount);
    }

    [Fact]
    public async Task Daily_rate_change_does_not_alter_past_documents()
    {
        await using var app = await TestApp.CreateAsync();
        var rates = app.Get<LenjExchange.Application.Rates.RateService>();
        var daily = await rates.SetDailyRateAsync(app.Clock.UtcNow, new ExchangeRate(CurrencyCode.USD, CurrencyCode.IRT, 100_000m));
        var suggested = await rates.LatestAsync(CurrencyCode.USD, CurrencyCode.IRT);
        var (_, receipt) = await Flow.ReceiveAsync(app, rate: suggested);

        await rates.UpdateDailyRateAsync(daily.Id, 120_000m, "افزایش نرخ");
        await rates.SetDailyRateAsync(app.Clock.UtcNow.AddMinutes(1), new ExchangeRate(CurrencyCode.USD, CurrencyCode.IRT, 125_000m));
        app.NewScope();
        var loaded = await app.Get<ReceiptService>().GetAsync(receipt.Id);
        Assert.Equal(100_000m, loaded.Rate!.Rate);
        Assert.Equal(1_000m, loaded.TargetAmount);
        app.Clock.UtcNow = app.Clock.UtcNow.AddMinutes(5);
        Assert.Equal(125_000m, (await app.Get<LenjExchange.Application.Rates.RateService>().LatestAsync(CurrencyCode.USD, CurrencyCode.IRT))!.Rate);
    }
}
