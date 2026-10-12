using System.Globalization;
using LenjExchange.Domain.Common;
using LenjExchange.Domain.Text;

namespace LenjExchange.Domain.Shamsi;

/// <summary>Solar Hijri (Shamsi) helpers on top of <see cref="PersianCalendar"/>.</summary>
public static class ShamsiDate
{
    private static readonly PersianCalendar Calendar = new();

    public static readonly string[] MonthNames =
    {
        "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
    };

    public static readonly string[] WeekDayNames =
    {
        "شنبه", "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه"
    };

    public static (int Year, int Month, int Day) Parts(DateTime date) =>
        (Calendar.GetYear(date), Calendar.GetMonth(date), Calendar.GetDayOfMonth(date));

    public static DateTime ToGregorian(int year, int month, int day) =>
        Calendar.ToDateTime(year, month, day, 0, 0, 0, 0);

    public static int DaysInMonth(int year, int month) => Calendar.GetDaysInMonth(year, month);

    public static int DaysInYear(int year) => Calendar.GetDaysInYear(year);

    /// <summary>yyyy/MM/dd (invariant digits).</summary>
    public static string Format(DateTime date, bool persianDigits = false)
    {
        var (y, m, d) = Parts(date);
        var s = $"{y:0000}/{m:00}/{d:00}";
        return persianDigits ? DigitNormalizer.ToPersianDigits(s) : s;
    }

    public static string FormatWithTime(DateTime date, bool persianDigits = false)
    {
        var s = $"{Format(date)} {date:HH:mm}";
        return persianDigits ? DigitNormalizer.ToPersianDigits(s) : s;
    }

    public static string FormatLong(DateTime date)
    {
        var (y, m, d) = Parts(date);
        return $"{WeekDayNames[PersianWeekDayIndex(date)]} {d} {MonthNames[m - 1]} {y}";
    }

    /// <summary>0 = Saturday (شنبه) … 6 = Friday (جمعه).</summary>
    public static int PersianWeekDayIndex(DateTime date) => ((int)date.DayOfWeek + 1) % 7;

    /// <summary>Parses "1405/07/12", "1405-7-12", "۱۴۰۵/۰۷/۱۲" (Persian/Arabic digits accepted).</summary>
    public static bool TryParse(string? text, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = DigitNormalizer.Normalize(text).Trim()
            .Split(new[] { '/', '-', '.', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var y) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var d)) return false;
        if (y < 100) y += 1400;
        if (m is < 1 or > 12 || y < 1300 || y > 1500) return false;
        if (d < 1 || d > DaysInMonth(y, m)) return false;
        date = ToGregorian(y, m, d);
        return true;
    }

    public static DateTime Parse(string text) =>
        TryParse(text, out var d) ? d : throw new DomainException("shamsi_date", $"تاریخ «{text}» معتبر نیست.");
}
