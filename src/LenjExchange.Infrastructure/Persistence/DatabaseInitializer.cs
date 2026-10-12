using LenjExchange.Application.MasterData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LenjExchange.Infrastructure.Persistence;

/// <summary>Applies EF migrations and the idempotent seed (system accounts, exchangers, agents, recipient).</summary>
public sealed class DatabaseInitializer
{
    private readonly LenjDbContext _db;
    private readonly MasterDataService _master;
    private readonly ILogger<DatabaseInitializer> _log;

    public DatabaseInitializer(LenjDbContext db, MasterDataService master, ILogger<DatabaseInitializer>? log = null)
    {
        _db = db;
        _master = master;
        _log = log ?? NullLogger<DatabaseInitializer>.Instance;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var pending = (await _db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Count > 0) _log.LogInformation("Applying {Count} migrations: {Names}", pending.Count, string.Join(", ", pending));
        await _db.Database.MigrateAsync(ct);
        await _master.SeedAsync(ct);
    }
}

/// <summary>Used by <c>dotnet ef</c> at design time.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LenjDbContext>
{
    public LenjDbContext CreateDbContext(string[] args)
    {
        var b = new DbContextOptionsBuilder<LenjDbContext>();
        DependencyInjection.Configure(b, DatabaseOptions.SqliteFile("design-time.db"));
        return new LenjDbContext(b.Options);
    }
}
