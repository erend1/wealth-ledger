using WealthLedger.Application.LocalData;
using WealthLedger.Infrastructure.LocalData;

namespace WealthLedger.Infrastructure.Tests.LocalData;

public sealed class SqliteLocalDatabaseMigrationTests
{
    private const string LedgerMarker = "SYNTHETIC_MIGRATION_MARKER";

    [Fact]
    public async Task Migration_FromM001BacksUpFullChainAndPreservesData()
    {
        const string startingMigration =
            "20260824074930_001_CoreLedger";
        const string commandReceiptMigration =
            "20260827072019_002_CommandReceipt";
        const string reversalSemanticsMigration =
            "20260831113310_003_ReversalDependencySemantics";
        const string navigationQueriesMigration =
            "20260902112549_004_LedgerNavigationQueries";
        const string endingMigration =
            "20260903075104_005_WorkspaceIdentity";
        var hooks = new RecordingMigrationHooks();
        await using var harness = await LocalBackupTestHarness.CreateAsync(
            hooks,
            startingMigration);
        await harness.InsertSyntheticHouseholdAsync(LedgerMarker);

        var initialVerification = await harness.DatabaseVerifier.VerifyAsync(
            harness.DatabasePath);

        Assert.True(initialVerification.Succeeded);
        Assert.Equal(
            LocalDatabaseCompatibility.MigrationRequired,
            initialVerification.Value!.Compatibility);
        Assert.Equal(
            [startingMigration],
            initialVerification.Value.AppliedMigrations);
        Assert.Equal(
            [
                commandReceiptMigration,
                reversalSemanticsMigration,
                navigationQueriesMigration,
                endingMigration
            ],
            initialVerification.Value.PendingMigrations);

        var result = await CreateUseCase(harness).ExecuteAsync();

        Assert.True(result.Succeeded, result.Failure?.Message);
        Assert.False(result.Value!.WasNoOp);
        Assert.Equal(startingMigration, result.Value.StartingMigration);
        Assert.Equal(endingMigration, result.Value.EndingMigration);
        Assert.True(File.Exists(result.Value.PreMigrationBackupPath));
        Assert.Equal(
            [
                LocalDataOperationCheckpoint.BeforeBackupPublish,
                LocalDataOperationCheckpoint.BeforeMigrationApply,
                LocalDataOperationCheckpoint.AfterMigrationApply
            ],
            hooks.RelevantCheckpoints);

        var restarted = await harness.DatabaseVerifier.VerifyAsync(
            harness.DatabasePath);
        var backupVerification = await harness.Verifier.VerifyAsync(
            result.Value.PreMigrationBackupPath!);
        var recoveryTarget = Path.Combine(
            harness.RootPath,
            "m001-recovery",
            "recovered.db");
        var recovery = await harness.RestoreStager.StageAsync(
            result.Value.PreMigrationBackupPath!,
            recoveryTarget);

        Assert.True(restarted.Succeeded);
        Assert.Equal(
            LocalDatabaseCompatibility.Compatible,
            restarted.Value!.Compatibility);
        Assert.Equal(5, restarted.Value.AppliedMigrations.Count);
        Assert.Empty(restarted.Value.PendingMigrations);
        Assert.Equal(
            1L,
            await LocalBackupTestHarness.CountSyntheticHouseholdsAsync(
                harness.DatabasePath,
                LedgerMarker));

        Assert.True(backupVerification.Succeeded);
        Assert.Equal(
            LocalDatabaseCompatibility.MigrationRequired,
            backupVerification.Value!.Compatibility);
        Assert.Equal(
            [startingMigration],
            backupVerification.Value.AppliedMigrations);
        Assert.True(recovery.Succeeded);
        Assert.Equal(
            LocalDatabaseCompatibility.MigrationRequired,
            recovery.Value!.Compatibility);
        Assert.Equal(
            1L,
            await LocalBackupTestHarness.CountSyntheticHouseholdsAsync(
                recoveryTarget,
                LedgerMarker));
    }

    [Fact]
    public async Task Migration_FromM003CreatesVerifiedBackupAndRestoresOldState()
    {
        const string startingMigration =
            "20260831113310_003_ReversalDependencySemantics";
        const string endingMigration =
            "20260903075104_005_WorkspaceIdentity";
        var hooks = new RecordingMigrationHooks();
        await using var harness = await LocalBackupTestHarness.CreateAsync(
            hooks,
            startingMigration);
        await harness.InsertSyntheticHouseholdAsync(LedgerMarker);
        var useCase = CreateUseCase(harness);

        var result = await useCase.ExecuteAsync();
        var restarted = await harness.DatabaseVerifier.VerifyAsync(
            harness.DatabasePath);

        Assert.True(result.Succeeded, result.Failure?.Message);
        Assert.False(result.Value!.WasNoOp);
        Assert.Equal(
            startingMigration,
            result.Value.StartingMigration);
        Assert.Equal(
            endingMigration,
            result.Value.EndingMigration);
        Assert.NotNull(result.Value.PreMigrationBackupPath);
        Assert.True(File.Exists(result.Value.PreMigrationBackupPath));
        Assert.Equal(
            [
                LocalDataOperationCheckpoint.BeforeBackupPublish,
                LocalDataOperationCheckpoint.BeforeMigrationApply,
                LocalDataOperationCheckpoint.AfterMigrationApply
            ],
            hooks.RelevantCheckpoints);
        Assert.True(restarted.Succeeded);
        Assert.Equal(
            LocalDatabaseCompatibility.Compatible,
            restarted.Value!.Compatibility);
        Assert.Empty(restarted.Value.PendingMigrations);
        Assert.Equal(
            1L,
            await LocalBackupTestHarness.CountSyntheticHouseholdsAsync(
                harness.DatabasePath,
                LedgerMarker));

        var backupVerification = await harness.Verifier.VerifyAsync(
            result.Value.PreMigrationBackupPath!);
        Assert.True(backupVerification.Succeeded);
        Assert.Equal(
            LocalDatabaseCompatibility.MigrationRequired,
            backupVerification.Value!.Compatibility);
        Assert.Equal(
            [
                "20260824074930_001_CoreLedger",
                "20260827072019_002_CommandReceipt",
                startingMigration
            ],
            backupVerification.Value.AppliedMigrations);

        var recoveryTarget = Path.Combine(
            harness.RootPath,
            "m003-recovery",
            "recovered.db");
        var recovery = await harness.RestoreStager.StageAsync(
            result.Value.PreMigrationBackupPath!,
            recoveryTarget);

        Assert.True(recovery.Succeeded);
        Assert.Equal(
            LocalDatabaseCompatibility.MigrationRequired,
            recovery.Value!.Compatibility);
        Assert.Equal(
            1L,
            await LocalBackupTestHarness.CountSyntheticHouseholdsAsync(
                recoveryTarget,
                LedgerMarker));
    }

    [Fact]
    public async Task Migration_NoPendingMigrationsIsIdempotentNoOpWithoutBackup()
    {
        var hooks = new RecordingMigrationHooks();
        await using var harness = await LocalBackupTestHarness.CreateAsync(
            hooks,
            includeBackupConfiguration: false);
        var useCase = CreateUseCase(harness);

        var first = await useCase.ExecuteAsync();
        var second = await CreateUseCase(harness).ExecuteAsync();

        Assert.True(first.Succeeded);
        Assert.True(first.Value!.WasNoOp);
        Assert.Null(first.Value.PreMigrationBackupPath);
        Assert.True(second.Succeeded);
        Assert.True(second.Value!.WasNoOp);
        Assert.False(Directory.Exists(harness.BackupDirectory));
        Assert.Empty(hooks.RelevantCheckpoints);
    }

    [Fact]
    public async Task Migration_ActiveOwnerFailsBeforeInspectionOrBackup()
    {
        await using var harness = await LocalBackupTestHarness.CreateAsync(
            targetMigration: "20260827072019_002_CommandReceipt");
        var ownership = harness.OwnershipGuard.Acquire(
            harness.DatabasePath,
            createDirectory: false);
        Assert.True(ownership.Succeeded);
        await using var lease = ownership.Value!;

        var result = await CreateUseCase(harness).ExecuteAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.OwnershipBusy,
            result.Failure!.Category);
        Assert.False(Directory.Exists(harness.BackupDirectory));
    }

    [Fact]
    public async Task Migration_PendingChainWithoutBackupConfigurationFailsBeforeApply()
    {
        await using var harness = await LocalBackupTestHarness.CreateAsync(
            targetMigration: "20260827072019_002_CommandReceipt",
            includeBackupConfiguration: false);

        var result = await CreateUseCase(harness).ExecuteAsync();
        var verification = await harness.DatabaseVerifier.VerifyAsync(
            harness.DatabasePath);

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.InvalidInputOrConfiguration,
            result.Failure!.Category);
        Assert.Equal(
            LocalDatabaseCompatibility.MigrationRequired,
            verification.Value!.Compatibility);
        Assert.False(Directory.Exists(harness.BackupDirectory));
    }

    [Fact]
    public async Task Migration_BackupFailurePreventsEfApplyAndLeavesNoPackage()
    {
        var hooks = new RecordingMigrationHooks(
            LocalDataOperationCheckpoint.BeforeBackupPublish,
            new IOException("Synthetic private backup failure."));
        await using var harness = await LocalBackupTestHarness.CreateAsync(
            hooks,
            "20260827072019_002_CommandReceipt");

        var result = await CreateUseCase(harness).ExecuteAsync();
        var verification = await harness.DatabaseVerifier.VerifyAsync(
            harness.DatabasePath);

        Assert.False(result.Succeeded);
        Assert.Equal(LocalDataFailureCategory.IoFailure, result.Failure!.Category);
        Assert.True(verification.Succeeded);
        Assert.Equal(
            LocalDatabaseCompatibility.MigrationRequired,
            verification.Value!.Compatibility);
        Assert.Empty(Directory.GetFiles(
            harness.BackupDirectory,
            "*.wlbackup"));
        Assert.DoesNotContain(
            LocalDataOperationCheckpoint.BeforeMigrationApply,
            hooks.RelevantCheckpoints);
        Assert.DoesNotContain("Synthetic", result.Failure.Message);
    }

    [Fact]
    public async Task Migration_PreApplyFailureRetainsVerifiedBackupAndOldDatabase()
    {
        var hooks = new RecordingMigrationHooks(
            LocalDataOperationCheckpoint.BeforeMigrationApply,
            new IOException("Synthetic private migration failure."));
        await using var harness = await LocalBackupTestHarness.CreateAsync(
            hooks,
            "20260827072019_002_CommandReceipt");

        var result = await CreateUseCase(harness).ExecuteAsync();
        var verification = await harness.DatabaseVerifier.VerifyAsync(
            harness.DatabasePath);

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.MigrationFailure,
            result.Failure!.Category);
        Assert.NotNull(result.Value?.PreMigrationBackupPath);
        Assert.True(File.Exists(result.Value!.PreMigrationBackupPath));
        Assert.True((await harness.Verifier.VerifyAsync(
            result.Value.PreMigrationBackupPath!)).Succeeded);
        Assert.Equal(
            LocalDatabaseCompatibility.MigrationRequired,
            verification.Value!.Compatibility);
        Assert.DoesNotContain(
            LocalDataOperationCheckpoint.AfterMigrationApply,
            hooks.RelevantCheckpoints);
    }

    [Fact]
    public async Task Migration_PostApplyFailureIsNotSuccessAndBackupRestoresOldState()
    {
        var hooks = new RecordingMigrationHooks(
            LocalDataOperationCheckpoint.AfterMigrationApply,
            new IOException("Synthetic private post-migration failure."));
        await using var harness = await LocalBackupTestHarness.CreateAsync(
            hooks,
            "20260827072019_002_CommandReceipt");
        await harness.InsertSyntheticHouseholdAsync(LedgerMarker);

        var result = await CreateUseCase(harness).ExecuteAsync();
        var liveVerification = await harness.DatabaseVerifier.VerifyAsync(
            harness.DatabasePath);
        var recoveryTarget = Path.Combine(
            harness.RootPath,
            "migration-recovery",
            "recovered.db");
        var recovery = await harness.RestoreStager.StageAsync(
            result.Value!.PreMigrationBackupPath!,
            recoveryTarget);

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.MigrationFailure,
            result.Failure!.Category);
        Assert.Equal(
            "20260903075104_005_WorkspaceIdentity",
            result.Value.EndingMigration);
        Assert.True(File.Exists(result.Value.PreMigrationBackupPath));
        Assert.True(liveVerification.Succeeded);
        Assert.Equal(
            LocalDatabaseCompatibility.Compatible,
            liveVerification.Value!.Compatibility);
        Assert.True(recovery.Succeeded);
        Assert.Equal(
            LocalDatabaseCompatibility.MigrationRequired,
            recovery.Value!.Compatibility);
        Assert.Equal(
            1L,
            await LocalBackupTestHarness.CountSyntheticHouseholdsAsync(
                recoveryTarget,
                LedgerMarker));
        Assert.DoesNotContain("Synthetic", result.Failure.Message);
    }

    [Fact]
    public async Task Migration_CancellationBeforeApplyRetainsBackupAndOldState()
    {
        var hooks = new RecordingMigrationHooks(
            LocalDataOperationCheckpoint.BeforeMigrationApply,
            new OperationCanceledException("Synthetic cancellation."));
        await using var harness = await LocalBackupTestHarness.CreateAsync(
            hooks,
            "20260827072019_002_CommandReceipt");

        var result = await CreateUseCase(harness).ExecuteAsync();
        var verification = await harness.DatabaseVerifier.VerifyAsync(
            harness.DatabasePath);

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.Cancelled,
            result.Failure!.Category);
        Assert.True(File.Exists(result.Value!.PreMigrationBackupPath));
        Assert.Equal(
            LocalDatabaseCompatibility.MigrationRequired,
            verification.Value!.Compatibility);
    }

    [Fact]
    public async Task Migration_SessionRefusesBackupWithoutPendingInspectedPlan()
    {
        await using var harness = await LocalBackupTestHarness.CreateAsync(
            targetMigration: "20260827072019_002_CommandReceipt");
        var opened = await harness.CreateMigrationSessionFactory().OpenAsync();
        Assert.True(opened.Succeeded);
        await using var session = opened.Value!;

        var result = await session.CreateVerifiedPreMigrationBackupAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.InvalidInputOrConfiguration,
            result.Failure!.Category);
        Assert.False(Directory.Exists(harness.BackupDirectory));
    }

    private static MigrateLocalDatabaseUseCase CreateUseCase(
        LocalBackupTestHarness harness)
        => new(
            harness.CreateMigrationSessionFactory(),
            new FixedTimeProvider(LocalBackupTestHarness.OperationTime));

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        internal FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class RecordingMigrationHooks : ILocalDataOperationHooks
    {
        private readonly LocalDataOperationCheckpoint? _failureCheckpoint;
        private readonly Exception? _exception;

        internal RecordingMigrationHooks(
            LocalDataOperationCheckpoint? failureCheckpoint = null,
            Exception? exception = null)
        {
            _failureCheckpoint = failureCheckpoint;
            _exception = exception;
        }

        internal List<LocalDataOperationCheckpoint> RelevantCheckpoints
        {
            get;
        } = [];

        public ValueTask OnCheckpointAsync(
            LocalDataOperationCheckpoint checkpoint,
            string primaryPath,
            string? secondaryPath,
            CancellationToken cancellationToken)
        {
            if (checkpoint
                is LocalDataOperationCheckpoint.BeforeBackupPublish
                or LocalDataOperationCheckpoint.BeforeMigrationApply
                or LocalDataOperationCheckpoint.AfterMigrationApply)
            {
                RelevantCheckpoints.Add(checkpoint);
            }

            return checkpoint == _failureCheckpoint
                ? ValueTask.FromException(
                    _exception
                    ?? new InvalidOperationException(
                        "A synthetic exception was not configured."))
                : ValueTask.CompletedTask;
        }
    }
}
