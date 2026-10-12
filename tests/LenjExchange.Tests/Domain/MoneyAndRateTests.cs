using LenjExchange.Domain.Common;
using LenjExchange.Domain.Monetary;

namespace LenjExchange.Tests.Domain;

public class MoneyAndRateTests
{
    [Fact]
    public void Adding_different_currencies_is_forbidden()
    {
        var usd = Money.Of(10, CurrencyCode.USD);
        var usdt = Money.Of(10, CurrencyCode.USDT);
        var ex = Assert.Throws<DomainException>(() => usd + usdt);
        Assert.Equal("currency_mismatch", ex.Code);
        Assert.Throws<DomainException>(() => Money.Of(1, CurrencyCode.IRT) + Money.Of(1, CurrencyCode.IRR));
    }

    [Fact]
    public void Same_currency_arithmetic_works()
    {
        var a = Money.Of(1_000.25m, CurrencyCode.USD) + Money.Of(0.75m, CurrencyCode.USD);
        Assert.Equal(1_001m, a.Amount);
        Assert.Equal(CurrencyCode.USD, a.Currency);
    }

    [Fact]
    public void Rial_toman_factor_is_ten()
    {
        Assert.Equal(Money.Of(1_000_000_000m, CurrencyCode.IRR), CurrencyMath.TomanToRial(Money.Of(100_000_000m, CurrencyCode.IRT)));
        Assert.Equal(Money.Of(100_000_000m, CurrencyCode.IRT), CurrencyMath.RialToToman(Money.Of(1_000_000_000m, CurrencyCode.IRR)));
        Assert.True(CurrencyMath.AreFixedEquivalent(CurrencyCode.IRR, CurrencyCode.IRT));
        Assert.False(CurrencyMath.AreFixedEquivalent(CurrencyCode.USD, CurrencyCode.USDT));
        Assert.Throws<DomainException>(() => CurrencyMath.ConvertFixed(Money.Of(1, CurrencyCode.USD), CurrencyCode.USDT));
    }

    [Fact]
    public void Rate_converts_in_both_directions_with_explicit_direction()
    {
        var rate = new ExchangeRate(CurrencyCode.USD, CurrencyCode.IRT, 100_000m);
        Assert.Equal(Money.Of(1_000m, CurrencyCode.USD), rate.Convert(Money.Of(100_000_000m, CurrencyCode.IRT), CurrencyCode.USD));
        Assert.Equal(Money.Of(100_000_000m, CurrencyCode.IRT), rate.Convert(Money.Of(1_000m, CurrencyCode.USD), CurrencyCode.IRT));
        Assert.Equal("1 USD = 100,000 IRT", rate.Describe());
    }

    [Fact]
    public void Rate_expressed_in_toman_handles_rial_amounts_via_factor()
    {
        var rate = new ExchangeRate(CurrencyCode.USD, CurrencyCode.IRT, 100_000m);
        Assert.Equal(Money.Of(1_000m, CurrencyCode.USD), rate.Convert(Money.Of(1_000_000_000m, CurrencyCode.IRR), CurrencyCode.USD));
        Assert.Equal(Money.Of(1_000_000_000m, CurrencyCode.IRR), rate.Convert(Money.Of(1_000m, CurrencyCode.USD), CurrencyCode.IRR));
        Assert.Equal(0.000001m, rate.FactorFor(CurrencyCode.IRR, CurrencyCode.USD));
    }

    [Fact]
    public void Rate_cannot_convert_unrelated_currencies()
    {
        var rate = new ExchangeRate(CurrencyCode.USD, CurrencyCode.CNY, 7.2m);
        Assert.Throws<DomainException>(() => rate.Convert(Money.Of(10, CurrencyCode.USDT), CurrencyCode.CNY));
        Assert.Throws<DomainException>(() => new ExchangeRate(CurrencyCode.USD, CurrencyCode.USD, 1));
        Assert.Throws<DomainException>(() => new ExchangeRate(CurrencyCode.USD, CurrencyCode.CNY, 0));
    }

    [Fact]
    public void Rounding_follows_currency_precision()
    {
        Assert.Equal(100m, Money.Of(99.5m, CurrencyCode.IRT).Amount);
        Assert.Equal(1.01m, Money.Of(1.005m, CurrencyCode.USD).Amount);
        Assert.Equal(1.123457m, Money.Of(1.1234567m, CurrencyCode.USDT).Amount);
        Assert.Equal("100,000,000", AmountFormatter.Format(100_000_000m, CurrencyCode.IRT));
        Assert.Equal("1,000.00", AmountFormatter.Format(1_000m, CurrencyCode.USD));
        Assert.Equal("12.50", AmountFormatter.Format(12.5m, CurrencyCode.USDT));
        Assert.Equal("12.123456", AmountFormatter.Format(12.123456m, CurrencyCode.USDT));
    }

    [Fact]
    public void Usd_to_cny_conversion_for_china_payment()
    {
        var rate = new ExchangeRate(CurrencyCode.USD, CurrencyCode.CNY, 7.18m);
        Assert.Equal(Money.Of(7_180m, CurrencyCode.CNY), rate.Convert(Money.Of(1_000m, CurrencyCode.USD), CurrencyCode.CNY));
        var usdt = new ExchangeRate(CurrencyCode.USD, CurrencyCode.USDT, 1.002m);
        Assert.Equal(Money.Of(1_002m, CurrencyCode.USDT), usdt.Convert(Money.Of(1_000m, CurrencyCode.USD), CurrencyCode.USDT));
    }
}
