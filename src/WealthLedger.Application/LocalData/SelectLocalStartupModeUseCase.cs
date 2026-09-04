using WealthLedger.Application.Setup;

namespace WealthLedger.Application.LocalData
{
    public sealed class SelectLocalStartupModeUseCase
    {
        private readonly ILocalDataStatusReader _statusReader;
        private readonly GetCoreLedgerSetupStateUseCase _getCoreLedgerSetupState;

        public SelectLocalStartupModeUseCase(
            ILocalDataStatusReader statusReader,
            GetCoreLedgerSetupStateUseCase getCoreLedgerSetupState)
        {
            _statusReader = statusReader
                ?? throw new ArgumentNullException(nameof(statusReader));

            _getCoreLedgerSetupState = getCoreLedgerSetupState
                ?? throw new ArgumentNullException(
                    nameof(getCoreLedgerSetupState));
        }

        public async Task<LocalStartupSelection> ExecuteAsync(
            CancellationToken cancellationToken = default)
        {
            /*
             * This inspection must run while the host owns no process-lifetime
             * database lease. ILocalDataStatusReader checks ownership
             * availability itself.
             */
            var statusResult =
                await _statusReader.ReadAsync(
                    cancellationToken);

            var status =
                statusResult.Value;

            if (status is null)
            {
                return Blocked(
                    status: null,
                    statusResult.Failure
                        ?? Failure(
                            LocalDataFailureCategory.DatabaseNotReady,
                            "Local data readiness could not be determined."));
            }

            if (!status.DatabasePathSafe)
            {
                return Blocked(
                    status,
                    Failure(
                        LocalDataFailureCategory.UnsafePath,
                        "The configured local data path is not safe."));
            }

            /*
             * The browser is deliberately unable to supply Backup:Directory.
             * Missing operator configuration therefore blocks even an otherwise
             * safe missing-database state.
             */
            if (!status.BackupDirectoryConfigured)
            {
                return Blocked(
                    status,
                    Failure(
                        LocalDataFailureCategory.InvalidInputOrConfiguration,
                        "The backup directory is not configured."));
            }

            if (!status.OwnershipAvailable)
            {
                return Blocked(
                    status,
                    Failure(
                        LocalDataFailureCategory.OwnershipBusy,
                        "Another local data operation is already in progress."));
            }

            /*
             * SqliteLocalDataStatusReader deliberately returns a populated status
             * together with NotFound for an uninitialized safe database.
             */
            if (!status.DatabaseExists)
            {
                if (statusResult.Failure?.Category
                    == LocalDataFailureCategory.NotFound)
                {
                    return new LocalStartupSelection(
                        LocalStartupMode.StorageUninitialized,
                        status,
                        Failure: null,
                        WorkspaceState: null);
                }

                return Blocked(
                    status,
                    statusResult.Failure
                        ?? Failure(
                            LocalDataFailureCategory.DatabaseNotReady,
                            "The configured database is not available."));
            }

            /*
             * For an existing database, any remaining status-reader failure is
             * fail-closed. DatabaseNotReady is intentionally not converted into
             * a setup mode.
             */
            if (!statusResult.Succeeded)
            {
                return Blocked(
                    status,
                    statusResult.Failure!);
            }

            if (status.Compatibility
                != LocalDatabaseCompatibility.Compatible)
            {
                return Blocked(
                    status,
                    Failure(
                        LocalDataFailureCategory.DatabaseNotReady,
                        "The database schema is not compatible with this application."));
            }

            if (status.PendingMigrations.Count != 0)
            {
                return Blocked(
                    status,
                    Failure(
                        LocalDataFailureCategory.DatabaseNotReady,
                        "The database requires explicit migration before use."));
            }

            if (status.IntegrityStatus
                != LocalDataIntegrityStatus.Passed)
            {
                return Blocked(
                    status,
                    Failure(
                        LocalDataFailureCategory.IntegrityFailure,
                        "The database did not pass integrity verification."));
            }

            if (string.IsNullOrWhiteSpace(
                    status.LiveWorkspaceId))
            {
                return Blocked(
                    status,
                    Failure(
                        LocalDataFailureCategory.DatabaseNotReady,
                        "The database has no current workspace identity."));
            }

            var setupStateResult =
                await _getCoreLedgerSetupState.ExecuteAsync(
                    cancellationToken);

            if (!setupStateResult.Succeeded
                || setupStateResult.Value is null)
            {
                return Blocked(
                    status,
                    setupStateResult.Failure
                        ?? Failure(
                            LocalDataFailureCategory.DatabaseNotReady,
                            "Core ledger setup state could not be determined."));
            }

            var workspaceState =
                setupStateResult.Value.State;

            if (workspaceState
                == CoreLedgerSetupState.Empty)
            {
                return new LocalStartupSelection(
                    LocalStartupMode.WorkspaceUninitialized,
                    status,
                    Failure: null,
                    workspaceState);
            }

            if (workspaceState
                == CoreLedgerSetupState.PartialOrConflicting)
            {
                return Blocked(
                    status,
                    Failure(
                        LocalDataFailureCategory.DatabaseNotReady,
                        "The core workspace is partial or conflicting."),
                    workspaceState);
            }

            if (workspaceState
                != CoreLedgerSetupState.Complete)
            {
                return Blocked(
                    status,
                    Failure(
                        LocalDataFailureCategory.DatabaseNotReady,
                        "The core workspace state is unsupported."),
                    workspaceState);
            }

            /*
             * M006 readiness deliberately does NOT use LocalProtectionReady:
             * the separation/encryption acknowledgement flags retain their M004
             * operator-attestation meaning but do not gate the local shell.
             */
            if (status.LatestVerifiedBackup is null)
            {
                return new LocalStartupSelection(
                    LocalStartupMode.InitialBackupRequired,
                    status,
                    Failure: null,
                    workspaceState);
            }

            /*
             * LatestVerifiedBackup is already filtered to lineage-matched
             * packages by the Infrastructure status reader. Retain this defensive
             * check so the Application boundary still fails closed if that
             * invariant changes accidentally.
             */
            if (status.LatestVerifiedBackup.WorkspaceBinding
                != LocalBackupWorkspaceBinding.Matched)
            {
                return Blocked(
                    status,
                    Failure(
                        LocalDataFailureCategory.InvalidBackup,
                        "The verified backup does not belong to the current workspace."),
                    workspaceState);
            }

            return new LocalStartupSelection(
                LocalStartupMode.Ready,
                status,
                Failure: null,
                workspaceState);
        }

        private static LocalStartupSelection Blocked(
            LocalDataStatus? status,
            LocalDataFailure failure,
            CoreLedgerSetupState? workspaceState = null)
            => new(
                LocalStartupMode.Blocked,
                status,
                failure,
                workspaceState);

        private static LocalDataFailure Failure(
            LocalDataFailureCategory category,
            string message)
            => new(
                category,
                message);
    }
}
