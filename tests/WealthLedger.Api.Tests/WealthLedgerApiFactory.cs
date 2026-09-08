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
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;
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

    internal async Task<ReadyUiLedgerFixture> SeedReadyUiLedgerAsync()
    {
        var createdAt = new DateTimeOffset(
            2026,
            9,
            7,
            6,
            0,
            0,
            TimeSpan.Zero);
        var currency = CurrencyCode.TRY;
        var fundAsset = Asset.Create(
            ReadySetup.FundAssetId,
            "SYNTHETIC_FUND",
            "Synthetic Fund",
            AssetType.Fund,
            AssetUnit.FundUnit,
            currency,
            LotTrackingMode.Required);

        var purchase = LedgerTransaction.CreateDraft(
            Guid.NewGuid(),
            ReadySetup.HouseholdId,
            TransactionType.Buy,
            createdAt,
            orderDate: new DateOnly(2026, 9, 5),
            executionDate: new DateOnly(2026, 9, 6),
            settlementDate: new DateOnly(2026, 9, 7),
            externalReference: "SHELL-PURCHASE-REFERENCE",
            note: "Synthetic shell purchase note.");
        var principal = purchase.AddEntry(
            ReadySetup.PortfolioId,
            ReadySetup.AccountId,
            ReadySetup.FundAssetId,
            QuantityDelta.FromRaw(125_000_000),
            EntryRole.Principal,
            UnitPrice.FromRaw(9_876_543_210, currency));
        var consideration = purchase.AddEntry(
            ReadySetup.PortfolioId,
            ReadySetup.AccountId,
            ReadySetup.CashAssetId,
            QuantityDelta.FromRaw(-12_345_000_000),
            EntryRole.Consideration);
        var cost = purchase.AddCost(
            CostType.Commission,
            CostTreatment.AdditionalCashOutflow,
            Money.FromMinorUnits(250, currency),
            "Synthetic shell cost note.");
        var lot = AssetLot.Create(
            Guid.NewGuid(),
            fundAsset,
            principal,
            Quantity.FromRaw(125_000_000),
            new DateOnly(2026, 9, 6),
            CostBasis.Known(
                Money.FromMinorUnits(12_345, currency)),
            createdAt.AddMinutes(1));
        purchase.Post(createdAt.AddMinutes(5));

        var contribution = LedgerTransaction.CreateDraft(
            Guid.NewGuid(),
            ReadySetup.HouseholdId,
            TransactionType.Contribution,
            createdAt.AddHours(1),
            executionDate: new DateOnly(2026, 9, 7),
            externalReference: "SHELL-CONTRIBUTION-REFERENCE",
            note: "Synthetic shell contribution note.");
        var contributionEntry = contribution.AddEntry(
            ReadySetup.PortfolioId,
            ReadySetup.AccountId,
            ReadySetup.CashAssetId,
            QuantityDelta.FromRaw(50_000_000_000),
            EntryRole.Principal);
        contribution.AttachCashFlowDetail(
            CashFlowCategory.AcademicIncome,
            ReadySetup.HouseholdMemberId);
        contribution.Post(createdAt.AddHours(1).AddMinutes(5));

        await using var context = CreateDbContext();
        var store = new EfCoreLedgerPostingStore(context);
        await store.SavePostedTransactionAsync(purchase, [lot]);
        await store.SavePostedTransactionAsync(contribution, []);

        return new ReadyUiLedgerFixture(
            purchase.Id,
            principal.Id,
            consideration.Id,
            cost.Id,
            lot.Id,
            lot.Allocations.Single().Id,
            contribution.Id,
            contributionEntry.Id);
    }

    internal async Task ArchiveReadyUiMastersAsync()
    {
        await using var context = CreateDbContext();
        var portfolio = await context.Portfolios.SingleAsync(
            row => row.Id == ReadySetup.PortfolioId);
        var account = await context.Accounts.SingleAsync(
            row => row.Id == ReadySetup.AccountId);
        var institution = await context.Institutions.SingleAsync(
            row => row.Id == ReadySetup.InstitutionId);
        var fund = await context.Assets.SingleAsync(
            row => row.Id == ReadySetup.FundAssetId);

        portfolio.Status = PortfolioStatus.Archived;
        portfolio.ClosedAtUtc = new DateTime(
            2026,
            9,
            7,
            8,
            0,
            0,
            DateTimeKind.Utc);
        account.IsActive = false;
        institution.IsActive = false;
        fund.IsActive = false;

        await context.SaveChangesAsync();
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

internal sealed record ReadyUiLedgerFixture(
    Guid PurchaseTransactionId,
    Guid PrincipalEntryId,
    Guid ConsiderationEntryId,
    Guid CostId,
    Guid AssetLotId,
    Guid OpeningAllocationId,
    Guid ContributionTransactionId,
    Guid ContributionEntryId);
