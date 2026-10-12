using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using LenjExchange.Application;
using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Backup;
using LenjExchange.Application.Settings;
using LenjExchange.Desktop.Services;
using LenjExchange.Desktop.ViewModels;
using LenjExchange.Domain.Common;
using LenjExchange.Infrastructure;
using LenjExchange.Infrastructure.Backup;
using LenjExchange.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace LenjExchange.Desktop;

public partial class App
{
    private IHost? _host;

    public static IServiceProvider Services => ((App)Current)._host!.Services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var culture = new CultureInfo("fa-IR");
        Thread.CurrentThread.CurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        var paths = new AppPaths();
        paths.EnsureCreated();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(System.IO.Path.Combine(paths.LogsRoot, "lenj-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 60)
            .CreateLogger();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Fatal(args.ExceptionObject as Exception, "Unhandled exception");

        try
        {
            _host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureServices(services => ConfigureServices(services, paths))
                .Build();

            using (var scope = _host.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
            await _host.StartAsync();

            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
            await _host.Services.GetRequiredService<NavigationService>().StartAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Startup failed");
            MessageBox.Show($"راه‌اندازی برنامه با خطا مواجه شد:\n{ex.Message}\n\nجزئیات در پوشه Logs ثبت شد.", "سامانه مالی لنج",
                MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            Shutdown(1);
        }
    }

    private static void ConfigureServices(IServiceCollection services, AppPaths paths)
    {
        var clock = new SystemClock();
        Display.Clock = clock;
        services.AddSingleton<IClock>(clock);
        services.AddLenjApplication();
        services.AddLenjInfrastructure(DatabaseOptions.SqliteFile(paths.DatabasePath), paths);
        services.AddHostedService<AutoBackupWorker>();

        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();

        services.AddScoped<LoginViewModel>();
        services.AddScoped<DashboardViewModel>();
        services.AddScoped<CustomersViewModel>();
        services.AddScoped<NewReceiptViewModel>();
        services.AddScoped<CasesViewModel>();
        services.AddScoped<ExchangersViewModel>();
        services.AddScoped<AgentsViewModel>();
        services.AddScoped<ChinaReceiptsViewModel>();
        services.AddScoped<RatesViewModel>();
        services.AddScoped<ReportsViewModel>();
        services.AddScoped<AttachmentsViewModel>();
        services.AddScoped<UsersViewModel>();
        services.AddScoped<SettingsViewModel>();
        services.AddScoped<BackupViewModel>();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "UI exception");
        var message = e.Exception is DomainException d ? d.Message : "خطای پیش‌بینی‌نشده رخ داد. جزئیات در فایل گزارش ثبت شد.\n" + e.Exception.Message;
        MessageBox.Show(message, "خطا", MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK,
            MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        e.Handled = true;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            try
            {
                using var scope = _host.Services.CreateScope();
                var session = scope.ServiceProvider.GetRequiredService<CurrentUserSession>();
                var settings = await scope.ServiceProvider.GetRequiredService<SettingsService>().GetBackupAsync();
                if (session.IsAuthenticated && settings.BackupOnExit)
                    await scope.ServiceProvider.GetRequiredService<IBackupService>().CreateBackupAsync(null, "exit");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Backup on exit failed");
            }
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }
        await Log.CloseAndFlushAsync();
        base.OnExit(e);
    }
}
