using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LenjExchange.Domain.Common;

namespace LenjExchange.Domain.Numbering;

public enum SequenceScope
{
    Global = 0,
    Yearly = 1,
    Monthly = 2,
    Daily = 3
}

/// <summary>
/// Configurable document-number pattern. Tokens (Shamsi calendar):
/// <c>{yyyy}</c> 4-digit year, <c>{yy}</c> 2-digit year, <c>{MM}</c> month, <c>{dd}</c> day,
/// <c>{seq}</c> or <c>{seq:N}</c> zero-padded sequence. Everything else is literal.
/// The default <c>YI{yyyy}{MM}{seq:3}LE</c> reproduces the sample <c>YI140507126LE</c>
/// (1405 / 07 / 126) — the real semantics are an OPEN QUESTION (docs/open-questions.md).
/// </summary>
public sealed partial class ReceiptNumberPattern
{
    public const string DefaultPattern = "YI{yyyy}{MM}{seq:3}LE";

    [GeneratedRegex(@"\{(yyyy|yy|MM|dd|seq(?::(\d{1,2}))?)\}")]
    private static partial Regex TokenRegex();

    public string Pattern { get; }
    public SequenceScope Scope { get; }

    public ReceiptNumberPattern(string pattern, SequenceScope scope)
    {
        Pattern = Guard.NotBlank(pattern, "الگوی شماره");
        if (!Pattern.Contains("{seq", StringComparison.Ordinal))
            throw new DomainException("pattern_seq", "الگوی شماره باید شامل {seq} باشد تا یکتا بماند.");
        Scope = scope;
        Validate();
    }

    public static ReceiptNumberPattern Default => new(DefaultPattern, SequenceScope.Monthly);

    private void Validate()
    {
        var stripped = TokenRegex().Replace(Pattern, "");
        if (stripped.Contains('{') || stripped.Contains('}'))
            throw new DomainException("pattern_token", "الگوی شماره شامل نشانهٔ ناشناخته است.");
    }

    /// <summary>Key of the DB sequence this date belongs to, e.g. "receipt:1405-07" for monthly scope.</summary>
    public string SequenceKey(string documentType, DateTime localDate)
    {
        var (y, m, d) = Shamsi.ShamsiDate.Parts(localDate);
        return Scope switch
        {
            SequenceScope.Global => $"{documentType}",
            SequenceScope.Yearly => $"{documentType}:{y:0000}",
            SequenceScope.Monthly => $"{documentType}:{y:0000}-{m:00}",
            SequenceScope.Daily => $"{documentType}:{y:0000}-{m:00}-{d:00}",
            _ => documentType
        };
    }

    public string Format(DateTime localDate, long sequence)
    {
        if (sequence <= 0) throw new DomainException("seq_positive", "شمارهٔ ترتیبی باید مثبت باشد.");
        var (y, m, d) = Shamsi.ShamsiDate.Parts(localDate);
        return TokenRegex().Replace(Pattern, match =>
        {
            var token = match.Groups[1].Value;
            return token switch
            {
                "yyyy" => y.ToString("0000", CultureInfo.InvariantCulture),
                "yy" => (y % 100).ToString("00", CultureInfo.InvariantCulture),
                "MM" => m.ToString("00", CultureInfo.InvariantCulture),
                "dd" => d.ToString("00", CultureInfo.InvariantCulture),
                _ => sequence.ToString(new string('0', match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 1), CultureInfo.InvariantCulture)
            };
        });
    }

    /// <summary>Regex that matches numbers produced by this pattern (used for validation/import).</summary>
    public Regex ToRegex()
    {
        var sb = new StringBuilder("^");
        var last = 0;
        foreach (Match m in TokenRegex().Matches(Pattern))
        {
            sb.Append(Regex.Escape(Pattern[last..m.Index]));
            var token = m.Groups[1].Value;
            sb.Append(token switch
            {
                "yyyy" => @"\d{4}",
                "yy" or "MM" or "dd" => @"\d{2}",
                _ => m.Groups[2].Success ? $@"\d{{{m.Groups[2].Value},}}" : @"\d+"
            });
            last = m.Index + m.Length;
        }
        sb.Append(Regex.Escape(Pattern[last..])).Append('$');
        return new Regex(sb.ToString());
    }
}

/// <summary>DB-backed counter; incremented inside the same transaction that finalizes the document.</summary>
public class NumberSequence
{
    public string Key { get; private set; } = "";
    public long LastValue { get; private set; }

    protected NumberSequence() { }

    public NumberSequence(string key)
    {
        Key = Guard.NotBlank(key, "کلید");
    }

    public long Next() => ++LastValue;
}
