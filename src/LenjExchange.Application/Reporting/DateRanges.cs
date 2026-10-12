using LenjExchange.Application.Abstractions;
using LenjExchange.Domain.Common;
using LenjExchange.Domain.Shamsi;

namespace LenjExchange.Application.Reporting;

public enum DatePreset
{
    Today = 1,
    Yesterday = 2,
    SpecificDay = 3,
    ThisWeek = 4,
    LastWeek = 5,
    ThisMonth = 6,
    LastMonth = 7,
    ThisYear = 8,
    Custom = 9,
    All = 10
}

/// <summary>
/// Inclusive local-calendar range [<see cref="FromLocal"/>, <see cref="ToLocalInclusive"/>] covering whole days.
/// Queries use the half-open UTC interval [<see cref="FromUtc"/>, <see cref="ToUtcExclusive"/>).
/// </summary>
public sealed record DateRange(DateTime FromLocal, DateTime ToLocalInclusive, DateTime FromUtc, DateTime ToUtcExclusive)
{
    public string Describe() => $"از {ShamsiDate.Format(FromLocal)} تا {ShamsiDate.Format(ToLocalInclusive)}";
    public bool Contains(DateTime utc) => utc >= FromUtc && utc < ToUtcExclusive;
}

public static class DatePresetText
{
    public static string Persian(DatePreset p) => p switch
    {
        DatePreset.Today => "امروز",
        DatePreset.Yesterday => "دیروز",
        DatePreset.SpecificDay => "یک روز مشخص",
        DatePreset.ThisWeek => "این هفته",
        DatePreset.LastWeek => "هفته گذشته",
        DatePreset.ThisMonth => "این ماه",
        DatePreset.LastMonth => "ماه گذشته",
        DatePreset.ThisYear => "امسال",
        DatePreset.Custom => "بازه دلخواه",
        DatePreset.All => "همه",
        _ => p.ToString()
    };
}

public static class DateRanges
{
    /// <summary>Builds a range in the clock's business time zone. Persian week starts on Saturday.</summary>
    public static DateRange For(IClock clock, DatePreset preset, DateTime? dayOrFromLocal = null, DateTime? toLocal = null)
    {
        var today = clock.LocalToday();
        DateTime from, to;
        switch (preset)
        {
            case DatePreset.Today: from = to = today; break;
            case DatePreset.Yesterday: from = to = today.AddDays(-1); break;
            case DatePreset.SpecificDay:
                from = to = (dayOrFromLocal ?? throw new DomainException("date_required", "تاریخ را مشخص کنید.")).Date;
                break;
            case DatePreset.ThisWeek:
                from = today.AddDays(-ShamsiDate.PersianWeekDayIndex(today));
                to = from.AddDays(6);
                break;
            case DatePreset.LastWeek:
                from = today.AddDays(-ShamsiDate.PersianWeekDayIndex(today) - 7);
                to = from.AddDays(6);
                break;
            case DatePreset.ThisMonth:
            {
                var (y, m, _) = ShamsiDate.Parts(today);
                from = ShamsiDate.ToGregorian(y, m, 1);
                to = ShamsiDate.ToGregorian(y, m, ShamsiDate.DaysInMonth(y, m));
                break;
            }
            case DatePreset.LastMonth:
            {
                var (y, m, _) = ShamsiDate.Parts(today);
                if (--m == 0) { m = 12; y--; }
                from = ShamsiDate.ToGregorian(y, m, 1);
                to = ShamsiDate.ToGregorian(y, m, ShamsiDate.DaysInMonth(y, m));
                break;
            }
            case DatePreset.ThisYear:
            {
                var (y, _, _) = ShamsiDate.Parts(today);
                from = ShamsiDate.ToGregorian(y, 1, 1);
                to = ShamsiDate.ToGregorian(y, 12, ShamsiDate.DaysInMonth(y, 12));
                break;
            }
            case DatePreset.Custom:
                from = (dayOrFromLocal ?? throw new DomainException("date_required", "تاریخ شروع را مشخص کنید.")).Date;
                to = (toLocal ?? throw new DomainException("date_required", "تاریخ پایان را مشخص کنید.")).Date;
                if (to < from) throw new DomainException("date_order", "تاریخ پایان قبل از تاریخ شروع است.");
                break;
            case DatePreset.All:
                from = new DateTime(2000, 1, 1);
                to = today.AddYears(50);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(preset));
        }
        return new DateRange(from, to, clock.ToUtc(from), clock.ToUtc(to.AddDays(1)));
    }

    public static DateRange ShamsiMonth(IClock clock, int year, int month) =>
        For(clock, DatePreset.Custom, ShamsiDate.ToGregorian(year, month, 1), ShamsiDate.ToGregorian(year, month, ShamsiDate.DaysInMonth(year, month)));
}
