using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Backup;
using LenjExchange.Desktop.Services;
using LenjExchange.Domain.Security;

namespace LenjExchange.Desktop.ViewModels;

public sealed record BackupRow(string FileName, string FullPath, string Date, string Size);

public sealed partial class BackupViewModel : ViewModelBase
{
    private readonly IBackupService _backup;
    private readonly ICurrentUser _user;
    private readonly IDialogService _dialogs;
    private readonly NavigationService _nav;

    [ObservableProperty] private BackupRow? _selected;
    [ObservableProperty] private string _verificationResult = "";

    public ObservableCollection<BackupRow> Items { get; } = new();
    public bool CanRestore => _user.Has(Permission.RestoreBackup);

    public BackupViewModel(IBackupService backup, ICurrentUser user, IDialogService dialogs, NavigationService nav)
    {
        _backup = backup;
        _user = user;
        _dialogs = dialogs;
        _nav = nav;
    }

    public override string Title => "پشتیبان‌گیری و بازیابی";

    public override Task LoadAsync() => RunAsync(async () =>
    {
        Items.Clear();
        foreach (var f in await _backup.ListBackupsAsync())
            Items.Add(new BackupRow(f.Name, f.FullName, Display.DateTime(f.LastWriteTimeUtc), $"{f.Length / 1024.0 / 1024.0:0.00} MB"));
    });

    [RelayCommand]
    private async Task Create()
    {
        BackupResult? r = null;
        if (!await RunAsync(async () => r = await _backup.CreateBackupAsync(null, "manual"))) return;
        await LoadAsync();
        StatusMessage = $"پشتیبان ساخته شد: {r!.ZipPath} ({r.Manifest.Files.Count} فایل)";
    }

    [RelayCommand]
    private async Task CreateTo()
    {
        var folder = _dialogs.PickFolder();
        if (folder is null) return;
        BackupResult? r = null;
        if (await RunAsync(async () => r = await _backup.CreateBackupAsync(folder, "manual-external")))
            StatusMessage = "پشتیبان ساخته شد: " + r!.ZipPath;
    }

    private string? PickZip() => Selected?.FullPath ?? _dialogs.OpenFile("پشتیبان لنج|*.zip", "انتخاب فایل پشتیبان");

    [RelayCommand]
    private Task Verify() => RunAsync(async () =>
    {
        var path = PickZip();
        if (path is null) return;
        var v = await _backup.VerifyAsync(path);
        VerificationResult = v.IsValid
            ? $"فایل سالم است. ساخته‌شده در {Display.DateTime(v.Manifest!.CreatedAtUtc)} توسط {v.Manifest.CreatedBy}، {v.Manifest.Files.Count} فایل."
            : "فایل پشتیبان معتبر نیست:\n" + string.Join("\n", v.Problems);
    });

    [RelayCommand]
    private Task CheckLive() => RunAsync(async () =>
    {
        var problems = await _backup.CheckLiveDatabaseAsync();
        VerificationResult = problems.Count == 0 ? "پایگاه داده فعلی سالم است (PRAGMA integrity_check = ok)." : string.Join("\n", problems);
    });

    [RelayCommand]
    private async Task Restore()
    {
        var path = PickZip();
        if (path is null) return;
        if (!_dialogs.Confirm("بازیابی، همه اطلاعات فعلی را با محتوای این پشتیبان جایگزین می‌کند.\n" +
                              "قبل از بازیابی، به‌طور خودکار از وضعیت فعلی پشتیبان گرفته می‌شود.\n\nادامه می‌دهید؟", "بازیابی پشتیبان")) return;
        if (!await RunAsync(() => _backup.RestoreAsync(path))) return;
        _dialogs.Info("بازیابی انجام شد. لطفاً دوباره وارد شوید.");
        await _nav.SignOutAsync();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (Selected is { } s) _dialogs.OpenWithShell(System.IO.Path.GetDirectoryName(s.FullPath)!);
    }
}
