using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Positions;
using WealthLedger.Application.Setup;
using WealthLedger.Infrastructure.LocalData;
using WealthLedger.Infrastructure.Persistence;

namespace WealthLedger.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddWealthLedgerInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        LocalDataRuntimeContext runtimeContext)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(runtimeContext);

        services.AddSingleton(runtimeContext);
        var pathEnvironment = LocalDataPathEnvironment.Create(runtimeContext);
        services.AddSingleton(pathEnvironment);
        services.AddSingleton(
            _ => new LocalDataPathResolver(configuration, pathEnvironment));
        services.AddSingleton(
            serviceProvider => new LocalDatabaseOwnershipGuard(
                serviceProvider.GetRequiredService<LocalDataPathResolver>()));
        services.AddSingleton(_ => new SqliteDatabaseVerifier());
        services.AddSingleton(
            serviceProvider => new LocalBackupPackageReader(
                serviceProvider.GetRequiredService<SqliteDatabaseVerifier>()));
        services.AddSingleton<ILocalDataOperationHooks>(
            NoOpLocalDataOperationHooks.Instance);
        services.AddSingleton(
            serviceProvider => new SqliteBackupService(
                serviceProvider.GetRequiredService<LocalDataPathResolver>(),
                serviceProvider.GetRequiredService<SqliteDatabaseVerifier>(),
                serviceProvider.GetRequiredService<LocalBackupPackageReader>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetRequiredService<ILocalDataOperationHooks>()));
        services.AddSingleton<ILocalDataStatusReader>(
            serviceProvider => new SqliteLocalDataStatusReader(
                serviceProvider.GetRequiredService<LocalDataPathResolver>(),
                serviceProvider.GetRequiredService<LocalDatabaseOwnershipGuard>(),
                serviceProvider.GetRequiredService<SqliteDatabaseVerifier>(),
                serviceProvider.GetRequiredService<LocalBackupPackageReader>()));
        services.AddSingleton<ILocalDatabaseInitializer>(
            serviceProvider => new SqliteLocalDatabaseInitializer(
                serviceProvider.GetRequiredService<LocalDataPathResolver>(),
                serviceProvider.GetRequiredService<LocalDatabaseOwnershipGuard>(),
                serviceProvider.GetRequiredService<SqliteDatabaseVerifier>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetRequiredService<ILocalDataOperationHooks>()));
        services.AddSingleton<ILocalBackupCreator>(
            serviceProvider => new SqliteLocalBackupCreator(
                serviceProvider.GetRequiredService<LocalDataPathResolver>(),
                serviceProvider.GetRequiredService<LocalDatabaseOwnershipGuard>(),
                serviceProvider.GetRequiredService<SqliteBackupService>()));
        services.AddSingleton<ILocalBackupVerifier>(
            serviceProvider => new SqliteLocalBackupVerifier(
                serviceProvider.GetRequiredService<LocalDataPathResolver>(),
                serviceProvider.GetRequiredService<LocalBackupPackageReader>()));
        services.AddSingleton<ILocalApiDatabaseStartup>(
            serviceProvider => new LocalApiDatabaseStartup(
                serviceProvider.GetRequiredService<LocalDataPathResolver>(),
                serviceProvider.GetRequiredService<LocalDatabaseOwnershipGuard>(),
                serviceProvider.GetRequiredService<SqliteDatabaseVerifier>()));

        services.AddDbContext<WealthLedgerDbContext>(
            (serviceProvider, options) =>
            {
                var pathResult = serviceProvider
                    .GetRequiredService<LocalDataPathResolver>()
                    .ResolveDatabasePath();

                if (!pathResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        pathResult.Failure!.Message);
                }

                options.UseSqlite(
                    SqliteLocalDataConnectionFactory.BuildConnectionString(
                        pathResult.Value!.FullPath));
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
