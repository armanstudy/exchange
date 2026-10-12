using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Documents;
using LenjExchange.Desktop.Services;
using LenjExchange.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace LenjExchange.Desktop.ViewModels;

public sealed record AttachmentRow(Guid Id, string FileName, string Type, string Customer, string CaseNumber, string Size, string Date,
    string CreatedBy, string Sha256, string FullPath);

public sealed partial class AttachmentsViewModel : ViewModelBase
{
    public static IReadOnlyList<Option<AttachmentDocType>> DocTypeOptions { get; } = new[]
    {
        new Option<AttachmentDocType>(AttachmentDocType.BankSlip, "فیش بانکی"),
        new Option<AttachmentDocType>(AttachmentDocType.ReceiptScan, "تصویر رسید"),
        new Option<AttachmentDocType>(AttachmentDocType.ExchangerConfirmation, "تأییدیه صرافی"),
        new Option<AttachmentDocType>(AttachmentDocType.AgentDeliveryProof, "مدرک تحویل به نماینده"),
        new Option<AttachmentDocType>(AttachmentDocType.ChinaReceiptProof, "مدرک دریافت در چین"),
        new Option<AttachmentDocType>(AttachmentDocType.IdentityDocument, "مدرک هویتی"),
        new Option<AttachmentDocType>(AttachmentDocType.Other, "سایر"),
    };

    private readonly AttachmentService _attachments;
    private readonly IAppDbContext _db;
    private readonly IDialogService _dialogs;

    [ObservableProperty] private IReadOnlyList<Lookup> _customers = Array.Empty<Lookup>();
    [ObservableProperty] private Lookup? _customerFilter;
    [ObservableProperty] private Option<AttachmentDocType?>? _typeFilter;
    [ObservableProperty] private Option<AttachmentDocType>? _newType;
    [ObservableProperty] private AttachmentRow? _selected;

    public ObservableCollection<AttachmentRow> Items { get; } = new();
    public IReadOnlyList<Option<AttachmentDocType?>> TypeFilters { get; } =
        new[] { new Option<AttachmentDocType?>(null, "همه انواع") }
            .Concat(DocTypeOptions.Select(o => new Option<AttachmentDocType?>(o.Value, o.Text))).ToList();
    public IReadOnlyList<Option<AttachmentDocType>> DocTypes => DocTypeOptions;

    public AttachmentsViewModel(AttachmentService attachments, IAppDbContext db, IDialogService dialogs)
    {
        _attachments = attachments;
        _db = db;
        _dialogs = dialogs;
        _typeFilter = TypeFilters[0];
        _newType = DocTypeOptions[0];
    }

    public override string Title => "بایگانی پیوست‌ها";

    public override Task LoadAsync() => RunAsync(async () =>
    {
        var customers = await _db.Customers.AsNoTracking().OrderBy(c => c.FullName).ToListAsync();
        if (Customers.Count == 0)
            Customers = customers.Select(c => new Lookup(c.Id, $"{c.FullName} ({c.Code})")).ToList();
        var names = customers.ToDictionary(c => c.Id, c => c.FullName);
        var cases = await _db.TransferCases.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.CaseNumber);
        var list = await _attachments.SearchAsync(CustomerFilter?.Id, null, TypeFilter?.Value);
        Items.Clear();
        foreach (var a in list)
            Items.Add(new AttachmentRow(a.Id, a.OriginalFileName, DocTypeOptions.FirstOrDefault(o => o.Value == a.DocType)?.Text ?? a.DocType.ToString(),
                a.CustomerId is { } c ? names.GetValueOrDefault(c, "") : "", a.TransferCaseId is { } k ? cases.GetValueOrDefault(k, "") : "",
                $"{a.SizeBytes / 1024.0:0.#} KB", Display.DateTime(a.CreatedAtUtc), a.CreatedBy ?? "", a.Sha256[..12], _attachments.ResolvePath(a)));
    });

    partial void OnCustomerFilterChanged(Lookup? value) => _ = LoadAsync();
    partial void OnTypeFilterChanged(Option<AttachmentDocType?>? value) => _ = LoadAsync();

    [RelayCommand]
    private void ClearFilter() => CustomerFilter = null;

    [RelayCommand]
    private async Task Add()
    {
        var path = _dialogs.OpenFile("تصویر یا PDF|*.jpg;*.jpeg;*.png;*.pdf", "افزودن پیوست");
        if (path is null) return;
        AttachmentResult? result = null;
        var input = new AttachmentInput(path, NewType?.Value ?? AttachmentDocType.Other, CustomerFilter?.Id);
        if (!await RunAsync(async () => result = await _attachments.AddAsync(input))) return;
        if (result!.IsDuplicate)
        {
            if (!_dialogs.Confirm($"این فایل قبلاً با نام «{result.ExistingDuplicate?.OriginalFileName}» بایگانی شده است. دوباره ذخیره شود؟")) return;
            if (!await RunAsync(() => _attachments.AddAsync(input, allowDuplicate: true))) return;
        }
        await LoadAsync();
        StatusMessage = "پیوست بایگانی شد.";
    }

    [RelayCommand]
    private void Open()
    {
        if (Selected is not { } s) return;
        try { _dialogs.OpenWithShell(s.FullPath); }
        catch (Exception ex) { ErrorMessage = "باز کردن فایل ممکن نشد: " + ex.Message; }
    }

    [RelayCommand]
    private Task Verify() => RunAsync(async () =>
    {
        var problems = await _attachments.VerifyAsync();
        if (problems.Count == 0) { StatusMessage = "همه فایل‌های پیوست سالم هستند (هش SHA-256 بررسی شد)."; return; }
        _dialogs.Error(string.Join("\n", problems.Take(50).Select(p => $"{p.Attachment.OriginalFileName}: {p.Problem}")), "مشکل در فایل‌های پیوست");
    });
}
