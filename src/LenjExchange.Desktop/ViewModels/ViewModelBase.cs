using CommunityToolkit.Mvvm.ComponentModel;
using LenjExchange.Domain.Common;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Shamsi;
using LenjExchange.Domain.Text;
using Serilog;

namespace LenjExchange.Desktop.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;

    public abstract string Title { get; }

    public virtual Task LoadAsync() => Task.CompletedTask;

    /// <summary>Runs an operation, showing domain errors (Persian messages) inline instead of crashing.</summary>
    protected async Task<bool> RunAsync(Func<Task> work, string? success = null)
    {
        ErrorMessage = null;
        StatusMessage = null;
        IsBusy = true;
        try
        {
            await work();
            if (success is not null) StatusMessage = success;
            return true;
        }
        catch (DomainException ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Operation failed in {Page}", GetType().Name);
            ErrorMessage = "خطا: " + ex.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected static decimal ParseAmount(string? text, string field)
    {
        if (!DigitNormalizer.TryParseDecimal(text, out var v))
            throw new DomainException("input_number", $"مقدار «{field}» عدد معتبر نیست.");
        return v;
    }

    protected static decimal? ParseOptionalAmount(string? text, string field) =>
        string.IsNullOrWhiteSpace(text) ? null : ParseAmount(text, field);

    protected static DateTime ParseShamsi(string? text, string field) =>
        ShamsiDate.TryParse(text, out var d) ? d : throw new DomainException("input_date", $"تاریخ «{field}» معتبر نیست (نمونه: 1405/07/20).");

    protected static string Amount(decimal v, CurrencyCode c) => AmountFormatter.Format(v, c);

    public static IReadOnlyList<CurrencyCode> AllCurrencies { get; } = Enum.GetValues<CurrencyCode>();
}
