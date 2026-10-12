using System.Globalization;
using System.Text;

namespace LenjExchange.Domain.Text;

/// <summary>
/// Normalizes user input that may contain Persian (۰-۹) or Arabic-Indic (٠-٩) digits, Persian thousand
/// separators (٬) and decimal separators (٫ or /) into invariant ASCII.
/// </summary>
public static class DigitNormalizer
{
    private const string PersianDigits = "۰۱۲۳۴۵۶۷۸۹";
    private const string ArabicDigits = "٠١٢٣٤٥٦٧٨٩";

    public static string Normalize(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            var p = PersianDigits.IndexOf(ch);
            if (p >= 0) { sb.Append((char)('0' + p)); continue; }
            var a = ArabicDigits.IndexOf(ch);
            if (a >= 0) { sb.Append((char)('0' + a)); continue; }
            sb.Append(ch switch
            {
                '٫' => '.',
                '٬' => ',',
                '،' => ',',
                _ => ch
            });
        }
        return sb.ToString();
    }

    public static string ToPersianDigits(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
            sb.Append(ch is >= '0' and <= '9' ? PersianDigits[ch - '0'] : ch);
        return sb.ToString();
    }

    /// <summary>Parses an amount typed with any digits and optional thousand separators.</summary>
    public static bool TryParseDecimal(string? input, out decimal value)
    {
        value = 0;
        var s = Normalize(input).Trim().Replace(",", "").Replace(" ", "").Replace("\u200c", "");
        if (s.Length == 0) return false;
        return decimal.TryParse(s, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value);
    }

    public static decimal ParseDecimal(string? input) =>
        TryParseDecimal(input, out var v) ? v : throw new FormatException($"عدد «{input}» معتبر نیست.");

    /// <summary>Normalizes Arabic letters (ي، ك) to their Persian forms for consistent search.</summary>
    public static string NormalizeLetters(string? input) =>
        string.IsNullOrEmpty(input) ? string.Empty : Normalize(input).Replace('ي', 'ی').Replace('ك', 'ک').Replace('ى', 'ی');
}
