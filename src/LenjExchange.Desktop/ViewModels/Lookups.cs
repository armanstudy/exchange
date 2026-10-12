using LenjExchange.Application.Abstractions;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Shamsi;

namespace LenjExchange.Desktop.ViewModels;

public sealed record Lookup(Guid Id, string Name)
{
    public override string ToString() => Name;
}

public sealed record Option<T>(T Value, string Text)
{
    public override string ToString() => Text;
}

public static class Display
{
    /// <summary>Set once at startup so all screens convert UTC with the same (configured) time zone as the services.</summary>
    public static IClock Clock { get; set; } = new SystemClock();

    public static string Date(DateTime utc) => ShamsiDate.Format(Clock.ToLocal(utc));
    public static string DateTime(DateTime utc) => ShamsiDate.FormatWithTime(Clock.ToLocal(utc));
    public static string Money(decimal amount, CurrencyCode c) => AmountFormatter.Format(amount, c) + " " + c;
    public static string Signed(decimal balance, CurrencyCode c) =>
        balance == 0 ? "0" : $"{AmountFormatter.Format(Math.Abs(balance), c)} {(balance > 0 ? "بد" : "بس")}";
}
