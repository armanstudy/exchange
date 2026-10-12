using LenjExchange.Domain.Common;

namespace LenjExchange.Domain.Documents;

public enum AttachmentDocType
{
    BankSlip = 1,
    ReceiptScan = 2,
    ExchangerConfirmation = 3,
    AgentDeliveryProof = 4,
    ChinaReceiptProof = 5,
    IdentityDocument = 6,
    Other = 99
}

public class Attachment : AuditableEntity
{
    public static readonly IReadOnlyList<string> AllowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".pdf" };

    public string OriginalFileName { get; private set; } = "";
    /// <summary>Path relative to the attachments root (never absolute, so backups are portable).</summary>
    public string StoredRelativePath { get; private set; } = "";
    public string ContentType { get; private set; } = "";
    public long SizeBytes { get; private set; }
    public string Sha256 { get; private set; } = "";
    public AttachmentDocType DocType { get; private set; }
    public Guid? CustomerId { get; private set; }
    public Guid? TransferCaseId { get; private set; }
    /// <summary>Owning document (receipt, deposit, delivery, china receipt…).</summary>
    public string? OwnerType { get; private set; }
    public Guid? OwnerId { get; private set; }
    public string? Notes { get; private set; }

    protected Attachment() { }

    public Attachment(string originalFileName, string storedRelativePath, string contentType, long sizeBytes, string sha256,
        AttachmentDocType docType, Guid? customerId, Guid? caseId, string? ownerType, Guid? ownerId, string? notes)
    {
        OriginalFileName = Guard.NotBlank(originalFileName, "نام فایل");
        StoredRelativePath = Guard.NotBlank(storedRelativePath, "مسیر فایل");
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Sha256 = Guard.NotBlank(sha256, "هش فایل");
        DocType = docType;
        CustomerId = customerId;
        TransferCaseId = caseId;
        OwnerType = ownerType;
        OwnerId = ownerId;
        Notes = notes;
    }

    public static bool IsAllowedExtension(string fileName) =>
        AllowedExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());
}

public enum PaperSize
{
    A4 = 1,
    A5 = 2
}

/// <summary>History of every receipt PDF produced/printed (سابقه چاپ).</summary>
public class ReceiptPrintRecord : Entity
{
    public Guid CustomerReceiptId { get; private set; }
    public string ReceiptNumber { get; private set; } = "";
    public DateTime PrintedAtUtc { get; private set; }
    public string PrintedBy { get; private set; } = "";
    public PaperSize PaperSize { get; private set; }
    public string FilePath { get; private set; } = "";
    public bool SentToPrinter { get; private set; }

    protected ReceiptPrintRecord() { }

    public ReceiptPrintRecord(Guid receiptId, string receiptNumber, DateTime utcNow, string user, PaperSize size, string filePath, bool sentToPrinter)
    {
        CustomerReceiptId = receiptId;
        ReceiptNumber = receiptNumber;
        PrintedAtUtc = Guard.Utc(utcNow);
        PrintedBy = user;
        PaperSize = size;
        FilePath = filePath;
        SentToPrinter = sentToPrinter;
    }
}

/// <summary>Key/value application setting persisted in the database.</summary>
public class AppSetting
{
    public string Key { get; private set; } = "";
    public string Value { get; private set; } = "";
    public DateTime UpdatedAtUtc { get; private set; }

    protected AppSetting() { }

    public AppSetting(string key, string value, DateTime utcNow)
    {
        Key = Guard.NotBlank(key, "کلید");
        Value = value;
        UpdatedAtUtc = Guard.Utc(utcNow);
    }

    public void Set(string value, DateTime utcNow)
    {
        Value = value;
        UpdatedAtUtc = Guard.Utc(utcNow);
    }
}
