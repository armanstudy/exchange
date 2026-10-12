using LenjExchange.Domain.Common;
using LenjExchange.Domain.Monetary;

namespace LenjExchange.Domain.Transfers;

/// <summary>Turkish exchanger's commitment to deliver USD in exchange for Iranian funds paid to it.</summary>
public class ExchangerCommitment : AuditableEntity
{
    public Guid TransferCaseId { get; private set; }
    public Guid ExchangerId { get; private set; }
    public DateTime DateUtc { get; private set; }
    public decimal PaidAmount { get; private set; }
    public CurrencyCode PaidCurrency { get; private set; }
    /// <summary>Bank account the Iranian funds were paid from (null → Iran cash).</summary>
    public Guid? FundingBankId { get; private set; }
    public ExchangeRate? Rate { get; private set; }
    public decimal CommittedAmount { get; private set; }
    public CurrencyCode CommittedCurrency { get; private set; }
    public bool ReadyForDelivery { get; private set; }
    public DocumentStatus Status { get; private set; } = DocumentStatus.Draft;
    public string? Notes { get; private set; }
    public Guid? JournalEntryId { get; private set; }

    public Money Paid => new(PaidAmount, PaidCurrency);
    public Money Committed => new(CommittedAmount, CommittedCurrency);

    protected ExchangerCommitment() { }

    public ExchangerCommitment(Guid caseId, Guid exchangerId, DateTime dateUtc, Money paid, ExchangeRate? rate,
        CurrencyCode committedCurrency, Guid? fundingBankId, string? notes)
    {
        TransferCaseId = caseId;
        ExchangerId = exchangerId;
        DateUtc = Guard.Utc(dateUtc);
        Guard.Positive(paid.Amount, "مبلغ پرداختی به صرافی");
        PaidAmount = Currencies.Round(paid.Amount, paid.Currency);
        PaidCurrency = paid.Currency;
        CommittedCurrency = committedCurrency;
        FundingBankId = fundingBankId;
        Notes = notes;
        if (CurrencyMath.AreFixedEquivalent(paid.Currency, committedCurrency))
            CommittedAmount = CurrencyMath.ConvertFixed(Paid, committedCurrency).Amount;
        else
        {
            Rate = rate ?? throw new DomainException("rate_required", "نرخ تعهد صرافی الزامی است.");
            CommittedAmount = rate.Convert(Paid, committedCurrency).Amount;
        }
    }

    public void Post(Guid journalEntryId)
    {
        if (Status != DocumentStatus.Draft) throw new DomainException("commitment_status", "تعهد قبلاً ثبت شده است.");
        Status = DocumentStatus.Posted;
        JournalEntryId = journalEntryId;
    }

    public void MarkReadyForDelivery(bool ready = true)
    {
        if (Status != DocumentStatus.Posted) throw new DomainException("commitment_status", "تعهد ثبت‌نشده است.");
        ReadyForDelivery = ready;
    }

    public void MarkReversed()
    {
        if (Status != DocumentStatus.Posted) throw new DomainException("reverse_status", "فقط تعهد قطعی قابل ابطال است.");
        Status = DocumentStatus.Reversed;
    }
}

/// <summary>Exchanger → Turkey agent USD delivery. Each approved delivery is a (partial) settlement of a commitment.</summary>
public class AgentDelivery : AuditableEntity
{
    public Guid TransferCaseId { get; private set; }
    public Guid ExchangerCommitmentId { get; private set; }
    public Guid ExchangerId { get; private set; }
    public Guid AgentId { get; private set; }
    public DateTime DateUtc { get; private set; }
    public decimal Amount { get; private set; }
    public CurrencyCode Currency { get; private set; } = CurrencyCode.USD;
    /// <summary>Internal auto-generated tracking number.</summary>
    public string TrackingNo { get; private set; } = "";
    public string? ExternalTrackingNo { get; private set; }
    public ApprovalStatus Approval { get; private set; } = ApprovalStatus.Pending;
    public string? ApprovedBy { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public bool IsReversed { get; private set; }
    public string? Notes { get; private set; }
    public Guid? JournalEntryId { get; private set; }

    public Money Money => new(Amount, Currency);
    public bool Counts => Approval == ApprovalStatus.Approved && !IsReversed;

    protected AgentDelivery() { }

    public AgentDelivery(Guid caseId, Guid commitmentId, Guid exchangerId, Guid agentId, DateTime dateUtc, Money amount,
        string trackingNo, string? externalTrackingNo, string? notes)
    {
        if (amount.Currency != Parties.Agent.HeldCurrency)
            throw new DomainException("agent_currency", "نماینده ترکیه فقط دلار (USD) دریافت می‌کند.");
        TransferCaseId = caseId;
        ExchangerCommitmentId = commitmentId;
        ExchangerId = exchangerId;
        AgentId = agentId;
        DateUtc = Guard.Utc(dateUtc);
        Amount = Currencies.Round(Guard.Positive(amount.Amount, "مبلغ تحویل"), amount.Currency);
        Currency = amount.Currency;
        TrackingNo = Guard.NotBlank(trackingNo, "شماره پیگیری");
        ExternalTrackingNo = externalTrackingNo;
        Notes = notes;
    }

    public void Approve(string user, DateTime utcNow, Guid journalEntryId)
    {
        if (Approval != ApprovalStatus.Pending) throw new DomainException("approval_status", "این تحویل قبلاً بررسی شده است.");
        Approval = ApprovalStatus.Approved;
        ApprovedBy = user;
        ApprovedAtUtc = Guard.Utc(utcNow);
        JournalEntryId = journalEntryId;
    }

    public void Reject(string user, DateTime utcNow)
    {
        if (Approval != ApprovalStatus.Pending) throw new DomainException("approval_status", "این تحویل قبلاً بررسی شده است.");
        Approval = ApprovalStatus.Rejected;
        ApprovedBy = user;
        ApprovedAtUtc = Guard.Utc(utcNow);
    }

    public void MarkReversed() => IsReversed = true;
}

/// <summary>
/// Money received by the final recipient in China. Registration means the agent has sent the USD (in transit);
/// approval confirms receipt by the recipient. Payment may be USD, CNY or USDT at an agreed rate.
/// </summary>
public class ChinaReceipt : AuditableEntity
{
    public Guid TransferCaseId { get; private set; }
    public Guid AgentId { get; private set; }
    public Guid FinalRecipientId { get; private set; }
    public DateTime DateUtc { get; private set; }
    /// <summary>USD amount from the agent settled by this receipt.</summary>
    public decimal SettledUsdAmount { get; private set; }
    public decimal PaidAmount { get; private set; }
    public CurrencyCode PaidCurrency { get; private set; }
    /// <summary>Agreed rate, e.g. 1 USD = 7.20 CNY. Null when paid in USD.</summary>
    public ExchangeRate? Rate { get; private set; }
    public string? TrackingNo { get; private set; }
    public ApprovalStatus Approval { get; private set; } = ApprovalStatus.Pending;
    public string? ApprovedBy { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public bool IsReversed { get; private set; }
    public string? Notes { get; private set; }
    public Guid? DispatchJournalEntryId { get; private set; }
    public Guid? ReceiptJournalEntryId { get; private set; }

    public Money SettledUsd => new(SettledUsdAmount, CurrencyCode.USD);
    public Money Paid => new(PaidAmount, PaidCurrency);
    public bool Counts => Approval == ApprovalStatus.Approved && !IsReversed;
    public bool IsPendingInTransit => Approval == ApprovalStatus.Pending && !IsReversed;

    protected ChinaReceipt() { }

    public ChinaReceipt(Guid caseId, Guid agentId, Guid recipientId, DateTime dateUtc, Money settledUsd,
        CurrencyCode paidCurrency, ExchangeRate? rate, string? trackingNo, string? notes)
    {
        if (settledUsd.Currency != CurrencyCode.USD)
            throw new DomainException("china_currency", "مبلغ تسویه از نماینده باید به دلار باشد.");
        TransferCaseId = caseId;
        AgentId = agentId;
        FinalRecipientId = recipientId;
        DateUtc = Guard.Utc(dateUtc);
        SettledUsdAmount = Currencies.Round(Guard.Positive(settledUsd.Amount, "مبلغ دریافت"), CurrencyCode.USD);
        PaidCurrency = paidCurrency;
        TrackingNo = trackingNo;
        Notes = notes;
        if (paidCurrency == CurrencyCode.USD)
            PaidAmount = SettledUsdAmount;
        else
        {
            Rate = rate ?? throw new DomainException("rate_required", $"برای پرداخت به {paidCurrency} نرخ توافقی الزامی است.");
            PaidAmount = rate.Convert(SettledUsd, paidCurrency).Amount;
        }
    }

    public void AttachDispatch(Guid journalEntryId) => DispatchJournalEntryId = journalEntryId;

    public void Approve(string user, DateTime utcNow, Guid journalEntryId)
    {
        if (Approval != ApprovalStatus.Pending) throw new DomainException("approval_status", "این دریافت قبلاً بررسی شده است.");
        Approval = ApprovalStatus.Approved;
        ApprovedBy = user;
        ApprovedAtUtc = Guard.Utc(utcNow);
        ReceiptJournalEntryId = journalEntryId;
    }

    public void Reject(string user, DateTime utcNow)
    {
        if (Approval != ApprovalStatus.Pending) throw new DomainException("approval_status", "این دریافت قبلاً بررسی شده است.");
        Approval = ApprovalStatus.Rejected;
        ApprovedBy = user;
        ApprovedAtUtc = Guard.Utc(utcNow);
    }

    public void MarkReversed() => IsReversed = true;
}
