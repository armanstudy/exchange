using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Common;
using LenjExchange.Application.Security;
using LenjExchange.Domain.Security;

namespace LenjExchange.Desktop.ViewModels;

public sealed record UserRow(Guid Id, string UserName, string DisplayName, Role Role, string RoleText, bool IsActive, string LastLogin,
    IReadOnlySet<Permission> Extra);

public sealed record AuditRow(string Time, string User, string Operation, string? Entity, string? Details);

public sealed partial class PermissionChoice : ObservableObject
{
    public Permission Permission { get; init; }
    public string Text { get; init; } = "";
    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private bool _isFromRole;
}

public sealed partial class UsersViewModel : ViewModelBase
{
    private readonly UserService _users;
    private readonly AuditService _audit;
    private readonly ICurrentUser _current;

    [ObservableProperty] private UserRow? _selected;
    [ObservableProperty] private string _newUserName = "";
    [ObservableProperty] private string _newDisplayName = "";
    [ObservableProperty] private Option<Role>? _newRole;
    [ObservableProperty] private Option<Role>? _editRole;
    [ObservableProperty] private bool _editActive = true;

    public string NewPassword { get; set; } = "";
    public string ResetPassword { get; set; } = "";
    public string CurrentPassword { get; set; } = "";

    public ObservableCollection<UserRow> Items { get; } = new();
    public ObservableCollection<PermissionChoice> Permissions { get; } = new();
    public ObservableCollection<AuditRow> Audit { get; } = new();
    public IReadOnlyList<Option<Role>> Roles { get; } = new[] { new Option<Role>(Role.Operator, "اپراتور"), new Option<Role>(Role.Admin, "مدیر") };
    public bool CanViewAudit => _current.Has(Permission.ViewAuditLog);
    public string PasswordHint => $"حداقل {UserService.MinPasswordLength} کاراکتر، شامل حرف و عدد، و متفاوت با نام کاربری.";

    public UsersViewModel(UserService users, AuditService audit, ICurrentUser current)
    {
        _users = users;
        _audit = audit;
        _current = current;
        _newRole = Roles[0];
        foreach (var p in Enum.GetValues<Permission>())
            Permissions.Add(new PermissionChoice { Permission = p, Text = PermissionText.Persian(p) });
    }

    public override string Title => "کاربران و دسترسی‌ها";

    public override Task LoadAsync() => RunAsync(async () =>
    {
        Items.Clear();
        foreach (var u in await _users.ListAsync())
            Items.Add(new UserRow(u.Id, u.UserName, u.DisplayName, u.Role, u.Role == Role.Admin ? "مدیر" : "اپراتور", u.IsActive,
                u.LastLoginUtc is { } l ? Display.DateTime(l) : "—",
                u.Permissions.Except(RolePermissions.For(u.Role)).ToHashSet()));
        Audit.Clear();
        if (CanViewAudit)
            foreach (var a in await _audit.RecentAsync(500))
                Audit.Add(new AuditRow(Display.DateTime(a.TimestampUtc), a.UserName, a.Operation, a.EntityType, a.Details));
    });

    partial void OnSelectedChanged(UserRow? value)
    {
        if (value is null) return;
        EditRole = Roles.First(r => r.Value == value.Role);
        EditActive = value.IsActive;
        SyncPermissions(value.Role, value.Extra);
    }

    partial void OnEditRoleChanged(Option<Role>? value)
    {
        if (value is not null) SyncPermissions(value.Value, Permissions.Where(p => p.IsChecked && !p.IsFromRole).Select(p => p.Permission).ToHashSet());
    }

    private void SyncPermissions(Role role, IReadOnlySet<Permission> extra)
    {
        var fromRole = RolePermissions.For(role);
        foreach (var p in Permissions)
        {
            p.IsFromRole = fromRole.Contains(p.Permission);
            p.IsChecked = p.IsFromRole || extra.Contains(p.Permission);
        }
    }

    [RelayCommand]
    private async Task Create()
    {
        var ok = await RunAsync(() => _users.CreateUserAsync(NewUserName, NewDisplayName, NewPassword, NewRole?.Value ?? Role.Operator));
        if (!ok) return;
        var msg = $"کاربر «{NewUserName}» ساخته شد.";
        NewUserName = NewDisplayName = "";
        await LoadAsync();
        StatusMessage = msg;
    }

    [RelayCommand]
    private async Task SaveAccess()
    {
        if (Selected is not { } s || EditRole is null) return;
        var extra = Permissions.Where(p => p.IsChecked && !p.IsFromRole).Select(p => p.Permission).ToList();
        if (await RunAsync(() => _users.SetRoleAsync(s.Id, EditRole.Value, extra, EditActive))) { await LoadAsync(); StatusMessage = "دسترسی‌ها ذخیره شد."; }
    }

    [RelayCommand]
    private async Task SetPassword()
    {
        if (Selected is not { } s) return;
        var self = string.Equals(s.UserName, _current.UserName, StringComparison.OrdinalIgnoreCase);
        if (await RunAsync(() => _users.ChangePasswordAsync(s.Id, self ? CurrentPassword : null, ResetPassword))) StatusMessage = $"رمز عبور «{s.UserName}» تغییر کرد.";
    }

    [RelayCommand] private Task Refresh() => LoadAsync();
}
