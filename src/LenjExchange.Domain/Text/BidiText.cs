using System.Text.RegularExpressions;

namespace LenjExchange.Domain.Text;

/// <summary>
/// Keeps Latin/number runs (rates such as "1 USD = 100,000 IRT", receipt numbers, amounts with currency codes)
/// in left-to-right order when they appear inside right-to-left Persian text, by wrapping each run in
/// LRE … PDF embedding marks. Applied only at render time; stored text stays plain.
/// </summary>
public static partial class BidiText
{
    public const char Lre = '\u202A';
    public const char Pdf = '\u202C';

    [GeneratedRegex(@"[0-9A-Za-z(][0-9A-Za-z ,.=:/\-()]*[0-9A-Za-z)]|[0-9A-Za-z]")]
    private static partial Regex LatinRun();

    public static string IsolateLatinRuns(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (text.Contains(Lre)) return text;
        return LatinRun().Replace(text, m => $"{Lre}{m.Value}{Pdf}");
    }

    public static string Strip(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Replace(Lre.ToString(), "").Replace(Pdf.ToString(), "");
}
