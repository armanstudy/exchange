using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Common;
using LenjExchange.Domain.Common;
using LenjExchange.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace LenjExchange.Application.Security;

public sealed class UserService
{
    public const int MinPasswordLength = 8;

    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly CurrentUserSession _session;
    private readonly PasswordHasher _hasher;
    private readonly AuditService _audit;

    public UserService(IAppDbContext db, IClock clock, CurrentUserSession session, PasswordHasher hasher, AuditService audit)
    {
        _db = db;
        _clock = clock;
        _session = session;
        _hasher = hasher;
        _audit = audit;
    }

    /// <summary>True until the first administrator has been created. There is no built-in default password.</summary>
    public async Task<bool> IsFirstRunAsync(CancellationToken ct = default) => !await _db.Users.AnyAsync(ct);

    public async Task<User> CreateInitialAdminAsync(string userName, string displayName, string password, CancellationToken ct = default)
    {
        if (!await IsFirstRunAsync(ct))
            throw new DomainException("setup_done", "حساب مدیر قبلاً ایجاد شده است.");
        ValidatePassword(password, userName);
        var user = new User(userName, displayName, _hasher.Hash(password), Role.Admin);
        user.StampCreated("setup", _clock.UtcNow);
        _db.Users.Add(user);
        _db.AuditLog.Add(new AuditLogEntry(_clock.UtcNow, user.UserName, "setup.admin", nameof(User), user.Id.ToString(), null));
        await _db.SaveChangesAsync(ct);
        _session.SignIn(user);
        return user;
    }

    public async Task<User?> LoginAsync(string userName, string password, CancellationToken ct = default)
    {
        var name = User.NormalizeUserName(userName);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserName == name, ct);
        var ok = user is { IsActive: true } && _hasher.Verify(password, user.PasswordHash);
        if (user is not null)
        {
            user.RecordLogin(ok, _clock.UtcNow);
            if (ok && _hasher.NeedsRehash(user.PasswordHash)) user.ChangePassword(_hasher.Hash(password));
        }
        _db.AuditLog.Add(new AuditLogEntry(_clock.UtcNow, name, ok ? "login.success" : "login.failed", nameof(User), user?.Id.ToString(), null));
        await _db.SaveChangesAsync(ct);
        if (!ok) return null;
        _session.SignIn(user!);
        return user;
    }

    public void Logout()
    {
        if (_session.IsAuthenticated) _audit.Record("logout");
        _session.SignOut();
    }

    public async Task<User> CreateUserAsync(string userName, string displayName, string password, Role role, CancellationToken ct = default)
    {
        _session.Demand(Permission.ManageUsers);
        var name = User.NormalizeUserName(userName);
        if (await _db.Users.AnyAsync(u => u.UserName == name, ct))
            throw new DomainException("user_dup", "نام کاربری تکراری است.");
        ValidatePassword(password, userName);
        var user = new User(name, displayName, _hasher.Hash(password), role);
        user.StampCreated(_session.UserName, _clock.UtcNow);
        _db.Users.Add(user);
        _audit.Record("user.create", nameof(User), user.Id, $"{name} ({role})");
        await _db.SaveChangesAsync(ct);
        return user;
    }

    public async Task ChangePasswordAsync(Guid userId, string? currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException("کاربر");
        var self = _session.User?.Id == userId;
        if (!self) _session.Demand(Permission.ManageUsers);
        else if (currentPassword is null || !_hasher.Verify(currentPassword, user.PasswordHash))
            throw new DomainException("password_wrong", "رمز فعلی نادرست است.");
        ValidatePassword(newPassword, user.UserName);
        user.ChangePassword(_hasher.Hash(newPassword));
        _audit.Record("user.password", nameof(User), user.Id);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetRoleAsync(Guid userId, Role role, IEnumerable<Permission>? extra, bool active, CancellationToken ct = default)
    {
        _session.Demand(Permission.ManageUsers);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException("کاربر");
        if (user.Role == Role.Admin && (role != Role.Admin || !active))
        {
            var otherAdmins = await _db.Users.CountAsync(u => u.Role == Role.Admin && u.IsActive && u.Id != userId, ct);
            if (otherAdmins == 0) throw new DomainException("last_admin", "حداقل یک مدیر فعال باید باقی بماند.");
        }
        user.ChangeRole(role);
        user.SetActive(active);
        if (extra is not null) user.SetExtraPermissions(extra);
        _audit.Record("user.role", nameof(User), user.Id, $"{role} active={active} extra={user.ExtraPermissionsCsv}");
        await _db.SaveChangesAsync(ct);
    }

    public Task<List<User>> ListAsync(CancellationToken ct = default)
    {
        _session.Demand(Permission.ManageUsers);
        return _db.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync(ct);
    }

    public static void ValidatePassword(string password, string userName)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            throw new DomainException("password_short", $"رمز عبور باید حداقل {MinPasswordLength} کاراکتر باشد.");
        if (string.Equals(password, userName, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("password_username", "رمز عبور نباید با نام کاربری یکسان باشد.");
        if (!password.Any(char.IsDigit) || !password.Any(char.IsLetter))
            throw new DomainException("password_weak", "رمز عبور باید شامل حرف و عدد باشد.");
    }
}
