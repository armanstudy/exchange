using LenjExchange.Domain.Common;
using LenjExchange.Domain.Monetary;

namespace LenjExchange.Domain.Rates;

/// <summary>
/// Reference rate table (نرخ روز). Used only as a default suggestion when entering documents; every document
/// stores its own <see cref="ExchangeRate"/> snapshot, so editing a daily rate never changes past documents.
/// </summary>
public class DailyRate : AuditableEntity
{
    public DateTime EffectiveDateUtc { get; private set; }
    public CurrencyCode BaseCurrency { get; private set; }
    public CurrencyCode QuoteCurrency { get; private set; }
    public decimal Rate { get; private set; }
    public string? Notes { get; private set; }

    protected DailyRate() { }

    public DailyRate(DateTime effectiveDateUtc, ExchangeRate rate, string? notes)
    {
        EffectiveDateUtc = Guard.Utc(effectiveDateUtc);
        BaseCurrency = rate.BaseCurrency;
        QuoteCurrency = rate.QuoteCurrency;
        Rate = rate.Rate;
        Notes = notes;
    }

    public ExchangeRate ToExchangeRate() => new(BaseCurrency, QuoteCurrency, Rate);

    public void Update(decimal rate, string? notes)
    {
        Rate = Guard.Positive(rate, "نرخ");
        Notes = notes;
    }
}
