using LenjExchange.Domain.Common;

namespace LenjExchange.Domain.Security;

public enum Role
{
    Admin = 1,
    Operator = 2
}

public enum Permission
{
    ViewDashboard = 1,
    ManageCustomers = 2,
    RegisterReceipts = 3,
    FinalizeReceipts = 4,
    ManageCases = 5,
    ManageExchangerDocuments = 6,
    ApproveDeliveries = 7,
    ApproveChinaReceipts = 8,
    ManageRates = 9,
    ViewReports = 10,
    ExportReports = 11,
    ManageAttachments = 12,
    /// <summary>Reversal/correction entries — deliberately not granted to Operators by default.</summary>
    ReverseEntries = 13,
    ManageUsers = 14,
    ManageSettings = 15,
    CreateBackup = 16,
    RestoreBackup = 17,
    ViewAuditLog = 18,
    ManageMasterData = 19,
    PrintReceipts = 20
}

public static class RolePermissions
{
    private static readonly HashSet<Permission> OperatorPermissions = new()
    {
        Permission.ViewDashboard, Permission.ManageCustomers, Permission.RegisterReceipts, Permission.FinalizeReceipts,
        Permission.ManageCases, Permission.ManageExchangerDocuments, Permission.ViewReports, Permission.ExportReports,
        Permission.ManageAttachments, Permission.CreateBackup, Permission.PrintReceipts
    };

    public static IReadOnlySet<Permission> For(Role role) => role switch
    {
        Role.Admin => Enum.GetValues<Permission>().ToHashSet(),
        Role.Operator => OperatorPermissions,
        _ => new HashSet<Permission>()
    };
}

public class User : AuditableEntity
{
    public string UserName { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public string PasswordHash { get; private set; } = "";
    public Role Role { get; private set; }
    /// <summary>Additional permissions granted on top of the role (comma-separated enum names).</summary>
    public string ExtraPermissionsCsv { get; private set; } = "";
    public bool IsActive { get; private set; } = true;
    public DateTime? LastLoginUtc { get; private set; }
    public int FailedLoginCount { get; private set; }

    protected User() { }

    public User(string userName, string displayName, string passwordHash, Role role)
    {
        UserName = NormalizeUserName(userName);
        DisplayName = Guard.NotBlank(displayName, "نام نمایشی");
        PasswordHash = Guard.NotBlank(passwordHash, "رمز عبور");
        Role = role;
    }

    public static string NormalizeUserName(string userName) => Guard.NotBlank(userName, "نام کاربری").ToLowerInvariant();

    public IReadOnlySet<Permission> Permissions
    {
        get
        {
            var set = RolePermissions.For(Role).ToHashSet();
            foreach (var p in ExtraPermissionsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (Enum.TryParse<Permission>(p, out var perm)) set.Add(perm);
            return set;
        }
    }

    public bool Has(Permission p) => IsActive && Permissions.Contains(p);

    public void SetExtraPermissions(IEnumerable<Permission> permissions) =>
        ExtraPermissionsCsv = string.Join(',', permissions.Distinct());

    public void ChangePassword(string newHash) => PasswordHash = Guard.NotBlank(newHash, "رمز عبور");
    public void ChangeRole(Role role) => Role = role;
    public void SetActive(bool active) => IsActive = active;
    public void Rename(string displayName) => DisplayName = Guard.NotBlank(displayName, "نام نمایشی");

    public void RecordLogin(bool success, DateTime utcNow)
    {
        if (success) { FailedLoginCount = 0; LastLoginUtc = Guard.Utc(utcNow); }
        else FailedLoginCount++;
    }
}

public class AuditLogEntry
{
    public long Id { get; private set; }
    public DateTime TimestampUtc { get; private set; }
    public string UserName { get; private set; } = "";
    public string Operation { get; private set; } = "";
    public string? EntityType { get; private set; }
    public string? EntityId { get; private set; }
    public string? Details { get; private set; }

    protected AuditLogEntry() { }

    public AuditLogEntry(DateTime utcNow, string userName, string operation, string? entityType, string? entityId, string? details)
    {
        TimestampUtc = Guard.Utc(utcNow);
        UserName = userName;
        Operation = operation;
        EntityType = entityType;
        EntityId = entityId;
        Details = details;
    }
}
