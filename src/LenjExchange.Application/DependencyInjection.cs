using LenjExchange.Application.Abstractions;
using LenjExchange.Application.Common;
using LenjExchange.Application.Customers;
using LenjExchange.Application.Documents;
using LenjExchange.Application.Ledger;
using LenjExchange.Application.MasterData;
using LenjExchange.Application.Numbering;
using LenjExchange.Application.Rates;
using LenjExchange.Application.Reporting;
using LenjExchange.Application.Security;
using LenjExchange.Application.Settings;
using LenjExchange.Application.Transfers;
using Microsoft.Extensions.DependencyInjection;

namespace LenjExchange.Application;

public static class DependencyInjection
{
    /// <summary>Registers application services as scoped (one DbContext/unit of work per scope).</summary>
    public static IServiceCollection AddLenjApplication(this IServiceCollection services)
    {
        services.AddSingleton<CurrentUserSession>();
        services.AddSingleton<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserSession>());
        services.AddSingleton(new PasswordHasher());

        services.AddScoped<UnitOfWork>();
        services.AddScoped<AuditService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<NumberingService>();
        services.AddScoped<AccountDirectory>();
        services.AddScoped<LedgerService>();
        services.AddScoped<CustomerService>();
        services.AddScoped<MasterDataService>();
        services.AddScoped<TransferCaseService>();
        services.AddScoped<ReceiptService>();
        services.AddScoped<TransferFlowService>();
        services.AddScoped<ReversalService>();
        services.AddScoped<RateService>();
        services.AddScoped<ReportService>();
        services.AddScoped<UserService>();
        services.AddScoped<AttachmentService>();
        services.AddScoped<ReceiptDocumentService>();
        return services;
    }
}
