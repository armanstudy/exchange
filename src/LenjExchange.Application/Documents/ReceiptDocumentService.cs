using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Common;
using LenjExchange.Application.Reporting;
using LenjExchange.Application.Settings;
using LenjExchange.Domain.Common;
using LenjExchange.Domain.Documents;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Security;
using LenjExchange.Domain.Shamsi;
using LenjExchange.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace LenjExchange.Application.Documents;

/// <summary>
/// Everything printed on a customer receipt. Deliberately limited to the five customer-facing fields plus header:
/// exchanger, agent, internal rates/accounts and notes are never part of this model.
/// </summary>
public sealed record ReceiptPrintModel(
    CompanyHeader Header,
    string ReceiptNumber,
    string CustomerFullName,
    string ShamsiDate,
    string AmountText,
    string? ConversionRateText,
    string? FinalRecipientName);

public interface IReceiptPdfRenderer
{
    byte[] Render(ReceiptPrintModel model, PaperSize size);
}

public interface IReportPdfRenderer
{
    byte[] RenderStatement(StatementReport report, CompanyHeader header);
    byte[] RenderAllCustomers(AllCustomersReport report, CompanyHeader header);
}

public interface IExcelExporter
{
    byte[] ExportStatement(StatementReport report);
    byte[] ExportAllCustomers(AllCustomersReport report);
    byte[] ExportOpenCases(IReadOnlyList<OpenCaseRow> rows);
}

public sealed class ReceiptDocumentService
{
    private readonly IAppDbContext _db;
    private readonly IAppPaths _paths;
    private readonly IClock _clock;
    private readonly ICurrentUser _user;
    private readonly SettingsService _settings;
    private readonly IReceiptPdfRenderer _renderer;
    private readonly AuditService _audit;

    public ReceiptDocumentService(IAppDbContext db, IAppPaths paths, IClock clock, ICurrentUser user, SettingsService settings,
        IReceiptPdfRenderer renderer, AuditService audit)
    {
        _db = db;
        _paths = paths;
        _clock = clock;
        _user = user;
        _settings = settings;
        _renderer = renderer;
        _audit = audit;
    }

    public async Task<ReceiptPrintModel> BuildModelAsync(Guid receiptId, CancellationToken ct = default)
    {
        var r = await _db.CustomerReceipts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == receiptId, ct) ?? throw new NotFoundException("رسید");
        if (r.Status != DocumentStatus.Posted || r.ReceiptNumber is null)
            throw new DomainException("receipt_not_final", "فقط رسید قطعی‌شده قابل چاپ است.");
        var customer = await _db.Customers.AsNoTracking().FirstAsync(c => c.Id == r.CustomerId, ct);
        var recipient = r.FinalRecipientId is { } rid
            ? await _db.FinalRecipients.AsNoTracking().Where(x => x.Id == rid).Select(x => x.Name).FirstOrDefaultAsync(ct)
            : null;
        var header = await _settings.GetCompanyAsync(ct);
        string? rateText = r.Rate is { } rate
            ? $"{rate.Describe()} — معادل {r.TargetTotal.Format()}"
            : null;
        return new ReceiptPrintModel(header, r.ReceiptNumber, customer.FullName,
            ShamsiDate.Format(_clock.ToLocal(r.DateUtc)), $"{r.Total.Format(false)} {Currencies.Info(r.Currency).PersianName} ({r.Currency})",
            rateText, recipient);
    }

    /// <summary>Renders the receipt PDF to &lt;root&gt;/&lt;Shamsi yyyy&gt;/&lt;MM&gt;/&lt;payer&gt; - &lt;receipt no&gt;.pdf without overwriting.</summary>
    public async Task<string> GeneratePdfAsync(Guid receiptId, PaperSize? size = null, bool sentToPrinter = false, CancellationToken ct = default)
    {
        _user.Demand(Permission.PrintReceipts);
        var model = await BuildModelAsync(receiptId, ct);
        var paper = size ?? (await _settings.GetAccountingAsync(ct)).DefaultReceiptPaper;
        var bytes = _renderer.Render(model, paper);

        var paths = await _settings.GetPathsAsync(ct);
        var root = string.IsNullOrWhiteSpace(paths.ReceiptsRoot) ? _paths.DefaultReceiptsRoot : paths.ReceiptsRoot!;
        var receipt = await _db.CustomerReceipts.AsNoTracking().FirstAsync(x => x.Id == receiptId, ct);
        var (y, m, _) = ShamsiDate.Parts(_clock.ToLocal(receipt.DateUtc));
        var dir = Path.Combine(root, y.ToString("0000"), m.ToString("00"));
        Directory.CreateDirectory(dir);
        var path = UniquePath(dir, $"{SanitizeFileName(model.CustomerFullName)} - {SanitizeFileName(model.ReceiptNumber)}", ".pdf");
        await File.WriteAllBytesAsync(path, bytes, ct);

        _db.ReceiptPrints.Add(new ReceiptPrintRecord(receiptId, model.ReceiptNumber, _clock.UtcNow, _user.UserName, paper, path, sentToPrinter));
        _audit.Record("receipt.pdf", nameof(CustomerReceipt), receiptId, path);
        await _db.SaveChangesAsync(ct);
        return path;
    }

    public Task<List<ReceiptPrintRecord>> PrintHistoryAsync(Guid? receiptId = null, CancellationToken ct = default)
    {
        var q = _db.ReceiptPrints.AsNoTracking();
        if (receiptId is { } id) q = q.Where(p => p.CustomerReceiptId == id);
        return q.OrderByDescending(p => p.PrintedAtUtc).Take(1000).ToListAsync(ct);
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }).ToHashSet();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) || char.IsControl(ch) ? '_' : ch).ToArray()).Trim().TrimEnd('.');
        return cleaned.Length == 0 ? "receipt" : cleaned;
    }

    /// <summary>Never overwrites: appends " (2)", " (3)" … when the file already exists.</summary>
    public static string UniquePath(string dir, string baseName, string ext)
    {
        var path = Path.Combine(dir, baseName + ext);
        for (var i = 2; File.Exists(path); i++)
            path = Path.Combine(dir, $"{baseName} ({i}){ext}");
        return path;
    }
}
