using LenjExchange.Domain.Common;
using LenjExchange.Domain.Monetary;

namespace LenjExchange.Domain.Transfers;

/// <summary>Money received from a customer in Iran (رسید دریافت وجه).</summary>
public class CustomerReceipt : AuditableEntity
{
    private readonly List<BankDeposit> _deposits = new();

    /// <summary>Assigned once at finalization and immutable afterwards.</summary>
    public string? ReceiptNumber { get; private set; }
    public Guid TransferCaseId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid? FinalRecipientId { get; private set; }
    public DateTime DateUtc { get; private set; }
    public decimal Amount { get; private set; }
    public CurrencyCode Currency { get; private set; }
    /// <summary>Rate snapshot used for this receipt (null when no conversion).</summary>
    public ExchangeRate? Rate { get; private set; }
    public CurrencyCode TargetCurrency { get; private set; }
    public decimal TargetAmount { get; private set; }
    public DocumentStatus Status { get; private set; } = DocumentStatus.Draft;
    /// <summary>True when the deposits did not reconcile and the user explicitly accepted the mismatch.</summary>
    public bool MismatchAccepted { get; private set; }
    public string? Notes { get; private set; }
    public DateTime? FinalizedAtUtc { get; private set; }
    public string? FinalizedBy { get; private set; }
    public Guid? JournalEntryId { get; private set; }

    public IReadOnlyList<BankDeposit> Deposits => _deposits;
    public Money Total => new(Amount, Currency);
    public Money TargetTotal => new(TargetAmount, TargetCurrency);

    protected CustomerReceipt() { }

    public CustomerReceipt(Guid transferCaseId, Guid customerId, DateTime dateUtc, Money amount,
        CurrencyCode targetCurrency, ExchangeRate? rate, Guid? finalRecipientId, string? notes)
    {
        TransferCaseId = transferCaseId;
        CustomerId = customerId;
        DateUtc = Guard.Utc(dateUtc);
        Notes = notes;
        FinalRecipientId = finalRecipientId;
        SetAmount(amount, targetCurrency, rate);
    }

    public void SetAmount(Money amount, CurrencyCode targetCurrency, ExchangeRate? rate)
    {
        EnsureDraft();
        Guard.Positive(amount.Amount, "مبلغ رسید");
        Amount = Currencies.Round(amount.Amount, amount.Currency);
        Currency = amount.Currency;
        TargetCurrency = targetCurrency;
        if (CurrencyMath.AreFixedEquivalent(amount.Currency, targetCurrency))
        {
            Rate = null;
            TargetAmount = CurrencyMath.ConvertFixed(Total, targetCurrency).Amount;
        }
        else
        {
            // Copy: each document owns its own rate snapshot.
            Rate = (rate ?? throw new DomainException("rate_required",
                $"برای تبدیل {amount.Currency} به {targetCurrency} وارد کردن نرخ الزامی است.")) with { };
            TargetAmount = Rate.Convert(Total, targetCurrency).Amount;
        }
    }

    public BankDeposit AddDeposit(string bankName, Guid? bankId, DateTime dateUtc, Money amount, string? trackingNo,
        string? depositor, string? notes)
    {
        EnsureDraft();
        var d = new BankDeposit(Id, bankName, bankId, dateUtc, amount, trackingNo, depositor, notes);
        _deposits.Add(d);
        return d;
    }

    public void RemoveDeposit(Guid depositId)
    {
        EnsureDraft();
        _deposits.RemoveAll(d => d.Id == depositId);
    }

    /// <summary>Deposit total normalized into the receipt currency (IRR deposits count at 1/10 for IRT receipts).</summary>
    public Money DepositsTotal()
    {
        var total = Money.Zero(Currency);
        foreach (var d in _deposits)
        {
            if (!CurrencyMath.AreFixedEquivalent(d.Currency, Currency))
                throw new DomainException("deposit_currency", $"ارز واریزی ({d.Currency}) با ارز رسید ({Currency}) سازگار نیست.");
            total += CurrencyMath.ConvertFixed(d.Money, Currency);
        }
        return total;
    }

    /// <summary>Positive = deposits exceed receipt; negative = deposits short. Zero when there are no deposits.</summary>
    public Money DepositDifference() => _deposits.Count == 0 ? Money.Zero(Currency) : DepositsTotal() - Total;

    public bool HasDepositMismatch => _deposits.Count > 0 && !DepositDifference().IsZero;

    public void Finalize(string receiptNumber, string user, DateTime utcNow, bool acceptMismatch)
    {
        EnsureDraft();
        if (ReceiptNumber is not null)
            throw new DomainException("receipt_number_immutable", "شمارهٔ رسید قبلاً تخصیص یافته و قابل تغییر نیست.");
        if (HasDepositMismatch && !acceptMismatch)
            throw new DomainException("deposit_mismatch",
                $"جمع واریزها ({DepositsTotal()}) با مبلغ رسید ({Total}) برابر نیست.");
        ReceiptNumber = Guard.NotBlank(receiptNumber, "شماره رسید");
        MismatchAccepted = HasDepositMismatch;
        FinalizedBy = user;
        FinalizedAtUtc = Guard.Utc(utcNow);
        Status = DocumentStatus.Posted;
    }

    public void AttachJournal(Guid journalEntryId)
    {
        if (JournalEntryId is not null) throw new DomainException("journal_linked", "سند حسابداری قبلاً پیوند شده است.");
        JournalEntryId = journalEntryId;
    }

    public void MarkReversed()
    {
        if (Status != DocumentStatus.Posted) throw new DomainException("reverse_status", "فقط رسید قطعی قابل ابطال است.");
        Status = DocumentStatus.Reversed;
    }

    private void EnsureDraft()
    {
        if (Status != DocumentStatus.Draft)
            throw new DomainException("receipt_immutable", "رسید قطعی‌شده قابل ویرایش نیست.");
    }
}

public class BankDeposit : Entity
{
    public Guid CustomerReceiptId { get; private set; }
    public Guid? BankId { get; private set; }
    public string BankName { get; private set; } = "";
    public DateTime DateUtc { get; private set; }
    public decimal Amount { get; private set; }
    public CurrencyCode Currency { get; private set; }
    /// <summary>Bank-issued tracking/reference number (کد رهگیری بانکی) — distinct from the receipt number.</summary>
    public string? BankTrackingNo { get; private set; }
    public string? Depositor { get; private set; }
    public string? Notes { get; private set; }

    public Money Money => new(Amount, Currency);

    protected BankDeposit() { }

    internal BankDeposit(Guid receiptId, string bankName, Guid? bankId, DateTime dateUtc, Money amount, string? trackingNo,
        string? depositor, string? notes)
    {
        CustomerReceiptId = receiptId;
        BankName = Guard.NotBlank(bankName, "نام بانک");
        BankId = bankId;
        DateUtc = Guard.Utc(dateUtc);
        Guard.Positive(amount.Amount, "مبلغ واریز");
        Amount = Currencies.Round(amount.Amount, amount.Currency);
        Currency = amount.Currency;
        BankTrackingNo = string.IsNullOrWhiteSpace(trackingNo) ? null : trackingNo.Trim();
        Depositor = depositor;
        Notes = notes;
    }
}
