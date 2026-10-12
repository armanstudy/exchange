using System.Security.Cryptography;
using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Common;
using LenjExchange.Domain.Common;
using LenjExchange.Domain.Documents;
using LenjExchange.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace LenjExchange.Application.Documents;

public sealed record AttachmentInput(string SourcePath, AttachmentDocType DocType, Guid? CustomerId = null, Guid? TransferCaseId = null,
    string? OwnerType = null, Guid? OwnerId = null, string? Notes = null);

public sealed record AttachmentResult(Attachment Attachment, bool IsDuplicate, Attachment? ExistingDuplicate);

/// <summary>
/// Stores JPG/PNG/PDF files under %LOCALAPPDATA%\LenjExchange\Attachments\yyyy\MM, with SHA-256 duplicate detection.
/// Stored files are marked read-only; the database keeps only relative paths.
/// </summary>
public sealed class AttachmentService
{
    public const long MaxSizeBytes = 25 * 1024 * 1024;

    private readonly IAppDbContext _db;
    private readonly IAppPaths _paths;
    private readonly IClock _clock;
    private readonly ICurrentUser _user;
    private readonly AuditService _audit;

    public AttachmentService(IAppDbContext db, IAppPaths paths, IClock clock, ICurrentUser user, AuditService audit)
    {
        _db = db;
        _paths = paths;
        _clock = clock;
        _user = user;
        _audit = audit;
    }

    public static string ComputeSha256(Stream s) => Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();

    public static string ComputeSha256File(string path)
    {
        using var fs = File.OpenRead(path);
        return ComputeSha256(fs);
    }

    /// <summary>Validates by extension and magic bytes, so a renamed .exe is not accepted as .pdf.</summary>
    public static string DetectContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (!Attachment.IsAllowedExtension(path))
            throw new DomainException("attachment_type", "فقط فایل‌های JPG، PNG و PDF مجاز هستند.");
        var head = new byte[8];
        int read;
        using (var fs = File.OpenRead(path))
            read = fs.Read(head, 0, head.Length);
        bool Starts(params byte[] sig) => read >= sig.Length && head.AsSpan(0, sig.Length).SequenceEqual(sig);
        var type = ext switch
        {
            ".jpg" or ".jpeg" when Starts(0xFF, 0xD8, 0xFF) => "image/jpeg",
            ".png" when Starts(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A) => "image/png",
            ".pdf" when Starts(0x25, 0x50, 0x44, 0x46) => "application/pdf",
            _ => null
        };
        return type ?? throw new DomainException("attachment_content", "محتوای فایل با پسوند آن مطابقت ندارد.");
    }

    public async Task<AttachmentResult> AddAsync(AttachmentInput input, bool allowDuplicate = false, CancellationToken ct = default)
    {
        _user.Demand(Permission.ManageAttachments);
        var info = new FileInfo(input.SourcePath);
        if (!info.Exists) throw new NotFoundException("فایل");
        if (info.Length == 0) throw new DomainException("attachment_empty", "فایل خالی است.");
        if (info.Length > MaxSizeBytes) throw new DomainException("attachment_size", "حجم فایل بیش از حد مجاز (۲۵ مگابایت) است.");
        var contentType = DetectContentType(info.FullName);
        var hash = ComputeSha256File(info.FullName);

        var existing = await _db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Sha256 == hash, ct);
        if (existing is not null && !allowDuplicate)
            return new AttachmentResult(existing, true, existing);

        var now = _clock.ToLocal(_clock.UtcNow);
        var relDir = Path.Combine(now.ToString("yyyy"), now.ToString("MM"));
        var storedName = $"{Guid.NewGuid():N}{info.Extension.ToLowerInvariant()}";
        var rel = Path.Combine(relDir, storedName);
        var dest = Path.Combine(_paths.AttachmentsRoot, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(info.FullName, dest, overwrite: false);
        File.SetAttributes(dest, File.GetAttributes(dest) | FileAttributes.ReadOnly);

        var att = new Attachment(info.Name, rel.Replace('\\', '/'), contentType, info.Length, hash, input.DocType,
            input.CustomerId, input.TransferCaseId, input.OwnerType, input.OwnerId, input.Notes);
        att.StampCreated(_user.UserName, _clock.UtcNow);
        _db.Attachments.Add(att);
        _audit.Record("attachment.add", nameof(Attachment), att.Id, info.Name);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            File.SetAttributes(dest, FileAttributes.Normal);
            File.Delete(dest);
            throw;
        }
        return new AttachmentResult(att, existing is not null, existing);
    }

    public string ResolvePath(Attachment a) => Path.Combine(_paths.AttachmentsRoot, a.StoredRelativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Re-hashes stored files; returns attachments whose file is missing or altered.</summary>
    public async Task<IReadOnlyList<(Attachment Attachment, string Problem)>> VerifyAsync(CancellationToken ct = default)
    {
        var list = await _db.Attachments.AsNoTracking().ToListAsync(ct);
        var problems = new List<(Attachment, string)>();
        foreach (var a in list)
        {
            var p = ResolvePath(a);
            if (!File.Exists(p)) problems.Add((a, "فایل یافت نشد"));
            else if (ComputeSha256File(p) != a.Sha256) problems.Add((a, "هش فایل تغییر کرده است"));
        }
        return problems;
    }

    public async Task<IReadOnlyList<Attachment>> SearchAsync(Guid? customerId = null, Guid? caseId = null, AttachmentDocType? type = null,
        string? ownerType = null, Guid? ownerId = null, CancellationToken ct = default)
    {
        var q = _db.Attachments.AsNoTracking();
        if (customerId is { } c) q = q.Where(a => a.CustomerId == c);
        if (caseId is { } k) q = q.Where(a => a.TransferCaseId == k);
        if (type is { } t) q = q.Where(a => a.DocType == t);
        if (ownerType is not null) q = q.Where(a => a.OwnerType == ownerType);
        if (ownerId is { } o) q = q.Where(a => a.OwnerId == o);
        return await q.OrderByDescending(a => a.CreatedAtUtc).Take(2000).ToListAsync(ct);
    }
}
