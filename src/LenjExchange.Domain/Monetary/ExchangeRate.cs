using System.Globalization;
using LenjExchange.Domain.Common;

namespace LenjExchange.Domain.Monetary;

/// <summary>
/// Explicit-direction rate: 1 <see cref="BaseCurrency"/> = <see cref="Rate"/> <see cref="QuoteCurrency"/>.
/// Example: 1 USD = 100,000 IRT → Base=USD, Quote=IRT, Rate=100000.
/// Stored as a snapshot on every document that uses it; never looked up retroactively.
/// </summary>
public sealed record ExchangeRate
{
    public CurrencyCode BaseCurrency { get; init; }
    public CurrencyCode QuoteCurrency { get; init; }
    public decimal Rate { get; init; }

    private ExchangeRate() { }

    public ExchangeRate(CurrencyCode baseCurrency, CurrencyCode quoteCurrency, decimal rate)
    {
        if (baseCurrency == quoteCurrency)
            throw new DomainException("rate_same_currency", "ارز پایه و ارز مقابل نمی‌توانند یکسان باشند.");
        if (rate <= 0)
            throw new DomainException("rate_positive", "نرخ تبدیل باید بزرگ‌تر از صفر باشد.");
        BaseCurrency = baseCurrency;
        QuoteCurrency = quoteCurrency;
        Rate = rate;
    }

    public bool Involves(CurrencyCode c) =>
        BaseCurrency == c || QuoteCurrency == c ||
        (CurrencyMath.IsIranian(c) && (CurrencyMath.IsIranian(BaseCurrency) || CurrencyMath.IsIranian(QuoteCurrency)));

    public ExchangeRate Inverse() => new(QuoteCurrency, BaseCurrency, 1m / Rate);

    /// <summary>
    /// Converts <paramref name="money"/> to <paramref name="target"/>. IRR input/output is handled through the
    /// fixed Rial/Toman factor when the rate is expressed in IRT (or vice versa).
    /// </summary>
    public Money Convert(Money money, CurrencyCode target)
    {
        var source = money;
        var sourceCurrency = Align(source.Currency);
        var targetCurrency = Align(target);
        if (sourceCurrency != source.Currency) source = CurrencyMath.ConvertFixed(source, sourceCurrency);

        decimal converted;
        if (sourceCurrency == BaseCurrency && targetCurrency == QuoteCurrency)
            converted = source.Amount * Rate;
        else if (sourceCurrency == QuoteCurrency && targetCurrency == BaseCurrency)
            converted = source.Amount / Rate;
        else
            throw new DomainException("rate_direction",
                $"نرخ {Describe()} برای تبدیل {money.Currency} به {target} قابل استفاده نیست.");

        var result = Money.Of(converted, targetCurrency);
        return targetCurrency != target ? CurrencyMath.ConvertFixed(result, target).Rounded() : result;
    }

    /// <summary>Units of <paramref name="target"/> per 1 unit of <paramref name="source"/>.</summary>
    public decimal FactorFor(CurrencyCode source, CurrencyCode target)
    {
        var s = Align(source);
        var t = Align(target);
        decimal factor;
        if (s == BaseCurrency && t == QuoteCurrency) factor = Rate;
        else if (s == QuoteCurrency && t == BaseCurrency) factor = 1m / Rate;
        else throw new DomainException("rate_direction", $"نرخ {Describe()} برای {source}/{target} قابل استفاده نیست.");
        if (source == CurrencyCode.IRR && s == CurrencyCode.IRT) factor /= Currencies.RialPerToman;
        if (source == CurrencyCode.IRT && s == CurrencyCode.IRR) factor *= Currencies.RialPerToman;
        if (target == CurrencyCode.IRR && t == CurrencyCode.IRT) factor *= Currencies.RialPerToman;
        if (target == CurrencyCode.IRT && t == CurrencyCode.IRR) factor /= Currencies.RialPerToman;
        return factor;
    }

    private CurrencyCode Align(CurrencyCode c)
    {
        if (c == BaseCurrency || c == QuoteCurrency) return c;
        if (CurrencyMath.IsIranian(c))
        {
            if (CurrencyMath.IsIranian(BaseCurrency)) return BaseCurrency;
            if (CurrencyMath.IsIranian(QuoteCurrency)) return QuoteCurrency;
        }
        return c;
    }

    public string Describe() =>
        $"1 {BaseCurrency} = {Rate.ToString("#,0.########", CultureInfo.InvariantCulture)} {QuoteCurrency}";

    public override string ToString() => Describe();
}
