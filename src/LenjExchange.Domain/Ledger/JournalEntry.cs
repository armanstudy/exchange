using LenjExchange.Domain.Common;
using LenjExchange.Domain.Monetary;

namespace LenjExchange.Domain.Ledger;

public enum JournalStatus
{
    Draft = 0,
    Posted = 1,
    /// <summary>Was posted, then neutralized by a reversal entry. Still part of the ledger history.</summary>
    Reversed = 2
}

public enum OperationType
{
    CustomerReceipt = 1,
    ExchangerCommitment = 2,
    AgentDelivery = 3,
    ChinaDispatch = 4,
    ChinaReceipt = 5,
    CustomerPayment = 6,
    FxConversion = 7,
    Fee = 8,
    Adjustment = 9,
    Reversal = 10,
    OpeningBalance = 11
}

public class JournalLine : Entity
{
    public Guid JournalEntryId { get; private set; }
    public int LineNo { get; private set; }
    public Guid AccountId { get; private set; }
    public CurrencyCode Currency { get; private set; }
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }
    /// <summary>Historic rate snapshot: units of base currency per 1 unit of <see cref="Currency"/>.</summary>
    public decimal? BaseRate { get; private set; }
    /// <summary>Equivalent in the configured base (reporting) currency using <see cref="BaseRate"/>.</summary>
    public decimal? BaseAmount { get; private set; }
    public string? Description { get; private set; }

    protected JournalLine() { }

    internal JournalLine(Guid entryId, int lineNo, Guid accountId, CurrencyCode currency, decimal debit, decimal credit,
        decimal? baseRate, CurrencyCode? baseCurrency, string? description)
    {
        if (debit < 0 || credit < 0)
            throw new DomainException("line_negative", "مبلغ بدهکار/بستانکار نمی‌تواند منفی باشد.");
        if ((debit == 0) == (credit == 0))
            throw new DomainException("line_side", "هر سطر سند باید دقیقاً یکی از بدهکار یا بستانکار را داشته باشد.");
        JournalEntryId = entryId;
        LineNo = lineNo;
        AccountId = accountId;
        Currency = currency;
        Debit = Currencies.Round(debit, currency);
        Credit = Currencies.Round(credit, currency);
        if (Debit == 0 && Credit == 0)
            throw new DomainException("line_zero", "مبلغ سطر پس از گرد کردن صفر شد.");
        BaseRate = baseRate;
        if (baseRate is { } r && baseCurrency is { } bc)
            BaseAmount = Currencies.Round((Debit - Credit) * r, bc);
        Description = description;
    }

    public decimal Signed => Debit - Credit;
}

/// <summary>
/// Double-entry journal entry. Each currency must balance independently (sum debit == sum credit per currency);
/// cross-currency movements must pass through the FX conversion account.
/// </summary>
public class JournalEntry : Entity
{
    private readonly List<JournalLine> _lines = new();

    public string? EntryNumber { get; private set; }
    public DateTime DateUtc { get; private set; }
    public OperationType OperationType { get; private set; }
    public string Description { get; private set; } = "";
    public JournalStatus Status { get; private set; } = JournalStatus.Draft;
    public Guid? TransferCaseId { get; private set; }
    public string? SourceDocumentType { get; private set; }
    public Guid? SourceDocumentId { get; private set; }
    public CurrencyCode? BaseCurrency { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTime? PostedAtUtc { get; private set; }
    public string? PostedBy { get; private set; }

    public Guid? ReversalOfEntryId { get; private set; }
    public Guid? ReversedByEntryId { get; private set; }
    public string? ReversalReason { get; private set; }
    public DateTime? ReversedAtUtc { get; private set; }
    public string? ReversedBy { get; private set; }

    public IReadOnlyList<JournalLine> Lines => _lines;

    protected JournalEntry() { }

    public JournalEntry(DateTime dateUtc, OperationType type, string description, string createdBy, DateTime createdAtUtc,
        Guid? transferCaseId = null, string? sourceDocumentType = null, Guid? sourceDocumentId = null,
        CurrencyCode? baseCurrency = null)
    {
        DateUtc = Guard.Utc(dateUtc);
        OperationType = type;
        Description = Guard.NotBlank(description, "شرح سند");
        CreatedBy = Guard.NotBlank(createdBy, "کاربر");
        CreatedAtUtc = Guard.Utc(createdAtUtc);
        TransferCaseId = transferCaseId;
        SourceDocumentType = sourceDocumentType;
        SourceDocumentId = sourceDocumentId;
        BaseCurrency = baseCurrency;
    }

    public JournalEntry Debit(Guid accountId, Money money, string? description = null, decimal? baseRate = null) =>
        AddLine(accountId, money.Currency, money.Amount, 0m, description, baseRate);

    public JournalEntry Credit(Guid accountId, Money money, string? description = null, decimal? baseRate = null) =>
        AddLine(accountId, money.Currency, 0m, money.Amount, description, baseRate);

    private readonly List<ExchangeRate> _historicRates = new();

    /// <summary>
    /// Registers the document's rate snapshots so subsequent lines get a base-currency equivalent
    /// (<see cref="JournalLine.BaseRate"/>) without looking up today's rates.
    /// </summary>
    public JournalEntry UseHistoricRates(params ExchangeRate?[] rates)
    {
        _historicRates.AddRange(rates.Where(r => r is not null)!);
        return this;
    }

    private decimal? ResolveBaseRate(CurrencyCode currency)
    {
        if (BaseCurrency is not { } bc) return null;
        if (currency == bc) return 1m;
        if (currency == CurrencyCode.IRR && bc == CurrencyCode.IRT) return 1m / Currencies.RialPerToman;
        if (currency == CurrencyCode.IRT && bc == CurrencyCode.IRR) return Currencies.RialPerToman;
        foreach (var r in _historicRates)
        {
            try
            {
                if (r.Involves(currency) && r.Involves(bc)) return r.FactorFor(currency, bc);
            }
            catch (DomainException) { }
        }
        return null;
    }

    public JournalEntry AddLine(Guid accountId, CurrencyCode currency, decimal debit, decimal credit,
        string? description = null, decimal? baseRate = null)
    {
        EnsureDraft();
        var rate = baseRate ?? ResolveBaseRate(currency);
        _lines.Add(new JournalLine(Id, _lines.Count + 1, accountId, currency, debit, credit, rate, BaseCurrency, description));
        return this;
    }

    /// <summary>
    /// Adds the two FX conversion legs: the FX account absorbs <paramref name="sourceAmountIn"/> (credit) and
    /// supplies <paramref name="targetAmountOut"/> (debit), so each currency balances on its own.
    /// The caller adds the matching business-account lines (see docs/accounting-rules.md).
    /// </summary>
    public JournalEntry AddFxLegs(Guid fxAccountId, Money sourceAmountIn, Money targetAmountOut, string? description = null)
    {
        Credit(fxAccountId, sourceAmountIn, description);
        Debit(fxAccountId, targetAmountOut, description);
        return this;
    }

    public IReadOnlyDictionary<CurrencyCode, decimal> ImbalanceByCurrency() =>
        _lines.GroupBy(l => l.Currency).ToDictionary(g => g.Key, g => g.Sum(l => l.Debit) - g.Sum(l => l.Credit));

    public bool IsBalanced => _lines.Count >= 2 && ImbalanceByCurrency().Values.All(v => v == 0m);

    public void EnsureBalanced()
    {
        if (_lines.Count < 2)
            throw new DomainException("entry_lines", "سند حسابداری باید حداقل دو سطر داشته باشد.");
        var bad = ImbalanceByCurrency().Where(kv => kv.Value != 0m).ToList();
        if (bad.Count > 0)
            throw new DomainException("entry_unbalanced",
                "سند تراز نیست: " + string.Join("، ", bad.Select(kv => $"{kv.Key}: {kv.Value:#,0.######}")));
    }

    public void Post(string entryNumber, string user, DateTime utcNow)
    {
        EnsureDraft();
        EnsureBalanced();
        EntryNumber = Guard.NotBlank(entryNumber, "شماره سند");
        PostedBy = Guard.NotBlank(user, "کاربر");
        PostedAtUtc = Guard.Utc(utcNow);
        Status = JournalStatus.Posted;
    }

    /// <summary>Creates the mirror entry (debits↔credits). The original is marked Reversed when the reversal posts.</summary>
    public JournalEntry CreateReversal(string reason, string user, DateTime reversalDateUtc, DateTime utcNow)
    {
        if (Status != JournalStatus.Posted)
            throw new DomainException("reverse_status", "فقط سند قطعی (ثبت‌شده) قابل برگشت است.");
        Guard.NotBlank(reason, "علت برگشت");
        var rev = new JournalEntry(reversalDateUtc, OperationType.Reversal, $"برگشت سند {EntryNumber}: {reason}", user, utcNow,
            TransferCaseId, SourceDocumentType, SourceDocumentId, BaseCurrency)
        {
            ReversalOfEntryId = Id,
            ReversalReason = reason
        };
        foreach (var l in _lines)
            rev.AddLine(l.AccountId, l.Currency, l.Credit, l.Debit, l.Description, l.BaseRate);
        return rev;
    }

    public void MarkReversed(JournalEntry reversal, string user, DateTime utcNow)
    {
        if (Status != JournalStatus.Posted)
            throw new DomainException("reverse_status", "فقط سند قطعی قابل برگشت است.");
        if (reversal.ReversalOfEntryId != Id || reversal.Status != JournalStatus.Posted)
            throw new DomainException("reverse_link", "سند برگشتی نامعتبر است.");
        Status = JournalStatus.Reversed;
        ReversedByEntryId = reversal.Id;
        ReversalReason = reversal.ReversalReason;
        ReversedBy = user;
        ReversedAtUtc = Guard.Utc(utcNow);
    }

    /// <summary>Posted and reversed entries both belong to the ledger; drafts never affect balances.</summary>
    public bool AffectsBalances => Status is JournalStatus.Posted or JournalStatus.Reversed;

    private void EnsureDraft()
    {
        if (Status != JournalStatus.Draft)
            throw new DomainException("entry_immutable", "سند قطعی‌شده قابل ویرایش نیست؛ برای اصلاح از سند برگشتی استفاده کنید.");
    }
}
