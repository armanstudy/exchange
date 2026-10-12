using LenjExchange.Domain.Documents;
using LenjExchange.Domain.Ledger;
using LenjExchange.Domain.Numbering;
using LenjExchange.Domain.Parties;
using LenjExchange.Domain.Rates;
using LenjExchange.Domain.Security;
using LenjExchange.Domain.Transfers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace LenjExchange.Application.Abstractions;

/// <summary>Provider-neutral persistence abstraction implemented by the Infrastructure DbContext.</summary>
public interface IAppDbContext
{
    DbSet<Customer> Customers { get; }
    DbSet<Exchanger> Exchangers { get; }
    DbSet<Agent> Agents { get; }
    DbSet<FinalRecipient> FinalRecipients { get; }
    DbSet<Bank> Banks { get; }
    DbSet<TransferCase> TransferCases { get; }
    DbSet<CustomerReceipt> CustomerReceipts { get; }
    DbSet<BankDeposit> BankDeposits { get; }
    DbSet<ExchangerCommitment> ExchangerCommitments { get; }
    DbSet<AgentDelivery> AgentDeliveries { get; }
    DbSet<ChinaReceipt> ChinaReceipts { get; }
    DbSet<Account> Accounts { get; }
    DbSet<JournalEntry> JournalEntries { get; }
    DbSet<JournalLine> JournalLines { get; }
    DbSet<DailyRate> DailyRates { get; }
    DbSet<NumberSequence> NumberSequences { get; }
    DbSet<User> Users { get; }
    DbSet<AuditLogEntry> AuditLog { get; }
    DbSet<Attachment> Attachments { get; }
    DbSet<ReceiptPrintRecord> ReceiptPrints { get; }
    DbSet<AppSetting> Settings { get; }

    DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IClock
{
    DateTime UtcNow { get; }
    /// <summary>Business time zone used for Shamsi day boundaries (default: the machine's local zone).</summary>
    TimeZoneInfo TimeZone { get; }
}

public static class ClockExtensions
{
    public static DateTime ToLocal(this IClock clock, DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), clock.TimeZone);

    public static DateTime ToUtc(this IClock clock, DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), clock.TimeZone);

    public static DateTime LocalToday(this IClock clock) => clock.ToLocal(clock.UtcNow).Date;
}

public sealed class SystemClock : IClock
{
    public SystemClock(TimeZoneInfo? zone = null) => TimeZone = zone ?? TimeZoneInfo.Local;
    public DateTime UtcNow => DateTime.UtcNow;
    public TimeZoneInfo TimeZone { get; }
}

public interface ICurrentUser
{
    string UserName { get; }
    bool IsAuthenticated { get; }
    bool Has(Permission permission);
}

/// <summary>Mutable session holder used by the desktop app after login.</summary>
public sealed class CurrentUserSession : ICurrentUser
{
    public User? User { get; private set; }
    public string UserName => User?.UserName ?? "system";
    public bool IsAuthenticated => User is not null;
    public bool Has(Permission permission) => User?.Has(permission) ?? false;
    public void SignIn(User user) => User = user;
    public void SignOut() => User = null;
}

/// <summary>File-system locations. All writable data lives under %LOCALAPPDATA%\LenjExchange, never Program Files.</summary>
public interface IAppPaths
{
    string DataRoot { get; }
    string DatabasePath { get; }
    string AttachmentsRoot { get; }
    string DefaultBackupRoot { get; }
    string LogsRoot { get; }
    string DefaultReceiptsRoot { get; }
    string DefaultReportsRoot { get; }
}

public sealed class AppPaths : IAppPaths
{
    public const string AppFolderName = "LenjExchange";

    public AppPaths(string? dataRoot = null, string? documentsRoot = null)
    {
        DataRoot = dataRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);
        var docs = documentsRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppFolderName);
        DatabasePath = Path.Combine(DataRoot, "Data", "lenj.db");
        AttachmentsRoot = Path.Combine(DataRoot, "Attachments");
        DefaultBackupRoot = Path.Combine(DataRoot, "Backups");
        LogsRoot = Path.Combine(DataRoot, "Logs");
        DefaultReceiptsRoot = Path.Combine(docs, "Receipts");
        DefaultReportsRoot = Path.Combine(docs, "Reports");
    }

    public string DataRoot { get; }
    public string DatabasePath { get; }
    public string AttachmentsRoot { get; }
    public string DefaultBackupRoot { get; }
    public string LogsRoot { get; }
    public string DefaultReceiptsRoot { get; }
    public string DefaultReportsRoot { get; }

    public void EnsureCreated()
    {
        foreach (var dir in new[] { DataRoot, Path.GetDirectoryName(DatabasePath)!, AttachmentsRoot, DefaultBackupRoot, LogsRoot })
            Directory.CreateDirectory(dir);
    }
}
