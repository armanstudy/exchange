using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Backup;
using LenjExchange.Application.Documents;
using LenjExchange.Infrastructure.Backup;
using LenjExchange.Infrastructure.Excel;
using LenjExchange.Infrastructure.Pdf;
using LenjExchange.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LenjExchange.Infrastructure;

public enum DatabaseProvider
{
    Sqlite = 1
    // SqlServer / PostgreSql: add the provider package, a provider-specific migrations assembly and a case below.
}

public sealed record DatabaseOptions(DatabaseProvider Provider, string ConnectionString)
{
    public static DatabaseOptions SqliteFile(string path) =>
        new(DatabaseProvider.Sqlite, new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true, Pooling = true }.ToString());

    /// <summary>Absolute path of the SQLite file (null for in-memory or non-SQLite providers).</summary>
    public string? SqliteFilePath
    {
        get
        {
            if (Provider != DatabaseProvider.Sqlite) return null;
            var ds = new SqliteConnectionStringBuilder(ConnectionString).DataSource;
            return string.IsNullOrEmpty(ds) || ds.Contains(":memory:", StringComparison.OrdinalIgnoreCase) ? null : Path.GetFullPath(ds);
        }
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddLenjInfrastructure(this IServiceCollection services, DatabaseOptions db, IAppPaths paths)
    {
        services.AddSingleton(db);
        services.AddSingleton(paths);
        services.AddDbContext<LenjDbContext>(o => Configure(o, db));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<LenjDbContext>());
        services.AddSingleton<IReceiptPdfRenderer, ReceiptPdfRenderer>();
        services.AddSingleton<IReportPdfRenderer, ReportPdfRenderer>();
        services.AddSingleton<IExcelExporter, ClosedXmlExporter>();
        services.AddScoped<IBackupService, SqliteBackupService>();
        services.AddScoped<DatabaseInitializer>();
        return services;
    }

    public static void Configure(DbContextOptionsBuilder o, DatabaseOptions db)
    {
        switch (db.Provider)
        {
            case DatabaseProvider.Sqlite:
                o.UseSqlite(db.ConnectionString, x => x.MigrationsAssembly(typeof(LenjDbContext).Assembly.FullName));
                break;
            default:
                throw new NotSupportedException($"Provider {db.Provider} is not configured.");
        }
    }
}
