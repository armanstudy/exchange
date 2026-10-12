using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Customers;
using LenjExchange.Application.Documents;
using LenjExchange.Application.MasterData;
using LenjExchange.Application.Rates;
using LenjExchange.Application.Settings;
using LenjExchange.Application.Transfers;
using LenjExchange.Desktop.Services;
using LenjExchange.Domain.Common;
using LenjExchange.Domain.Documents;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Security;
using LenjExchange.Domain.Shamsi;
using LenjExchange.Domain.Transfers;

namespace LenjExchange.Desktop.ViewModels;

public sealed partial class DepositRow : ObservableObject
{
    [ObservableProperty] private string _bankName = "";
    [ObservableProperty] private string _date = "";
    [ObservableProperty] private string _amount = "";
    [ObservableProperty] private CurrencyCode _currency = CurrencyCode.IRR;
    [ObservableProperty] private string _trackingNo = "";
    [ObservableProperty] private string _depositor = "";
}

public sealed record PendingAttachment(string Path, AttachmentDocType DocType)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// Customer receipt entry: amount, optional conversion, any number of bank deposits, attachments, then
/// registration (number allocated only when finalized) and the customer-facing PDF.
/// </summary>
public sealed partial class NewReceiptViewModel : ViewModelBase
{
    private readonly CustomerService _customers;
    private readonly MasterDataService _master;
    private readonly ReceiptService _receipts;
    private readonly RateService _rates;
    private readonly AttachmentService _attachments;
    private readonly ReceiptDocumentService _documents;
    private readonly SettingsService _settings;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IDialogService _dialogs;
    private Guid? _preselectedCustomer;

    [ObservableProperty] private IReadOnlyList<Lookup> _customerOptions = Array.Empty<Lookup>();
    [ObservableProperty] private Lookup? _customer;
    [ObservableProperty] private IReadOnlyList<Lookup> _recipientOptions = Array.Empty<Lookup>();
    [ObservableProperty] private Lookup? _recipient;
    [ObservableProperty] private IReadOnlyList<string> _bankNames = Array.Empty<string>();

    [ObservableProperty] private string _date = "";
    [ObservableProperty] private string _amount = "";
    [ObservableProperty] private CurrencyCode _currency = CurrencyCode.IRR;
    [ObservableProperty] private CurrencyCode _targetCurrency = CurrencyCode.USD;
    [ObservableProperty] private string _rate = "";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string _conversionPreview = "";
    [ObservableProperty] private string? _warning;

    [ObservableProperty] private PaperSize _paper = PaperSize.A5;
    [ObservableProperty] private Guid? _registeredId;
    [ObservableProperty] private string? _registeredNumber;
    [ObservableProperty] private string? _lastPdfPath;

    public ObservableCollection<DepositRow> Deposits { get; } = new();
    public ObservableCollection<PendingAttachment> Attachments { get; } = new();
    public IReadOnlyList<PaperSize> PaperSizes { get; } = new[] { PaperSize.A5, PaperSize.A4 };
    public IReadOnlyList<Option<AttachmentDocType>> DocTypes { get; } = AttachmentsViewModel.DocTypeOptions;

    public NewReceiptViewModel(CustomerService customers, MasterDataService master, ReceiptService receipts, RateService rates,
        AttachmentService attachments, ReceiptDocumentService documents, SettingsService settings, ICurrentUser user, IClock clock,
        IDialogService dialogs)
    {
        _customers = customers;
        _master = master;
        _receipts = receipts;
        _rates = rates;
        _attachments = attachments;
        _documents = documents;
        _settings = settings;
        _user = user;
        _clock = clock;
        _dialogs = dialogs;
    }

    public override string Title => "ثبت رسید دریافت وجه";

    /// <summary>Rate is entered as: 1 {TargetCurrency} = Rate {RateQuoteCurrency}. Rial amounts are quoted in Toman.</summary>
    public CurrencyCode RateQuoteCurrency => Currency == CurrencyCode.IRR ? CurrencyCode.IRT : Currency;
    public bool NeedsRate => !CurrencyMath.AreFixedEquivalent(Currency, TargetCurrency);
    public string RateLabel => $"نرخ تبدیل: هر ۱ {TargetCurrency} چند {RateQuoteCurrency}؟";
    public bool IsRegistered => RegisteredId is not null;
    public bool CanPrint => _user.Has(Permission.PrintReceipts);

    public void PreselectCustomer(Guid id) => _preselectedCustomer = id;

    public override Task LoadAsync() => RunAsync(async () =>
    {
        CustomerOptions = (await _customers.SearchAsync(null)).Select(c => new Lookup(c.Id, $"{c.FullName} ({c.Code})")).ToList();
        RecipientOptions = (await _master.RecipientsAsync()).Where(r => r.IsActive).Select(r => new Lookup(r.Id, $"{r.Name} - {r.Country}")).ToList();
        BankNames = (await _master.BanksAsync()).Where(b => b.IsActive).Select(b => b.Name).ToList();
        Paper = (await _settings.GetAccountingAsync()).DefaultReceiptPaper;
        if (_preselectedCustomer is { } id) Customer = CustomerOptions.FirstOrDefault(c => c.Id == id);
        Reset();
    });

    private void Reset()
    {
        Date = ShamsiDate.Format(_clock.LocalToday());
        Amount = Rate = Notes = "";
        Currency = CurrencyCode.IRR;
        TargetCurrency = CurrencyCode.USD;
        Deposits.Clear();
        Attachments.Clear();
        RegisteredId = null;
        RegisteredNumber = null;
        LastPdfPath = null;
        Warning = null;
    }

    partial void OnCurrencyChanged(CurrencyCode value)
    {
        foreach (var d in Deposits) d.Currency = value;
        RateFieldsChanged();
    }

    partial void OnTargetCurrencyChanged(CurrencyCode value) => RateFieldsChanged();
    partial void OnAmountChanged(string value) => UpdatePreview();
    partial void OnRateChanged(string value) => UpdatePreview();
    partial void OnRegisteredIdChanged(Guid? value) => OnPropertyChanged(nameof(IsRegistered));

    private void RateFieldsChanged()
    {
        OnPropertyChanged(nameof(RateQuoteCurrency));
        OnPropertyChanged(nameof(NeedsRate));
        OnPropertyChanged(nameof(RateLabel));
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        try
        {
            if (!DigitNormalizerTry(Amount, out var amount)) { ConversionPreview = ""; return; }
            var money = new Money(amount, Currency);
            if (!NeedsRate)
            {
                ConversionPreview = "معادل: " + CurrencyMath.ConvertFixed(money, TargetCurrency).Format();
                return;
            }
            if (!DigitNormalizerTry(Rate, out var rate) || rate <= 0) { ConversionPreview = ""; return; }
            ConversionPreview = "معادل: " + BuildRate(rate).Convert(money, TargetCurrency).Format();
        }
        catch (DomainException ex)
        {
            ConversionPreview = ex.Message;
        }
    }

    private static bool DigitNormalizerTry(string? text, out decimal value) =>
        Domain.Text.DigitNormalizer.TryParseDecimal(text, out value);

    private ExchangeRate BuildRate(decimal value) => new(TargetCurrency, RateQuoteCurrency, value);

    [RelayCommand]
    private Task LoadLatestRate() => RunAsync(async () =>
    {
        var r = await _rates.LatestAsync(TargetCurrency, RateQuoteCurrency);
        if (r is null) { StatusMessage = "نرخی برای این جفت‌ارز ثبت نشده است."; return; }
        Rate = r.Rate.ToString("0.########");
        StatusMessage = "آخرین نرخ ثبت‌شده: " + r.Describe();
    });

    [RelayCommand]
    private void AddDeposit() =>
        Deposits.Add(new DepositRow { Currency = Currency, Date = Date, BankName = BankNames.FirstOrDefault() ?? "" });

    [RelayCommand]
    private void RemoveDeposit(DepositRow? row)
    {
        if (row is not null) Deposits.Remove(row);
    }

    [RelayCommand]
    private void AddAttachment(Option<AttachmentDocType>? type)
    {
        var path = _dialogs.OpenFile("تصویر یا PDF|*.jpg;*.jpeg;*.png;*.pdf", "انتخاب فیش/مدرک");
        if (path is null) return;
        if (!Attachment.IsAllowedExtension(path)) { ErrorMessage = "فقط فایل‌های JPG، PNG و PDF مجاز است."; return; }
        Attachments.Add(new PendingAttachment(path, type?.Value ?? AttachmentDocType.BankSlip));
    }

    [RelayCommand]
    private void RemoveAttachment(PendingAttachment? a)
    {
        if (a is not null) Attachments.Remove(a);
    }

    private ReceiptInput BuildInput()
    {
        if (Customer is null) throw new DomainException("input_customer", "مشتری را انتخاب کنید.");
        var date = _clock.ToUtc(ParseShamsi(Date, "تاریخ رسید").Add(_clock.ToLocal(_clock.UtcNow).TimeOfDay));
        var amount = ParseAmount(Amount, "مبلغ");
        ExchangeRate? rate = NeedsRate ? BuildRate(ParseAmount(Rate, "نرخ تبدیل")) : null;
        var deposits = Deposits.Select((d, i) => new DepositInput(
            string.IsNullOrWhiteSpace(d.BankName) ? throw new DomainException("input_bank", $"نام بانک واریز ردیف {i + 1} خالی است.") : d.BankName.Trim(),
            _clock.ToUtc(ParseShamsi(d.Date, $"تاریخ واریز ردیف {i + 1}")),
            ParseAmount(d.Amount, $"مبلغ واریز ردیف {i + 1}"), d.Currency,
            string.IsNullOrWhiteSpace(d.TrackingNo) ? null : Domain.Text.DigitNormalizer.Normalize(d.TrackingNo.Trim()),
            string.IsNullOrWhiteSpace(d.Depositor) ? null : d.Depositor.Trim())).ToList();
        return new ReceiptInput(Customer.Id, date, amount, Currency, TargetCurrency, rate, Recipient?.Id, deposits,
            string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim());
    }

    [RelayCommand]
    private Task Check() => RunAsync(async () =>
    {
        var input = BuildInput();
        var result = await _receipts.CheckAsync(input.Amount, input.Currency, input.Deposits!);
        Warning = Describe(result);
        if (Warning is null) StatusMessage = "بررسی انجام شد؛ مغایرت یا واریز تکراری یافت نشد.";
    });

    private static string? Describe(ReceiptCheckResult r)
    {
        var parts = new List<string>();
        if (r.HasMismatch) parts.Add(r.MismatchMessage!);
        parts.AddRange(r.Duplicates.Select(d => $"ردیف {d.InputIndex + 1}: {d.Message}"));
        return parts.Count == 0 ? null : string.Join("\n", parts);
    }

    [RelayCommand]
    private async Task Register()
    {
        if (IsRegistered) { ErrorMessage = "این رسید قبلاً ثبت شده است؛ برای رسید بعدی «رسید جدید» را بزنید."; return; }
        ReceiptInput? input = null;
        ReceiptCheckResult? check = null;
        if (!await RunAsync(async () =>
            {
                input = BuildInput();
                check = await _receipts.CheckAsync(input.Amount, input.Currency, input.Deposits!);
            })) return;

        Warning = Describe(check!);
        var acceptMismatch = false;
        var acceptDuplicates = false;
        if (check!.HasMismatch)
        {
            if (!_dialogs.Confirm(check.MismatchMessage + "\n\nاختلاف به حساب «مغایرت» منتقل و پرونده دارای مغایرت علامت‌گذاری می‌شود. ادامه می‌دهید؟")) return;
            acceptMismatch = true;
        }
        if (check.Duplicates.Count > 0)
        {
            var text = string.Join("\n", check.Duplicates.Select(d => "• " + d.Message));
            if (!_dialogs.Confirm("احتمال واریز تکراری:\n" + text + "\n\nآیا مطمئن هستید که این واریزها تکراری نیستند؟")) return;
            acceptDuplicates = true;
        }

        CustomerReceipt? receipt = null;
        if (!await RunAsync(async () => receipt = await _receipts.RegisterAsync(input!, acceptMismatch, acceptDuplicates))) return;
        RegisteredId = receipt!.Id;
        RegisteredNumber = receipt.ReceiptNumber;
        var message = $"رسید شماره {receipt.ReceiptNumber} ثبت شد.";
        if (Attachments.Count > 0 && !await RunAsync(async () =>
            {
                foreach (var a in Attachments)
                    await _attachments.AddAsync(new AttachmentInput(a.Path, a.DocType, receipt.CustomerId, receipt.TransferCaseId,
                        ReceiptService.SourceType, receipt.Id), allowDuplicate: true);
            }))
        {
            ErrorMessage = message + " اما ذخیره پیوست‌ها ناموفق بود: " + ErrorMessage;
            return;
        }
        StatusMessage = message;
    }

    [RelayCommand]
    private Task GeneratePdf() => CreatePdf(print: false);

    [RelayCommand]
    private Task Print() => CreatePdf(print: true);

    private async Task CreatePdf(bool print)
    {
        if (RegisteredId is not { } id) { ErrorMessage = "ابتدا رسید را ثبت کنید."; return; }
        string? path = null;
        if (!await RunAsync(async () => path = await _documents.GeneratePdfAsync(id, Paper, print))) return;
        LastPdfPath = path;
        StatusMessage = "فایل PDF ساخته شد: " + path;
        try
        {
            if (print) _dialogs.PrintWithShell(path!);
            else _dialogs.OpenWithShell(path!);
        }
        catch (Exception ex)
        {
            ErrorMessage = "باز کردن/چاپ فایل ممکن نشد (یک نمایشگر PDF نصب کنید): " + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (LastPdfPath is not null) _dialogs.OpenWithShell(System.IO.Path.GetDirectoryName(LastPdfPath)!);
    }

    [RelayCommand]
    private void NewReceipt() => Reset();
}
