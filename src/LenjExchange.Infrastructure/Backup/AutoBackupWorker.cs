using LenjExchange.Application.Backup;
using LenjExchange.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LenjExchange.Infrastructure.Backup;

/// <summary>Periodic automatic backup driven by <see cref="BackupSettings"/>; checks every 15 minutes.</summary>
public sealed class AutoBackupWorker : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AutoBackupWorker> _log;

    public AutoBackupWorker(IServiceScopeFactory scopes, ILogger<AutoBackupWorker> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "Automatic backup failed");
            }
            await Task.Delay(CheckInterval, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        }
    }

    public async Task<bool> RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var settings = await scope.ServiceProvider.GetRequiredService<SettingsService>().GetBackupAsync(ct);
        if (!settings.AutoBackupEnabled) return false;
        var backup = scope.ServiceProvider.GetRequiredService<IBackupService>();
        var latest = (await backup.ListBackupsAsync(null, ct)).FirstOrDefault();
        if (latest is not null && DateTime.UtcNow - latest.CreationTimeUtc < TimeSpan.FromHours(Math.Max(1, settings.AutoBackupIntervalHours)))
            return false;
        var result = await backup.CreateBackupAsync(null, "auto", ct);
        _log.LogInformation("Automatic backup written to {Path}", result.ZipPath);
        return true;
    }
}
