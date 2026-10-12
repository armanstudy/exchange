using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Common;
using LenjExchange.Application.Ledger;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Parties;
using LenjExchange.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace LenjExchange.Application.MasterData;

/// <summary>Exchangers, agents, final recipients and banks (اطلاعات پایه), plus initial seed data.</summary>
public sealed class MasterDataService
{
    public static readonly string[] SeedExchangers = { "صرافی عابدی", "داریوش عباسی" };
    public static readonly string[] SeedAgents = { "آیدین", "چاتای" };
    public const string SeedRecipient = "محمد";

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly AccountDirectory _accounts;
    private readonly AuditService _audit;
    private readonly UnitOfWork _uow;

    public MasterDataService(IAppDbContext db, ICurrentUser user, IClock clock, AccountDirectory accounts, AuditService audit, UnitOfWork uow)
    {
        _db = db;
        _user = user;
        _clock = clock;
        _accounts = accounts;
        _audit = audit;
        _uow = uow;
    }

    /// <summary>Idempotent seed of system accounts and the parties named in the specification.</summary>
    public Task SeedAsync(CancellationToken ct = default) => _uow.ExecuteAsync(async () =>
    {
        await _accounts.EnsureSystemAccountsAsync(ct);
        foreach (var name in SeedExchangers)
            if (!await _db.Exchangers.AnyAsync(e => e.Name == name, ct))
            {
                var e = new Exchanger(name);
                e.StampCreated("system", _clock.UtcNow);
                _db.Exchangers.Add(e);
                await _accounts.ForExchangerAsync(e, ct);
            }
        foreach (var name in SeedAgents)
            if (!await _db.Agents.AnyAsync(a => a.Name == name, ct))
            {
                var a = new Agent(name);
                a.StampCreated("system", _clock.UtcNow);
                _db.Agents.Add(a);
                await _accounts.ForAgentAsync(a, ct);
            }
        if (!await _db.FinalRecipients.AnyAsync(r => r.Name == SeedRecipient, ct))
        {
            var r = new FinalRecipient(SeedRecipient, "چین", new[] { CurrencyCode.USD, CurrencyCode.CNY, CurrencyCode.USDT });
            r.StampCreated("system", _clock.UtcNow);
            _db.FinalRecipients.Add(r);
            await _accounts.ForRecipientAsync(r, ct);
        }
    }, ct);

    public Task<List<Exchanger>> ExchangersAsync(CancellationToken ct = default) => _db.Exchangers.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
    public Task<List<Agent>> AgentsAsync(CancellationToken ct = default) => _db.Agents.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
    public Task<List<FinalRecipient>> RecipientsAsync(CancellationToken ct = default) => _db.FinalRecipients.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
    public Task<List<Bank>> BanksAsync(CancellationToken ct = default) => _db.Banks.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);

    public async Task<Exchanger> ExchangerByNameAsync(string name, CancellationToken ct = default) =>
        await _db.Exchangers.FirstOrDefaultAsync(x => x.Name == name, ct) ?? throw new NotFoundException($"صرافی «{name}»");
    public async Task<Agent> AgentByNameAsync(string name, CancellationToken ct = default) =>
        await _db.Agents.FirstOrDefaultAsync(x => x.Name == name, ct) ?? throw new NotFoundException($"نماینده «{name}»");
    public async Task<FinalRecipient> RecipientByNameAsync(string name, CancellationToken ct = default) =>
        await _db.FinalRecipients.FirstOrDefaultAsync(x => x.Name == name, ct) ?? throw new NotFoundException($"گیرنده «{name}»");

    public Task<Exchanger> AddExchangerAsync(string name, string? phone = null, CancellationToken ct = default) =>
        Add(new Exchanger(name, phone), e => _db.Exchangers.Add(e), e => _accounts.ForExchangerAsync(e, ct), ct);

    public Task<Agent> AddAgentAsync(string name, string? phone = null, CancellationToken ct = default) =>
        Add(new Agent(name, phone), a => _db.Agents.Add(a), a => _accounts.ForAgentAsync(a, ct), ct);

    public Task<FinalRecipient> AddRecipientAsync(string name, string country, IEnumerable<CurrencyCode> currencies, CancellationToken ct = default) =>
        Add(new FinalRecipient(name, country, currencies), r => _db.FinalRecipients.Add(r), r => _accounts.ForRecipientAsync(r, ct), ct);

    public Task<Bank> AddBankAsync(string name, string? accountNumber = null, CancellationToken ct = default) =>
        Add(new Bank(name, accountNumber), b => _db.Banks.Add(b), b => _accounts.ForBankAsync(b, ct), ct);

    /// <summary>Used while registering deposits: operators may introduce a new bank name without master-data rights.</summary>
    public async Task<Bank> GetOrAddBankAsync(string name, CancellationToken ct = default) =>
        _db.Banks.Local.FirstOrDefault(b => b.Name == name.Trim())
        ?? await _db.Banks.FirstOrDefaultAsync(b => b.Name == name.Trim(), ct)
        ?? await Add(new Bank(name), b => _db.Banks.Add(b), b => _accounts.ForBankAsync(b, ct), ct, demand: false);

    public async Task UpdateRecipientCurrenciesAsync(Guid recipientId, IEnumerable<CurrencyCode> currencies, CancellationToken ct = default)
    {
        _user.Demand(Permission.ManageMasterData);
        var r = await _db.FinalRecipients.FirstOrDefaultAsync(x => x.Id == recipientId, ct) ?? throw new NotFoundException("گیرنده");
        r.SetAcceptedCurrencies(currencies);
        _audit.Record("recipient.currencies", nameof(FinalRecipient), r.Id, r.AcceptedCurrenciesCsv);
        await _db.SaveChangesAsync(ct);
    }

    private Task<T> Add<T>(T entity, Action<T> add, Func<T, Task> createAccount, CancellationToken ct, bool demand = true)
        where T : Domain.Common.AuditableEntity
    {
        if (demand) _user.Demand(Permission.ManageMasterData);
        return _uow.ExecuteAsync(async () =>
        {
            entity.StampCreated(_user.UserName, _clock.UtcNow);
            add(entity);
            await createAccount(entity);
            _audit.Record("masterdata.add", typeof(T).Name, entity.Id);
            return entity;
        }, ct);
    }
}
