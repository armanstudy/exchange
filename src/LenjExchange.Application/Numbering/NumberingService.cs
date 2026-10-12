using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Settings;
using LenjExchange.Domain.Numbering;
using Microsoft.EntityFrameworkCore;

namespace LenjExchange.Application.Numbering;

public static class DocumentKinds
{
    public const string Receipt = "receipt";
    public const string Case = "case";
    public const string Journal = "journal";
    public const string Delivery = "delivery";
    public const string Customer = "customer";
}

/// <summary>
/// Allocates document numbers from DB-backed sequences. Must be called inside the transaction that persists the
/// document so that a rolled-back document does not consume a number; the unique index on each number column
/// is the final guarantee against duplicates.
/// </summary>
public sealed class NumberingService
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly SettingsService _settings;

    public NumberingService(IAppDbContext db, IClock clock, SettingsService settings)
    {
        _db = db;
        _clock = clock;
        _settings = settings;
    }

    public async Task<string> NextAsync(string kind, DateTime documentDateUtc, CancellationToken ct = default)
    {
        var numbering = await _settings.GetNumberingAsync(ct);
        var pattern = kind switch
        {
            DocumentKinds.Receipt => numbering.Receipt,
            DocumentKinds.Case => numbering.Case,
            DocumentKinds.Journal => numbering.Journal,
            DocumentKinds.Delivery => numbering.Delivery,
            DocumentKinds.Customer => numbering.CustomerCode,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var local = _clock.ToLocal(documentDateUtc);
        var key = pattern.SequenceKey(kind, local);

        var seq = _db.NumberSequences.Local.FirstOrDefault(s => s.Key == key)
                  ?? await _db.NumberSequences.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (seq is null)
        {
            seq = new NumberSequence(key);
            _db.NumberSequences.Add(seq);
        }
        var value = seq.Next();
        return pattern.Format(local, value);
    }

    public async Task<string> PreviewAsync(string kind, DateTime documentDateUtc, CancellationToken ct = default)
    {
        var numbering = await _settings.GetNumberingAsync(ct);
        var pattern = kind == DocumentKinds.Receipt ? numbering.Receipt : numbering.Case;
        var local = _clock.ToLocal(documentDateUtc);
        var key = pattern.SequenceKey(kind, local);
        var seq = await _db.NumberSequences.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
        return pattern.Format(local, (seq?.LastValue ?? 0) + 1);
    }
}
