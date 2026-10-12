using LenjExchange.Application.Security;
using LenjExchange.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace LenjExchange.Desktop.Services;

/// <summary>
/// Each page gets its own DI scope (and therefore its own DbContext); the previous page's scope is disposed on
/// navigation, so long-running sessions never accumulate tracked entities.
/// </summary>
public sealed class NavigationService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ShellViewModel _shell;
    private IServiceScope? _current;

    public NavigationService(IServiceScopeFactory scopes, ShellViewModel shell)
    {
        _scopes = scopes;
        _shell = shell;
        _shell.Navigation = this;
    }

    public async Task StartAsync() => await NavigateAsync<LoginViewModel>();

    public async Task NavigateAsync<TViewModel>(Func<TViewModel, Task>? init = null) where TViewModel : ViewModelBase
    {
        var scope = _scopes.CreateScope();
        var vm = scope.ServiceProvider.GetRequiredService<TViewModel>();
        if (init is not null) await init(vm);
        _shell.CurrentPage = vm;
        _current?.Dispose();
        _current = scope;
        await vm.LoadAsync();
    }

    public Task NavigateAsync(string key) => key switch
    {
        "dashboard" => NavigateAsync<DashboardViewModel>(),
        "customers" => NavigateAsync<CustomersViewModel>(),
        "receipt" => NavigateAsync<NewReceiptViewModel>(),
        "cases" => NavigateAsync<CasesViewModel>(),
        "exchangers" => NavigateAsync<ExchangersViewModel>(),
        "agents" => NavigateAsync<AgentsViewModel>(),
        "china" => NavigateAsync<ChinaReceiptsViewModel>(),
        "rates" => NavigateAsync<RatesViewModel>(),
        "reports" => NavigateAsync<ReportsViewModel>(),
        "attachments" => NavigateAsync<AttachmentsViewModel>(),
        "users" => NavigateAsync<UsersViewModel>(),
        "settings" => NavigateAsync<SettingsViewModel>(),
        "backup" => NavigateAsync<BackupViewModel>(),
        _ => NavigateAsync<DashboardViewModel>()
    };

    public async Task SignedInAsync()
    {
        using (var scope = _scopes.CreateScope())
            _shell.OnSignedIn(scope.ServiceProvider.GetRequiredService<LenjExchange.Application.Abstractions.CurrentUserSession>());
        await NavigateAsync<DashboardViewModel>();
    }

    public async Task SignOutAsync()
    {
        using (var scope = _scopes.CreateScope())
            scope.ServiceProvider.GetRequiredService<UserService>().Logout();
        _shell.OnSignedOut();
        await NavigateAsync<LoginViewModel>();
    }
}
