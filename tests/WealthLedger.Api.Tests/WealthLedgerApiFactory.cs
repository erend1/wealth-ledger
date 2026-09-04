using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WealthLedger.Api.Contracts;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Setup;
using WealthLedger.Infrastructure;
using WealthLedger.Infrastructure.LocalData;
using WealthLedger.Infrastructure.Persistence;

namespace WealthLedger.Api.Tests;

internal sealed class WealthLedgerApiFactory
    : WebApplicationFactory<Program>
{
    private readonly string _directoryPath;
    private readonly bool _setupEnabled;
    private readonly ApiTestStartupMode _startupMode;

    internal WealthLedgerApiFactory(
        ApiTestStartupMode startupMode =
            ApiTestStartupMode.Ready,
        bool setupEnabled = true)
    {
        _startupMode = startupMode;
        _setupEnabled = setupEnabled;

        _directoryPath = Path.Combine(
            Path.GetTempPath(),
            "WealthLedger.Api.Tests",
            Guid.NewGuid().ToString("N"));

        var dataDirectory = Path.Combine(
            _directoryPath,
            "data");

        BackupDirectory = Path.Combine(
            _directoryPath,
            "backups");

        Directory.CreateDirectory(
            dataDirectory);

        DatabasePath = Path.Combine(
            dataDirectory,
            "wealthledger.db");

        if (_startupMode
            != ApiTestStartupMode.StorageUninitialized)
        {
            MigrateCurrentDatabase();
        }

        if (_startupMode
            == ApiTestStartupMode.Ready)
        {
            ReadySetup =
                PrepareReadyWorkspace();
        }
    }

    internal string DatabasePath { get; }

    internal string BackupDirectory { get; }

    internal InitializeCoreLedgerResponse ReadySetup
    {
        get;
        private set;
    } = null!;

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(
            (_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    CreateConfiguration());
            });
    }

    private void MigrateCurrentDatabase()
    {
        var connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                ForeignKeys = true,
                Pooling = false
            }.ToString();

        var options =
            new DbContextOptionsBuilder<
                    WealthLedgerDbContext>()
                .UseSqlite(connectionString)
                .Options;

        using var context =
            new WealthLedgerDbContext(
                options);

        context.Database.Migrate();
    }

    private InitializeCoreLedgerResponse
        PrepareReadyWorkspace()
    {
        var configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    CreateConfiguration())
                .Build();

        var services =
            new ServiceCollection();

        services.AddSingleton<TimeProvider>(
            TimeProvider.System);

        services.AddWealthLedgerInfrastructure(
            configuration,
            new LocalDataRuntimeContext(
                "Testing",
                Directory.GetCurrentDirectory()));

        services.AddScoped<
            InitializeCoreLedgerUseCase>();

        services.AddScoped<
            CreateLocalBackupUseCase>();

        using var serviceProvider =
            services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateScopes = true,
                    ValidateOnBuild = true
                });

        using var scope =
            serviceProvider.CreateScope();

        var setupResult =
            scope.ServiceProvider
                .GetRequiredService<
                    InitializeCoreLedgerUseCase>()
                .ExecuteAsync(
                    ApiTestData.CreateSetupCommand())
                .GetAwaiter()
                .GetResult();

        var backupResult =
            scope.ServiceProvider
                .GetRequiredService<
                    CreateLocalBackupUseCase>()
                .ExecuteAsync()
                .GetAwaiter()
                .GetResult();

        if (!backupResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Synthetic Ready backup creation failed: "
                + $"{backupResult.Failure!.Category}.");
        }

        return new InitializeCoreLedgerResponse(
            setupResult.HouseholdId,
            setupResult.HouseholdMemberId,
            setupResult.InstitutionId,
            setupResult.PortfolioId,
            setupResult.AccountId,
            setupResult.CashAssetId,
            setupResult.FundAssetId);
    }

    private Dictionary<string, string?>
        CreateConfiguration()
        => new()
        {
            ["Storage:DatabasePath"] =
                DatabasePath,

            ["Backup:Directory"] =
                BackupDirectory,

            ["Backup:DestinationSeparationConfirmed"] =
                "true",

            ["Backup:DestinationEncryptionConfirmed"] =
                "true",

            ["Setup:Enabled"] =
                _setupEnabled.ToString(),

            ["urls"] =
                "http://127.0.0.1:0",

            ["AllowedHosts"] =
                "localhost;127.0.0.1;[::1]"
        };

    protected override void Dispose(
        bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        SqliteConnection.ClearAllPools();

        if (Directory.Exists(
                _directoryPath))
        {
            Directory.Delete(
                _directoryPath,
                recursive: true);
        }
    }
}