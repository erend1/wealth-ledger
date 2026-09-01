using System.Security.Cryptography;
using WealthLedger.Application.LocalData;
using WealthLedger.Infrastructure.LocalData;

namespace WealthLedger.Infrastructure.Tests.LocalData;

public sealed class SqliteLocalRestoreTests
{
    private const string SourceMarker = "SYNTHETIC_RESTORE_SOURCE";
    private const string LiveMarker = "SYNTHETIC_RESTORE_LIVE";

    [Fact]
    public async Task RestoreStage_VerifiedPackagePublishesNewEquivalentTarget()
    {
        await using var harness = await LocalBackupTestHarness.CreateAsync();
        await harness.InsertSyntheticHouseholdAsync(SourceMarker);
        var backup = await harness.CreateBackupAsync();
        var target = Path.Combine(
            harness.RootPath,
            "restore-drill",
            "restored.db");

        var result = await harness.RestoreStager.StageAsync(
            backup.Value!.FilePath,
            target);
        var restarted = await harness.DatabaseVerifier.VerifyAsync(target);

        Assert.True(result.Succeeded, result.Failure?.Message);
        Assert.Equal(Path.GetFullPath(target), result.Value!.TargetDatabasePath);
        Assert.Equal(
            LocalDatabaseCompatibility.Compatible,
            result.Value.Compatibility);
        Assert.True(restarted.Succeeded);
        Assert.Equal(
            1L,
            await LocalBackupTestHarness.CountSyntheticHouseholdsAsync(
                target,
                SourceMarker));
        Assert.Equal(
            1L,
            await LocalBackupTestHarness.CountSyntheticHouseholdsAsync(
                harness.DatabasePath,
                SourceMarker));
        Assert.Empty(Directory.GetFiles(
            Path.GetDirectoryName(target)!,
            "*.wlrestore"));
    }

    [Fact]
    public async Task RestoreStage_ExistingTargetIsPreservedAndNeverOverwritten()
    {
        await using var harness = await LocalBackupTestHarness.CreateAsync();
        var backup = await harness.CreateBackupAsync();
        var target = Path.Combine(
            harness.RootPath,
            "restore-drill",
            "existing.db");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var existingBytes = "synthetic existing target"u8.ToArray();
        await File.WriteAllBytesAsync(target, existingBytes);
        var hashBefore = ComputeSha256(existingBytes);

        var result = await harness.RestoreStager.StageAsync(
            backup.Value!.FilePath,
            target);

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.AlreadyExists,
            result.Failure!.Category);
        Assert.Equal(hashBefore, await HashFileAsync(target));
    }

    [Fact]
    public async Task RestoreStage_ExistingCompanionIsPreservedAndRefused()
    {
        await using var harness = await LocalBackupTestHarness.CreateAsync();
        var backup = await harness.CreateBackupAsync();
        var target = Path.Combine(
            harness.RootPath,
            "restore-drill",
            "companion.db");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var companion = target + "-wal";
        await File.WriteAllTextAsync(companion, "synthetic existing state");

        var result = await harness.RestoreStager.StageAsync(
            backup.Value!.FilePath,
            target);

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.AlreadyExists,
            result.Failure!.Category);
        Assert.False(File.Exists(target));
        Assert.Equal(
            "synthetic existing state",
            await File.ReadAllTextAsync(companion));
    }

    [Fact]
    public async Task RestoreStage_UnsafeLiveAndBackupDirectoryTargetsAreRejected()
    {
        await using var harness = await LocalBackupTestHarness.CreateAsync();
        var backup = await harness.CreateBackupAsync();
        var insideBackups = Path.Combine(
            harness.BackupDirectory,
            "restored.db");

        var liveResult = await harness.RestoreStager.StageAsync(
            backup.Value!.FilePath,
            harness.DatabasePath);
        var backupResult = await harness.RestoreStager.StageAsync(
            backup.Value.FilePath,
            insideBackups);

        Assert.False(liveResult.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.UnsafePath,
            liveResult.Failure!.Category);
        Assert.False(backupResult.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.UnsafePath,
            backupResult.Failure!.Category);
        Assert.False(File.Exists(insideBackups));
    }

    [Fact]
    public async Task RestoreStage_InvalidPackageLeavesNoTargetOrStage()
    {
        await using var harness = await LocalBackupTestHarness.CreateAsync();
        var invalidPackage = Path.Combine(
            harness.RootPath,
            "invalid.wlbackup");
        await File.WriteAllTextAsync(
            invalidPackage,
            "synthetic invalid package");
        var target = Path.Combine(
            harness.RootPath,
            "restore-drill",
            "invalid.db");

        var result = await harness.RestoreStager.StageAsync(
            invalidPackage,
            target);

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.InvalidBackup,
            result.Failure!.Category);
        Assert.False(File.Exists(target));
        Assert.Empty(Directory.GetFiles(
            Path.GetDirectoryName(target)!,
            "*.wlrestore"));
    }

    [Theory]
    [InlineData(
        "before",
        LocalDataFailureCategory.IoFailure)]
    [InlineData(
        "after",
        LocalDataFailureCategory.IoFailure)]
    public async Task RestoreStage_InjectedIoFailureRemovesOwnedTargetAndStage(
        string checkpointName,
        LocalDataFailureCategory expectedCategory)
    {
        await using var source = await LocalBackupTestHarness.CreateAsync();
        var backup = await source.CreateBackupAsync();
        var hooks = new CheckpointFailureHooks(
            checkpointName == "before"
                ? LocalDataOperationCheckpoint.BeforeRestoreStagePublish
                : LocalDataOperationCheckpoint.AfterRestoreStagePublish,
            new IOException("Synthetic private restore I/O detail."));
        await using var targetHarness =
            await LocalBackupTestHarness.CreateAsync(hooks);
        var target = Path.Combine(
            targetHarness.RootPath,
            "restore-drill",
            "injected.db");

        var result = await targetHarness.RestoreStager.StageAsync(
            backup.Value!.FilePath,
            target);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCategory, result.Failure!.Category);
        Assert.False(File.Exists(target));
        Assert.Empty(Directory.GetFiles(
            Path.GetDirectoryName(target)!,
            "*.wlrestore"));
        Assert.DoesNotContain("Synthetic", result.Failure.Message);
    }

    [Fact]
    public async Task RestoreStage_CancellationAfterPublishRemovesOwnedTarget()
    {
        await using var source = await LocalBackupTestHarness.CreateAsync();
        var backup = await source.CreateBackupAsync();
        var hooks = new CheckpointFailureHooks(
            LocalDataOperationCheckpoint.AfterRestoreStagePublish,
            new OperationCanceledException("Synthetic cancellation."));
        await using var targetHarness =
            await LocalBackupTestHarness.CreateAsync(hooks);
        var target = Path.Combine(
            targetHarness.RootPath,
            "restore-drill",
            "cancelled.db");

        var result = await targetHarness.RestoreStager.StageAsync(
            backup.Value!.FilePath,
            target);

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.Cancelled,
            result.Failure!.Category);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task RestoreStage_CollisionInjectedBeforePublishIsPreserved()
    {
        await using var source = await LocalBackupTestHarness.CreateAsync();
        var backup = await source.CreateBackupAsync();
        var hooks = new RestoreTargetCollisionHooks();
        await using var targetHarness =
            await LocalBackupTestHarness.CreateAsync(hooks);
        var target = Path.Combine(
            targetHarness.RootPath,
            "restore-drill",
            "collision.db");

        var result = await targetHarness.RestoreStager.StageAsync(
            backup.Value!.FilePath,
            target);

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.AlreadyExists,
            result.Failure!.Category);
        Assert.Equal(
            RestoreTargetCollisionHooks.Marker,
            await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task RestoreReplace_PreservesPreBackupAndSupersededGeneration()
    {
        await using var source = await CreateSourceBackupAsync();
        var sourceBackup = Directory.GetFiles(
            source.BackupDirectory,
            "*.wlbackup").Single();
        await using var live = await LocalBackupTestHarness.CreateAsync();
        await live.InsertSyntheticHouseholdAsync(LiveMarker);
        var useCase = new ReplaceLocalDatabaseUseCase(
            live.CreateReplacementSessionFactory());

        var result = await useCase.ExecuteAsync(
            sourceBackup,
            confirmReplaceActive: true);

        Assert.True(result.Succeeded, result.Failure?.Message);
        Assert.True(File.Exists(result.Value!.PreRestoreBackupPath));
        Assert.True(File.Exists(result.Value.SupersededDatabasePath));
        Assert.Equal(
            1L,
            await LocalBackupTestHarness.CountSyntheticHouseholdsAsync(
                live.DatabasePath,
                SourceMarker));
        Assert.Equal(
            0L,
            await LocalBackupTestHarness.CountSyntheticHouseholdsAsync(
                live.DatabasePath,
                LiveMarker));
        Assert.Equal(
            1L,
            await LocalBackupTestHarness.CountSyntheticHouseholdsAsync(
                result.Value.SupersededDatabasePath,
                LiveMarker));
        Assert.True((await live.Verifier.VerifyAsync(
            result.Value.PreRestoreBackupPath)).Succeeded);
        Assert.True((await live.DatabaseVerifier.VerifyAsync(
            live.DatabasePath)).Succeeded);
        Assert.False(LocalDatabaseFiles.AnyCompanionArtifactExists(
            live.DatabasePath));
        Assert.Empty(Directory.GetFiles(
            Path.GetDirectoryName(live.DatabasePath)!,
            "*.wlrestore"));
    }

    [Fact]
    public async Task RestoreReplace_RemovesStaleJournalBeforePromotion()
    {
        await using var source = await CreateSourceBackupAsync();
        var sourceBackup = Directory.GetFiles(
            source.BackupDirectory,
            "*.wlbackup").Single();
        await using var live = await LocalBackupTestHarness.CreateAsync();
        var factory = live.CreateReplacementSessionFactory();
        var opened = await factory.OpenAsync(sourceBackup);
        Assert.True(opened.Succeeded);
        await using var session = opened.Value!;
        var stage = await session.StageCandidateAsync();
        var backup = await session.CreateVerifiedPreRestoreBackupAsync();
        var journalPath = live.DatabasePath + "-journal";
        await File.WriteAllBytesAsync(journalPath, new byte[512]);

        var result = await session.PromoteAsync(
            stage.Value!,
            backup.Value!);

        Assert.True(result.Succeeded, result.Failure?.Message);
        Assert.False(File.Exists(journalPath));
        Assert.True(File.Exists(result.Value!.SupersededDatabasePath));
    }

    [Fact]
    public async Task RestoreReplace_CheckpointsValidWalAndPreservesItsFacts()
    {
        await using var source = await CreateSourceBackupAsync();
        var sourceBackup = Directory.GetFiles(
            source.BackupDirectory,
            "*.wlbackup").Single();
        await using var live = await LocalBackupTestHarness.CreateAsync();
        await live.CreateDetachedWalGenerationAsync(LiveMarker);
        Assert.True(File.Exists(live.DatabasePath + "-wal"));
        Assert.True(File.Exists(live.DatabasePath + "-shm"));
        var useCase = new ReplaceLocalDatabaseUseCase(
            live.CreateReplacementSessionFactory());

        var result = await useCase.ExecuteAsync(
            sourceBackup,
            confirmReplaceActive: true);

        Assert.True(result.Succeeded, result.Failure?.Message);
        Assert.Equal(
            1L,
            await LocalBackupTestHarness.CountSyntheticHouseholdsAsync(
                result.Value!.SupersededDatabasePath,
                LiveMarker));
        Assert.True((await live.Verifier.VerifyAsync(
            result.Value.PreRestoreBackupPath)).Succeeded);
        Assert.False(File.Exists(live.DatabasePath + "-wal"));
        Assert.False(File.Exists(live.DatabasePath + "-shm"));
    }

    [Fact]
    public async Task RestoreReplace_InjectedPromotionFailureRollsBackAndRetainsEvidence()
    {
        await using var source = await CreateSourceBackupAsync();
        var sourceBackup = Directory.GetFiles(
            source.BackupDirectory,
            "*.wlbackup").Single();
        var hooks = new CheckpointFailureHooks(
            LocalDataOperationCheckpoint.AfterRestorePromotion,
            new IOException("Synthetic private promotion failure."));
        await using var live = await LocalBackupTestHarness.CreateAsync(hooks);
        await live.InsertSyntheticHouseholdAsync(LiveMarker);
        var useCase = new ReplaceLocalDatabaseUseCase(
            live.CreateReplacementSessionFactory());

        var result = await useCase.ExecuteAsync(
            sourceBackup,
            confirmReplaceActive: true);

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.RestoreFailure,
            result.Failure!.Category);
        Assert.Equal(
            1L,
            await LocalBackupTestHarness.CountSyntheticHouseholdsAsync(
                live.DatabasePath,
                LiveMarker));
        Assert.Equal(
            0L,
            await LocalBackupTestHarness.CountSyntheticHouseholdsAsync(
                live.DatabasePath,
                SourceMarker));
        var retainedCandidate = Directory.GetFiles(
            Path.GetDirectoryName(live.DatabasePath)!,
            "*.wlrestore").Single();
        Assert.Equal(
            1L,
            await LocalBackupTestHarness.CountSyntheticHouseholdsAsync(
                retainedCandidate,
                SourceMarker));
        var preRestoreBackup = Directory.GetFiles(
            live.BackupDirectory,
            "*.wlbackup").Single();
        Assert.True((await live.Verifier.VerifyAsync(
            preRestoreBackup)).Succeeded);
        Assert.Empty(Directory.GetFiles(
            Path.GetDirectoryName(live.DatabasePath)!,
            "*.superseded.db"));
        Assert.DoesNotContain("Synthetic", result.Failure.Message);
        var ownership = live.OwnershipGuard.Acquire(
            live.DatabasePath,
            createDirectory: false);
        Assert.True(ownership.Succeeded);
        ownership.Value!.Dispose();
    }

    [Fact]
    public async Task RestoreReplace_PreBackupFailureLeavesLiveAndNoStage()
    {
        await using var source = await CreateSourceBackupAsync();
        var sourceBackup = Directory.GetFiles(
            source.BackupDirectory,
            "*.wlbackup").Single();
        var hooks = new CheckpointFailureHooks(
            LocalDataOperationCheckpoint.BeforeBackupPublish,
            new IOException("Synthetic private backup failure."));
        await using var live = await LocalBackupTestHarness.CreateAsync(hooks);
        await live.InsertSyntheticHouseholdAsync(LiveMarker);
        var useCase = new ReplaceLocalDatabaseUseCase(
            live.CreateReplacementSessionFactory());

        var result = await useCase.ExecuteAsync(
            sourceBackup,
            confirmReplaceActive: true);

        Assert.False(result.Succeeded);
        Assert.Equal(LocalDataFailureCategory.IoFailure, result.Failure!.Category);
        Assert.Equal(
            1L,
            await LocalBackupTestHarness.CountSyntheticHouseholdsAsync(
                live.DatabasePath,
                LiveMarker));
        Assert.Empty(Directory.GetFiles(
            Path.GetDirectoryName(live.DatabasePath)!,
            "*.wlrestore"));
        Assert.Empty(Directory.GetFiles(
            live.BackupDirectory,
            "*.wlbackup"));
    }

    [Fact]
    public async Task RestoreReplace_CancellationBeforeSwapLeavesLiveUntouched()
    {
        await using var source = await CreateSourceBackupAsync();
        var sourceBackup = Directory.GetFiles(
            source.BackupDirectory,
            "*.wlbackup").Single();
        var hooks = new CheckpointFailureHooks(
            LocalDataOperationCheckpoint.BeforeRestorePromotion,
            new OperationCanceledException("Synthetic cancellation."));
        await using var live = await LocalBackupTestHarness.CreateAsync(hooks);
        await live.InsertSyntheticHouseholdAsync(LiveMarker);
        var useCase = new ReplaceLocalDatabaseUseCase(
            live.CreateReplacementSessionFactory());

        var result = await useCase.ExecuteAsync(
            sourceBackup,
            confirmReplaceActive: true);

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.Cancelled,
            result.Failure!.Category);
        Assert.Equal(
            1L,
            await LocalBackupTestHarness.CountSyntheticHouseholdsAsync(
                live.DatabasePath,
                LiveMarker));
        Assert.Empty(Directory.GetFiles(
            Path.GetDirectoryName(live.DatabasePath)!,
            "*.wlrestore"));
        Assert.Single(Directory.GetFiles(
            live.BackupDirectory,
            "*.wlbackup"));
    }

    [Fact]
    public async Task RestoreReplace_ActiveOwnerFailsBeforeAnyMutation()
    {
        await using var source = await CreateSourceBackupAsync();
        var sourceBackup = Directory.GetFiles(
            source.BackupDirectory,
            "*.wlbackup").Single();
        await using var live = await LocalBackupTestHarness.CreateAsync();
        var ownership = live.OwnershipGuard.Acquire(
            live.DatabasePath,
            createDirectory: false);
        Assert.True(ownership.Succeeded);
        await using var lease = ownership.Value!;
        var useCase = new ReplaceLocalDatabaseUseCase(
            live.CreateReplacementSessionFactory());

        var result = await useCase.ExecuteAsync(
            sourceBackup,
            confirmReplaceActive: true);

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.OwnershipBusy,
            result.Failure!.Category);
        Assert.False(Directory.Exists(live.BackupDirectory));
    }

    [Fact]
    public async Task RestoreReplace_OldSchemaPackagePromotesAsMigrationRequired()
    {
        await using var source = await LocalBackupTestHarness.CreateAsync(
            targetMigration: "20260827072019_002_CommandReceipt");
        Directory.CreateDirectory(source.BackupDirectory);
        var backup = await source.BackupService.CreateVerifiedBackupAsync(
            source.DatabasePath,
            source.BackupDirectory,
            LocalBackupPurpose.PreMigration,
            allowMigrationRequired: true);
        await using var live = await LocalBackupTestHarness.CreateAsync();
        var useCase = new ReplaceLocalDatabaseUseCase(
            live.CreateReplacementSessionFactory());

        var result = await useCase.ExecuteAsync(
            backup.Value!.FilePath,
            confirmReplaceActive: true);

        Assert.True(result.Succeeded, result.Failure?.Message);
        var verification = await live.DatabaseVerifier.VerifyAsync(
            live.DatabasePath);
        Assert.True(verification.Succeeded);
        Assert.Equal(
            LocalDatabaseCompatibility.MigrationRequired,
            verification.Value!.Compatibility);
        Assert.True(File.Exists(result.Value!.PreRestoreBackupPath));
        Assert.True(File.Exists(result.Value.SupersededDatabasePath));
    }

    private static async Task<LocalBackupTestHarness> CreateSourceBackupAsync()
    {
        var source = await LocalBackupTestHarness.CreateAsync();
        await source.InsertSyntheticHouseholdAsync(SourceMarker);
        var result = await source.CreateBackupAsync();

        if (!result.Succeeded)
        {
            await source.DisposeAsync();
            throw new InvalidOperationException(result.Failure!.Message);
        }

        return source;
    }

    private static string ComputeSha256(byte[] content)
        => Convert.ToHexString(SHA256.HashData(content));

    private static async Task<string> HashFileAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private sealed class CheckpointFailureHooks : ILocalDataOperationHooks
    {
        private readonly LocalDataOperationCheckpoint _checkpoint;
        private readonly Exception _exception;

        internal CheckpointFailureHooks(
            LocalDataOperationCheckpoint checkpoint,
            Exception exception)
        {
            _checkpoint = checkpoint;
            _exception = exception;
        }

        public ValueTask OnCheckpointAsync(
            LocalDataOperationCheckpoint checkpoint,
            string primaryPath,
            string? secondaryPath,
            CancellationToken cancellationToken)
            => checkpoint == _checkpoint
                ? ValueTask.FromException(_exception)
                : ValueTask.CompletedTask;
    }

    private sealed class RestoreTargetCollisionHooks : ILocalDataOperationHooks
    {
        internal const string Marker = "synthetic preexisting restore target";

        public async ValueTask OnCheckpointAsync(
            LocalDataOperationCheckpoint checkpoint,
            string primaryPath,
            string? secondaryPath,
            CancellationToken cancellationToken)
        {
            if (checkpoint
                != LocalDataOperationCheckpoint.BeforeRestoreStagePublish)
            {
                return;
            }

            await File.WriteAllTextAsync(
                secondaryPath!,
                Marker,
                cancellationToken);
        }
    }
}
