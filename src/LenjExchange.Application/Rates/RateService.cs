using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Common;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Rates;
using LenjExchange.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace LenjExchange.Application.Rates;

/// <summary>Daily reference rates. Documents copy the rate they use, so changes here never affect history.</summary>
public sealed class RateService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly AuditService _audit;

    public RateService(IAppDbContext db, ICurrentUser user, IClock clock, AuditService audit)
    {
        _db = db;
        _user = user;
        _clock = clock;
        _audit = audit;
    }

    public async Task<DailyRate> SetDailyRateAsync(DateTime effectiveDateUtc, ExchangeRate rate, string? notes = null, CancellationToken ct = default)
    {
        _user.Demand(Permission.ManageRates);
        var r = new DailyRate(effectiveDateUtc, rate, notes);
        r.StampCreated(_user.UserName, _clock.UtcNow);
        _db.DailyRates.Add(r);
        _audit.Record("rate.set", nameof(DailyRate), r.Id, rate.Describe());
        await _db.SaveChangesAsync(ct);
        return r;
    }

    public async Task UpdateDailyRateAsync(Guid id, decimal rate, string? notes, CancellationToken ct = default)
    {
        _user.Demand(Permission.ManageRates);
        var r = await _db.DailyRates.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("نرخ");
        r.Update(rate, notes);
        _audit.Record("rate.update", nameof(DailyRate), r.Id, rate.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Most recent rate for the pair on/before the given instant (used as a default suggestion only).</summary>
    public async Task<ExchangeRate?> LatestAsync(CurrencyCode baseCurrency, CurrencyCode quoteCurrency, DateTime? asOfUtc = null, CancellationToken ct = default)
    {
        var at = asOfUtc ?? _clock.UtcNow;
        var rows = await _db.DailyRates.AsNoTracking()
            .Where(r => r.BaseCurrency == baseCurrency && r.QuoteCurrency == quoteCurrency && r.EffectiveDateUtc <= at)
            .OrderByDescending(r => r.EffectiveDateUtc).ThenByDescending(r => r.CreatedAtUtc).FirstOrDefaultAsync(ct);
        return rows?.ToExchangeRate();
    }

    public Task<List<DailyRate>> ListAsync(int take = 500, CancellationToken ct = default) =>
        _db.DailyRates.AsNoTracking().OrderByDescending(r => r.EffectiveDateUtc).Take(take).ToListAsync(ct);
}
