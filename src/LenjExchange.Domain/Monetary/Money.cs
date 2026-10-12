using System.Globalization;
using LenjExchange.Domain.Common;

namespace LenjExchange.Domain.Monetary;

/// <summary>
/// Immutable amount + currency. Arithmetic between different currencies is forbidden;
/// use <see cref="ExchangeRate.Convert"/> or <see cref="CurrencyMath"/> explicitly.
/// </summary>
public readonly record struct Money(decimal Amount, CurrencyCode Currency)
{
    public static Money Zero(CurrencyCode currency) => new(0m, currency);

    public static Money Of(decimal amount, CurrencyCode currency) =>
        new(Currencies.Round(amount, currency), currency);

    public Money Rounded() => new(Currencies.Round(Amount, Currency), Currency);

    public bool IsZero => Amount == 0m;
    public bool IsNegative => Amount < 0m;

    public static Money operator +(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.Amount + b.Amount, a.Currency);
    }

    public static Money operator -(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.Amount - b.Amount, a.Currency);
    }

    public static Money operator -(Money a) => new(-a.Amount, a.Currency);

    public static Money operator *(Money a, decimal factor) => new(a.Amount * factor, a.Currency);

    public static bool operator >(Money a, Money b) { EnsureSameCurrency(a, b); return a.Amount > b.Amount; }
    public static bool operator <(Money a, Money b) { EnsureSameCurrency(a, b); return a.Amount < b.Amount; }
    public static bool operator >=(Money a, Money b) { EnsureSameCurrency(a, b); return a.Amount >= b.Amount; }
    public static bool operator <=(Money a, Money b) { EnsureSameCurrency(a, b); return a.Amount <= b.Amount; }

    private static void EnsureSameCurrency(Money a, Money b)
    {
        if (a.Currency != b.Currency)
            throw new DomainException("currency_mismatch",
                $"جمع یا مقایسهٔ ارزهای متفاوت ({a.Currency} و {b.Currency}) بدون نرخ تبدیل مجاز نیست.");
    }

    public string Format(bool withCode = true)
    {
        var info = Currencies.Info(Currency);
        var text = AmountFormatter.Format(Amount, Currency);
        return withCode ? $"{text} {info.Symbol}" : text;
    }

    public override string ToString() => Format();
}

public static class AmountFormatter
{
    /// <summary>Formats with thousand separators and the currency's display precision (invariant digits).</summary>
    public static string Format(decimal amount, CurrencyCode currency)
    {
        var info = Currencies.Info(currency);
        var rounded = Math.Round(amount, info.MaxDisplayDecimals, MidpointRounding.AwayFromZero);
        var fmt = "#,0";
        if (info.MaxDisplayDecimals > 0)
            fmt += "." + new string('0', info.MinDisplayDecimals) + new string('#', info.MaxDisplayDecimals - info.MinDisplayDecimals);
        return rounded.ToString(fmt, CultureInfo.InvariantCulture);
    }
}

public static class CurrencyMath
{
    public static Money RialToToman(Money rial)
    {
        if (rial.Currency != CurrencyCode.IRR) throw new DomainException("currency", "ورودی باید ریال باشد.");
        return Money.Of(rial.Amount / Currencies.RialPerToman, CurrencyCode.IRT);
    }

    public static Money TomanToRial(Money toman)
    {
        if (toman.Currency != CurrencyCode.IRT) throw new DomainException("currency", "ورودی باید تومان باشد.");
        return new Money(toman.Amount * Currencies.RialPerToman, CurrencyCode.IRR);
    }

    /// <summary>
    /// IRR and IRT are the same currency at a fixed factor of 10; reconciliation normalizes IRR into IRT.
    /// Other currencies are returned unchanged.
    /// </summary>
    public static Money NormalizeIranian(Money money) =>
        money.Currency == CurrencyCode.IRR ? RialToToman(money) : money;

    public static bool AreFixedEquivalent(CurrencyCode a, CurrencyCode b) =>
        a == b || (IsIranian(a) && IsIranian(b));

    public static bool IsIranian(CurrencyCode c) => c is CurrencyCode.IRT or CurrencyCode.IRR;

    /// <summary>Converts between currencies that are equal or IRR/IRT (fixed factor); throws otherwise.</summary>
    public static Money ConvertFixed(Money money, CurrencyCode target)
    {
        if (money.Currency == target) return money;
        if (money.Currency == CurrencyCode.IRR && target == CurrencyCode.IRT) return RialToToman(money);
        if (money.Currency == CurrencyCode.IRT && target == CurrencyCode.IRR) return TomanToRial(money);
        throw new DomainException("rate_required", $"برای تبدیل {money.Currency} به {target} نرخ صریح لازم است.");
    }
}
