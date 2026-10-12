namespace LenjExchange.Domain.Monetary;

public enum CurrencyCode
{
    IRT = 1,
    IRR = 2,
    USD = 3,
    CNY = 4,
    USDT = 5
}

public sealed record CurrencyInfo(
    CurrencyCode Code,
    string PersianName,
    string Symbol,
    int StorageDecimals,
    int MinDisplayDecimals,
    int MaxDisplayDecimals);

public static class Currencies
{
    /// <summary>1 Toman = 10 Rial.</summary>
    public const decimal RialPerToman = 10m;

    private static readonly Dictionary<CurrencyCode, CurrencyInfo> Map = new()
    {
        [CurrencyCode.IRT] = new(CurrencyCode.IRT, "تومان", "IRT", 0, 0, 0),
        [CurrencyCode.IRR] = new(CurrencyCode.IRR, "ریال", "IRR", 0, 0, 0),
        [CurrencyCode.USD] = new(CurrencyCode.USD, "دلار آمریکا", "USD", 2, 2, 2),
        [CurrencyCode.CNY] = new(CurrencyCode.CNY, "یوان چین", "CNY", 2, 2, 2),
        // USDT is a separate asset from USD; it is never netted against USD balances.
        [CurrencyCode.USDT] = new(CurrencyCode.USDT, "تتر", "USDT", 6, 2, 6),
    };

    public static IReadOnlyCollection<CurrencyInfo> All => Map.Values;

    public static CurrencyInfo Info(CurrencyCode code) => Map[code];

    public static int Decimals(CurrencyCode code) => Map[code].StorageDecimals;

    public static decimal Round(decimal amount, CurrencyCode code) =>
        Math.Round(amount, Decimals(code), MidpointRounding.AwayFromZero);

    public static bool TryParse(string? value, out CurrencyCode code)
    {
        code = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return Enum.TryParse(value.Trim(), true, out code) && Enum.IsDefined(code);
    }
}
