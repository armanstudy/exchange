using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Backup;
using LenjExchange.Application.Common;
using LenjExchange.Application.Settings;
using LenjExchange.Domain.Common;
using LenjExchange.Domain.Security;
using LenjExchange.Domain.Shamsi;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LenjExchange.Infrastructure.Backup;

/// <summary>
/// Backup zip layout: <c>manifest.json</c>, <c>database/lenj.db</c> (SQLite online-backup snapshot) and
/// <c>attachments/…</c>. Every file is listed in the manifest with its SHA-256.
/// </summary>
public sealed class SqliteBackupService : IBackupService
{
    public const string ManifestName = "manifest.json";
    public const string DbEntry = "database/lenj.db";
    public const string AttachmentsPrefix = "attachments/";
    public const string FilePrefix = "LenjBackup_";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly DatabaseOptions _db;
    private readonly IAppPaths _paths;
    private readonly IClock _clock;
    private readonly ICurrentUser _user;
    private readonly SettingsService _settings;
    private readonly AuditService _audit;
    private readonly IAppDbContext _context;
    private readonly ILogger<SqliteBackupService> _log;

    public SqliteBackupService(DatabaseOptions db, IAppPaths paths, IClock clock, ICurrentUser user, SettingsService settings,
        AuditService audit, IAppDbContext context, ILogger<SqliteBackupService>? log = null)
    {
        _db = db;
        _context = context;
        _paths = paths;
        _clock = clock;
        _user = user;
        _settings = settings;
        _audit = audit;
        _log = log ?? NullLogger<SqliteBackupService>.Instance;
    }

    private string LiveDbPath => _db.SqliteFilePath ?? throw new DomainException("backup_db", "پشتیبان‌گیری فقط برای پایگاه دادهٔ فایل SQLite پشتیبانی می‌شود.");

    public async Task<BackupResult> CreateBackupAsync(string? targetDirectory = null, string reason = "manual", CancellationToken ct = default)
    {
        if (reason == "manual") _user.Demand(Permission.CreateBackup);
        var settings = await _settings.GetBackupAsync(ct);
        var paths = await _settings.GetPathsAsync(ct);
        var dir = targetDirectory ?? (string.IsNullOrWhiteSpace(paths.BackupRoot) ? _paths.DefaultBackupRoot : paths.BackupRoot!);
        Directory.CreateDirectory(dir);

        var work = Directory.CreateTempSubdirectory("lenj-backup-");
        try
        {
            var snapshot = Path.Combine(work.FullName, "lenj.db");
            SnapshotDatabase(LiveDbPath, snapshot);

            var local = _clock.ToLocal(_clock.UtcNow);
            var stamp = $"{ShamsiDate.Format(local).Replace("/", "")}_{local:HHmmss}";
            var zipPath = UniqueZip(dir, $"{FilePrefix}{stamp}_{Sanitize(reason)}");
            var files = new List<BackupManifestFile>();

            await using (var fs = new FileStream(zipPath, FileMode.CreateNew))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                files.Add(AddFile(zip, snapshot, DbEntry));
                if (Directory.Exists(_paths.AttachmentsRoot))
                    foreach (var f in Directory.EnumerateFiles(_paths.AttachmentsRoot, "*", SearchOption.AllDirectories))
                    {
                        ct.ThrowIfCancellationRequested();
                        var rel = Path.GetRelativePath(_paths.AttachmentsRoot, f).Replace('\\', '/');
                        files.Add(AddFile(zip, f, AttachmentsPrefix + rel));
                    }
                var manifest = new BackupManifest("LenjExchange", AppVersion(), _clock.UtcNow, _user.UserName, DbEntry, files);
                var entry = zip.CreateEntry(ManifestName, CompressionLevel.Optimal);
                await using var es = entry.Open();
                await JsonSerializer.SerializeAsync(es, manifest, Json, ct);
            }

            Prune(dir, settings.KeepVersions);
            _audit.Record("backup.create", null, null, zipPath);
            await _context.SaveChangesAsync(ct);
            var verified = await VerifyAsync(zipPath, ct);
            if (!verified.IsValid) throw new DomainException("backup_invalid", "پشتیبان ایجادشده معتبر نیست: " + string.Join("، ", verified.Problems));
            _log.LogInformation("Backup created at {Path}", zipPath);
            return new BackupResult(zipPath, verified.Manifest!, new FileInfo(zipPath).Length);
        }
        finally
        {
            TryDelete(work.FullName);
        }
    }

    public async Task<BackupVerification> VerifyAsync(string zipPath, CancellationToken ct = default)
    {
        var problems = new List<string>();
        if (!File.Exists(zipPath)) return new BackupVerification(false, new[] { "فایل پشتیبان یافت نشد." }, null);
        BackupManifest? manifest;
        var work = Directory.CreateTempSubdirectory("lenj-verify-");
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var me = zip.GetEntry(ManifestName);
            if (me is null) return new BackupVerification(false, new[] { "فایل manifest در پشتیبان وجود ندارد." }, null);
            await using (var ms = me.Open())
                manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(ms, Json, ct);
            if (manifest is null) return new BackupVerification(false, new[] { "manifest نامعتبر است." }, null);

            foreach (var f in manifest.Files)
            {
                var e = zip.GetEntry(f.Path);
                if (e is null) { problems.Add($"فایل {f.Path} در پشتیبان نیست."); continue; }
                await using var s = e.Open();
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(s, ct)).ToLowerInvariant();
                if (hash != f.Sha256) problems.Add($"هش فایل {f.Path} مطابقت ندارد.");
            }
            var extra = zip.Entries.Where(e => e.FullName != ManifestName && manifest.Files.All(f => f.Path != e.FullName)).ToList();
            if (extra.Count > 0) problems.Add($"{extra.Count} فایل اضافهٔ ثبت‌نشده در manifest وجود دارد.");

            var dbEntry = zip.GetEntry(manifest.DatabaseFile);
            if (dbEntry is null) problems.Add("پایگاه داده در پشتیبان نیست.");
            else
            {
                var tmp = Path.Combine(work.FullName, "verify.db");
                dbEntry.ExtractToFile(tmp);
                var result = IntegrityCheck(tmp);
                if (!(result.Count == 1 && result[0] == "ok")) problems.Add("PRAGMA integrity_check: " + string.Join("; ", result));
            }
        }
        catch (InvalidDataException ex)
        {
            return new BackupVerification(false, new[] { "فایل zip خراب است: " + ex.Message }, null);
        }
        finally
        {
            TryDelete(work.FullName);
        }
        return new BackupVerification(problems.Count == 0, problems, manifest);
    }

    public async Task RestoreAsync(string zipPath, CancellationToken ct = default)
    {
        _user.Demand(Permission.RestoreBackup);
        var v = await VerifyAsync(zipPath, ct);
        if (!v.IsValid) throw new DomainException("backup_invalid", "پشتیبان معتبر نیست: " + string.Join("، ", v.Problems));

        await CreateBackupAsync(null, "pre-restore", ct);

        var work = Directory.CreateTempSubdirectory("lenj-restore-");
        try
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                zip.GetEntry(v.Manifest!.DatabaseFile)!.ExtractToFile(Path.Combine(work.FullName, "lenj.db"));
                foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith(AttachmentsPrefix, StringComparison.Ordinal) && e.Name.Length > 0))
                {
                    var rel = e.FullName[AttachmentsPrefix.Length..];
                    var dest = Path.GetFullPath(Path.Combine(work.FullName, "attachments", rel));
                    if (!dest.StartsWith(Path.GetFullPath(Path.Combine(work.FullName, "attachments")), StringComparison.Ordinal))
                        throw new DomainException("backup_path", "مسیر نامعتبر در پشتیبان.");
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    e.ExtractToFile(dest);
                }
            }

            SqliteConnection.ClearAllPools();
            // Restore through the backup API into the live file so open handles see a consistent database.
            SnapshotDatabase(Path.Combine(work.FullName, "lenj.db"), LiveDbPath);

            var att = _paths.AttachmentsRoot;
            if (Directory.Exists(att))
            {
                var aside = att.TrimEnd(Path.DirectorySeparatorChar) + "_before_restore_" + _clock.UtcNow.ToString("yyyyMMddHHmmss");
                Directory.Move(att, aside);
            }
            var restoredAtt = Path.Combine(work.FullName, "attachments");
            Directory.CreateDirectory(att);
            if (Directory.Exists(restoredAtt))
                foreach (var f in Directory.EnumerateFiles(restoredAtt, "*", SearchOption.AllDirectories))
                {
                    var dest = Path.Combine(att, Path.GetRelativePath(restoredAtt, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(f, dest);
                    File.SetAttributes(dest, File.GetAttributes(dest) | FileAttributes.ReadOnly);
                }
            WriteRestoreAudit(LiveDbPath, zipPath);
            _log.LogWarning("Database restored from {Path}", zipPath);
        }
        finally
        {
            TryDelete(work.FullName);
        }
    }

    public Task<IReadOnlyList<string>> CheckLiveDatabaseAsync(CancellationToken ct = default) =>
        Task.FromResult(IntegrityCheck(LiveDbPath));

    public async Task<IReadOnlyList<FileInfo>> ListBackupsAsync(string? directory = null, CancellationToken ct = default)
    {
        var paths = await _settings.GetPathsAsync(ct);
        var dir = directory ?? (string.IsNullOrWhiteSpace(paths.BackupRoot) ? _paths.DefaultBackupRoot : paths.BackupRoot!);
        if (!Directory.Exists(dir)) return Array.Empty<FileInfo>();
        return new DirectoryInfo(dir).GetFiles(FilePrefix + "*.zip").OrderByDescending(f => f.CreationTimeUtc).ThenByDescending(f => f.Name).ToList();
    }

    public static void SnapshotDatabase(string sourcePath, string destinationPath)
    {
        using var src = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        using var dst = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destinationPath, Pooling = false }.ToString());
        src.Open();
        dst.Open();
        src.BackupDatabase(dst);
    }

    public static IReadOnlyList<string> IntegrityCheck(string dbPath)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    /// <summary>The restored file replaces the live DB, so the audit row is written into the restored database itself.</summary>
    private void WriteRestoreAudit(string dbPath, string zipPath)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO AuditLog (TimestampUtc, UserName, Operation, Details) VALUES ($t, $u, 'backup.restore', $d);";
        cmd.Parameters.AddWithValue("$t", _clock.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", System.Globalization.CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$u", _user.UserName);
        cmd.Parameters.AddWithValue("$d", zipPath);
        cmd.ExecuteNonQuery();
    }

    private static BackupManifestFile AddFile(ZipArchive zip, string path, string entryName)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using (var input = File.OpenRead(path))
        using (var output = entry.Open())
            input.CopyTo(output);
        return new BackupManifestFile(entryName, new FileInfo(path).Length, ComputeHash(path));
    }

    private static string ComputeHash(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    private void Prune(string dir, int keep)
    {
        if (keep <= 0) return;
        foreach (var old in new DirectoryInfo(dir).GetFiles(FilePrefix + "*.zip").OrderByDescending(f => f.Name).Skip(keep))
        {
            try { old.Delete(); }
            catch (IOException ex) { _log.LogWarning(ex, "Could not prune {File}", old.FullName); }
        }
    }

    private static string UniqueZip(string dir, string baseName)
    {
        var p = Path.Combine(dir, baseName + ".zip");
        for (var i = 2; File.Exists(p); i++) p = Path.Combine(dir, $"{baseName}_{i}.zip");
        return p;
    }

    private static string Sanitize(string s) => new(s.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' ? ch : '-').ToArray());

    private static string AppVersion() => typeof(SqliteBackupService).Assembly.GetName().Version?.ToString() ?? "0";

    private static void TryDelete(string dir)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
