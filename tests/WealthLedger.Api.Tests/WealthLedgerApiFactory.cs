using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
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
    private readonly LocalDataFailure? _backupCreationFailure;
    private readonly bool _destinationEncryptionConfirmed;
    private readonly bool _destinationSeparationConfirmed;
    private readonly bool _setupEnabled;
    private readonly ApiTestStartupMode _startupMode;

    internal WealthLedgerApiFactory(
        ApiTestStartupMode startupMode =
            ApiTestStartupMode.Ready,
        bool setupEnabled = true,
        LocalDataFailure? backupCreationFailure = null,
        bool destinationSeparationConfirmed = true,
        bool destinationEncryptionConfirmed = true)
    {
        _startupMode = startupMode;
        _setupEnabled = setupEnabled;
        _backupCreationFailure = backupCreationFailure;
        _destinationSeparationConfirmed =
            destinationSeparationConfirmed;
        _destinationEncryptionConfirmed =
            destinationEncryptionConfirmed;

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

        if (_startupMode is not (
                ApiTestStartupMode.StorageUninitialized
                or ApiTestStartupMode.Blocked))
        {
            MigrateDatabase(DatabasePath);
        }

        if (_startupMode is
            ApiTestStartupMode.InitialBackupRequired
            or ApiTestStartupMode.Ready)
        {
            ReadySetup = PrepareWorkspace(
                    DatabasePath,
                    createVerifiedBackup:
                        _startupMode
                        == ApiTestStartupMode.Ready)
                .Setup;
        }
    }

    internal string DatabasePath { get; }

    internal string BackupDirectory { get; }

    internal InitializeCoreLedgerResponse ReadySetup
    {
        get;
        private set;
    } = null!;

    internal TestLogCollector Logs { get; } = new();

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureLogging(
            logging => logging.AddProvider(Logs));

        builder.ConfigureAppConfiguration(
            (_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    CreateHostConfiguration());
            });

        if (_backupCreationFailure is not null)
        {
            builder.ConfigureTestServices(
                services =>
                {
                    services.RemoveAll<ILocalBackupCreator>();
                    services.AddSingleton<ILocalBackupCreator>(
                        new FailingLocalBackupCreator(
                            _backupCreationFailure));
                });
        }
    }

    private static void MigrateDatabase(string databasePath)
    {
        var connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
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

    internal WealthLedgerDbContext CreateDbContext()
    {
        var connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                ForeignKeys = true,
                Pooling = false
            }.ToString();

        var options =
            new DbContextOptionsBuilder<WealthLedgerDbContext>()
                .UseSqlite(connectionString)
                .Options;

        return new WealthLedgerDbContext(options);
    }

    internal FileStream AcquireDatabaseOwnership()
    {
        var lockPath = Path.ChangeExtension(
            DatabasePath,
            ".wloperation.lock");

        return new FileStream(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 1,
            FileOptions.WriteThrough);
    }

    internal string CreateUnrelatedVerifiedBackup()
    {
        var unrelatedDataDirectory = Path.Combine(
            _directoryPath,
            "unrelated",
            "data");
        Directory.CreateDirectory(unrelatedDataDirectory);
        var unrelatedDatabasePath = Path.Combine(
            unrelatedDataDirectory,
            "wealthledger.db");

        MigrateDatabase(unrelatedDatabasePath);
        var prepared = PrepareWorkspace(
            unrelatedDatabasePath,
            createVerifiedBackup: true);

        return prepared.Backup!.FilePath;
    }

    internal string[] GetBackupPackagePaths()
        => Directory.Exists(BackupDirectory)
            ? Directory.GetFiles(
                    BackupDirectory,
                    "*.wlbackup",
                    SearchOption.TopDirectoryOnly)
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];

    internal async Task<LocalDataStatus> ReadLocalDataStatusAsync()
    {
        using var scope = Services.CreateScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<GetLocalDataStatusUseCase>()
            .ExecuteAsync();

        Assert.True(result.Succeeded, result.Failure?.Message);
        return result.Value!;
    }

    private PreparedWorkspace PrepareWorkspace(
        string databasePath,
        bool createVerifiedBackup)
    {
        var configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    CreateConfiguration(
                        databasePath,
                        BackupDirectory,
                        _setupEnabled,
                        _destinationSeparationConfirmed,
                        _destinationEncryptionConfirmed))
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

        LocalBackupCreation? backup = null;

        if (createVerifiedBackup)
        {
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

            backup = backupResult.Value;
        }

        return new PreparedWorkspace(
            new InitializeCoreLedgerResponse(
                setupResult.HouseholdId,
                setupResult.HouseholdMemberId,
                setupResult.InstitutionId,
                setupResult.PortfolioId,
                setupResult.AccountId,
                setupResult.CashAssetId,
                setupResult.FundAssetId),
            backup);
    }

    private Dictionary<string, string?> CreateHostConfiguration()
        => CreateConfiguration(
            DatabasePath,
            _startupMode == ApiTestStartupMode.Blocked
                ? null
                : BackupDirectory,
            _setupEnabled,
            _destinationSeparationConfirmed,
            _destinationEncryptionConfirmed);

    private static Dictionary<string, string?> CreateConfiguration(
        string databasePath,
        string? backupDirectory,
        bool setupEnabled,
        bool destinationSeparationConfirmed,
        bool destinationEncryptionConfirmed)
        => new()
        {
            ["Storage:DatabasePath"] = databasePath,
            ["Backup:Directory"] = backupDirectory,
            ["Backup:DestinationSeparationConfirmed"] =
                destinationSeparationConfirmed.ToString(),
            ["Backup:DestinationEncryptionConfirmed"] =
                destinationEncryptionConfirmed.ToString(),
            ["Setup:Enabled"] = setupEnabled.ToString(),
            ["urls"] = "http://127.0.0.1:0",
            ["AllowedHosts"] = "localhost;127.0.0.1;[::1]"
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

    private sealed record PreparedWorkspace(
        InitializeCoreLedgerResponse Setup,
        LocalBackupCreation? Backup);

    private sealed class FailingLocalBackupCreator
        : ILocalBackupCreator
    {
        private readonly LocalDataFailure _failure;

        internal FailingLocalBackupCreator(
            LocalDataFailure failure)
        {
            _failure = failure;
        }

        public Task<LocalDataOperationResult<LocalBackupCreation>> CreateAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                new LocalDataOperationResult<LocalBackupCreation>(
                    Value: null,
                    _failure));
    }
}
