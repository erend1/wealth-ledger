using WealthLedger.Application.LocalData;

namespace WealthLedger.Application.Tests.LocalData;

public sealed class LocalDataOperationUseCaseTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task VerifyBackup_RelativePath_IsRejectedBeforePortCall()
    {
        var verifier = new BackupVerifierFake();
        var useCase = new VerifyLocalBackupUseCase(verifier);

        var result = await useCase.ExecuteAsync("relative.wlbackup");

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.InvalidInputOrConfiguration,
            result.Failure!.Category);
        Assert.Equal(0, verifier.CallCount);
    }

    [Fact]
    public async Task StageRestore_IdenticalPaths_AreRejectedBeforePortCall()
    {
        var stager = new RestoreStagerFake();
        var useCase = new StageLocalRestoreUseCase(stager);
        var path = AbsolutePath("candidate.wlbackup");

        var result = await useCase.ExecuteAsync(path, path);

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.InvalidInputOrConfiguration,
            result.Failure!.Category);
        Assert.Equal(0, stager.CallCount);
    }

    [Fact]
    public async Task Migrate_NoPendingMigration_IsIdempotentNoOpWithoutBackup()
    {
        var session = new MigrationSessionFake(
            new LocalDatabaseMigrationPlan(
                AbsolutePath("wealthledger.db"),
                ["001_CoreLedger", "002_CommandReceipt"],
                []));
        var factory = new MigrationSessionFactoryFake(session);
        var useCase = new MigrateLocalDatabaseUseCase(
            factory,
            new FixedTimeProvider(Now));

        var result = await useCase.ExecuteAsync();

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.WasNoOp);
        Assert.Null(result.Value.PreMigrationBackupPath);
        Assert.Equal("002_CommandReceipt", result.Value.StartingMigration);
        Assert.Equal("002_CommandReceipt", result.Value.EndingMigration);
        Assert.Equal(["inspect"], session.Calls);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task Migrate_PendingMigration_VerifiesBackupBeforeApply()
    {
        var databasePath = AbsolutePath("wealthledger.db");
        var backupPath = AbsolutePath("pre-migration.wlbackup");
        var plan = new LocalDatabaseMigrationPlan(
            databasePath,
            ["001_CoreLedger", "002_CommandReceipt"],
            ["003_ReversalDependencySemantics"]);
        var backup = BackupCreation(backupPath);
        var migration = new LocalDatabaseMigration(
            databasePath,
            "002_CommandReceipt",
            "003_ReversalDependencySemantics",
            backupPath,
            WasNoOp: false,
            Now);
        var session = new MigrationSessionFake(plan)
        {
            BackupResult = LocalDataOperationResult<LocalBackupCreation>
                .Success(backup),
            ApplyResult = LocalDataOperationResult<LocalDatabaseMigration>
                .Success(migration)
        };
        var useCase = new MigrateLocalDatabaseUseCase(
            new MigrationSessionFactoryFake(session),
            new FixedTimeProvider(Now));

        var result = await useCase.ExecuteAsync();

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.WasNoOp);
        Assert.Equal(
            ["inspect", "backup", "apply"],
            session.Calls);
        Assert.Same(plan, session.AppliedPlan);
        Assert.Same(backup, session.AppliedBackup);
    }

    [Fact]
    public async Task Migrate_FailedBackup_PreventsMigration()
    {
        var session = new MigrationSessionFake(
            new LocalDatabaseMigrationPlan(
                AbsolutePath("wealthledger.db"),
                ["001_CoreLedger"],
                ["002_CommandReceipt"]))
        {
            BackupResult = LocalDataOperationResult<LocalBackupCreation>.Failed(
                LocalDataFailureCategory.IoFailure,
                "The pre-migration backup could not be created.")
        };
        var useCase = new MigrateLocalDatabaseUseCase(
            new MigrationSessionFactoryFake(session),
            new FixedTimeProvider(Now));

        var result = await useCase.ExecuteAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(LocalDataFailureCategory.IoFailure, result.Failure!.Category);
        Assert.Equal(["inspect", "backup"], session.Calls);
        Assert.Null(session.AppliedPlan);
    }

    [Fact]
    public async Task Replace_MissingConfirmation_DoesNotAcquireOwnership()
    {
        var factory = new ReplacementSessionFactoryFake(
            new ReplacementSessionFake());
        var useCase = new ReplaceLocalDatabaseUseCase(factory);

        var result = await useCase.ExecuteAsync(
            AbsolutePath("candidate.wlbackup"),
            confirmReplaceActive: false);

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.InvalidInputOrConfiguration,
            result.Failure!.Category);
        Assert.Equal(0, factory.CallCount);
    }

    [Fact]
    public async Task Replace_StagesAndBacksUpBeforePromotion()
    {
        var databasePath = AbsolutePath("wealthledger.db");
        var backupFilePath = AbsolutePath("candidate.wlbackup");
        var stage = new LocalRestoreStage(
            backupFilePath,
            AbsolutePath("candidate.wlrestore"),
            LocalDatabaseCompatibility.Compatible,
            "003_ReversalDependencySemantics",
            Now);
        var preRestoreBackup = BackupCreation(
            AbsolutePath("pre-restore.wlbackup"));
        var replacement = new LocalDatabaseReplacement(
            databasePath,
            preRestoreBackup.FilePath,
            AbsolutePath("superseded.db"),
            "003_ReversalDependencySemantics",
            Now);
        var session = new ReplacementSessionFake
        {
            StageResult = LocalDataOperationResult<LocalRestoreStage>.Success(stage),
            BackupResult = LocalDataOperationResult<LocalBackupCreation>
                .Success(preRestoreBackup),
            PromoteResult = LocalDataOperationResult<LocalDatabaseReplacement>
                .Success(replacement)
        };
        var useCase = new ReplaceLocalDatabaseUseCase(
            new ReplacementSessionFactoryFake(session));

        var result = await useCase.ExecuteAsync(
            backupFilePath,
            confirmReplaceActive: true);

        Assert.True(result.Succeeded);
        Assert.Equal(["stage", "backup", "promote"], session.Calls);
        Assert.Same(stage, session.PromotedStage);
        Assert.Same(preRestoreBackup, session.PromotedBackup);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task Replace_FailedStage_PreventsBackupAndPromotion()
    {
        var session = new ReplacementSessionFake
        {
            StageResult = LocalDataOperationResult<LocalRestoreStage>.Failed(
                LocalDataFailureCategory.InvalidBackup,
                "The backup package is invalid.")
        };
        var useCase = new ReplaceLocalDatabaseUseCase(
            new ReplacementSessionFactoryFake(session));

        var result = await useCase.ExecuteAsync(
            AbsolutePath("candidate.wlbackup"),
            confirmReplaceActive: true);

        Assert.False(result.Succeeded);
        Assert.Equal(LocalDataFailureCategory.InvalidBackup, result.Failure!.Category);
        Assert.Equal(["stage"], session.Calls);
    }

    [Fact]
    public void RoutineModels_DoNotExposeLedgerValueOrPrivateIdentityFields()
    {
        var forbiddenFragments = new[]
        {
            "Household",
            "Member",
            "Account",
            "AssetId",
            "TransactionId",
            "Balance",
            "Quantity",
            "Amount",
            "Note",
            "Reference"
        };
        var modelTypes = new[]
        {
            typeof(LocalDataStatus),
            typeof(LocalBackupSummary),
            typeof(LocalDatabaseInitialization),
            typeof(LocalDatabaseMigration),
            typeof(LocalBackupCreation),
            typeof(LocalBackupVerification),
            typeof(LocalRestoreStage),
            typeof(LocalDatabaseReplacement)
        };

        foreach (var property in modelTypes.SelectMany(x => x.GetProperties()))
        {
            Assert.DoesNotContain(
                forbiddenFragments,
                fragment => property.Name.Contains(
                    fragment,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    private static LocalBackupCreation BackupCreation(string path)
        => new(
            path,
            Now,
            Now,
            "0123456789AB",
            ["003_ReversalDependencySemantics"],
            "003_ReversalDependencySemantics",
            "PLAINTEXT");

    private static string AbsolutePath(string fileName)
        => Path.Combine(Path.GetTempPath(), "WealthLedger.Tests", fileName);

    private sealed class BackupVerifierFake : ILocalBackupVerifier
    {
        internal int CallCount { get; private set; }

        public Task<LocalDataOperationResult<LocalBackupVerification>> VerifyAsync(
            string backupFilePath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("The fake should not be called.");
        }
    }

    private sealed class RestoreStagerFake : ILocalRestoreStager
    {
        internal int CallCount { get; private set; }

        public Task<LocalDataOperationResult<LocalRestoreStage>> StageAsync(
            string backupFilePath,
            string targetDatabasePath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("The fake should not be called.");
        }
    }

    private sealed class MigrationSessionFactoryFake
        : ILocalDatabaseMigrationSessionFactory
    {
        private readonly ILocalDatabaseMigrationSession _session;

        internal MigrationSessionFactoryFake(
            ILocalDatabaseMigrationSession session)
        {
            _session = session;
        }

        public Task<LocalDataOperationResult<ILocalDatabaseMigrationSession>>
            OpenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(
                LocalDataOperationResult<ILocalDatabaseMigrationSession>
                    .Success(_session));
    }

    private sealed class MigrationSessionFake : ILocalDatabaseMigrationSession
    {
        private readonly LocalDatabaseMigrationPlan _plan;

        internal MigrationSessionFake(LocalDatabaseMigrationPlan plan)
        {
            _plan = plan;
            BackupResult = LocalDataOperationResult<LocalBackupCreation>.Failed(
                LocalDataFailureCategory.IoFailure,
                "No backup result was configured.");
            ApplyResult = LocalDataOperationResult<LocalDatabaseMigration>.Failed(
                LocalDataFailureCategory.MigrationFailure,
                "No migration result was configured.");
        }

        internal List<string> Calls { get; } = [];

        internal LocalDataOperationResult<LocalBackupCreation> BackupResult
        {
            get;
            init;
        }

        internal LocalDataOperationResult<LocalDatabaseMigration> ApplyResult
        {
            get;
            init;
        }

        internal LocalDatabaseMigrationPlan? AppliedPlan { get; private set; }

        internal LocalBackupCreation? AppliedBackup { get; private set; }

        internal bool Disposed { get; private set; }

        public Task<LocalDataOperationResult<LocalDatabaseMigrationPlan>> InspectAsync(
            CancellationToken cancellationToken = default)
        {
            Calls.Add("inspect");
            return Task.FromResult(
                LocalDataOperationResult<LocalDatabaseMigrationPlan>.Success(_plan));
        }

        public Task<LocalDataOperationResult<LocalBackupCreation>>
            CreateVerifiedPreMigrationBackupAsync(
                CancellationToken cancellationToken = default)
        {
            Calls.Add("backup");
            return Task.FromResult(BackupResult);
        }

        public Task<LocalDataOperationResult<LocalDatabaseMigration>> ApplyAsync(
            LocalDatabaseMigrationPlan plan,
            LocalBackupCreation verifiedPreMigrationBackup,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("apply");
            AppliedPlan = plan;
            AppliedBackup = verifiedPreMigrationBackup;
            return Task.FromResult(ApplyResult);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ReplacementSessionFactoryFake
        : ILocalDatabaseReplacementSessionFactory
    {
        private readonly ILocalDatabaseReplacementSession _session;

        internal ReplacementSessionFactoryFake(
            ILocalDatabaseReplacementSession session)
        {
            _session = session;
        }

        internal int CallCount { get; private set; }

        public Task<LocalDataOperationResult<ILocalDatabaseReplacementSession>>
            OpenAsync(
                string backupFilePath,
                CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(
                LocalDataOperationResult<ILocalDatabaseReplacementSession>
                    .Success(_session));
        }
    }

    private sealed class ReplacementSessionFake
        : ILocalDatabaseReplacementSession
    {
        internal List<string> Calls { get; } = [];

        internal LocalDataOperationResult<LocalRestoreStage> StageResult
        {
            get;
            init;
        } = LocalDataOperationResult<LocalRestoreStage>.Failed(
            LocalDataFailureCategory.RestoreFailure,
            "No stage result was configured.");

        internal LocalDataOperationResult<LocalBackupCreation> BackupResult
        {
            get;
            init;
        } = LocalDataOperationResult<LocalBackupCreation>.Failed(
            LocalDataFailureCategory.IoFailure,
            "No backup result was configured.");

        internal LocalDataOperationResult<LocalDatabaseReplacement> PromoteResult
        {
            get;
            init;
        } = LocalDataOperationResult<LocalDatabaseReplacement>.Failed(
            LocalDataFailureCategory.RestoreFailure,
            "No promotion result was configured.");

        internal LocalRestoreStage? PromotedStage { get; private set; }

        internal LocalBackupCreation? PromotedBackup { get; private set; }

        internal bool Disposed { get; private set; }

        public Task<LocalDataOperationResult<LocalRestoreStage>> StageCandidateAsync(
            CancellationToken cancellationToken = default)
        {
            Calls.Add("stage");
            return Task.FromResult(StageResult);
        }

        public Task<LocalDataOperationResult<LocalBackupCreation>>
            CreateVerifiedPreRestoreBackupAsync(
                CancellationToken cancellationToken = default)
        {
            Calls.Add("backup");
            return Task.FromResult(BackupResult);
        }

        public Task<LocalDataOperationResult<LocalDatabaseReplacement>> PromoteAsync(
            LocalRestoreStage verifiedStage,
            LocalBackupCreation verifiedPreRestoreBackup,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("promote");
            PromotedStage = verifiedStage;
            PromotedBackup = verifiedPreRestoreBackup;
            return Task.FromResult(PromoteResult);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        internal FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
