using LenjExchange.Application.Abstractions;
using LenjExchange.Domain.Common;
using LenjExchange.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace LenjExchange.Application.Common;

public sealed class PermissionDeniedException : DomainException
{
    public PermissionDeniedException(Permission permission)
        : base("permission_denied", $"شما مجوز «{PermissionText.Persian(permission)}» را ندارید.") { }
}

public sealed class NotFoundException : DomainException
{
    public NotFoundException(string what) : base("not_found", $"{what} یافت نشد.") { }
}

public static class PermissionText
{
    public static string Persian(Permission p) => p switch
    {
        Permission.ViewDashboard => "مشاهده داشبورد",
        Permission.ManageCustomers => "مدیریت مشتریان",
        Permission.RegisterReceipts => "ثبت رسید",
        Permission.FinalizeReceipts => "قطعی کردن رسید",
        Permission.ManageCases => "مدیریت پرونده‌ها",
        Permission.ManageExchangerDocuments => "ثبت اسناد صرافی",
        Permission.ApproveDeliveries => "تأیید تحویل به نماینده",
        Permission.ApproveChinaReceipts => "تأیید دریافت در چین",
        Permission.ManageRates => "مدیریت نرخ‌ها",
        Permission.ViewReports => "مشاهده گزارش‌ها",
        Permission.ExportReports => "خروجی گزارش",
        Permission.ManageAttachments => "مدیریت پیوست‌ها",
        Permission.ReverseEntries => "برگشت/اصلاح اسناد",
        Permission.ManageUsers => "مدیریت کاربران",
        Permission.ManageSettings => "تنظیمات",
        Permission.CreateBackup => "تهیه پشتیبان",
        Permission.RestoreBackup => "بازیابی پشتیبان",
        Permission.ViewAuditLog => "مشاهده گزارش رویدادها",
        Permission.ManageMasterData => "اطلاعات پایه",
        Permission.PrintReceipts => "چاپ رسید",
        _ => p.ToString()
    };
}

public static class AuthorizationExtensions
{
    public static void Demand(this ICurrentUser user, Permission permission)
    {
        if (!user.Has(permission)) throw new PermissionDeniedException(permission);
    }
}

/// <summary>Runs work atomically; joins an ambient transaction when one is already open.</summary>
public sealed class UnitOfWork
{
    private readonly IAppDbContext _db;

    public UnitOfWork(IAppDbContext db) => _db = db;

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> work, CancellationToken ct = default)
    {
        if (_db.Database.CurrentTransaction is not null)
        {
            var nested = await work();
            await _db.SaveChangesAsync(ct);
            return nested;
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var result = await work();
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            DetachPending();
            throw;
        }
    }

    public Task ExecuteAsync(Func<Task> work, CancellationToken ct = default) =>
        ExecuteAsync(async () => { await work(); return true; }, ct);

    private void DetachPending()
    {
        if (_db is DbContext ctx)
            foreach (var e in ctx.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
                e.State = EntityState.Detached;
    }
}

public sealed class AuditService
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _user;

    public AuditService(IAppDbContext db, IClock clock, ICurrentUser user)
    {
        _db = db;
        _clock = clock;
        _user = user;
    }

    /// <summary>Adds an audit row to the current unit of work (saved together with the business change).</summary>
    public void Record(string operation, string? entityType = null, object? entityId = null, string? details = null) =>
        _db.AuditLog.Add(new AuditLogEntry(_clock.UtcNow, _user.UserName, operation, entityType, entityId?.ToString(), details));

    public async Task<IReadOnlyList<AuditLogEntry>> RecentAsync(int take = 500, CancellationToken ct = default)
    {
        _user.Demand(Permission.ViewAuditLog);
        return await _db.AuditLog.AsNoTracking().OrderByDescending(a => a.Id).Take(take).ToListAsync(ct);
    }
}
