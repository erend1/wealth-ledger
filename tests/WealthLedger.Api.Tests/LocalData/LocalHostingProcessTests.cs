using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using WealthLedger.Api.Contracts;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Application.PhysicalGold;
using WealthLedger.Application.Setup;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;
using WealthLedger.Infrastructure.LocalData;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure;

namespace WealthLedger.Api.Tests.LocalData;

public sealed class LocalHostingProcessTests : IAsyncLifetime
{
    private readonly string _testRoot;
    private readonly string _databasePath;
    private readonly string _backupDirectory;

    public LocalHostingProcessTests()
    {
        _testRoot =
            Path.Combine(
                Path.GetTempPath(),
                "WealthLedger.Api.Tests",
                nameof(LocalHostingProcessTests),
                Guid.NewGuid().ToString("N"));

        _databasePath =
            Path.Combine(
                _testRoot,
                "live",
                "wealthledger.db");

        _backupDirectory =
            Path.Combine(
                _testRoot,
                "backups");

        Directory.CreateDirectory(
            _backupDirectory);
    }

    [Fact]
    public async Task LocalHosting_DefaultTrackedHostStartsOnlyOnLoopback()
    {
        await InitializeDatabaseAsync();
        await using var process = ApiProcess.Start(_databasePath, _backupDirectory);

        var listeningUrl = await process.WaitForListeningUrlAsync();
        var uri = new Uri(listeningUrl);

        Assert.True(
            string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(uri.Host, out var address)
            && IPAddress.IsLoopback(address));

        using var client = new HttpClient();
        using var response = await client.GetAsync(
            new Uri(uri, "/api/not-a-route"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("0.0.0.0", listeningUrl);
    }

    [Fact]
    public async Task LocalHosting_NonLoopbackOverrideExitsBeforeEndpointService()
    {
        await InitializeDatabaseAsync();
        await using var process = ApiProcess.Start(
            _databasePath,
            _backupDirectory,
            "--urls=http://0.0.0.0:0");

        var exitCode = await process.WaitForExitAsync();
        var output = process.CombinedOutput;

        Assert.Equal(
            (int)LocalDataFailureCategory.InvalidInputOrConfiguration,
            exitCode);
        Assert.Contains("STARTUP INVALIDINPUTORCONFIGURATION", output);
        Assert.DoesNotContain("Now listening on", output);
        Assert.DoesNotContain("ConnectionString", output);
        Assert.DoesNotContain(" at ", output);
    }

    [Fact]
    public async Task
        LocalHosting_ReadyOwnershipCollisionBlocksSecondHostAndRecoversAfterExit()
    {
        await PrepareReadyWorkspaceAsync();

        await using var first =
            ApiProcess.Start(
                _databasePath,
                _backupDirectory);

        var firstUrl =
            await first.WaitForListeningUrlAsync();

        Assert.Contains(
            "Local startup mode: Ready",
            first.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);

        using var firstClient =
            new HttpClient();

        using var firstResponse =
            await firstClient.GetAsync(
                new Uri(
                    new Uri(firstUrl),
                    "/api/households"));

        Assert.Equal(
            HttpStatusCode.OK,
            firstResponse.StatusCode);

        /*
         * The second process sees the first Ready host's
         * process-lifetime ownership lease. It must remain alive
         * in Blocked mode rather than exiting or exposing ledger
         * endpoints.
         */
        await using var collision =
            ApiProcess.Start(
                _databasePath,
                _backupDirectory);

        var collisionUrl =
            await collision.WaitForListeningUrlAsync();

        Assert.Contains(
            "Local startup mode: Blocked",
            collision.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            nameof(LocalDataFailureCategory.OwnershipBusy),
            collision.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);

        using var collisionClient =
            new HttpClient();

        using var collisionResponse =
            await collisionClient.GetAsync(
                new Uri(
                    new Uri(collisionUrl),
                    "/api/households"));

        Assert.Equal(
            HttpStatusCode.NotFound,
            collisionResponse.StatusCode);

        await first.StopAsync();

        /*
         * The Blocked collision process owns no lifetime lease,
         * so a fresh process can become Ready after the original
         * Ready owner exits.
         */
        await using var restarted =
            ApiProcess.Start(
                _databasePath,
                _backupDirectory);

        var restartedUrl =
            await restarted.WaitForListeningUrlAsync();

        Assert.Contains(
            "Local startup mode: Ready",
            restarted.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);

        using var restartedClient =
            new HttpClient();

        using var restartedResponse =
            await restartedClient.GetAsync(
                new Uri(
                    new Uri(restartedUrl),
                    "/api/households"));

        Assert.Equal(
            HttpStatusCode.OK,
            restartedResponse.StatusCode);
    }

    [Fact]
    public async Task
        LocalHosting_PhysicalGoldBackupStagesAndReadsBackFromFreshProcess()
    {
        await PrepareReadyWorkspaceAsync();
        var evidence = await CreatePhysicalGoldRecoveryEvidenceAsync();

        Assert.True(File.Exists(evidence.BackupFilePath));
        Assert.True(File.Exists(evidence.RestoredDatabasePath));

        await using var process = ApiProcess.Start(
            evidence.RestoredDatabasePath,
            _backupDirectory);
        var baseAddress = new Uri(await process.WaitForListeningUrlAsync());
        using var client = new HttpClient { BaseAddress = baseAddress };

        var custody = await client.GetFromJsonAsync<
            PhysicalGoldCustodyInventoryResponse>(
            $"/api/households/{evidence.HouseholdId:D}/physical-gold/custody");
        Assert.NotNull(custody);
        var positions = custody.Items
            .Where(x => x.AssetLotId == evidence.AssetLotId)
            .OrderBy(x => x.AccountId)
            .ToArray();
        Assert.Equal(2, positions.Length);
        Assert.Contains(
            positions,
            x => x.AccountId == evidence.SourceVaultId
                 && x.GrossWeightRawE8 == 16_00000000L
                 && x.PieceCount == 1
                 && x.FineWeightGrams == 14.656m);
        Assert.Contains(
            positions,
            x => x.AccountId == evidence.DestinationVaultId
                 && x.GrossWeightRawE8 == 8_00000000L
                 && x.PieceCount == 1
                 && x.FineWeightGrams == 7.328m);

        var originalSale = await ReadGoldVerificationAsync(
            client,
            evidence.HouseholdId,
            evidence.OriginalSaleTransactionId);
        Assert.Equal(100_000, originalSale.Economics.NetCashEffectMinorUnits);
        Assert.NotNull(originalSale.ReversedByTransactionId);
        Assert.False(originalSale.RealizedCost!.SourceSaleIsEffective);

        var correctedSale = await ReadGoldVerificationAsync(
            client,
            evidence.HouseholdId,
            evidence.CorrectedSaleTransactionId);
        Assert.Equal(60_000, correctedSale.Economics.NetCashEffectMinorUnits);
        Assert.Equal(
            60_000,
            Assert.Single(correctedSale.RealizedCost!.KnownAmounts).MinorUnits);
        Assert.True(correctedSale.RealizedCost.SourceSaleIsEffective);

        var transfer = await ReadGoldVerificationAsync(
            client,
            evidence.HouseholdId,
            evidence.TransferTransactionId);
        Assert.Equal(0, transfer.Economics.NetCashEffectMinorUnits);
        Assert.Equal(2, transfer.Allocations.Count);
        Assert.Equal(
            0,
            transfer.Allocations.Sum(x => x.GrossWeightDeltaRawE8));
        Assert.Equal(0, transfer.Allocations.Sum(x => x.PieceDelta));

        Assert.Contains(
            "Local startup mode: Ready",
            process.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            evidence.RestoredDatabasePath,
            process.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RECOVERY-GOLD", process.CombinedOutput);
        Assert.DoesNotContain("Data Source=", process.CombinedOutput);
        Assert.DoesNotContain("SELECT ", process.CombinedOutput);
    }

    [Fact]
    public async Task
        LocalHosting_MissingDatabaseStartsStorageUninitializedWithoutCreatingIt()
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(
                _databasePath)!);

        await using var process =
            ApiProcess.Start(
                _databasePath,
                _backupDirectory);

        var listeningUrl =
            await process.WaitForListeningUrlAsync();

        Assert.False(
            File.Exists(_databasePath));

        Assert.Contains(
            "Local startup mode: StorageUninitialized",
            process.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);

        using var client =
            new HttpClient();

        using var response =
            await client.GetAsync(
                new Uri(
                    new Uri(listeningUrl),
                    "/api/households"));

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);

        using var setupResponse =
            await client.GetAsync(
                new Uri(
                    new Uri(listeningUrl),
                    "/setup/storage"));

        Assert.Equal(
            HttpStatusCode.OK,
            setupResponse.StatusCode);

        var setupHtml =
            await setupResponse.Content.ReadAsStringAsync();

        Assert.Contains(
            _databasePath,
            setupHtml);

        using var styleResponse =
            await client.GetAsync(
                new Uri(
                    new Uri(listeningUrl),
                    "/_content/WealthLedger.UI/css/setup.css"));

        Assert.Equal(
            HttpStatusCode.OK,
            styleResponse.StatusCode);

        Assert.Contains(
            ":root",
            await styleResponse.Content.ReadAsStringAsync());

        Assert.False(
            File.Exists(_databasePath));

        Assert.DoesNotContain(
            "SQLite Error",
            process.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "Data Source",
            process.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task
        LocalHosting_RetiredStartupSwitchCannotMigratePendingDatabase()
    {
        await InitializeDatabaseAsync(
            "20260827072019_002_CommandReceipt");

        await using var process =
            ApiProcess.Start(
                _databasePath,
                _backupDirectory,
                "--Database:ApplyMigrationsOnStartup=true");

        var listeningUrl =
            await process.WaitForListeningUrlAsync();

        Assert.Contains(
            "Local startup mode: Blocked",
            process.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            nameof(LocalDataFailureCategory.DatabaseNotReady),
            process.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);

        using var client =
            new HttpClient();

        using var response =
            await client.GetAsync(
                new Uri(
                    new Uri(listeningUrl),
                    "/api/households"));

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);

        await using var context =
            CreateContext();

        var applied =
            await context.Database
                .GetAppliedMigrationsAsync();

        Assert.Equal(
            2,
            applied.Count());

        Assert.DoesNotContain(
            applied,
            migration =>
                migration.EndsWith(
                    "_003_ReversalDependencySemantics",
                    StringComparison.Ordinal));

        Assert.DoesNotContain(
            applied,
            migration =>
                migration.EndsWith(
                    "_004_LedgerNavigationQueries",
                    StringComparison.Ordinal));

        Assert.DoesNotContain(
            applied,
            migration =>
                migration.EndsWith(
                    "_005_WorkspaceIdentity",
                    StringComparison.Ordinal));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await Task.Yield();

        var resolvedRoot = Path.GetFullPath(_testRoot);
        var allowedRoot = Path.GetFullPath(
            Path.Combine(Path.GetTempPath(), "WealthLedger.Api.Tests"));

        if (Directory.Exists(resolvedRoot)
            && resolvedRoot.StartsWith(
                allowedRoot + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            Directory.Delete(resolvedRoot, recursive: true);
        }
    }

    private async Task InitializeDatabaseAsync(string? targetMigration = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        await using var context = CreateContext();

        if (targetMigration is null)
        {
            await context.Database.MigrateAsync();
        }
        else
        {
            await context.Database.MigrateAsync(targetMigration);
        }
    }

    private WealthLedgerDbContext CreateContext()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
        var options = new DbContextOptionsBuilder<WealthLedgerDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new WealthLedgerDbContext(options);
    }

    private async Task PrepareReadyWorkspaceAsync()
    {
        await InitializeDatabaseAsync();

        var configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Storage:DatabasePath"] =
                            _databasePath,

                        ["Backup:Directory"] =
                            _backupDirectory,

                        ["Backup:DestinationSeparationConfirmed"] =
                            "true",

                        ["Backup:DestinationEncryptionConfirmed"] =
                            "true"
                    })
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

        await using var serviceProvider =
            services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateScopes = true,
                    ValidateOnBuild = true
                });

        await using var scope =
            serviceProvider.CreateAsyncScope();

        _ = await scope.ServiceProvider
            .GetRequiredService<
                InitializeCoreLedgerUseCase>()
            .ExecuteAsync(
                ApiTestData.CreateSetupCommand());

        var backupResult =
            await scope.ServiceProvider
                .GetRequiredService<
                    CreateLocalBackupUseCase>()
                .ExecuteAsync();

        Assert.True(
            backupResult.Succeeded,
            backupResult.Failure?.Message);
    }

    private async Task<PhysicalGoldRecoveryEvidence>
        CreatePhysicalGoldRecoveryEvidenceAsync()
    {
        var configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Storage:DatabasePath"] = _databasePath,
                        ["Backup:Directory"] = _backupDirectory,
                        ["Backup:DestinationSeparationConfirmed"] = "true",
                        ["Backup:DestinationEncryptionConfirmed"] = "true"
                    })
                .Build();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddWealthLedgerInfrastructure(
            configuration,
            new LocalDataRuntimeContext(
                "Testing",
                Directory.GetCurrentDirectory()));
        services.AddScoped<CreateOpeningBalanceAccountUseCase>();
        services.AddScoped<CreateOpeningBalanceAssetUseCase>();
        services.AddScoped<RecordPhysicalGoldPurchaseUseCase>();
        services.AddScoped<PreviewPhysicalGoldSaleUseCase>();
        services.AddScoped<RecordPhysicalGoldSaleUseCase>();
        services.AddScoped<PreviewPhysicalGoldTransferUseCase>();
        services.AddScoped<RecordPhysicalGoldTransferUseCase>();
        services.AddScoped<ReversePostedTransactionUseCase>();
        services.AddScoped<CreateLocalBackupUseCase>();
        services.AddScoped<VerifyLocalBackupUseCase>();
        services.AddScoped<StageLocalRestoreUseCase>();

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });

        Guid householdId;
        Guid portfolioId;
        Guid cashAccountId;
        Guid cashAssetId;
        Guid sourceVaultId;
        Guid destinationVaultId;
        Guid goldAssetId;
        Guid assetLotId;
        Guid originalSaleTransactionId;
        Guid correctedSaleTransactionId;
        Guid transferTransactionId;

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<WealthLedgerDbContext>();
            householdId = await context.Households
                .Select(x => x.Id)
                .SingleAsync();
            portfolioId = await context.Portfolios
                .Select(x => x.Id)
                .SingleAsync();
            cashAccountId = await context.Accounts
                .Where(x => x.Code == "PRIMARY")
                .Select(x => x.Id)
                .SingleAsync();
            cashAssetId = await context.Assets
                .Where(x => x.Code == "SYNTHETIC_CASH")
                .Select(x => x.Id)
                .SingleAsync();

            var createAccount = scope.ServiceProvider
                .GetRequiredService<CreateOpeningBalanceAccountUseCase>();
            sourceVaultId = (await createAccount.ExecuteAsync(
                    new CreateOpeningBalanceAccountCommand(
                        householdId,
                        InstitutionId: null,
                        "RECOVERY_VAULT_A",
                        "Synthetic Recovery Vault A",
                        AccountType.PhysicalVault,
                        new DateOnly(2026, 1, 1))))
                .Reference.AccountId;
            destinationVaultId = (await createAccount.ExecuteAsync(
                    new CreateOpeningBalanceAccountCommand(
                        householdId,
                        InstitutionId: null,
                        "RECOVERY_VAULT_B",
                        "Synthetic Recovery Vault B",
                        AccountType.PhysicalVault,
                        new DateOnly(2026, 1, 1))))
                .Reference.AccountId;
            goldAssetId = (await scope.ServiceProvider
                    .GetRequiredService<CreateOpeningBalanceAssetUseCase>()
                    .ExecuteAsync(
                        new CreateOpeningBalanceAssetCommand(
                            "RECOVERY_GOLD",
                            "Synthetic Recovery Gold",
                            AssetType.PhysicalGold,
                            CurrencyCode.TRY,
                            LotTrackingMode.Required)))
                .Reference.AssetId;

            var purchase = await scope.ServiceProvider
                .GetRequiredService<RecordPhysicalGoldPurchaseUseCase>()
                .ExecuteAsync(
                    Guid.NewGuid().ToString("D"),
                    new PhysicalGoldPurchaseCommand(
                        householdId,
                        portfolioId,
                        sourceVaultId,
                        cashAccountId,
                        goldAssetId,
                        cashAssetId,
                        Quantity.FromDecimal(30m),
                        new Fineness(916_000),
                        PieceCount: 3,
                        Money.FromMinorUnits(300_000, CurrencyCode.TRY),
                        new DateOnly(2026, 9, 15),
                        ExternalReference: "RECOVERY-GOLD-PURCHASE",
                        Note: "Synthetic recovery purchase evidence."));
            assetLotId = purchase.AssetLotId;

            var saleSelection = new PhysicalGoldSelectedLot(
                assetLotId,
                Quantity.FromDecimal(10m),
                PieceCount: 1);
            var originalSaleCommand = new PhysicalGoldSaleCommand(
                householdId,
                portfolioId,
                sourceVaultId,
                cashAccountId,
                goldAssetId,
                cashAssetId,
                Quantity.FromDecimal(10m),
                PieceCount: 1,
                [saleSelection],
                Money.FromMinorUnits(100_000, CurrencyCode.TRY),
                new DateOnly(2026, 9, 15),
                ExternalReference: "RECOVERY-GOLD-SALE-ORIGINAL",
                Note: "Synthetic recovery original sale.");
            var salePreview = await scope.ServiceProvider
                .GetRequiredService<PreviewPhysicalGoldSaleUseCase>()
                .ExecuteAsync(originalSaleCommand);
            originalSaleCommand = originalSaleCommand with
            {
                ReviewedPlanFingerprint = salePreview.PlanFingerprint
            };
            originalSaleTransactionId = (await scope.ServiceProvider
                    .GetRequiredService<RecordPhysicalGoldSaleUseCase>()
                    .ExecuteAsync(
                        Guid.NewGuid().ToString("D"),
                        originalSaleCommand))
                .TransactionId;

            _ = await scope.ServiceProvider
                .GetRequiredService<ReversePostedTransactionUseCase>()
                .ExecuteAsync(
                    Guid.NewGuid().ToString("D"),
                    new ReversePostedTransactionCommand(
                        originalSaleTransactionId,
                        "Synthetic recovery correction reason."));

            var correctedSelection = new PhysicalGoldSelectedLot(
                assetLotId,
                Quantity.FromDecimal(6m),
                PieceCount: 1);
            var correctedSaleCommand = originalSaleCommand with
            {
                GrossWeight = Quantity.FromDecimal(6m),
                CashConsideration = Money.FromMinorUnits(
                    60_000,
                    CurrencyCode.TRY),
                SelectedLots = [correctedSelection],
                ExternalReference = "RECOVERY-GOLD-SALE-CORRECTED",
                Note = "Synthetic recovery corrected sale.",
                ReviewedPlanFingerprint = null
            };
            salePreview = await scope.ServiceProvider
                .GetRequiredService<PreviewPhysicalGoldSaleUseCase>()
                .ExecuteAsync(correctedSaleCommand);
            correctedSaleCommand = correctedSaleCommand with
            {
                ReviewedPlanFingerprint = salePreview.PlanFingerprint
            };
            correctedSaleTransactionId = (await scope.ServiceProvider
                    .GetRequiredService<RecordPhysicalGoldSaleUseCase>()
                    .ExecuteAsync(
                        Guid.NewGuid().ToString("D"),
                        correctedSaleCommand))
                .TransactionId;

            var transferSelection = new PhysicalGoldSelectedLot(
                assetLotId,
                Quantity.FromDecimal(8m),
                PieceCount: 1);
            var transferCommand = new PhysicalGoldTransferCommand(
                householdId,
                portfolioId,
                sourceVaultId,
                portfolioId,
                destinationVaultId,
                goldAssetId,
                Quantity.FromDecimal(8m),
                PieceCount: 1,
                [transferSelection],
                new DateOnly(2026, 9, 15),
                ExternalReference: "RECOVERY-GOLD-TRANSFER",
                Note: "Synthetic recovery transfer.");
            var transferPreview = await scope.ServiceProvider
                .GetRequiredService<PreviewPhysicalGoldTransferUseCase>()
                .ExecuteAsync(transferCommand);
            transferCommand = transferCommand with
            {
                ReviewedPlanFingerprint = transferPreview.PlanFingerprint
            };
            transferTransactionId = (await scope.ServiceProvider
                    .GetRequiredService<RecordPhysicalGoldTransferUseCase>()
                    .ExecuteAsync(
                        Guid.NewGuid().ToString("D"),
                        transferCommand))
                .TransactionId;
        }

        string backupFilePath;
        var restoredDatabasePath = Path.Combine(
            _testRoot,
            "physical-gold-restore-drill",
            "restored.db");
        await using (var scope = provider.CreateAsyncScope())
        {
            var backup = await scope.ServiceProvider
                .GetRequiredService<CreateLocalBackupUseCase>()
                .ExecuteAsync();
            Assert.True(backup.Succeeded, backup.Failure?.Message);
            backupFilePath = backup.Value!.FilePath;

            var verification = await scope.ServiceProvider
                .GetRequiredService<VerifyLocalBackupUseCase>()
                .ExecuteAsync(backupFilePath);
            Assert.True(
                verification.Succeeded,
                verification.Failure?.Message);

            var restore = await scope.ServiceProvider
                .GetRequiredService<StageLocalRestoreUseCase>()
                .ExecuteAsync(backupFilePath, restoredDatabasePath);
            Assert.True(restore.Succeeded, restore.Failure?.Message);
        }

        return new PhysicalGoldRecoveryEvidence(
            householdId,
            sourceVaultId,
            destinationVaultId,
            assetLotId,
            originalSaleTransactionId,
            correctedSaleTransactionId,
            transferTransactionId,
            backupFilePath,
            restoredDatabasePath);
    }

    private static async Task<PhysicalGoldActivityVerificationResponse>
        ReadGoldVerificationAsync(
            HttpClient client,
            Guid householdId,
            Guid transactionId)
    {
        var result = await client.GetFromJsonAsync<
            PhysicalGoldActivityVerificationResponse>(
            $"/api/households/{householdId:D}/ledger/physical-gold-activities/{transactionId:D}/verification");
        return Assert.IsType<PhysicalGoldActivityVerificationResponse>(result);
    }

    private sealed record PhysicalGoldRecoveryEvidence(
        Guid HouseholdId,
        Guid SourceVaultId,
        Guid DestinationVaultId,
        Guid AssetLotId,
        Guid OriginalSaleTransactionId,
        Guid CorrectedSaleTransactionId,
        Guid TransferTransactionId,
        string BackupFilePath,
        string RestoredDatabasePath);

    private sealed class ApiProcess : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _standardOutput = new();
        private readonly StringBuilder _standardError = new();
        private readonly object _outputLock = new();
        private readonly TaskCompletionSource<string> _listeningUrl =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private ApiProcess(Process process)
        {
            _process = process;
        }

        internal string CombinedOutput
        {
            get
            {
                lock (_outputLock)
                {
                    return _standardOutput + Environment.NewLine
                        + _standardError;
                }
            }
        }

        internal static ApiProcess Start(
            string databasePath,
            string backupDirectory,
            params string[] additionalArguments)
        {
            var apiAssemblyPath = typeof(Program).Assembly.Location;
            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Path.GetDirectoryName(apiAssemblyPath)!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(apiAssemblyPath);
            startInfo.ArgumentList.Add(
                $"--Storage:DatabasePath={Path.GetFullPath(databasePath)}");
            startInfo.ArgumentList.Add(
                $"--Backup:Directory={Path.GetFullPath(backupDirectory)}");

            foreach (var argument in additionalArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
            startInfo.Environment["DOTNET_NOLOGO"] = "1";
            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            var result = new ApiProcess(process);
            process.OutputDataReceived += result.OnOutput;
            process.ErrorDataReceived += result.OnError;

            if (!process.Start())
            {
                throw new InvalidOperationException("The API process did not start.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return result;
        }

        internal async Task<string> WaitForListeningUrlAsync()
        {
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(20));
            var exitTask = _process.WaitForExitAsync(timeout.Token);
            var completed = await Task.WhenAny(_listeningUrl.Task, exitTask);

            if (completed == _listeningUrl.Task)
            {
                return await _listeningUrl.Task;
            }

            await exitTask;
            throw new InvalidOperationException(
                $"The API exited before listening. Output: {CombinedOutput}");
        }

        internal async Task<int> WaitForExitAsync()
        {
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(20));
            await _process.WaitForExitAsync(timeout.Token);
            _process.WaitForExit();
            return _process.ExitCode;
        }

        internal async Task StopAsync()
        {
            if (_process.HasExited)
            {
                return;
            }

            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            _process.Dispose();
        }

        private void OnOutput(object sender, DataReceivedEventArgs eventArgs)
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            lock (_outputLock)
            {
                _standardOutput.AppendLine(eventArgs.Data);
            }

            const string marker = "Now listening on: ";
            var markerIndex = eventArgs.Data.IndexOf(
                marker,
                StringComparison.Ordinal);

            if (markerIndex >= 0)
            {
                _listeningUrl.TrySetResult(
                    eventArgs.Data[(markerIndex + marker.Length)..].Trim());
            }
        }

        private void OnError(object sender, DataReceivedEventArgs eventArgs)
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            lock (_outputLock)
            {
                _standardError.AppendLine(eventArgs.Data);
            }
        }
    }
}
