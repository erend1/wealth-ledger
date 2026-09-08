using WealthLedger.Application.LocalData;
using WealthLedger.Application.Setup;

namespace WealthLedger.Application.Tests.LocalData
{
    public sealed class SelectLocalStartupModeUseCaseTests
    {
        private static readonly DateTimeOffset BackupTime =
            new(
                2026,
                9,
                4,
                8,
                0,
                0,
                TimeSpan.Zero);

        [Fact]
        public async Task Execute_StatusWithoutUsableValue_ReturnsBlocked()
        {
            var statusReader =
                new StubLocalDataStatusReader(
                    LocalDataOperationResult<LocalDataStatus>.Failed(
                        LocalDataFailureCategory.UnsafePath,
                        "Synthetic private detail."));

            var setupReader =
                CompleteSetupReader();

            var useCase =
                CreateUseCase(
                    statusReader,
                    setupReader);

            var result =
                await useCase.ExecuteAsync();

            Assert.Equal(
                LocalStartupMode.Blocked,
                result.Mode);

            Assert.Equal(
                LocalDataFailureCategory.UnsafePath,
                result.Failure!.Category);

            Assert.Equal(
                0,
                setupReader.ReadCount);
        }

        [Fact]
        public async Task Execute_UnsafeDatabasePath_ReturnsBlocked()
        {
            var status =
                CreateStatus() with
                {
                    DatabasePathSafe = false
                };

            var setupReader =
                CompleteSetupReader();

            var result =
                await CreateUseCase(
                        SuccessStatusReader(status),
                        setupReader)
                    .ExecuteAsync();

            Assert.Equal(
                LocalStartupMode.Blocked,
                result.Mode);

            Assert.Equal(
                LocalDataFailureCategory.UnsafePath,
                result.Failure!.Category);

            Assert.Equal(
                0,
                setupReader.ReadCount);
        }

        [Fact]
        public async Task Execute_BackupDirectoryUnconfigured_ReturnsBlocked()
        {
            var status =
                CreateStatus() with
                {
                    BackupDirectory = null,
                    BackupDirectoryConfigured = false,
                    BackupDirectoryExists = false
                };

            var setupReader =
                CompleteSetupReader();

            var result =
                await CreateUseCase(
                        SuccessStatusReader(status),
                        setupReader)
                    .ExecuteAsync();

            Assert.Equal(
                LocalStartupMode.Blocked,
                result.Mode);

            Assert.Equal(
                LocalDataFailureCategory.InvalidInputOrConfiguration,
                result.Failure!.Category);

            Assert.Equal(
                0,
                setupReader.ReadCount);
        }

        [Fact]
        public async Task Execute_OwnershipUnavailable_ReturnsBlocked()
        {
            var status =
                CreateStatus() with
                {
                    OwnershipAvailable = false
                };

            var setupReader =
                CompleteSetupReader();

            var result =
                await CreateUseCase(
                        SuccessStatusReader(status),
                        setupReader)
                    .ExecuteAsync();

            Assert.Equal(
                LocalStartupMode.Blocked,
                result.Mode);

            Assert.Equal(
                LocalDataFailureCategory.OwnershipBusy,
                result.Failure!.Category);

            Assert.Equal(
                0,
                setupReader.ReadCount);
        }

        [Fact]
        public async Task Execute_MissingSafeDatabase_ReturnsStorageUninitialized()
        {
            var status =
                CreateStatus() with
                {
                    DatabaseExists = false,
                    Compatibility =
                        LocalDatabaseCompatibility.Uninitialized,
                    IntegrityStatus =
                        LocalDataIntegrityStatus.NotChecked,
                    AppliedMigrations = [],
                    LatestVerifiedBackup = null,
                    LiveWorkspaceId = null
                };

            var statusReader =
                new StubLocalDataStatusReader(
                    LocalDataOperationResult<LocalDataStatus>.Failed(
                        LocalDataFailureCategory.NotFound,
                        "Synthetic missing database.",
                        status));

            var setupReader =
                CompleteSetupReader();

            var result =
                await CreateUseCase(
                        statusReader,
                        setupReader)
                    .ExecuteAsync();

            Assert.Equal(
                LocalStartupMode.StorageUninitialized,
                result.Mode);

            Assert.Null(
                result.Failure);

            Assert.Null(
                result.WorkspaceState);

            Assert.Equal(
                0,
                setupReader.ReadCount);
        }

        [Fact]
        public async Task Execute_MissingDatabaseWithUnconfiguredBackup_ReturnsBlocked()
        {
            var status =
                CreateStatus() with
                {
                    DatabaseExists = false,
                    BackupDirectory = null,
                    BackupDirectoryConfigured = false,
                    BackupDirectoryExists = false,
                    Compatibility =
                        LocalDatabaseCompatibility.Uninitialized,
                    IntegrityStatus =
                        LocalDataIntegrityStatus.NotChecked,
                    AppliedMigrations = [],
                    LatestVerifiedBackup = null,
                    LiveWorkspaceId = null
                };

            var statusReader =
                new StubLocalDataStatusReader(
                    LocalDataOperationResult<LocalDataStatus>.Failed(
                        LocalDataFailureCategory.NotFound,
                        "Synthetic missing database.",
                        status));

            var setupReader =
                CompleteSetupReader();

            var result =
                await CreateUseCase(
                        statusReader,
                        setupReader)
                    .ExecuteAsync();

            Assert.Equal(
                LocalStartupMode.Blocked,
                result.Mode);

            Assert.Equal(
                LocalDataFailureCategory.InvalidInputOrConfiguration,
                result.Failure!.Category);

            Assert.Equal(
                0,
                setupReader.ReadCount);
        }

        [Fact]
        public async Task Execute_MigrationRequired_ReturnsBlocked()
        {
            var status =
                CreateStatus() with
                {
                    Compatibility =
                        LocalDatabaseCompatibility.MigrationRequired,
                    PendingMigrations =
                    [
                        "20260903075104_005_WorkspaceIdentity"
                    ]
                };

            var statusReader =
                new StubLocalDataStatusReader(
                    LocalDataOperationResult<LocalDataStatus>.Failed(
                        LocalDataFailureCategory.DatabaseNotReady,
                        "Synthetic migration detail.",
                        status));

            var setupReader =
                CompleteSetupReader();

            var result =
                await CreateUseCase(
                        statusReader,
                        setupReader)
                    .ExecuteAsync();

            Assert.Equal(
                LocalStartupMode.Blocked,
                result.Mode);

            Assert.Equal(
                LocalDataFailureCategory.DatabaseNotReady,
                result.Failure!.Category);

            Assert.Equal(
                0,
                setupReader.ReadCount);
        }

        [Fact]
        public async Task Execute_IncompatibleDatabase_ReturnsBlocked()
        {
            var status =
                CreateStatus() with
                {
                    Compatibility =
                        LocalDatabaseCompatibility.Incompatible
                };

            var setupReader =
                CompleteSetupReader();

            var result =
                await CreateUseCase(
                        SuccessStatusReader(status),
                        setupReader)
                    .ExecuteAsync();

            Assert.Equal(
                LocalStartupMode.Blocked,
                result.Mode);

            Assert.Equal(
                0,
                setupReader.ReadCount);
        }

        [Fact]
        public async Task Execute_IntegrityFailure_ReturnsBlocked()
        {
            var status =
                CreateStatus() with
                {
                    IntegrityStatus =
                        LocalDataIntegrityStatus.Failed
                };

            var setupReader =
                CompleteSetupReader();

            var result =
                await CreateUseCase(
                        SuccessStatusReader(status),
                        setupReader)
                    .ExecuteAsync();

            Assert.Equal(
                LocalStartupMode.Blocked,
                result.Mode);

            Assert.Equal(
                LocalDataFailureCategory.IntegrityFailure,
                result.Failure!.Category);

            Assert.Equal(
                0,
                setupReader.ReadCount);
        }

        [Fact]
        public async Task Execute_MissingWorkspaceIdentity_ReturnsBlocked()
        {
            var status =
                CreateStatus() with
                {
                    LiveWorkspaceId = null
                };

            var setupReader =
                CompleteSetupReader();

            var result =
                await CreateUseCase(
                        SuccessStatusReader(status),
                        setupReader)
                    .ExecuteAsync();

            Assert.Equal(
                LocalStartupMode.Blocked,
                result.Mode);

            Assert.Equal(
                LocalDataFailureCategory.DatabaseNotReady,
                result.Failure!.Category);

            Assert.Equal(
                0,
                setupReader.ReadCount);
        }

        [Fact]
        public async Task Execute_EmptyWorkspace_ReturnsWorkspaceUninitialized()
        {
            var setupReader =
                SetupReader(
                    CoreLedgerSetupState.Empty);

            var result =
                await CreateUseCase(
                        SuccessStatusReader(
                            CreateStatus()),
                        setupReader)
                    .ExecuteAsync();

            Assert.Equal(
                LocalStartupMode.WorkspaceUninitialized,
                result.Mode);

            Assert.Equal(
                CoreLedgerSetupState.Empty,
                result.WorkspaceState);

            Assert.Null(
                result.Failure);

            Assert.Equal(
                1,
                setupReader.ReadCount);
        }

        [Fact]
        public async Task Execute_PartialWorkspace_ReturnsBlocked()
        {
            var setupReader =
                SetupReader(
                    CoreLedgerSetupState.PartialOrConflicting);

            var result =
                await CreateUseCase(
                        SuccessStatusReader(
                            CreateStatus()),
                        setupReader)
                    .ExecuteAsync();

            Assert.Equal(
                LocalStartupMode.Blocked,
                result.Mode);

            Assert.Equal(
                CoreLedgerSetupState.PartialOrConflicting,
                result.WorkspaceState);

            Assert.Equal(
                LocalDataFailureCategory.DatabaseNotReady,
                result.Failure!.Category);
        }

        [Fact]
        public async Task Execute_CompleteWorkspaceWithoutBackup_ReturnsInitialBackupRequired()
        {
            var status =
                CreateStatus() with
                {
                    LatestVerifiedBackup = null,
                    LocalProtectionReady = false
                };

            var setupReader =
                CompleteSetupReader();

            var result =
                await CreateUseCase(
                        SuccessStatusReader(status),
                        setupReader)
                    .ExecuteAsync();

            Assert.Equal(
                LocalStartupMode.InitialBackupRequired,
                result.Mode);

            Assert.Equal(
                CoreLedgerSetupState.Complete,
                result.WorkspaceState);

            Assert.Null(
                result.Failure);
        }

        [Fact]
        public async Task Execute_CompleteWorkspaceWithMatchedBackup_ReturnsReady()
        {
            var result =
                await CreateUseCase(
                        SuccessStatusReader(
                            CreateStatus()),
                        CompleteSetupReader())
                    .ExecuteAsync();

            Assert.Equal(
                LocalStartupMode.Ready,
                result.Mode);

            Assert.Equal(
                CoreLedgerSetupState.Complete,
                result.WorkspaceState);

            Assert.Null(
                result.Failure);
        }

        [Fact]
        public async Task Execute_ReadyDoesNotRequireSeparationAcknowledgement()
        {
            var status =
                CreateStatus() with
                {
                    DestinationSeparationConfirmed = false,
                    LocalProtectionReady = false
                };

            var result =
                await CreateUseCase(
                        SuccessStatusReader(status),
                        CompleteSetupReader())
                    .ExecuteAsync();

            Assert.Equal(
                LocalStartupMode.Ready,
                result.Mode);
        }

        [Fact]
        public async Task Execute_ReadyDoesNotRequireEncryptionAcknowledgement()
        {
            var status =
                CreateStatus() with
                {
                    DestinationEncryptionConfirmed = false,
                    LocalProtectionReady = false
                };

            var result =
                await CreateUseCase(
                        SuccessStatusReader(status),
                        CompleteSetupReader())
                    .ExecuteAsync();

            Assert.Equal(
                LocalStartupMode.Ready,
                result.Mode);
        }

        [Fact]
        public async Task Execute_NonMatchedLatestBackupFailsClosed()
        {
            var status =
                CreateStatus() with
                {
                    LatestVerifiedBackup =
                        CreateBackup(
                            LocalBackupWorkspaceBinding.Unrelated),
                    LocalProtectionReady = false
                };

            var result =
                await CreateUseCase(
                        SuccessStatusReader(status),
                        CompleteSetupReader())
                    .ExecuteAsync();

            Assert.Equal(
                LocalStartupMode.Blocked,
                result.Mode);

            Assert.Equal(
                LocalDataFailureCategory.InvalidBackup,
                result.Failure!.Category);
        }

        [Fact]
        public async Task Execute_SetupStateReadFailure_ReturnsBlocked()
        {
            var setupReader =
                new StubCoreLedgerSetupStateReader(
                    LocalDataOperationResult<
                        CoreLedgerSetupStateSnapshot>.Failed(
                            LocalDataFailureCategory.DatabaseNotReady,
                            "Synthetic private persistence detail."));

            var result =
                await CreateUseCase(
                        SuccessStatusReader(
                            CreateStatus()),
                        setupReader)
                    .ExecuteAsync();

            Assert.Equal(
                LocalStartupMode.Blocked,
                result.Mode);

            Assert.Equal(
                LocalDataFailureCategory.DatabaseNotReady,
                result.Failure!.Category);

            Assert.Equal(
                1,
                setupReader.ReadCount);
        }

        [Fact]
        public async Task Execute_CancelledStatusInspection_ReturnsBlocked()
        {
            var statusReader =
                new StubLocalDataStatusReader(
                    LocalDataOperationResult<LocalDataStatus>.Failed(
                        LocalDataFailureCategory.Cancelled,
                        "Synthetic cancellation."));

            var setupReader =
                CompleteSetupReader();

            var result =
                await CreateUseCase(
                        statusReader,
                        setupReader)
                    .ExecuteAsync();

            Assert.Equal(
                LocalStartupMode.Blocked,
                result.Mode);

            Assert.Equal(
                LocalDataFailureCategory.Cancelled,
                result.Failure!.Category);

            Assert.Equal(
                0,
                setupReader.ReadCount);
        }

        private static SelectLocalStartupModeUseCase CreateUseCase(
            ILocalDataStatusReader statusReader,
            StubCoreLedgerSetupStateReader setupReader)
            => new(
                statusReader,
                new GetCoreLedgerSetupStateUseCase(
                    setupReader));

        private static StubLocalDataStatusReader SuccessStatusReader(
            LocalDataStatus status)
            => new(
                LocalDataOperationResult<
                    LocalDataStatus>.Success(
                        status));

        private static StubCoreLedgerSetupStateReader
            CompleteSetupReader()
            => SetupReader(
                CoreLedgerSetupState.Complete);

        private static StubCoreLedgerSetupStateReader SetupReader(
            CoreLedgerSetupState state)
            => new(
                LocalDataOperationResult<
                    CoreLedgerSetupStateSnapshot>.Success(
                        new CoreLedgerSetupStateSnapshot(
                            state)));

        private static LocalDataStatus CreateStatus()
            => new(
                DatabasePath:
                    @"C:\synthetic\wealthledger.db",
                BackupDirectory:
                    @"C:\synthetic\backups",
                ApplicationVersion:
                    "1.0.0",
                DatabasePathSafe:
                    true,
                DatabaseExists:
                    true,
                BackupDirectoryConfigured:
                    true,
                BackupDirectoryExists:
                    true,
                OwnershipAvailable:
                    true,
                AppliedMigrations:
                [
                    "20260903075104_005_WorkspaceIdentity"
                ],
                PendingMigrations:
                    [],
                Compatibility:
                    LocalDatabaseCompatibility.Compatible,
                IntegrityStatus:
                    LocalDataIntegrityStatus.Passed,
                LatestVerifiedBackup:
                    CreateBackup(
                        LocalBackupWorkspaceBinding.Matched),
                UnrelatedVerifiedBackupCount:
                    0,
                LiveWorkspaceId:
                    "00000000-0000-0000-0000-000000000001",
                DestinationSeparationConfirmed:
                    true,
                DestinationEncryptionConfirmed:
                    true,
                LocalProtectionReady:
                    true,
                EncryptionMode:
                    "PLAINTEXT");

        private static LocalBackupSummary CreateBackup(
            LocalBackupWorkspaceBinding binding)
            => new(
                FilePath:
                    @"C:\synthetic\backups\synthetic.wlbackup",
                CreatedAtUtc:
                    BackupTime,
                VerifiedAtUtc:
                    BackupTime,
                DigestPrefix:
                    "0123456789ab",
                LatestMigration:
                    "20260903075104_005_WorkspaceIdentity",
                EncryptionMode:
                    "PLAINTEXT",
                WorkspaceBinding:
                    binding);

        private sealed class StubLocalDataStatusReader
            : ILocalDataStatusReader
        {
            private readonly LocalDataOperationResult<
                LocalDataStatus> _result;

            internal StubLocalDataStatusReader(
                LocalDataOperationResult<
                    LocalDataStatus> result)
            {
                _result = result;
            }

            public Task<LocalDataOperationResult<
                LocalDataStatus>> ReadAsync(
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();

                return Task.FromResult(
                    _result);
            }
        }

        private sealed class StubCoreLedgerSetupStateReader
            : ICoreLedgerSetupStateReader
        {
            private readonly LocalDataOperationResult<
                CoreLedgerSetupStateSnapshot> _result;

            internal StubCoreLedgerSetupStateReader(
                LocalDataOperationResult<
                    CoreLedgerSetupStateSnapshot> result)
            {
                _result = result;
            }

            internal int ReadCount { get; private set; }

            public Task<LocalDataOperationResult<
                CoreLedgerSetupStateSnapshot>> ReadAsync(
                CancellationToken cancellationToken = default)
            {
                ReadCount++;

                cancellationToken.ThrowIfCancellationRequested();

                return Task.FromResult(
                    _result);
            }
        }
    }
}
