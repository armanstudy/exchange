using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Reporting;
using LenjExchange.Application.Transfers;
using LenjExchange.Domain.Ledger;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Shamsi;
using LenjExchange.Tests.Support;

namespace LenjExchange.Tests.Integration;

public class ReportTests
{
    private static FixedClock Clock(DateTime localNow) =>
        new(TimeZoneInfo.ConvertTimeToUtc(localNow, FixedClock.Tehran), FixedClock.Tehran);

    [Fact]
    public void Daily_presets_cover_whole_local_days()
    {
        var clock = Clock(new DateTime(2026, 10, 12, 0, 10, 0)); // 1405/07/20 00:10 Tehran (still 2026-10-11 in UTC)
        var today = DateRanges.For(clock, DatePreset.Today);
        Assert.Equal("1405/07/20", ShamsiDate.Format(today.FromLocal));
        Assert.Equal(today.FromLocal, today.ToLocalInclusive);
        Assert.Equal(new DateTime(2026, 10, 11, 20, 30, 0, DateTimeKind.Utc), today.FromUtc); // Tehran is UTC+3:30
        Assert.Equal(TimeSpan.FromDays(1), today.ToUtcExclusive - today.FromUtc);
        Assert.True(today.Contains(clock.UtcNow));

        var yesterday = DateRanges.For(clock, DatePreset.Yesterday);
        Assert.Equal("1405/07/19", ShamsiDate.Format(yesterday.FromLocal));
        Assert.Equal(today.FromUtc, yesterday.ToUtcExclusive);

        var day = DateRanges.For(clock, DatePreset.SpecificDay, ShamsiDate.ToGregorian(1405, 1, 1));
        Assert.Equal(new DateTime(2026, 3, 21), day.FromLocal);
    }

    [Fact]
    public void Weekly_presets_start_on_saturday()
    {
        var clock = Clock(new DateTime(2026, 10, 12, 12, 0, 0)); // Monday
        var week = DateRanges.For(clock, DatePreset.ThisWeek);
        Assert.Equal(DayOfWeek.Saturday, week.FromLocal.DayOfWeek);
        Assert.Equal(DayOfWeek.Friday, week.ToLocalInclusive.DayOfWeek);
        Assert.Equal("1405/07/18", ShamsiDate.Format(week.FromLocal));
        Assert.Equal("1405/07/24", ShamsiDate.Format(week.ToLocalInclusive));
        var last = DateRanges.For(clock, DatePreset.LastWeek);
        Assert.Equal("1405/07/11", ShamsiDate.Format(last.FromLocal));
        Assert.Equal(week.FromUtc, last.ToUtcExclusive);

        var saturday = Clock(new DateTime(2026, 10, 10, 9, 0, 0));
        Assert.Equal(new DateTime(2026, 10, 10), DateRanges.For(saturday, DatePreset.ThisWeek).FromLocal);
        var friday = Clock(new DateTime(2026, 10, 16, 9, 0, 0));
        Assert.Equal(new DateTime(2026, 10, 10), DateRanges.For(friday, DatePreset.ThisWeek).FromLocal);
    }

    [Fact]
    public void Monthly_and_yearly_presets_follow_shamsi_calendar()
    {
        var clock = Clock(new DateTime(2026, 10, 12, 12, 0, 0));
        var month = DateRanges.For(clock, DatePreset.ThisMonth);
        Assert.Equal("1405/07/01", ShamsiDate.Format(month.FromLocal));
        Assert.Equal("1405/07/30", ShamsiDate.Format(month.ToLocalInclusive));
        var last = DateRanges.For(clock, DatePreset.LastMonth);
        Assert.Equal("1405/06/01", ShamsiDate.Format(last.FromLocal));
        Assert.Equal("1405/06/31", ShamsiDate.Format(last.ToLocalInclusive));
        Assert.Equal(month.FromUtc, last.ToUtcExclusive);

        var farvardin = Clock(new DateTime(2026, 3, 25, 12, 0, 0)); // 1405/01/05 → last month = Esfand 1404
        var esfand = DateRanges.For(farvardin, DatePreset.LastMonth);
        Assert.Equal("1404/12/01", ShamsiDate.Format(esfand.FromLocal));
        Assert.Equal("1404/12/29", ShamsiDate.Format(esfand.ToLocalInclusive));

        var year = DateRanges.For(clock, DatePreset.ThisYear);
        Assert.Equal("1405/01/01", ShamsiDate.Format(year.FromLocal));
        Assert.Equal("1405/12/" + ShamsiDate.DaysInMonth(1405, 12).ToString("00"), ShamsiDate.Format(year.ToLocalInclusive));
    }

    [Fact]
    public void Custom_range_is_inclusive_and_validated()
    {
        var clock = Clock(new DateTime(2026, 10, 12, 12, 0, 0));
        var r = DateRanges.For(clock, DatePreset.Custom, ShamsiDate.Parse("1405/07/01"), ShamsiDate.Parse("1405/07/10"));
        Assert.True(r.Contains(clock.ToUtc(ShamsiDate.Parse("1405/07/10").AddHours(23).AddMinutes(59))));
        Assert.False(r.Contains(clock.ToUtc(ShamsiDate.Parse("1405/07/11"))));
        Assert.True(r.Contains(clock.ToUtc(ShamsiDate.Parse("1405/07/01"))));
        Assert.False(r.Contains(clock.ToUtc(ShamsiDate.Parse("1405/07/01")).AddTicks(-1)));
        Assert.Throws<LenjExchange.Domain.Common.DomainException>(() =>
            DateRanges.For(clock, DatePreset.Custom, ShamsiDate.Parse("1405/07/10"), ShamsiDate.Parse("1405/07/01")));
    }

    [Fact]
    public async Task Statement_respects_day_boundaries_and_filters()
    {
        await using var app = await TestApp.CreateAsync();
        var c = await app.NewCustomerAsync("نگار");
        await Flow.ReceiveAsync(app, 10_000_000m, customer: c, dateUtc: app.Local(2026, 10, 11, 23, 59)); // 1405/07/19 late night
        await Flow.ReceiveAsync(app, 20_000_000m, customer: c, dateUtc: app.Local(2026, 10, 12, 0, 1));   // 1405/07/20 just after midnight
        await Flow.ReceiveAsync(app, 30_000_000m, customer: c, dateUtc: app.Local(2026, 10, 12, 23, 58));

        var reports = app.Get<ReportService>();
        var today = DateRanges.For(app.Clock, DatePreset.Today);
        var st = await reports.CustomerStatementAsync(c.Id, today, CurrencyCode.USD);
        var usd = st.Sections.Single();
        Assert.Equal(-100m, usd.Opening);
        Assert.Equal(500m, usd.TotalCredit);
        Assert.Equal(2, usd.Rows.Count);
        Assert.Equal("1405/07/20", usd.Rows[0].ShamsiDate);
        Assert.Equal(-600m, usd.Closing);

        var yesterday = await reports.CustomerStatementAsync(c.Id, DateRanges.For(app.Clock, DatePreset.Yesterday), CurrencyCode.USD);
        Assert.Single(yesterday.Sections.Single().Rows);

        var receiptsOnly = await reports.CustomerStatementAsync(c.Id, today, null, OperationType.CustomerPayment);
        Assert.All(receiptsOnly.Sections, s => Assert.Empty(s.Rows));
        var week = await reports.CustomerStatementAsync(c.Id, DateRanges.For(app.Clock, DatePreset.ThisWeek), CurrencyCode.USD);
        Assert.Equal(3, week.Sections.Single().Rows.Count);
    }

    [Fact]
    public async Task Excluding_reversed_entries_keeps_balances_consistent()
    {
        await using var app = await TestApp.CreateAsync();
        var (c, r) = await Flow.ReceiveAsync(app);
        await Flow.ReceiveAsync(app, 20_000_000m, customer: c);
        await app.Get<ReversalService>().ReverseReceiptAsync(r.Id, "ثبت تکراری");
        var range = DateRanges.For(app.Clock, DatePreset.ThisMonth);
        var all = await app.Get<ReportService>().CustomerStatementAsync(c.Id, range, CurrencyCode.USD);
        var clean = await app.Get<ReportService>().CustomerStatementAsync(c.Id, range, CurrencyCode.USD, status: StatusFilter.ExcludeReversed);
        Assert.Equal(-200m, all.Sections.Single().Closing);
        Assert.Equal(-200m, clean.Sections.Single().Closing);
        Assert.Equal(3, all.Sections.Single().Rows.Count);
        Assert.Single(clean.Sections.Single().Rows);
    }

    [Fact]
    public async Task Party_reports_and_dashboard_keep_currencies_separate()
    {
        await using var app = await TestApp.CreateAsync();
        var (_, r) = await Flow.ReceiveAsync(app);
        var c = await Flow.CommitAsync(app, r);
        await Flow.DeliverAsync(app, c, 1_000m);
        await Flow.ChinaAsync(app, r.TransferCaseId, 300m, CurrencyCode.CNY, new ExchangeRate(CurrencyCode.USD, CurrencyCode.CNY, 7.2m));
        await Flow.ChinaAsync(app, r.TransferCaseId, 200m);

        var reports = app.Get<ReportService>();
        var range = DateRanges.For(app.Clock, DatePreset.ThisMonth);
        var agents = await reports.PartyBalancesAsync(AccountCategory.AgentFunds, range);
        Assert.Equal(500m, agents.Rows.Single(x => x.AccountName.Contains("آیدین")).Balance);
        var recipients = await reports.PartyBalancesAsync(AccountCategory.Recipient, range);
        Assert.Equal(2_160m, recipients.Rows.Single(x => x.Currency == CurrencyCode.CNY).Balance);
        Assert.Equal(200m, recipients.Rows.Single(x => x.Currency == CurrencyCode.USD).Balance);
        var exch = await reports.PartyBalancesAsync(AccountCategory.Exchanger, range);
        Assert.All(exch.Rows, x => Assert.Equal(0m, x.Balance));

        var dash = await reports.DashboardAsync();
        Assert.Equal(1, dash.CustomerCount);
        Assert.Equal(100_000_000m, dash.TodayReceipts.Single(m => m.Currency == CurrencyCode.IRT).Amount);
        Assert.Equal(1, dash.OpenCases);
        Assert.Equal(0, dash.SettledCases);
        Assert.Equal(500m, dash.AgentUsdHeld.Single(a => a.Name.Contains("آیدین")).Balances.Single().Amount);
        var moh = dash.RecipientReceipts.Single();
        Assert.Equal(2, moh.Balances.Count);
        Assert.NotEmpty(dash.LatestTransactions);
    }
}
