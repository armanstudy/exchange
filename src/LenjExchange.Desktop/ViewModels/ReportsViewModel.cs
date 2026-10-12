using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Common;
using LenjExchange.Application.Customers;
using LenjExchange.Application.Documents;
using LenjExchange.Application.Reporting;
using LenjExchange.Application.Settings;
using LenjExchange.Desktop.Services;
using LenjExchange.Domain.Common;
using LenjExchange.Domain.Ledger;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Security;
using LenjExchange.Domain.Shamsi;
using LenjExchange.Domain.Transfers;

namespace LenjExchange.Desktop.ViewModels;

public enum ReportKind
{
    CustomerStatement = 1,
    AllCustomers = 2,
    OpenCases = 3,
    Exchangers = 4,
    Agents = 5,
    Recipients = 6,
    Banks = 7
}

/// <summary>Generic display row so one grid can show every report; amounts are pre-formatted per currency.</summary>
public sealed record ReportRow(string C1, string C2, string C3, string C4, string C5, string C6, string C7, string C8);

public sealed partial class ReportsViewModel : ViewModelBase
{
    private readonly ReportService _reports;
    private readonly CustomerService _customers;
    private readonly SettingsService _settings;
    private readonly IReportPdfRenderer _pdf;
    private readonly IExcelExporter _excel;
    private readonly IAppPaths _paths;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IDialogService _dialogs;
    private Guid? _preselectedCustomer;

    private StatementReport? _statement;
    private AllCustomersReport? _allCustomers;
    private IReadOnlyList<OpenCaseRow>? _openCases;

    [ObservableProperty] private Option<ReportKind>? _kind;
    [ObservableProperty] private Option<DatePreset>? _preset;
    [ObservableProperty] private string _fromDate = "";
    [ObservableProperty] private string _toDate = "";
    [ObservableProperty] private IReadOnlyList<Lookup> _customerOptions = Array.Empty<Lookup>();
    [ObservableProperty] private Lookup? _customer;
    [ObservableProperty] private Option<CurrencyCode?>? _currency;
    [ObservableProperty] private Option<OperationType?>? _opType;
    [ObservableProperty] private bool _excludeReversed;
    [ObservableProperty] private string _rangeText = "";
    [ObservableProperty] private IReadOnlyList<string> _headers = Array.Empty<string>();
    [ObservableProperty] private string _summaryText = "";

    public ObservableCollection<ReportRow> Rows { get; } = new();

    public IReadOnlyList<Option<ReportKind>> Kinds { get; } = new[]
    {
        new Option<ReportKind>(ReportKind.CustomerStatement, "صورتحساب مشتری"),
        new Option<ReportKind>(ReportKind.AllCustomers, "مانده همه مشتریان"),
        new Option<ReportKind>(ReportKind.OpenCases, "پرونده‌های باز / تسویه‌نشده"),
        new Option<ReportKind>(ReportKind.Exchangers, "مانده صرافی‌ها"),
        new Option<ReportKind>(ReportKind.Agents, "مانده نمایندگان"),
        new Option<ReportKind>(ReportKind.Recipients, "دریافتی گیرندگان چین"),
        new Option<ReportKind>(ReportKind.Banks, "مانده بانک‌ها"),
    };

    public IReadOnlyList<Option<DatePreset>> Presets { get; } =
        Enum.GetValues<DatePreset>().Select(p => new Option<DatePreset>(p, DatePresetText.Persian(p))).ToList();

    public IReadOnlyList<Option<CurrencyCode?>> Currencies { get; } =
        new[] { new Option<CurrencyCode?>(null, "همه ارزها") }
            .Concat(Enum.GetValues<CurrencyCode>().Select(c => new Option<CurrencyCode?>(c, c.ToString()))).ToList();

    public IReadOnlyList<Option<OperationType?>> OperationTypes { get; } =
        new[] { new Option<OperationType?>(null, "همه عملیات") }
            .Concat(Enum.GetValues<OperationType>().Select(t => new Option<OperationType?>(t, LedgerText.Persian(t)))).ToList();

    public bool IsStatement => Kind?.Value == ReportKind.CustomerStatement;
    public bool NeedsDates => Preset?.Value is DatePreset.SpecificDay or DatePreset.Custom;
    public bool CanExport => _user.Has(Permission.ExportReports);
    public bool HasPdf => Kind?.Value is ReportKind.CustomerStatement or ReportKind.AllCustomers;

    public ReportsViewModel(ReportService reports, CustomerService customers, SettingsService settings, IReportPdfRenderer pdf,
        IExcelExporter excel, IAppPaths paths, ICurrentUser user, IClock clock, IDialogService dialogs)
    {
        _reports = reports;
        _customers = customers;
        _settings = settings;
        _pdf = pdf;
        _excel = excel;
        _paths = paths;
        _user = user;
        _clock = clock;
        _dialogs = dialogs;
        _kind = Kinds[0];
        _preset = Presets.First(p => p.Value == DatePreset.ThisMonth);
        _currency = Currencies[0];
        _opType = OperationTypes[0];
    }

    public override string Title => "گزارش‌ها";

    public void PreselectCustomer(Guid id) => _preselectedCustomer = id;

    public override Task LoadAsync() => RunAsync(async () =>
    {
        CustomerOptions = (await _customers.SearchAsync(null, includeInactive: true)).Select(c => new Lookup(c.Id, $"{c.FullName} ({c.Code})")).ToList();
        FromDate = ToDate = ShamsiDate.Format(_clock.LocalToday());
        if (_preselectedCustomer is { } id)
        {
            Customer = CustomerOptions.FirstOrDefault(c => c.Id == id);
            await RunReportCore();
        }
    });

    partial void OnKindChanged(Option<ReportKind>? value)
    {
        OnPropertyChanged(nameof(IsStatement));
        OnPropertyChanged(nameof(HasPdf));
        Rows.Clear();
        SummaryText = "";
    }

    partial void OnPresetChanged(Option<DatePreset>? value) => OnPropertyChanged(nameof(NeedsDates));

    private DateRange Range()
    {
        var preset = Preset?.Value ?? DatePreset.ThisMonth;
        return preset switch
        {
            DatePreset.SpecificDay => DateRanges.For(_clock, preset, ParseShamsi(FromDate, "روز")),
            DatePreset.Custom => DateRanges.For(_clock, preset, ParseShamsi(FromDate, "از تاریخ"), ParseShamsi(ToDate, "تا تاریخ")),
            _ => DateRanges.For(_clock, preset)
        };
    }

    [RelayCommand]
    private Task RunReport() => RunAsync(RunReportCore);

    private async Task RunReportCore()
    {
        _statement = null;
        _allCustomers = null;
        _openCases = null;
        Rows.Clear();
        SummaryText = "";
        var range = Range();
        RangeText = range.Describe();
        switch (Kind?.Value ?? ReportKind.CustomerStatement)
        {
            case ReportKind.CustomerStatement:
                if (Customer is null) throw new DomainException("input_customer", "مشتری را انتخاب کنید.");
                _statement = await _reports.CustomerStatementAsync(Customer.Id, range, Currency?.Value, OpType?.Value,
                    ExcludeReversed ? StatusFilter.ExcludeReversed : StatusFilter.AllPosted);
                Headers = new[] { "شماره سند", "تاریخ", "نوع", "شرح", "ارز", "بدهکار", "بستانکار", "مانده" };
                foreach (var s in _statement.Sections)
                {
                    Rows.Add(new ReportRow("", "", "", "مانده از قبل", s.Currency.ToString(), "", "", Display.Signed(s.Opening, s.Currency)));
                    foreach (var r in s.Rows)
                        Rows.Add(new ReportRow(r.EntryNumber, r.ShamsiDate, LedgerText.Persian(r.OperationType) + (r.Status == JournalStatus.Reversed ? " (برگشت‌خورده)" : ""),
                            r.Description, r.Currency.ToString(), Amt(r.Debit, r.Currency), Amt(r.Credit, r.Currency), Display.Signed(r.Balance, r.Currency)));
                }
                SummaryText = string.Join("\n", _statement.Sections.Select(s =>
                    $"{s.Currency}: مانده قبلی {Display.Signed(s.Opening, s.Currency)} | جمع بدهکار {Amt(s.TotalDebit, s.Currency)} | جمع بستانکار {Amt(s.TotalCredit, s.Currency)} | مانده نهایی {Display.Signed(s.Closing, s.Currency)} ({StatementCurrencySection.Side(s.Closing)})"));
                break;

            case ReportKind.AllCustomers:
                _allCustomers = await _reports.AllCustomersAsync(range, Currency?.Value);
                Headers = new[] { "کد", "نام مشتری", "ارز", "تعداد تراکنش", "جمع دریافتی", "جمع پرداختی", "مانده", "آخرین تراکنش" };
                foreach (var r in _allCustomers.Rows)
                    Rows.Add(new ReportRow(r.Code, r.FullName, r.Currency.ToString(), r.TransactionCount.ToString(), Amt(r.TotalReceived, r.Currency),
                        Amt(r.TotalPaid, r.Currency), Display.Signed(r.Balance, r.Currency), r.LastTransactionUtc is { } l ? Display.Date(l) : "—"));
                SummaryText = "جمع هر ارز (ارزها با هم جمع نمی‌شوند):\n" + string.Join("\n", _allCustomers.Totals.Select(t =>
                    $"{t.Currency}: دریافتی {Amt(t.Received, t.Currency)} | پرداختی {Amt(t.Paid, t.Currency)} | مانده {Display.Signed(t.Balance, t.Currency)}"));
                break;

            case ReportKind.OpenCases:
                _openCases = await _reports.OpenCasesAsync();
                RangeText = "همه پرونده‌های باز (مستقل از بازه زمانی)";
                Headers = new[] { "شماره پرونده", "مشتری", "وضعیت", "هدف", "تعهد صرافی", "تحویل به نماینده", "دریافت در چین", "مانده / مشکلات" };
                foreach (var r in _openCases)
                    Rows.Add(new ReportRow(r.CaseNumber, r.CustomerName, TransferStatusText.Persian(r.Status), Amt(r.Target, r.Currency),
                        Amt(r.Committed, r.Currency), Amt(r.DeliveredToAgents, r.Currency), Amt(r.ReceivedInChina, r.Currency),
                        Amt(r.Unsettled, r.Currency) + (r.Issues.Count > 0 ? " — " + string.Join("؛ ", r.Issues) : "")));
                SummaryText = $"{_openCases.Count} پرونده باز.";
                break;

            default:
                var category = Kind!.Value switch
                {
                    ReportKind.Exchangers => AccountCategory.Exchanger,
                    ReportKind.Agents => AccountCategory.AgentFunds,
                    ReportKind.Recipients => AccountCategory.Recipient,
                    _ => AccountCategory.Bank
                };
                var party = await _reports.PartyBalancesAsync(category, range);
                Headers = new[] { "حساب", "ارز", "گردش بدهکار دوره", "گردش بستانکار دوره", "مانده پایان دوره", "", "", "" };
                foreach (var r in party.Rows.Where(r => Currency?.Value is null || r.Currency == Currency.Value))
                    Rows.Add(new ReportRow(r.AccountName, r.Currency.ToString(), Amt(r.PeriodDebit, r.Currency), Amt(r.PeriodCredit, r.Currency),
                        Display.Signed(r.Balance, r.Currency), "", "", ""));
                break;
        }
    }

    private static string Amt(decimal v, CurrencyCode c) => AmountFormatter.Format(v, c);

    [RelayCommand]
    private Task ExportPdf() => RunAsync(async () =>
    {
        _user.Demand(Permission.ExportReports);
        var header = await _settings.GetCompanyAsync();
        byte[] bytes;
        string name;
        if (_statement is not null) { bytes = _pdf.RenderStatement(_statement, header); name = $"صورتحساب {_statement.AccountName}"; }
        else if (_allCustomers is not null) { bytes = _pdf.RenderAllCustomers(_allCustomers, header); name = "مانده همه مشتریان"; }
        else throw new DomainException("report_none", "ابتدا گزارش صورتحساب یا مانده همه مشتریان را اجرا کنید.");
        await Save(bytes, name, "pdf", "PDF|*.pdf");
    });

    [RelayCommand]
    private Task ExportExcel() => RunAsync(async () =>
    {
        _user.Demand(Permission.ExportReports);
        byte[] bytes;
        string name;
        if (_statement is not null) { bytes = _excel.ExportStatement(_statement); name = $"صورتحساب {_statement.AccountName}"; }
        else if (_allCustomers is not null) { bytes = _excel.ExportAllCustomers(_allCustomers); name = "مانده همه مشتریان"; }
        else if (_openCases is not null) { bytes = _excel.ExportOpenCases(_openCases); name = "پرونده‌های باز"; }
        else throw new DomainException("report_none", "خروجی Excel برای صورتحساب، مانده مشتریان و پرونده‌های باز موجود است؛ ابتدا گزارش را اجرا کنید.");
        await Save(bytes, name, "xlsx", "Excel|*.xlsx");
    });

    private async Task Save(byte[] bytes, string name, string ext, string filter)
    {
        var paths = await _settings.GetPathsAsync();
        var root = string.IsNullOrWhiteSpace(paths.ReportsRoot) ? _paths.DefaultReportsRoot : paths.ReportsRoot!;
        Directory.CreateDirectory(root);
        var suggested = ReceiptDocumentService.SanitizeFileName($"{name} {ShamsiDate.Format(_clock.LocalToday()).Replace('/', '-')}");
        var target = _dialogs.SaveFile(System.IO.Path.Combine(root, suggested + "." + ext), filter);
        if (target is null) return;
        await File.WriteAllBytesAsync(target, bytes);
        StatusMessage = "ذخیره شد: " + target;
        try { _dialogs.OpenWithShell(target); } catch { /* no associated viewer: the file is still saved */ }
    }
}
