using System.Text.Json;
using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Common;
using LenjExchange.Domain.Documents;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Numbering;
using LenjExchange.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace LenjExchange.Application.Settings;

/// <summary>Header printed on receipts and reports. All values are editable in Settings.</summary>
public sealed record CompanyHeader
{
    public string PersianName { get; init; } = "شرکت بازرگانی لنج – برادران بارگاهی – واردات و صادرات";
    public string EnglishName { get; init; } = "YIWU LENGJI IMPORT AND EXPORT CO., LTD";
    /// <summary>Placeholder until the real contact details are provided (open question).</summary>
    public string ContactLine { get; init; } = "تلفن: ——— | واتس‌اپ/وی‌چت: ——— | نشانی: ———";
    public string? LogoPath { get; init; }
    public string PrimaryColor { get; init; } = "#0B2F5B";
    public string LightColor { get; init; } = "#EEF2F7";
    public string BackgroundColor { get; init; } = "#F5F8FB";
    public string MutedTextColor { get; init; } = "#526174";
}

public sealed record NumberingSettings
{
    public string ReceiptPattern { get; init; } = ReceiptNumberPattern.DefaultPattern;
    public SequenceScope ReceiptScope { get; init; } = SequenceScope.Monthly;
    public string CasePattern { get; init; } = "TC{yyyy}{seq:5}";
    public SequenceScope CaseScope { get; init; } = SequenceScope.Yearly;
    public string JournalPattern { get; init; } = "JV{yyyy}{seq:6}";
    public string DeliveryPattern { get; init; } = "AD{yyyy}{MM}{seq:4}";
    public string CustomerCodePattern { get; init; } = "C{seq:5}";

    public ReceiptNumberPattern Receipt => new(ReceiptPattern, ReceiptScope);
    public ReceiptNumberPattern Case => new(CasePattern, CaseScope);
    public ReceiptNumberPattern Journal => new(JournalPattern, SequenceScope.Yearly);
    public ReceiptNumberPattern Delivery => new(DeliveryPattern, SequenceScope.Monthly);
    public ReceiptNumberPattern CustomerCode => new(CustomerCodePattern, SequenceScope.Global);
}

public sealed record PathSettings
{
    /// <summary>Root for generated receipt PDFs; null → Documents\LenjExchange\Receipts.</summary>
    public string? ReceiptsRoot { get; init; }
    public string? ReportsRoot { get; init; }
    public string? BackupRoot { get; init; }
}

public sealed record BackupSettings
{
    public bool AutoBackupEnabled { get; init; } = true;
    public int AutoBackupIntervalHours { get; init; } = 24;
    public int KeepVersions { get; init; } = 10;
    public bool BackupOnExit { get; init; } = true;
}

public sealed record AccountingSettings
{
    /// <summary>Reporting/base currency for line equivalents. USD by default — open question for the owner.</summary>
    public CurrencyCode BaseCurrency { get; init; } = CurrencyCode.USD;
    public PaperSize DefaultReceiptPaper { get; init; } = PaperSize.A5;
    /// <summary>Allows finalizing a receipt whose deposits do not reconcile (difference goes to discrepancy account).</summary>
    public bool AllowDepositMismatch { get; init; } = false;
}

public sealed class SettingsService
{
    public const string CompanyKey = "company.header";
    public const string NumberingKey = "numbering";
    public const string PathsKey = "paths";
    public const string BackupKey = "backup";
    public const string AccountingKey = "accounting";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _user;
    private readonly AuditService _audit;

    public SettingsService(IAppDbContext db, IClock clock, ICurrentUser user, AuditService audit)
    {
        _db = db;
        _clock = clock;
        _user = user;
        _audit = audit;
    }

    public Task<CompanyHeader> GetCompanyAsync(CancellationToken ct = default) => GetAsync<CompanyHeader>(CompanyKey, ct);
    public Task<NumberingSettings> GetNumberingAsync(CancellationToken ct = default) => GetAsync<NumberingSettings>(NumberingKey, ct);
    public Task<PathSettings> GetPathsAsync(CancellationToken ct = default) => GetAsync<PathSettings>(PathsKey, ct);
    public Task<BackupSettings> GetBackupAsync(CancellationToken ct = default) => GetAsync<BackupSettings>(BackupKey, ct);
    public Task<AccountingSettings> GetAccountingAsync(CancellationToken ct = default) => GetAsync<AccountingSettings>(AccountingKey, ct);

    public Task SaveCompanyAsync(CompanyHeader v, CancellationToken ct = default) => SaveAsync(CompanyKey, v, ct);
    public Task SavePathsAsync(PathSettings v, CancellationToken ct = default) => SaveAsync(PathsKey, v, ct);
    public Task SaveBackupAsync(BackupSettings v, CancellationToken ct = default) => SaveAsync(BackupKey, v, ct);
    public Task SaveAccountingAsync(AccountingSettings v, CancellationToken ct = default) => SaveAsync(AccountingKey, v, ct);

    public Task SaveNumberingAsync(NumberingSettings v, CancellationToken ct = default)
    {
        _ = v.Receipt; _ = v.Case; _ = v.Journal; _ = v.Delivery; _ = v.CustomerCode;
        return SaveAsync(NumberingKey, v, ct);
    }

    private async Task<T> GetAsync<T>(string key, CancellationToken ct) where T : new()
    {
        var row = await _db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null || string.IsNullOrWhiteSpace(row.Value)) return new T();
        return JsonSerializer.Deserialize<T>(row.Value, Json) ?? new T();
    }

    private async Task SaveAsync<T>(string key, T value, CancellationToken ct)
    {
        _user.Demand(Permission.ManageSettings);
        var json = JsonSerializer.Serialize(value, Json);
        var row = await _db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null) _db.Settings.Add(new Domain.Documents.AppSetting(key, json, _clock.UtcNow));
        else row.Set(json, _clock.UtcNow);
        _audit.Record("settings.update", "AppSetting", key);
        await _db.SaveChangesAsync(ct);
    }
}
