using LenjExchange.Application.Abstractions;
using LenjExchange.Domain.Ledger;
using LenjExchange.Domain.Parties;
using Microsoft.EntityFrameworkCore;

namespace LenjExchange.Application.Ledger;

/// <summary>Resolves (and lazily creates) the ledger account for each party and the fixed system accounts.</summary>
public sealed class AccountDirectory
{
    private readonly IAppDbContext _db;

    public AccountDirectory(IAppDbContext db) => _db = db;

    public static readonly (string Code, string Name, AccountCategory Category)[] SystemAccounts =
    {
        (Account.Codes.IranCash, "صندوق ایران", AccountCategory.IranCash),
        (Account.Codes.InTransitToChina, "وجوه در مسیر چین", AccountCategory.InTransitToChina),
        (Account.Codes.Clearing, "حساب واسط/تسویه", AccountCategory.Clearing),
        (Account.Codes.FxConversion, "حساب تبدیل ارز", AccountCategory.FxConversion),
        (Account.Codes.Fees, "کارمزدها", AccountCategory.Fees),
        (Account.Codes.FxGainLoss, "سود و زیان تسعیر/تبدیل ارز", AccountCategory.FxGainLoss),
        (Account.Codes.Discrepancy, "مغایرت‌ها", AccountCategory.Discrepancy),
    };

    public async Task EnsureSystemAccountsAsync(CancellationToken ct = default)
    {
        foreach (var (code, name, cat) in SystemAccounts)
            await GetOrCreateAsync(code, name, cat, null, true, ct);
    }

    public Task<Account> SystemAsync(AccountCategory category, CancellationToken ct = default)
    {
        var def = SystemAccounts.First(a => a.Category == category);
        return GetOrCreateAsync(def.Code, def.Name, def.Category, null, true, ct);
    }

    public Task<Account> ForCustomerAsync(Customer c, CancellationToken ct = default) =>
        GetOrCreateAsync(Account.Codes.CustomerPrefix + c.Code, $"مشتری: {c.FullName}", AccountCategory.Customer, c.Id, false, ct);

    public Task<Account> ForBankAsync(Bank b, CancellationToken ct = default) =>
        GetOrCreateAsync(Account.Codes.BankPrefix + Short(b.Id), $"بانک: {b.Name}", AccountCategory.Bank, b.Id, false, ct);

    public Task<Account> ForExchangerAsync(Exchanger e, CancellationToken ct = default) =>
        GetOrCreateAsync(Account.Codes.ExchangerPrefix + Short(e.Id), $"صرافی: {e.Name}", AccountCategory.Exchanger, e.Id, false, ct);

    public Task<Account> ForAgentAsync(Agent a, CancellationToken ct = default) =>
        GetOrCreateAsync(Account.Codes.AgentPrefix + Short(a.Id), $"وجوه نزد نماینده: {a.Name}", AccountCategory.AgentFunds, a.Id, false, ct);

    public Task<Account> ForRecipientAsync(FinalRecipient r, CancellationToken ct = default) =>
        GetOrCreateAsync(Account.Codes.RecipientPrefix + Short(r.Id), $"حساب {r.Name} ({r.Country})", AccountCategory.Recipient, r.Id, false, ct);

    public async Task<Account?> FindLinkedAsync(Guid linkedEntityId, AccountCategory category, CancellationToken ct = default) =>
        _db.Accounts.Local.FirstOrDefault(a => a.LinkedEntityId == linkedEntityId && a.Category == category)
        ?? await _db.Accounts.FirstOrDefaultAsync(a => a.LinkedEntityId == linkedEntityId && a.Category == category, ct);

    private static string Short(Guid id) => id.ToString("N")[..8].ToUpperInvariant();

    private async Task<Account> GetOrCreateAsync(string code, string name, AccountCategory category, Guid? linked, bool system, CancellationToken ct)
    {
        var acc = _db.Accounts.Local.FirstOrDefault(a => a.Code == code)
                  ?? await _db.Accounts.FirstOrDefaultAsync(a => a.Code == code, ct);
        if (acc is not null) return acc;
        acc = new Account(code, name, category, linked, system);
        _db.Accounts.Add(acc);
        return acc;
    }
}
