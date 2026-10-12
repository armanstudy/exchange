using LenjExchange.Domain.Common;

namespace LenjExchange.Domain.Ledger;

public enum AccountCategory
{
    Customer = 1,
    IranCash = 2,
    Bank = 3,
    Exchanger = 4,
    AgentFunds = 5,
    InTransitToChina = 6,
    Recipient = 7,
    Clearing = 8,
    FxConversion = 9,
    Fees = 10,
    FxGainLoss = 11,
    Discrepancy = 12
}

public enum AccountNature
{
    /// <summary>Asset / expense: balance normally on the debit side.</summary>
    Debit = 1,
    /// <summary>Liability / income: balance normally on the credit side.</summary>
    Credit = 2
}

/// <summary>
/// Ledger account. Accounts are multi-currency: every line carries its own currency and balances are always
/// reported per currency. Balances are never stored; they are computed from posted journal lines.
/// </summary>
public class Account : Entity
{
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public AccountCategory Category { get; private set; }
    public AccountNature Nature { get; private set; }
    /// <summary>Id of the linked customer/bank/exchanger/agent/recipient, when applicable.</summary>
    public Guid? LinkedEntityId { get; private set; }
    public bool IsSystem { get; private set; }
    public bool IsActive { get; private set; } = true;

    protected Account() { }

    public Account(string code, string name, AccountCategory category, Guid? linkedEntityId = null, bool isSystem = false)
    {
        Code = Guard.NotBlank(code, "کد حساب");
        Name = Guard.NotBlank(name, "نام حساب");
        Category = category;
        Nature = DefaultNature(category);
        LinkedEntityId = linkedEntityId;
        IsSystem = isSystem;
    }

    public void Rename(string name) => Name = Guard.NotBlank(name, "نام حساب");

    public static AccountNature DefaultNature(AccountCategory category) => category switch
    {
        AccountCategory.Customer => AccountNature.Credit,
        AccountCategory.FxGainLoss => AccountNature.Credit,
        AccountCategory.Clearing => AccountNature.Credit,
        AccountCategory.FxConversion => AccountNature.Credit,
        _ => AccountNature.Debit
    };

    public static class Codes
    {
        public const string IranCash = "1100";
        public const string BankPrefix = "1200-";
        public const string ExchangerPrefix = "1300-";
        public const string AgentPrefix = "1400-";
        public const string InTransitToChina = "1500";
        public const string RecipientPrefix = "1600-";
        public const string CustomerPrefix = "2100-";
        public const string Clearing = "3100";
        public const string FxConversion = "3200";
        public const string Fees = "4100";
        public const string FxGainLoss = "4200";
        public const string Discrepancy = "4300";
    }
}
