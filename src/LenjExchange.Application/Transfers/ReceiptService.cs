using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Common;
using LenjExchange.Application.Ledger;
using LenjExchange.Application.MasterData;
using LenjExchange.Application.Numbering;
using LenjExchange.Application.Settings;
using LenjExchange.Domain.Common;
using LenjExchange.Domain.Ledger;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Security;
using LenjExchange.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace LenjExchange.Application.Transfers;

public sealed record DepositInput(string BankName, DateTime DateUtc, decimal Amount, CurrencyCode Currency,
    string? BankTrackingNo = null, string? Depositor = null, string? Notes = null);

public sealed record ReceiptInput(
    Guid CustomerId,
    DateTime DateUtc,
    decimal Amount,
    CurrencyCode Currency,
    CurrencyCode TargetCurrency,
    ExchangeRate? Rate,
    Guid? FinalRecipientId,
    IReadOnlyList<DepositInput>? Deposits = null,
    string? Notes = null,
    Guid? TransferCaseId = null);

public enum DuplicateKind
{
    /// <summary>Same bank tracking number already registered — almost certainly a duplicate.</summary>
    SameTrackingNo = 1,
    /// <summary>Same bank, amount, currency and calendar day — possibly a duplicate.</summary>
    SameAmountBankDate = 2
}

public sealed record DuplicateDepositWarning(int InputIndex, DuplicateKind Kind, Guid? ExistingDepositId, string? ExistingReceiptNumber, string Message);

public sealed record ReceiptCheckResult(Money Total, Money DepositsTotal, Money Difference, IReadOnlyList<DuplicateDepositWarning> Duplicates)
{
    public bool HasMismatch => !Difference.IsZero;
    public string? MismatchMessage => HasMismatch
        ? $"جمع واریزها ({DepositsTotal}) با مبلغ رسید ({Total}) {(Difference.IsNegative ? "کمتر" : "بیشتر")} است؛ اختلاف: {new Money(Math.Abs(Difference.Amount), Difference.Currency)}"
        : null;
}

public sealed class DuplicateDepositException : DomainException
{
    public IReadOnlyList<DuplicateDepositWarning> Warnings { get; }

    public DuplicateDepositException(IReadOnlyList<DuplicateDepositWarning> warnings)
        : base("deposit_duplicate", "واریز تکراری: " + string.Join(" | ", warnings.Select(w => w.Message)))
        => Warnings = warnings;
}

public sealed class ReceiptService
{
    public const string SourceType = nameof(CustomerReceipt);

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly NumberingService _numbering;
    private readonly LedgerService _ledger;
    private readonly AccountDirectory _accounts;
    private readonly MasterDataService _master;
    private readonly TransferCaseService _cases;
    private readonly SettingsService _settings;
    private readonly AuditService _audit;
    private readonly UnitOfWork _uow;

    public ReceiptService(IAppDbContext db, ICurrentUser user, IClock clock, NumberingService numbering, LedgerService ledger,
        AccountDirectory accounts, MasterDataService master, TransferCaseService cases, SettingsService settings, AuditService audit, UnitOfWork uow)
    {
        _db = db;
        _user = user;
        _clock = clock;
        _numbering = numbering;
        _ledger = ledger;
        _accounts = accounts;
        _master = master;
        _cases = cases;
        _settings = settings;
        _audit = audit;
        _uow = uow;
    }

    /// <summary>Pre-flight check for the UI: deposit-sum mismatch and duplicate deposits (no data is written).</summary>
    public async Task<ReceiptCheckResult> CheckAsync(decimal amount, CurrencyCode currency, IReadOnlyList<DepositInput> deposits,
        Guid? excludeReceiptId = null, CancellationToken ct = default)
    {
        var total = Money.Of(amount, currency);
        var sum = Money.Zero(currency);
        foreach (var d in deposits)
        {
            if (!CurrencyMath.AreFixedEquivalent(d.Currency, currency))
                throw new DomainException("deposit_currency", $"ارز واریزی ({d.Currency}) با ارز رسید ({currency}) سازگار نیست.");
            sum += CurrencyMath.ConvertFixed(Money.Of(d.Amount, d.Currency), currency);
        }
        var diff = deposits.Count == 0 ? Money.Zero(currency) : sum - total;
        var dups = await FindDuplicatesAsync(deposits, excludeReceiptId, ct);
        return new ReceiptCheckResult(total, sum, diff, dups);
    }

    public async Task<IReadOnlyList<DuplicateDepositWarning>> FindDuplicatesAsync(IReadOnlyList<DepositInput> deposits, Guid? excludeReceiptId, CancellationToken ct = default)
    {
        var warnings = new List<DuplicateDepositWarning>();
        var trackingNos = deposits.Where(d => !string.IsNullOrWhiteSpace(d.BankTrackingNo)).Select(d => d.BankTrackingNo!.Trim()).Distinct().ToList();
        var minDate = deposits.Count == 0 ? DateTime.MaxValue : deposits.Min(d => Guard.Utc(d.DateUtc)).AddDays(-1);
        var maxDate = deposits.Count == 0 ? DateTime.MinValue : deposits.Max(d => Guard.Utc(d.DateUtc)).AddDays(1);

        var candidates = await (
            from d in _db.BankDeposits.AsNoTracking()
            join r in _db.CustomerReceipts.AsNoTracking() on d.CustomerReceiptId equals r.Id
            where r.Status != DocumentStatus.Reversed
                  && (excludeReceiptId == null || r.Id != excludeReceiptId)
                  && ((d.BankTrackingNo != null && trackingNos.Contains(d.BankTrackingNo)) || (d.DateUtc >= minDate && d.DateUtc <= maxDate))
            select new { Deposit = d, r.ReceiptNumber }).ToListAsync(ct);

        for (var i = 0; i < deposits.Count; i++)
        {
            var input = deposits[i];
            var day = _clock.ToLocal(Guard.Utc(input.DateUtc)).Date;
            var amount = Currencies.Round(input.Amount, input.Currency);
            if (!string.IsNullOrWhiteSpace(input.BankTrackingNo))
            {
                var tn = input.BankTrackingNo.Trim();
                foreach (var c in candidates.Where(c => c.Deposit.BankTrackingNo == tn))
                    warnings.Add(new(i, DuplicateKind.SameTrackingNo, c.Deposit.Id, c.ReceiptNumber,
                        $"کد رهگیری بانکی «{tn}» قبلاً در رسید {c.ReceiptNumber ?? "پیش‌نویس"} ثبت شده است."));
                for (var j = 0; j < i; j++)
                    if (deposits[j].BankTrackingNo?.Trim() == tn)
                        warnings.Add(new(i, DuplicateKind.SameTrackingNo, null, null, $"کد رهگیری «{tn}» در همین رسید تکرار شده است."));
            }
            foreach (var c in candidates.Where(c => c.Deposit.Amount == amount && c.Deposit.Currency == input.Currency
                                                    && c.Deposit.BankName == input.BankName.Trim()
                                                    && _clock.ToLocal(c.Deposit.DateUtc).Date == day
                                                    && c.Deposit.BankTrackingNo != input.BankTrackingNo?.Trim()))
                warnings.Add(new(i, DuplicateKind.SameAmountBankDate, c.Deposit.Id, c.ReceiptNumber,
                    $"واریز مشابه ({c.Deposit.Money}، {c.Deposit.BankName}، همان روز) در رسید {c.ReceiptNumber ?? "پیش‌نویس"} وجود دارد."));
        }
        return warnings;
    }

    /// <summary>Creates a draft receipt (and its transfer case when none is given). No number, no journal yet.</summary>
    public Task<CustomerReceipt> CreateDraftAsync(ReceiptInput input, CancellationToken ct = default)
    {
        _user.Demand(Permission.RegisterReceipts);
        return _uow.ExecuteAsync(async () =>
        {
            var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == input.CustomerId && !c.IsDeleted, ct)
                           ?? throw new NotFoundException("مشتری");
            if (!customer.IsActive) throw new DomainException("customer_inactive", "مشتری غیرفعال است.");
            if (input.FinalRecipientId is { } rid)
            {
                var rec = await _db.FinalRecipients.FirstOrDefaultAsync(r => r.Id == rid, ct) ?? throw new NotFoundException("گیرنده نهایی");
                if (!rec.Accepts(input.TargetCurrency))
                    throw new DomainException("recipient_currency", $"گیرنده «{rec.Name}» ارز {input.TargetCurrency} را نمی‌پذیرد.");
            }

            var caseId = input.TransferCaseId;
            if (caseId is null)
                caseId = (await _cases.CreateInternalAsync(customer.Id, input.FinalRecipientId, input.TargetCurrency, input.Notes, ct)).Id;
            else
            {
                var tc = await _db.TransferCases.FirstOrDefaultAsync(c => c.Id == caseId, ct) ?? throw new NotFoundException("پرونده");
                if (tc.CustomerId != customer.Id) throw new DomainException("case_customer", "پرونده متعلق به این مشتری نیست.");
                if (!CurrencyMath.AreFixedEquivalent(tc.TargetCurrency, input.TargetCurrency))
                    throw new DomainException("case_currency", "ارز تعهد رسید با ارز پرونده یکسان نیست.");
            }

            var receipt = new CustomerReceipt(caseId.Value, customer.Id, input.DateUtc, Money.Of(input.Amount, input.Currency),
                input.TargetCurrency, input.Rate, input.FinalRecipientId, input.Notes);
            receipt.StampCreated(_user.UserName, _clock.UtcNow);
            foreach (var d in input.Deposits ?? Array.Empty<DepositInput>())
            {
                var bank = await _master.GetOrAddBankAsync(d.BankName, ct);
                receipt.AddDeposit(bank.Name, bank.Id, d.DateUtc, Money.Of(d.Amount, d.Currency), d.BankTrackingNo, d.Depositor, d.Notes);
            }
            _db.CustomerReceipts.Add(receipt);
            _audit.Record("receipt.draft", SourceType, receipt.Id, receipt.Total.Format());
            return receipt;
        }, ct);
    }

    /// <summary>
    /// Finalizes: assigns the immutable receipt number, posts the balanced journal entry and re-derives the case status,
    /// all in one transaction. Duplicate deposits block finalization unless <paramref name="acceptDuplicates"/>.
    /// </summary>
    public Task<CustomerReceipt> FinalizeAsync(Guid receiptId, bool acceptMismatch = false, bool acceptDuplicates = false, CancellationToken ct = default)
    {
        _user.Demand(Permission.FinalizeReceipts);
        return _uow.ExecuteAsync(async () =>
        {
            var receipt = await _db.CustomerReceipts.Include(r => r.Deposits).FirstOrDefaultAsync(r => r.Id == receiptId, ct)
                          ?? throw new NotFoundException("رسید");
            var acct = await _settings.GetAccountingAsync(ct);
            if (receipt.HasDepositMismatch && acceptMismatch && !acct.AllowDepositMismatch && !_user.Has(Permission.ReverseEntries))
                throw new DomainException("deposit_mismatch_forbidden", "ثبت رسید با مغایرت واریز فقط توسط مدیر مجاز است.");

            var dupInputs = receipt.Deposits.Select(d => new DepositInput(d.BankName, d.DateUtc, d.Amount, d.Currency, d.BankTrackingNo)).ToList();
            var dups = await FindDuplicatesAsync(dupInputs, receipt.Id, ct);
            if (dups.Count > 0 && !acceptDuplicates) throw new DuplicateDepositException(dups);

            var number = await _numbering.NextAsync(DocumentKinds.Receipt, receipt.DateUtc, ct);
            if (await _db.CustomerReceipts.AnyAsync(r => r.ReceiptNumber == number, ct))
                throw new DomainException("receipt_number_dup", $"شمارهٔ رسید {number} تکراری است؛ الگو/دنباله را بررسی کنید.");
            receipt.Finalize(number, _user.UserName, _clock.UtcNow, acceptMismatch);

            var entry = await BuildJournalAsync(receipt, ct);
            await _ledger.PostAsync(entry, ct);
            receipt.AttachJournal(entry.Id);
            _audit.Record("receipt.finalize", SourceType, receipt.Id, number);
            await _cases.RecalculateAsync(receipt.TransferCaseId, ct);
            return receipt;
        }, ct);
    }

    /// <summary>Convenience: draft + finalize in a single transaction.</summary>
    public Task<CustomerReceipt> RegisterAsync(ReceiptInput input, bool acceptMismatch = false, bool acceptDuplicates = false, CancellationToken ct = default) =>
        _uow.ExecuteAsync(async () =>
        {
            var draft = await CreateDraftAsync(input, ct);
            return await FinalizeAsync(draft.Id, acceptMismatch, acceptDuplicates, ct);
        }, ct);

    private async Task<JournalEntry> BuildJournalAsync(CustomerReceipt r, CancellationToken ct)
    {
        var customer = await _db.Customers.FirstAsync(c => c.Id == r.CustomerId, ct);
        var customerAcc = await _accounts.ForCustomerAsync(customer, ct);
        var entry = await _ledger.NewEntryAsync(r.DateUtc, OperationType.CustomerReceipt,
            $"دریافت وجه از {customer.FullName} - رسید {r.ReceiptNumber}", r.TransferCaseId, SourceType, r.Id, ct);
        entry.UseHistoricRates(r.Rate);

        if (r.Deposits.Count == 0)
        {
            var cash = await _accounts.SystemAsync(AccountCategory.IranCash, ct);
            entry.Debit(cash.Id, r.Total, "دریافت نقدی");
        }
        else
        {
            foreach (var d in r.Deposits)
            {
                var bank = d.BankId is { } bid ? await _db.Banks.FirstAsync(b => b.Id == bid, ct) : await _master.GetOrAddBankAsync(d.BankName, ct);
                var bankAcc = await _accounts.ForBankAsync(bank, ct);
                entry.Debit(bankAcc.Id, CurrencyMath.ConvertFixed(d.Money, r.Currency), $"واریز {d.BankName} {d.BankTrackingNo}");
            }
            var diff = r.DepositDifference();
            if (!diff.IsZero)
            {
                var disc = await _accounts.SystemAsync(AccountCategory.Discrepancy, ct);
                if (diff.IsNegative) entry.Debit(disc.Id, -diff, "کسری واریز نسبت به رسید");
                else entry.Credit(disc.Id, diff, "مازاد واریز نسبت به رسید");
            }
        }
        entry.Credit(customerAcc.Id, r.Total, $"دریافت وجه - رسید {r.ReceiptNumber}");

        if (r.Currency != r.TargetCurrency)
        {
            var fx = await _accounts.SystemAsync(AccountCategory.FxConversion, ct);
            var rateText = r.Rate?.Describe() ?? "تبدیل ثابت ریال/تومان";
            entry.Debit(customerAcc.Id, r.Total, $"تبدیل به {r.TargetCurrency} ({rateText})");
            entry.Credit(fx.Id, r.Total, rateText);
            entry.Debit(fx.Id, r.TargetTotal, rateText);
            entry.Credit(customerAcc.Id, r.TargetTotal, $"تعهد {r.TargetTotal} ({rateText})");
        }
        return entry;
    }

    public async Task<CustomerReceipt> GetAsync(Guid id, CancellationToken ct = default) =>
        await _db.CustomerReceipts.AsNoTracking().Include(r => r.Deposits).FirstOrDefaultAsync(r => r.Id == id, ct)
        ?? throw new NotFoundException("رسید");

    public async Task<IReadOnlyList<CustomerReceipt>> ListAsync(Guid? customerId = null, CancellationToken ct = default)
    {
        var q = _db.CustomerReceipts.AsNoTracking().Include(r => r.Deposits).AsQueryable();
        if (customerId is { } cid) q = q.Where(r => r.CustomerId == cid);
        return await q.OrderByDescending(r => r.DateUtc).Take(2000).ToListAsync(ct);
    }

    /// <summary>Payment from the company to a customer (refund / payout). Debits the customer account.</summary>
    public Task<JournalEntry> RegisterCustomerPaymentAsync(Guid customerId, Money amount, DateTime dateUtc, Guid? bankId, string description, CancellationToken ct = default)
    {
        _user.Demand(Permission.FinalizeReceipts);
        return _uow.ExecuteAsync(async () =>
        {
            var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct) ?? throw new NotFoundException("مشتری");
            var custAcc = await _accounts.ForCustomerAsync(customer, ct);
            var source = bankId is { } b
                ? await _accounts.ForBankAsync(await _db.Banks.FirstAsync(x => x.Id == b, ct), ct)
                : await _accounts.SystemAsync(AccountCategory.IranCash, ct);
            var entry = await _ledger.NewEntryAsync(dateUtc, OperationType.CustomerPayment, Guard.NotBlank(description, "شرح"), null, "CustomerPayment", null, ct);
            entry.Debit(custAcc.Id, amount, description).Credit(source.Id, amount, description);
            await _ledger.PostAsync(entry, ct);
            return entry;
        }, ct);
    }
}
