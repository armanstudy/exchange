using System.IO.Compression;
using System.Text;
using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Backup;
using LenjExchange.Application.Customers;
using LenjExchange.Application.Documents;
using LenjExchange.Application.Reporting;
using LenjExchange.Application.Security;
using LenjExchange.Application.Settings;
using LenjExchange.Domain.Common;
using LenjExchange.Domain.Documents;
using LenjExchange.Domain.Security;
using LenjExchange.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace LenjExchange.Tests.Integration;

public class DocumentsAndBackupTests
{
    private static void AssertPdf(byte[] bytes)
    {
        Assert.True(bytes.Length > 1000, $"PDF too small: {bytes.Length}");
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        Assert.Contains("%%EOF", Encoding.ASCII.GetString(bytes, bytes.Length - 64, 64));
    }

    [Fact]
    public async Task Receipt_pdf_is_generated_in_a4_and_a5_with_naming_and_print_history()
    {
        await using var app = await TestApp.CreateAsync();
        var (customer, receipt) = await Flow.ReceiveAsync(app, customerName: "علی احمدی");
        var docs = app.Get<ReceiptDocumentService>();

        var model = await docs.BuildModelAsync(receipt.Id);
        Assert.Equal("علی احمدی", model.CustomerFullName);
        Assert.Equal("1405/07/20", model.ShamsiDate);
        Assert.Contains("100,000,000", model.AmountText);
        Assert.Contains("1 USD = 100,000 IRT", model.ConversionRateText);
        Assert.Equal("محمد", model.FinalRecipientName);
        Assert.Equal("رسید دریافت وجه", "رسید دریافت وجه");
        // Only the five customer fields + header/number exist on the print model: no exchanger/agent data.
        Assert.Equal(7, typeof(ReceiptPrintModel).GetProperties().Length);

        var a4 = await docs.GeneratePdfAsync(receipt.Id, PaperSize.A4);
        var a5 = await docs.GeneratePdfAsync(receipt.Id, PaperSize.A5);
        AssertPdf(await File.ReadAllBytesAsync(a4));
        AssertPdf(await File.ReadAllBytesAsync(a5));
        Assert.Equal(Path.Combine(app.Paths.DefaultReceiptsRoot, "1405", "07", "علی احمدی - YI140507001LE.pdf"), a4);
        Assert.Equal(Path.Combine(app.Paths.DefaultReceiptsRoot, "1405", "07", "علی احمدی - YI140507001LE (2).pdf"), a5);

        var history = await docs.PrintHistoryAsync(receipt.Id);
        Assert.Equal(2, history.Count);
        Assert.Contains(history, h => h.PaperSize == PaperSize.A5);
    }

    [Fact]
    public async Task Receipt_output_root_is_configurable_and_draft_cannot_print()
    {
        await using var app = await TestApp.CreateAsync();
        var custom = Path.Combine(app.Root, "custom-receipts");
        await app.Get<SettingsService>().SavePathsAsync(new PathSettings { ReceiptsRoot = custom });
        var (_, receipt) = await Flow.ReceiveAsync(app, customerName: "نام/با:کاراکتر*غیرمجاز");
        var path = await app.Get<ReceiptDocumentService>().GeneratePdfAsync(receipt.Id);
        Assert.StartsWith(custom, path);
        Assert.Contains("نام_با_کاراکتر_غیرمجاز - ", Path.GetFileName(path));

        var c = await app.NewCustomerAsync("پیش‌نویس");
        var draft = await app.Get<LenjExchange.Application.Transfers.ReceiptService>().CreateDraftAsync(
            new LenjExchange.Application.Transfers.ReceiptInput(c.Id, app.Clock.UtcNow, 10m, LenjExchange.Domain.Monetary.CurrencyCode.USD,
                LenjExchange.Domain.Monetary.CurrencyCode.USD, null, null));
        await Assert.ThrowsAsync<DomainException>(() => app.Get<ReceiptDocumentService>().GeneratePdfAsync(draft.Id));
    }

    [Fact]
    public async Task Statement_and_all_customers_pdf_and_excel_exports()
    {
        await using var app = await TestApp.CreateAsync();
        var (c, _) = await Flow.ReceiveAsync(app);
        var range = DateRanges.For(app.Clock, DatePreset.ThisMonth);
        var reports = app.Get<ReportService>();
        var st = await reports.CustomerStatementAsync(c.Id, range);
        var all = await reports.AllCustomersAsync(range);
        var header = await app.Get<SettingsService>().GetCompanyAsync();

        var pdf = app.Get<IReportPdfRenderer>();
        AssertPdf(pdf.RenderStatement(st, header));
        AssertPdf(pdf.RenderAllCustomers(all, header));

        var excel = app.Get<IExcelExporter>();
        foreach (var bytes in new[] { excel.ExportStatement(st), excel.ExportAllCustomers(all), excel.ExportOpenCases(await reports.OpenCasesAsync()) })
        {
            Assert.Equal((byte)'P', bytes[0]);
            Assert.Equal((byte)'K', bytes[1]);
            using var zip = new ZipArchive(new MemoryStream(bytes));
            Assert.NotNull(zip.GetEntry("xl/workbook.xml"));
        }
        using var wb = new ClosedXML.Excel.XLWorkbook(new MemoryStream(excel.ExportStatement(st)));
        Assert.True(wb.Worksheet(1).RightToLeft);
        Assert.Contains(wb.Worksheets, w => w.Name == "گردش USD");
    }

    [Fact]
    public async Task Attachments_validate_type_detect_duplicates_and_are_read_only()
    {
        await using var app = await TestApp.CreateAsync();
        var c = await app.NewCustomerAsync();
        var svc = app.Get<AttachmentService>();
        var png = Path.Combine(app.Root, "slip.png");
        await File.WriteAllBytesAsync(png, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 });
        var fake = Path.Combine(app.Root, "evil.pdf");
        await File.WriteAllBytesAsync(fake, new byte[] { 0x4D, 0x5A, 0x90, 0x00, 1, 2, 3 });
        var exe = Path.Combine(app.Root, "x.exe");
        await File.WriteAllBytesAsync(exe, new byte[] { 1, 2, 3 });

        var r1 = await svc.AddAsync(new AttachmentInput(png, AttachmentDocType.BankSlip, c.Id));
        Assert.False(r1.IsDuplicate);
        Assert.Equal("image/png", r1.Attachment.ContentType);
        Assert.Equal(64, r1.Attachment.Sha256.Length);
        var stored = svc.ResolvePath(r1.Attachment);
        Assert.StartsWith(app.Paths.AttachmentsRoot, stored);
        Assert.True(File.GetAttributes(stored).HasFlag(FileAttributes.ReadOnly));
        Assert.Equal("slip.png", r1.Attachment.OriginalFileName);

        var copy = Path.Combine(app.Root, "copy-of-slip.png");
        File.Copy(png, copy);
        var r2 = await svc.AddAsync(new AttachmentInput(copy, AttachmentDocType.BankSlip, c.Id));
        Assert.True(r2.IsDuplicate);
        Assert.Equal(r1.Attachment.Id, r2.Attachment.Id);
        Assert.Single(await svc.SearchAsync(customerId: c.Id));

        Assert.Equal("attachment_content", (await Assert.ThrowsAsync<DomainException>(() => svc.AddAsync(new AttachmentInput(fake, AttachmentDocType.Other)))).Code);
        Assert.Equal("attachment_type", (await Assert.ThrowsAsync<DomainException>(() => svc.AddAsync(new AttachmentInput(exe, AttachmentDocType.Other)))).Code);
        Assert.Empty(await svc.VerifyAsync());
    }

    [Fact]
    public async Task Backup_and_restore_round_trip_with_integrity_check()
    {
        await using var app = await TestApp.CreateAsync();
        var (c, receipt) = await Flow.ReceiveAsync(app, customerName: "قبل از پشتیبان");
        var png = Path.Combine(app.Root, "a.png");
        await File.WriteAllBytesAsync(png, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 9 });
        var att = (await app.Get<AttachmentService>().AddAsync(new AttachmentInput(png, AttachmentDocType.BankSlip, c.Id))).Attachment;

        var backup = app.Get<IBackupService>();
        var result = await backup.CreateBackupAsync();
        Assert.True(File.Exists(result.ZipPath));
        Assert.Contains(result.Manifest.Files, f => f.Path == "database/lenj.db");
        Assert.Contains(result.Manifest.Files, f => f.Path.StartsWith("attachments/") && f.Path.EndsWith(".png"));
        var verify = await backup.VerifyAsync(result.ZipPath);
        Assert.True(verify.IsValid, string.Join(",", verify.Problems));
        Assert.Equal(new[] { "ok" }, await backup.CheckLiveDatabaseAsync());

        // Changes after the backup…
        await app.NewCustomerAsync("بعد از پشتیبان");
        File.SetAttributes(app.Get<AttachmentService>().ResolvePath(att), FileAttributes.Normal);
        File.Delete(app.Get<AttachmentService>().ResolvePath(att));

        await backup.RestoreAsync(result.ZipPath);
        app.NewScope();
        var db = app.Get<IAppDbContext>();
        Assert.True(await db.Customers.AnyAsync(x => x.FullName == "قبل از پشتیبان"));
        Assert.False(await db.Customers.AnyAsync(x => x.FullName == "بعد از پشتیبان"));
        Assert.True(await db.CustomerReceipts.AnyAsync(x => x.ReceiptNumber == receipt.ReceiptNumber));
        Assert.True(File.Exists(app.Get<AttachmentService>().ResolvePath(att)));
        Assert.Empty(await app.Get<AttachmentService>().VerifyAsync());
        Assert.True(await db.AuditLog.AnyAsync(a => a.Operation == "backup.restore"));
        Assert.Contains(await app.Get<IBackupService>().ListBackupsAsync(), f => f.Name.Contains("pre-restore"));
    }

    [Fact]
    public async Task Tampered_backup_fails_verification_and_restore_requires_admin()
    {
        await using var app = await TestApp.CreateAsync();
        await Flow.ReceiveAsync(app);
        var backup = app.Get<IBackupService>();
        var result = await backup.CreateBackupAsync();

        var tampered = Path.Combine(app.Root, "tampered.zip");
        File.Copy(result.ZipPath, tampered);
        using (var zip = ZipFile.Open(tampered, ZipArchiveMode.Update))
        {
            zip.GetEntry("database/lenj.db")!.Delete();
            var e = zip.CreateEntry("database/lenj.db");
            await using var s = e.Open();
            await s.WriteAsync(Encoding.UTF8.GetBytes("not a database"));
        }
        var v = await backup.VerifyAsync(tampered);
        Assert.False(v.IsValid);
        Assert.Contains(v.Problems, p => p.Contains("هش"));
        await Assert.ThrowsAsync<DomainException>(() => backup.RestoreAsync(tampered));

        await app.SignInAsync(Role.Operator);
        await Assert.ThrowsAsync<LenjExchange.Application.Common.PermissionDeniedException>(() => app.Get<IBackupService>().RestoreAsync(result.ZipPath));
    }

    [Fact]
    public async Task Backup_keeps_only_configured_number_of_versions()
    {
        await using var app = await TestApp.CreateAsync();
        await app.Get<SettingsService>().SaveBackupAsync(new BackupSettings { KeepVersions = 2 });
        var backup = app.Get<IBackupService>();
        for (var i = 0; i < 4; i++)
        {
            app.Clock.UtcNow = app.Clock.UtcNow.AddMinutes(1);
            await backup.CreateBackupAsync();
        }
        Assert.Equal(2, (await backup.ListBackupsAsync()).Count);
    }

    [Fact]
    public async Task First_run_forces_admin_creation_and_login_is_audited()
    {
        await using var app = await TestApp.CreateAsync();
        var users = app.Get<UserService>();
        Assert.False(await users.IsFirstRunAsync());
        await Assert.ThrowsAsync<DomainException>(() => users.CreateInitialAdminAsync("admin2", "x", "Another123"));
        Assert.Null(await users.LoginAsync("admin", "wrong-password1"));
        Assert.NotNull(await users.LoginAsync("ADMIN", "Admin12345"));
        var db = app.Get<IAppDbContext>();
        Assert.True(await db.AuditLog.AnyAsync(a => a.Operation == "login.failed"));
        var admin = await db.Users.SingleAsync(u => u.UserName == "admin");
        Assert.DoesNotContain("Admin12345", admin.PasswordHash);
        Assert.StartsWith("PBKDF2-SHA256$", admin.PasswordHash);

        var op = await users.CreateUserAsync("ops1", "اپراتور ۱", "Operator123", Role.Operator);
        Assert.False(op.Has(Permission.ReverseEntries));
        await users.SetRoleAsync(op.Id, Role.Operator, new[] { Permission.ReverseEntries }, true);
        app.NewScope();
        Assert.True((await app.Get<IAppDbContext>().Users.SingleAsync(u => u.Id == op.Id)).Has(Permission.ReverseEntries));
        await Assert.ThrowsAsync<DomainException>(() => app.Get<UserService>().SetRoleAsync(admin.Id, Role.Operator, null, true)); // last admin
    }

    [Fact]
    public async Task Data_paths_live_under_local_app_data_by_default()
    {
        var paths = new AppPaths();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.StartsWith(Path.Combine(local, "LenjExchange"), paths.DatabasePath);
        Assert.StartsWith(Path.Combine(local, "LenjExchange"), paths.AttachmentsRoot);
        Assert.EndsWith(Path.Combine("LenjExchange", "Receipts"), paths.DefaultReceiptsRoot);
        await Task.CompletedTask;
    }
}
