namespace LenjExchange.Application.Backup;

public sealed record BackupManifestFile(string Path, long Size, string Sha256);

public sealed record BackupManifest(
    string Application,
    string AppVersion,
    DateTime CreatedAtUtc,
    string CreatedBy,
    string DatabaseFile,
    IReadOnlyList<BackupManifestFile> Files);

public sealed record BackupResult(string ZipPath, BackupManifest Manifest, long SizeBytes);

public sealed record BackupVerification(bool IsValid, IReadOnlyList<string> Problems, BackupManifest? Manifest);

public interface IBackupService
{
    /// <summary>Consistent DB snapshot via the SQLite online-backup API + attachments + manifest (SHA-256).</summary>
    Task<BackupResult> CreateBackupAsync(string? targetDirectory = null, string reason = "manual", CancellationToken ct = default);

    /// <summary>Checks manifest hashes and runs PRAGMA integrity_check on the contained database.</summary>
    Task<BackupVerification> VerifyAsync(string zipPath, CancellationToken ct = default);

    /// <summary>Admin only. Verifies first, takes a safety backup of the current data, then replaces DB and attachments.</summary>
    Task RestoreAsync(string zipPath, CancellationToken ct = default);

    /// <summary>Runs PRAGMA integrity_check on the live database.</summary>
    Task<IReadOnlyList<string>> CheckLiveDatabaseAsync(CancellationToken ct = default);

    Task<IReadOnlyList<FileInfo>> ListBackupsAsync(string? directory = null, CancellationToken ct = default);
}
