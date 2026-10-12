using LenjExchange.Application.Abstractions;
using LenjExchange.Domain.Documents;
using LenjExchange.Domain.Ledger;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Numbering;
using LenjExchange.Domain.Parties;
using LenjExchange.Domain.Rates;
using LenjExchange.Domain.Security;
using LenjExchange.Domain.Transfers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LenjExchange.Infrastructure.Persistence;

public class LenjDbContext : DbContext, IAppDbContext
{
    public LenjDbContext(DbContextOptions<LenjDbContext> options) : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Exchanger> Exchangers => Set<Exchanger>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<FinalRecipient> FinalRecipients => Set<FinalRecipient>();
    public DbSet<Bank> Banks => Set<Bank>();
    public DbSet<TransferCase> TransferCases => Set<TransferCase>();
    public DbSet<CustomerReceipt> CustomerReceipts => Set<CustomerReceipt>();
    public DbSet<BankDeposit> BankDeposits => Set<BankDeposit>();
    public DbSet<ExchangerCommitment> ExchangerCommitments => Set<ExchangerCommitment>();
    public DbSet<AgentDelivery> AgentDeliveries => Set<AgentDelivery>();
    public DbSet<ChinaReceipt> ChinaReceipts => Set<ChinaReceipt>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();
    public DbSet<DailyRate> DailyRates => Set<DailyRate>();
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<ReceiptPrintRecord> ReceiptPrints => Set<ReceiptPrintRecord>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();

    Task<int> IAppDbContext.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        b.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
        b.Properties<decimal>().HavePrecision(28, 8);
        b.Properties<CurrencyCode>().HaveConversion<string>().HaveMaxLength(8);
        b.Properties<string>().HaveMaxLength(1000);
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Customer>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.FullName);
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.FullName).HasMaxLength(200);
        });
        m.Entity<Exchanger>().HasIndex(x => x.Name).IsUnique();
        m.Entity<Agent>().HasIndex(x => x.Name).IsUnique();
        m.Entity<FinalRecipient>().HasIndex(x => x.Name);
        m.Entity<Bank>().HasIndex(x => x.Name).IsUnique();

        m.Entity<TransferCase>(e =>
        {
            e.HasIndex(x => x.CaseNumber).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CustomerId);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
        });

        m.Entity<CustomerReceipt>(e =>
        {
            e.HasIndex(x => x.ReceiptNumber).IsUnique();
            e.HasIndex(x => x.CustomerId);
            e.HasIndex(x => x.DateUtc);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            OwnRate(e.OwnsOne(x => x.Rate));
            e.HasMany(x => x.Deposits).WithOne().HasForeignKey(d => d.CustomerReceiptId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Deposits).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasOne<TransferCase>().WithMany().HasForeignKey(x => x.TransferCaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });
        m.Entity<BankDeposit>(e =>
        {
            e.HasIndex(x => x.BankTrackingNo);
            e.HasIndex(x => new { x.BankName, x.DateUtc });
        });

        m.Entity<ExchangerCommitment>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            OwnRate(e.OwnsOne(x => x.Rate));
            e.HasIndex(x => x.TransferCaseId);
            e.HasOne<Exchanger>().WithMany().HasForeignKey(x => x.ExchangerId).OnDelete(DeleteBehavior.Restrict);
        });
        m.Entity<AgentDelivery>(e =>
        {
            e.HasIndex(x => x.TrackingNo).IsUnique();
            e.HasIndex(x => x.TransferCaseId);
            e.Property(x => x.Approval).HasConversion<string>().HasMaxLength(20);
            e.HasOne<ExchangerCommitment>().WithMany().HasForeignKey(x => x.ExchangerCommitmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Agent>().WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Restrict);
        });
        m.Entity<ChinaReceipt>(e =>
        {
            e.HasIndex(x => x.TransferCaseId);
            e.Property(x => x.Approval).HasConversion<string>().HasMaxLength(20);
            OwnRate(e.OwnsOne(x => x.Rate));
            e.HasOne<Agent>().WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FinalRecipient>().WithMany().HasForeignKey(x => x.FinalRecipientId).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<Account>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => new { x.Category, x.LinkedEntityId });
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Nature).HasConversion<string>().HasMaxLength(10);
        });
        m.Entity<JournalEntry>(e =>
        {
            e.HasIndex(x => x.EntryNumber).IsUnique();
            e.HasIndex(x => x.DateUtc);
            e.HasIndex(x => x.TransferCaseId);
            e.HasIndex(x => new { x.SourceDocumentType, x.SourceDocumentId });
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.OperationType).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.BaseCurrency).HasConversion<string>().HasMaxLength(8);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.JournalEntryId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        m.Entity<JournalLine>(e =>
        {
            e.HasIndex(x => x.AccountId);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(x => x.Signed);
        });

        m.Entity<DailyRate>().HasIndex(x => new { x.BaseCurrency, x.QuoteCurrency, x.EffectiveDateUtc });
        m.Entity<NumberSequence>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(100);
        });
        m.Entity<User>(e =>
        {
            e.HasIndex(x => x.UserName).IsUnique();
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
        });
        m.Entity<AuditLogEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.HasIndex(x => x.TimestampUtc);
            e.Property(x => x.Details).HasMaxLength(4000);
        });
        m.Entity<Attachment>(e =>
        {
            e.HasIndex(x => x.Sha256);
            e.HasIndex(x => new { x.OwnerType, x.OwnerId });
            e.Property(x => x.DocType).HasConversion<string>().HasMaxLength(30);
        });
        m.Entity<ReceiptPrintRecord>(e =>
        {
            e.HasIndex(x => x.CustomerReceiptId);
            e.Property(x => x.PaperSize).HasConversion<string>().HasMaxLength(5);
        });
        m.Entity<AppSetting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Value).HasMaxLength(20000);
        });

        // Ids are assigned in domain constructors; never let EF treat a set Guid as "already persisted".
        foreach (var entity in m.Model.GetEntityTypes().Where(t => !t.IsOwned()))
        {
            var id = entity.FindProperty("Id");
            if (id?.ClrType == typeof(Guid)) id.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
        }
    }

    private static void OwnRate<T>(OwnedNavigationBuilder<T, ExchangeRate> r) where T : class
    {
        r.Property(p => p.BaseCurrency).HasColumnName("RateBase");
        r.Property(p => p.QuoteCurrency).HasColumnName("RateQuote");
        r.Property(p => p.Rate).HasColumnName("RateValue");
    }
}

public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter() : base(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc)) { }
}

public sealed class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
{
    public NullableUtcDateTimeConverter() : base(
        v => v == null ? null : v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime(),
        v => v == null ? null : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) { }
}
