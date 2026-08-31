using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Positions;
using WealthLedger.Application.Setup;
using WealthLedger.Infrastructure.Persistence;

namespace WealthLedger.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddWealthLedgerInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<WealthLedgerDbContext>(
            options =>
            {
                var connectionString = configuration
                    .GetConnectionString("WealthLedger")
                    ?? throw new InvalidOperationException(
                        "Connection string 'WealthLedger' is required.");

                options.UseSqlite(connectionString);
            });

        services.AddScoped<ILedgerReferenceData, EfCoreLedgerReferenceData>();
        services.AddScoped<IPostedEntrySource, EfCorePostedEntrySource>();
        services.AddScoped<ICoreLedgerSetupStore, EfCoreLedgerSetupStore>();

        services.AddScoped<EfCoreLedgerPostingStore>();
        services.AddScoped<ILedgerPostingStore>(
            serviceProvider =>
                serviceProvider.GetRequiredService<EfCoreLedgerPostingStore>());
        services.AddScoped<ILedgerSubmissionStore>(
            serviceProvider =>
                serviceProvider.GetRequiredService<EfCoreLedgerPostingStore>());

        services.AddScoped<ILedgerTransactionReadStore, EfCoreLedgerTransactionReadStore>();

        services.AddScoped<ILedgerReversalStore, EfCoreLedgerReversalStore>();

        return services;
    }
}
