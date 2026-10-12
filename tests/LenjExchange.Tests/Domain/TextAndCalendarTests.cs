using LenjExchange.Domain.Common;
using LenjExchange.Domain.Numbering;
using LenjExchange.Domain.Shamsi;
using LenjExchange.Domain.Text;

namespace LenjExchange.Tests.Domain;

public class TextAndCalendarTests
{
    [Theory]
    [InlineData("۱۲۳٬۴۵۶", "123,456")]
    [InlineData("٠١٢٣٤٥٦٧٨٩", "0123456789")]
    [InlineData("۱۲٫۵", "12.5")]
    [InlineData("abc 123", "abc 123")]
    [InlineData("", "")]
    public void Normalizes_persian_and_arabic_digits(string input, string expected) =>
        Assert.Equal(expected, DigitNormalizer.Normalize(input));

    [Theory]
    [InlineData("۱۰۰٬۰۰۰٬۰۰۰", 100_000_000)]
    [InlineData("100,000,000", 100_000_000)]
    [InlineData("۱,۰۰۰.۵۰", 1000.50)]
    [InlineData(" 7.2 ", 7.2)]
    public void Parses_amounts_typed_with_any_digits(string input, decimal expected)
    {
        Assert.True(DigitNormalizer.TryParseDecimal(input, out var v));
        Assert.Equal(expected, v);
    }

    [Fact]
    public void Rejects_invalid_amounts()
    {
        Assert.False(DigitNormalizer.TryParseDecimal("abc", out _));
        Assert.False(DigitNormalizer.TryParseDecimal("", out _));
    }

    [Fact]
    public void Converts_to_persian_digits_and_normalizes_letters()
    {
        Assert.Equal("۱۴۰۵/۰۷/۲۰", DigitNormalizer.ToPersianDigits("1405/07/20"));
        Assert.Equal("علی", DigitNormalizer.NormalizeLetters("علي"));
        Assert.Equal("کریم", DigitNormalizer.NormalizeLetters("كريم"));
    }

    [Fact]
    public void Shamsi_round_trip_and_known_dates()
    {
        var d = new DateTime(2026, 10, 12);
        Assert.Equal("1405/07/20", ShamsiDate.Format(d));
        Assert.Equal(d, ShamsiDate.Parse("1405/07/20"));
        Assert.Equal(d, ShamsiDate.Parse("۱۴۰۵/۷/۲۰"));
        Assert.Equal(new DateTime(2026, 3, 21), ShamsiDate.ToGregorian(1405, 1, 1));
        Assert.False(ShamsiDate.TryParse("1405/13/01", out _));
        Assert.False(ShamsiDate.TryParse("1405/07/31", out _)); // Mehr has 30 days
        Assert.Equal(30, ShamsiDate.DaysInMonth(1405, 7));
        Assert.Equal(31, ShamsiDate.DaysInMonth(1405, 1));
    }

    [Fact]
    public void Persian_week_starts_on_saturday()
    {
        Assert.Equal(0, ShamsiDate.PersianWeekDayIndex(new DateTime(2026, 10, 10))); // Saturday
        Assert.Equal(2, ShamsiDate.PersianWeekDayIndex(new DateTime(2026, 10, 12))); // Monday
        Assert.Equal(6, ShamsiDate.PersianWeekDayIndex(new DateTime(2026, 10, 16))); // Friday
        Assert.StartsWith("دوشنبه 20 مهر 1405", ShamsiDate.FormatLong(new DateTime(2026, 10, 12)));
    }

    [Fact]
    public void Default_receipt_pattern_reproduces_sample_number()
    {
        var p = ReceiptNumberPattern.Default;
        var mehr12 = ShamsiDate.ToGregorian(1405, 7, 12);
        Assert.Equal("YI140507126LE", p.Format(mehr12, 126));
        Assert.Equal("YI140507012LE", p.Format(mehr12, 12));
        Assert.Equal("receipt:1405-07", p.SequenceKey("receipt", mehr12));
        Assert.Matches(p.ToRegex(), "YI140507126LE");
        Assert.DoesNotMatch(p.ToRegex(), "YI1405071LE");
    }

    [Fact]
    public void Pattern_tokens_and_validation()
    {
        var p = new ReceiptNumberPattern("R-{yy}{MM}{dd}-{seq:4}", SequenceScope.Daily);
        Assert.Equal("R-050720-0007", p.Format(new DateTime(2026, 10, 12), 7));
        Assert.Equal("x:1405-07-20", p.SequenceKey("x", new DateTime(2026, 10, 12)));
        Assert.Throws<DomainException>(() => new ReceiptNumberPattern("YI{yyyy}LE", SequenceScope.Monthly));
        Assert.Throws<DomainException>(() => new ReceiptNumberPattern("YI{foo}{seq}", SequenceScope.Monthly));
    }
}
