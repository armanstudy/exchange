using LenjExchange.Application;
using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Customers;
using LenjExchange.Application.MasterData;
using LenjExchange.Application.Security;
using LenjExchange.Domain.Security;
using LenjExchange.Infrastructure;
using LenjExchange.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace LenjExchange.Tests.Support;

public sealed class FixedClock : IClock
{
    public FixedClock(DateTime utcNow, TimeZoneInfo zone)
    {
        UtcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        TimeZone = zone;
    }

    public DateTime UtcNow { get; set; }
    public TimeZoneInfo TimeZone { get; }

    public static TimeZoneInfo Tehran => TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Iran Standard Time" : "Asia/Tehran");
}

/// <summary>Real DI graph (Application + Infrastructure) over a temporary SQLite file with migrations applied.</summary>
public sealed class TestApp : IAsyncDisposable
{
    /// <summary>Monday 2026-10-12 12:00 Tehran = 1405/07/20.</summary>
    public static readonly DateTime DefaultNowUtc = new(2026, 10, 12, 8, 30, 0, DateTimeKind.Utc);

    private ServiceProvider _sp = null!;
    private IServiceScope _scope = null!;

    public string Root { get; }
    public FixedClock Clock { get; }
    public AppPaths Paths { get; }
    public DatabaseOptions Db { get; }

    private TestApp()
    {
        Root = Path.Combine(Path.GetTempPath(), "lenj-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Clock = new FixedClock(DefaultNowUtc, FixedClock.Tehran);
        Paths = new AppPaths(Path.Combine(Root, "data"), Path.Combine(Root, "docs"));
        Paths.EnsureCreated();
        Db = DatabaseOptions.SqliteFile(Paths.DatabasePath);
    }

    public static async Task<TestApp> CreateAsync(Role signInAs = Role.Admin)
    {
        var app = new TestApp();
        app.Build();
        await app.Get<DatabaseInitializer>().InitializeAsync();
        await app.SignInAsync(signInAs);
        return app;
    }

    private void Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(Clock);
        services.AddLenjApplication();
        services.AddSingleton(new PasswordHasher(10_000));
        services.AddLenjInfrastructure(Db, Paths);
        _sp = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        _scope = _sp.CreateScope();
    }

    public T Get<T>() where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();

    public CurrentUserSession Session => Get<CurrentUserSession>();

    /// <summary>Fresh scope (new DbContext) — proves data was actually persisted.</summary>
    public void NewScope()
    {
        _scope.Dispose();
        _scope = _sp.CreateScope();
    }

    public async Task<User> SignInAsync(Role role)
    {
        var users = Get<UserService>();
        var name = role == Role.Admin ? "admin" : "operator";
        if (await users.IsFirstRunAsync())
            await users.CreateInitialAdminAsync("admin", "مدیر سیستم", "Admin12345");
        else
            await users.LoginAsync("admin", "Admin12345");
        if (role == Role.Operator)
        {
            if (await users.LoginAsync(name, "Operator123") is null)
            {
                await users.CreateUserAsync(name, "اپراتور", "Operator123", Role.Operator);
                await users.LoginAsync(name, "Operator123");
            }
        }
        return Session.User!;
    }

    public async Task<LenjExchange.Domain.Parties.Customer> NewCustomerAsync(string name = "علی احمدی") =>
        await Get<CustomerService>().CreateAsync(new CustomerInput(name, "09120000000"));

    public Task<LenjExchange.Domain.Parties.Exchanger> ExchangerAsync(string name = "صرافی عابدی") => Get<MasterDataService>().ExchangerByNameAsync(name);
    public Task<LenjExchange.Domain.Parties.Agent> AgentAsync(string name = "آیدین") => Get<MasterDataService>().AgentByNameAsync(name);
    public Task<LenjExchange.Domain.Parties.FinalRecipient> MohammadAsync() => Get<MasterDataService>().RecipientByNameAsync("محمد");

    /// <summary>Local Tehran wall-clock time → UTC.</summary>
    public DateTime Local(int y, int m, int d, int h = 10, int min = 0) => Clock.ToUtc(new DateTime(y, m, d, h, min, 0));

    /// <summary>Shamsi date → UTC at the given local hour.</summary>
    public DateTime Shamsi(int y, int m, int d, int h = 10) => Clock.ToUtc(LenjExchange.Domain.Shamsi.ShamsiDate.ToGregorian(y, m, d).AddHours(h));

    public async ValueTask DisposeAsync()
    {
        _scope.Dispose();
        await _sp.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            foreach (var f in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(Root, true);
        }
        catch (IOException) { }
    }
}
