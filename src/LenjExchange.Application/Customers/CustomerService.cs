using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Common;
using LenjExchange.Application.Ledger;
using LenjExchange.Application.Numbering;
using LenjExchange.Domain.Common;
using LenjExchange.Domain.Parties;
using LenjExchange.Domain.Security;
using LenjExchange.Domain.Text;
using Microsoft.EntityFrameworkCore;

namespace LenjExchange.Application.Customers;

public sealed record CustomerInput(string FullName, string? Phone = null, string? Address = null, string? Notes = null, string? Code = null);

public sealed class CustomerService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly NumberingService _numbering;
    private readonly AccountDirectory _accounts;
    private readonly AuditService _audit;
    private readonly UnitOfWork _uow;

    public CustomerService(IAppDbContext db, ICurrentUser user, IClock clock, NumberingService numbering,
        AccountDirectory accounts, AuditService audit, UnitOfWork uow)
    {
        _db = db;
        _user = user;
        _clock = clock;
        _numbering = numbering;
        _accounts = accounts;
        _audit = audit;
        _uow = uow;
    }

    public Task<Customer> CreateAsync(CustomerInput input, CancellationToken ct = default)
    {
        _user.Demand(Permission.ManageCustomers);
        return _uow.ExecuteAsync(async () =>
        {
            var code = string.IsNullOrWhiteSpace(input.Code)
                ? await _numbering.NextAsync(DocumentKinds.Customer, _clock.UtcNow, ct)
                : DigitNormalizer.Normalize(input.Code).Trim();
            if (await _db.Customers.AnyAsync(c => c.Code == code, ct))
                throw new DomainException("customer_code_dup", $"کد مشتری «{code}» تکراری است.");
            var customer = new Customer(code, DigitNormalizer.NormalizeLetters(input.FullName),
                DigitNormalizer.Normalize(input.Phone), input.Address, input.Notes);
            customer.StampCreated(_user.UserName, _clock.UtcNow);
            _db.Customers.Add(customer);
            await _accounts.ForCustomerAsync(customer, ct);
            _audit.Record("customer.create", nameof(Customer), customer.Id, customer.FullName);
            return customer;
        }, ct);
    }

    public async Task<Customer> UpdateAsync(Guid id, CustomerInput input, CancellationToken ct = default)
    {
        _user.Demand(Permission.ManageCustomers);
        var c = await GetAsync(id, ct);
        c.Update(DigitNormalizer.NormalizeLetters(input.FullName), DigitNormalizer.Normalize(input.Phone), input.Address, input.Notes);
        _audit.Record("customer.update", nameof(Customer), c.Id);
        await _db.SaveChangesAsync(ct);
        return c;
    }

    public async Task SetActiveAsync(Guid id, bool active, CancellationToken ct = default)
    {
        _user.Demand(Permission.ManageCustomers);
        var c = await GetAsync(id, ct);
        c.SetActive(active);
        _audit.Record(active ? "customer.activate" : "customer.deactivate", nameof(Customer), c.Id);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Soft delete only; the customer and its ledger history remain in the database.</summary>
    public async Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        _user.Demand(Permission.ManageCustomers);
        var c = await GetAsync(id, ct);
        c.SoftDelete(_clock.UtcNow);
        _audit.Record("customer.delete", nameof(Customer), c.Id);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<Customer> GetAsync(Guid id, CancellationToken ct = default) =>
        await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("مشتری");

    public async Task<IReadOnlyList<Customer>> SearchAsync(string? text, bool includeInactive = false, CancellationToken ct = default)
    {
        var q = _db.Customers.AsNoTracking().Where(c => !c.IsDeleted);
        if (!includeInactive) q = q.Where(c => c.IsActive);
        var term = DigitNormalizer.NormalizeLetters(text).Trim();
        if (term.Length > 0)
            q = q.Where(c => c.FullName.Contains(term) || c.Code.Contains(term) || (c.Phone != null && c.Phone.Contains(term)));
        return await q.OrderBy(c => c.FullName).Take(1000).ToListAsync(ct);
    }
}
