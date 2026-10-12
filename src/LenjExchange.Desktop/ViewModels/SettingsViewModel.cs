using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Settings;
using LenjExchange.Desktop.Services;
using LenjExchange.Domain.Documents;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Numbering;

namespace LenjExchange.Desktop.ViewModels;

public sealed partial class CompanyEditor : ObservableObject
{
    [ObservableProperty] private string _persianName = "";
    [ObservableProperty] private string _englishName = "";
    [ObservableProperty] private string _chinaLabel = "";
    [ObservableProperty] private string _chinaAddress = "";
    [ObservableProperty] private string _chinaContact = "";
    [ObservableProperty] private string _iranLabel = "";
    [ObservableProperty] private string _iranAddress = "";
    [ObservableProperty] private string _iranContact = "";
    [ObservableProperty] private string _footerText = "";
    [ObservableProperty] private string? _logoPath;
    [ObservableProperty] private string _primaryColor = "";
    [ObservableProperty] private string _secondaryColor = "";
    private CompanyHeader _source = new();

    public void Load(CompanyHeader h)
    {
        _source = h;
        PersianName = h.PersianName; EnglishName = h.EnglishName;
        ChinaLabel = h.ChinaLabel; ChinaAddress = h.ChinaAddress; ChinaContact = h.ChinaContact;
        IranLabel = h.IranLabel; IranAddress = h.IranAddress; IranContact = h.IranContact;
        FooterText = h.FooterText; LogoPath = h.LogoPath;
        PrimaryColor = h.PrimaryColor; SecondaryColor = h.SecondaryColor;
    }

    public CompanyHeader ToHeader() => _source with
    {
        PersianName = PersianName.Trim(), EnglishName = EnglishName.Trim(),
        ChinaLabel = ChinaLabel.Trim(), ChinaAddress = ChinaAddress.Trim(), ChinaContact = ChinaContact.Trim(),
        IranLabel = IranLabel.Trim(), IranAddress = IranAddress.Trim(), IranContact = IranContact.Trim(),
        FooterText = FooterText.Trim(), LogoPath = string.IsNullOrWhiteSpace(LogoPath) ? null : LogoPath,
        PrimaryColor = PrimaryColor.Trim(), SecondaryColor = SecondaryColor.Trim()
    };
}

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly IAppPaths _paths;
    private readonly IClock _clock;
    private readonly IDialogService _dialogs;

    [ObservableProperty] private NumberingSettings _numbering = new();
    [ObservableProperty] private PathSettings _pathSettings = new();
    [ObservableProperty] private BackupSettings _backup = new();
    [ObservableProperty] private AccountingSettings _accounting = new();

    [ObservableProperty] private string _receiptPattern = "";
    [ObservableProperty] private Option<SequenceScope>? _receiptScope;
    [ObservableProperty] private string _casePattern = "";
    [ObservableProperty] private string _numberPreview = "";

    [ObservableProperty] private string _receiptsRoot = "";
    [ObservableProperty] private string _reportsRoot = "";
    [ObservableProperty] private string _backupRoot = "";

    [ObservableProperty] private bool _autoBackup;
    [ObservableProperty] private string _intervalHours = "";
    [ObservableProperty] private string _keepVersions = "";
    [ObservableProperty] private bool _backupOnExit;

    [ObservableProperty] private CurrencyCode _baseCurrency;
    [ObservableProperty] private PaperSize _defaultPaper;
    [ObservableProperty] private bool _allowMismatch;

    public IReadOnlyList<Option<SequenceScope>> Scopes { get; } = new[]
    {
        new Option<SequenceScope>(SequenceScope.Daily, "روزانه"),
        new Option<SequenceScope>(SequenceScope.Monthly, "ماهانه"),
        new Option<SequenceScope>(SequenceScope.Yearly, "سالانه"),
        new Option<SequenceScope>(SequenceScope.Global, "بدون بازنشانی"),
    };
    public IReadOnlyList<PaperSize> PaperSizes { get; } = new[] { PaperSize.A5, PaperSize.A4 };
    public CompanyEditor Company { get; } = new();
    public string DataFolder => _paths.DataRoot;

    public SettingsViewModel(SettingsService settings, IAppPaths paths, IClock clock, IDialogService dialogs)
    {
        _settings = settings;
        _paths = paths;
        _clock = clock;
        _dialogs = dialogs;
    }

    public override string Title => "تنظیمات";

    public override Task LoadAsync() => RunAsync(async () =>
    {
        Company.Load(await _settings.GetCompanyAsync());
        Numbering = await _settings.GetNumberingAsync();
        PathSettings = await _settings.GetPathsAsync();
        Backup = await _settings.GetBackupAsync();
        Accounting = await _settings.GetAccountingAsync();

        ReceiptPattern = Numbering.ReceiptPattern;
        ReceiptScope = Scopes.FirstOrDefault(s => s.Value == Numbering.ReceiptScope) ?? Scopes[1];
        CasePattern = Numbering.CasePattern;
        ReceiptsRoot = PathSettings.ReceiptsRoot ?? _paths.DefaultReceiptsRoot;
        ReportsRoot = PathSettings.ReportsRoot ?? _paths.DefaultReportsRoot;
        BackupRoot = PathSettings.BackupRoot ?? _paths.DefaultBackupRoot;
        AutoBackup = Backup.AutoBackupEnabled;
        IntervalHours = Backup.AutoBackupIntervalHours.ToString();
        KeepVersions = Backup.KeepVersions.ToString();
        BackupOnExit = Backup.BackupOnExit;
        BaseCurrency = Accounting.BaseCurrency;
        DefaultPaper = Accounting.DefaultReceiptPaper;
        AllowMismatch = Accounting.AllowDepositMismatch;
        UpdatePreview();
    });

    partial void OnReceiptPatternChanged(string value) => UpdatePreview();
    partial void OnReceiptScopeChanged(Option<SequenceScope>? value) => UpdatePreview();

    private void UpdatePreview()
    {
        try
        {
            var p = new ReceiptNumberPattern(ReceiptPattern, ReceiptScope?.Value ?? SequenceScope.Monthly);
            NumberPreview = "نمونه: " + p.Format(_clock.LocalToday(), 126);
        }
        catch (Exception ex)
        {
            NumberPreview = "الگو نامعتبر است: " + ex.Message;
        }
    }

    [RelayCommand]
    private void PickLogo()
    {
        var path = _dialogs.OpenFile("تصویر|*.png;*.jpg;*.jpeg", "انتخاب لوگو");
        if (path is not null) Company.LogoPath = path;
    }

    [RelayCommand]
    private void ClearLogo() => Company.LogoPath = null;

    [RelayCommand]
    private void PickFolder(string which)
    {
        var current = which switch { "receipts" => ReceiptsRoot, "reports" => ReportsRoot, _ => BackupRoot };
        var folder = _dialogs.PickFolder(current);
        if (folder is null) return;
        switch (which)
        {
            case "receipts": ReceiptsRoot = folder; break;
            case "reports": ReportsRoot = folder; break;
            default: BackupRoot = folder; break;
        }
    }

    [RelayCommand]
    private Task Save() => RunAsync(async () =>
    {
        var header = Company.ToHeader();
        if (!IsColor(header.PrimaryColor) || !IsColor(header.SecondaryColor))
            throw new Domain.Common.DomainException("settings_color", "رنگ باید به شکل #RRGGBB باشد.");
        await _settings.SaveCompanyAsync(header);
        await _settings.SaveNumberingAsync(Numbering with
        {
            ReceiptPattern = ReceiptPattern.Trim(),
            ReceiptScope = ReceiptScope?.Value ?? SequenceScope.Monthly,
            CasePattern = CasePattern.Trim()
        });
        await _settings.SavePathsAsync(new PathSettings
        {
            ReceiptsRoot = Same(ReceiptsRoot, _paths.DefaultReceiptsRoot),
            ReportsRoot = Same(ReportsRoot, _paths.DefaultReportsRoot),
            BackupRoot = Same(BackupRoot, _paths.DefaultBackupRoot)
        });
        await _settings.SaveBackupAsync(new BackupSettings
        {
            AutoBackupEnabled = AutoBackup,
            AutoBackupIntervalHours = Math.Max(1, (int)ParseAmount(IntervalHours, "فاصله پشتیبان‌گیری")),
            KeepVersions = Math.Max(1, (int)ParseAmount(KeepVersions, "تعداد نسخه‌ها")),
            BackupOnExit = BackupOnExit
        });
        await _settings.SaveAccountingAsync(new AccountingSettings
        {
            BaseCurrency = BaseCurrency,
            DefaultReceiptPaper = DefaultPaper,
            AllowDepositMismatch = AllowMismatch
        });
    }, "تنظیمات ذخیره شد. تغییر الگوی شماره فقط روی اسناد جدید اثر دارد.");

    private static bool IsColor(string s) => System.Text.RegularExpressions.Regex.IsMatch(s, "^#[0-9A-Fa-f]{6}$");

    private static string? Same(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value.TrimEnd('\\', '/'), fallback.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
            ? null : value.Trim();

    [RelayCommand]
    private void OpenDataFolder() => _dialogs.OpenWithShell(_paths.DataRoot);
}
