using Microsoft.Data.Sqlite;
using WealthLedger.Application.LocalData;

namespace WealthLedger.Infrastructure.LocalData;

internal sealed class SqliteLocalDatabaseReplacementSessionFactory
    : ILocalDatabaseReplacementSessionFactory
{
    private readonly LocalDataPathResolver _pathResolver;
    private readonly LocalDatabaseOwnershipGuard _ownershipGuard;
    private readonly SqliteRestoreService _restoreService;
    private readonly SqliteBackupService _backupService;
    private readonly LocalBackupPackageReader _packageReader;
    private readonly SqliteDatabaseVerifier _databaseVerifier;
    private readonly TimeProvider _timeProvider;
    private readonly ILocalDataOperationHooks _hooks;

    internal SqliteLocalDatabaseReplacementSessionFactory(
        LocalDataPathResolver pathResolver,
        LocalDatabaseOwnershipGuard ownershipGuard,
        SqliteRestoreService restoreService,
        SqliteBackupService backupService,
        LocalBackupPackageReader packageReader,
        SqliteDatabaseVerifier databaseVerifier,
        TimeProvider timeProvider,
        ILocalDataOperationHooks? hooks = null)
    {
        _pathResolver = pathResolver
            ?? throw new ArgumentNullException(nameof(pathResolver));
        _ownershipGuard = ownershipGuard
            ?? throw new ArgumentNullException(nameof(ownershipGuard));
        _restoreService = restoreService
            ?? throw new ArgumentNullException(nameof(restoreService));
        _backupService = backupService
            ?? throw new ArgumentNullException(nameof(backupService));
        _packageReader = packageReader
            ?? throw new ArgumentNullException(nameof(packageReader));
        _databaseVerifier = databaseVerifier
            ?? throw new ArgumentNullException(nameof(databaseVerifier));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        _hooks = hooks ?? NoOpLocalDataOperationHooks.Instance;
    }

    public Task<
        LocalDataOperationResult<ILocalDatabaseReplacementSession>> OpenAsync(
        string backupFilePath,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(
                LocalDataOperationResult<ILocalDatabaseReplacementSession>
                    .Failed(
                        LocalDataFailureCategory.Cancelled,
                        "Active database replacement was cancelled before ownership was acquired."));
        }

        var databaseResult = _pathResolver.ResolveDatabasePath();

        if (!databaseResult.Succeeded)
        {
            return Failure(databaseResult.Failure!);
        }

        var databasePath = databaseResult.Value!.FullPath;

        if (!File.Exists(databasePath))
        {
            return Task.FromResult(
                LocalDataOperationResult<ILocalDatabaseReplacementSession>
                    .Failed(
                        LocalDataFailureCategory.NotFound,
                        "The authoritative database does not exist."));
        }

        var backupDirectoryResult =
            _pathResolver.ResolveBackupDirectory(databasePath);

        if (!backupDirectoryResult.Succeeded)
        {
            return Failure(backupDirectoryResult.Failure!);
        }

        var packagePathResult =
            _pathResolver.ValidateBackupFilePath(backupFilePath);

        if (!packagePathResult.Succeeded)
        {
            return Failure(packagePathResult.Failure!);
        }

        var packagePath = packagePathResult.Value!.FullPath;

        if (_pathResolver.PathEquals(packagePath, databasePath))
        {
            return Task.FromResult(
                LocalDataOperationResult<ILocalDatabaseReplacementSession>
                    .Failed(
                        LocalDataFailureCategory.InvalidInputOrConfiguration,
                        "The replacement package cannot be the authoritative database."));
        }

        if (!File.Exists(packagePath))
        {
            return Task.FromResult(
                LocalDataOperationResult<ILocalDatabaseReplacementSession>
                    .Failed(
                        LocalDataFailureCategory.NotFound,
                        "The replacement backup package does not exist."));
        }

        var ownershipResult = _ownershipGuard.Acquire(
            databasePath,
            createDirectory: false);

        if (!ownershipResult.Succeeded)
        {
            return Failure(ownershipResult.Failure!);
        }

        ILocalDatabaseReplacementSession session =
            new SqliteLocalDatabaseReplacementSession(
                databasePath,
                backupDirectoryResult.Value!.FullPath,
                packagePath,
                ownershipResult.Value!,
                _restoreService,
                _backupService,
                _packageReader,
                _databaseVerifier,
                _timeProvider,
                _hooks);

        return Task.FromResult(
            LocalDataOperationResult<ILocalDatabaseReplacementSession>.Success(
                session));
    }

    private static Task<
        LocalDataOperationResult<ILocalDatabaseReplacementSession>> Failure(
        LocalDataFailure failure)
        => Task.FromResult(
            LocalDataOperationResult<ILocalDatabaseReplacementSession>.Failed(
                failure.Category,
                failure.Message));
}

internal sealed class SqliteLocalDatabaseReplacementSession
    : ILocalDatabaseReplacementSession
{
    private readonly string _databasePath;
    private readonly string _backupDirectory;
    private readonly string _packagePath;
    private readonly SqliteRestoreService _restoreService;
    private readonly SqliteBackupService _backupService;
    private readonly LocalBackupPackageReader _packageReader;
    private readonly SqliteDatabaseVerifier _databaseVerifier;
    private readonly TimeProvider _timeProvider;
    private readonly ILocalDataOperationHooks _hooks;
    private LocalDatabaseOwnershipLease? _ownership;
    private VerifiedRestoreDatabase? _candidate;
    private LocalBackupCreation? _preRestoreBackup;
    private string? _candidatePath;
    private bool _retainCandidate;
    private bool _promotionAttempted;

    internal SqliteLocalDatabaseReplacementSession(
        string databasePath,
        string backupDirectory,
        string packagePath,
        LocalDatabaseOwnershipLease ownership,
        SqliteRestoreService restoreService,
        SqliteBackupService backupService,
        LocalBackupPackageReader packageReader,
        SqliteDatabaseVerifier databaseVerifier,
        TimeProvider timeProvider,
        ILocalDataOperationHooks hooks)
    {
        _databasePath = databasePath;
        _backupDirectory = backupDirectory;
        _packagePath = packagePath;
        _ownership = ownership;
        _restoreService = restoreService;
        _backupService = backupService;
        _packageReader = packageReader;
        _databaseVerifier = databaseVerifier;
        _timeProvider = timeProvider;
        _hooks = hooks;
    }

    public async Task<LocalDataOperationResult<LocalRestoreStage>>
        StageCandidateAsync(CancellationToken cancellationToken = default)
    {
        if (_candidate is not null || _promotionAttempted)
        {
            return LocalDataOperationResult<LocalRestoreStage>.Failed(
                LocalDataFailureCategory.AlreadyExists,
                "A replacement candidate has already been staged in this session.");
        }

        var candidatePath = LocalDatabaseFiles.CreateUniqueSiblingPath(
            _databasePath,
            ".wlrestore");
        var result = await _restoreService.CreateVerifiedDatabaseAsync(
            _packagePath,
            candidatePath,
            requireCurrentSchema: false,
            cancellationToken);

        if (!result.Succeeded)
        {
            return LocalDataResult<LocalRestoreStage>.FromFailure(
                result.Failure!);
        }

        _candidate = result.Value;
        _candidatePath = candidatePath;

        return LocalDataOperationResult<LocalRestoreStage>.Success(
            result.Value!.Stage);
    }

    public async Task<LocalDataOperationResult<LocalBackupCreation>>
        CreateVerifiedPreRestoreBackupAsync(
            CancellationToken cancellationToken = default)
    {
        if (_candidate is null
            || _candidatePath is null
            || !File.Exists(_candidatePath))
        {
            return LocalDataOperationResult<LocalBackupCreation>.Failed(
                LocalDataFailureCategory.RestoreFailure,
                "A verified replacement candidate is required before the pre-restore backup.");
        }

        if (_preRestoreBackup is not null)
        {
            return LocalDataOperationResult<LocalBackupCreation>.Failed(
                LocalDataFailureCategory.AlreadyExists,
                "A pre-restore backup has already been created in this session.");
        }

        var result = await _backupService.CreateVerifiedBackupAsync(
            _databasePath,
            _backupDirectory,
            LocalBackupPurpose.PreRestore,
            allowMigrationRequired: true,
            cancellationToken);

        if (result.Succeeded)
        {
            _preRestoreBackup = result.Value;
        }

        return result;
    }

    public async Task<LocalDataOperationResult<LocalDatabaseReplacement>>
        PromoteAsync(
            LocalRestoreStage verifiedStage,
            LocalBackupCreation verifiedPreRestoreBackup,
            CancellationToken cancellationToken = default)
    {
        if (_promotionAttempted)
        {
            return LocalDataOperationResult<LocalDatabaseReplacement>.Failed(
                LocalDataFailureCategory.RestoreFailure,
                "This replacement session has already attempted promotion.");
        }

        _promotionAttempted = true;

        if (_candidate is null
            || _candidatePath is null
            || _preRestoreBackup is null
            || verifiedStage != _candidate.Stage
            || verifiedPreRestoreBackup != _preRestoreBackup)
        {
            return LocalDataOperationResult<LocalDatabaseReplacement>.Failed(
                LocalDataFailureCategory.InvalidInputOrConfiguration,
                "Promotion requires the verified stage and pre-restore backup from this session.");
        }

        var supersededPath = LocalDatabaseFiles.CreateUniqueSiblingPath(
            _databasePath,
            ".superseded.db");
        var liveMoved = false;
        var candidatePromoted = false;
        var swapStarted = false;
        SqliteDatabaseVerification? liveBefore = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidateVerification = await _databaseVerifier.VerifyAsync(
                _candidatePath,
                cancellationToken);

            if (!candidateVerification.Succeeded
                || !SqliteRestoreService.Equivalent(
                    _candidate.DatabaseVerification,
                    candidateVerification.Value!))
            {
                return LocalDataOperationResult<LocalDatabaseReplacement>.Failed(
                    LocalDataFailureCategory.RestoreFailure,
                    "The replacement candidate changed after staging.");
            }

            var backupVerification = await _packageReader.OpenVerifiedAsync(
                _preRestoreBackup.FilePath,
                cancellationToken);

            if (!backupVerification.Succeeded)
            {
                return LocalDataOperationResult<LocalDatabaseReplacement>.Failed(
                    LocalDataFailureCategory.RestoreFailure,
                    "The pre-restore backup no longer passes independent verification.");
            }

            await using (backupVerification.Value!)
            {
                if (!string.Equals(
                        backupVerification.Value!.Manifest.SnapshotSha256[..12],
                        _preRestoreBackup.DigestPrefix,
                        StringComparison.Ordinal))
                {
                    return LocalDataOperationResult<LocalDatabaseReplacement>
                        .Failed(
                            LocalDataFailureCategory.RestoreFailure,
                            "The pre-restore backup identity changed before promotion.");
                }
            }

            var livePreparation = await PrepareLiveDatabaseAsync(
                cancellationToken);

            if (!livePreparation.Succeeded)
            {
                return LocalDataResult<LocalDatabaseReplacement>.FromFailure(
                    livePreparation.Failure!);
            }

            liveBefore = livePreparation.Value!.Verification;

            await _hooks.OnCheckpointAsync(
                LocalDataOperationCheckpoint.BeforeRestorePromotion,
                _candidatePath,
                _databasePath,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (LocalDatabaseFiles.AnyCompanionArtifactExists(_databasePath)
                || LocalDatabaseFiles.AnyCompanionArtifactExists(
                    _candidatePath))
            {
                return LocalDataOperationResult<LocalDatabaseReplacement>.Failed(
                    LocalDataFailureCategory.OwnershipBusy,
                    "Database journal state changed before the replacement swap.");
            }

            swapStarted = true;
            File.Move(_databasePath, supersededPath, overwrite: false);
            liveMoved = true;
            File.Move(_candidatePath, _databasePath, overwrite: false);
            candidatePromoted = true;

            await _hooks.OnCheckpointAsync(
                LocalDataOperationCheckpoint.AfterRestorePromotion,
                _databasePath,
                supersededPath,
                CancellationToken.None);

            var promotedVerification = await _databaseVerifier.VerifyAsync(
                _databasePath,
                CancellationToken.None);

            if (!promotedVerification.Succeeded)
            {
                throw new RestorePromotionException(
                    "The promoted database failed independent verification.");
            }

            if (!SqliteRestoreService.Equivalent(
                    _candidate.DatabaseVerification,
                    promotedVerification.Value!))
            {
                throw new RestorePromotionException(
                    "The promoted database did not match the verified candidate.");
            }

            if (LocalDatabaseFiles.AnyCompanionArtifactExists(_databasePath))
            {
                throw new RestorePromotionException(
                    "The promoted database acquired unsafe journal companions.");
            }

            _candidatePath = null;

            return LocalDataOperationResult<LocalDatabaseReplacement>.Success(
                new LocalDatabaseReplacement(
                    _databasePath,
                    _preRestoreBackup.FilePath,
                    supersededPath,
                    promotedVerification.Value!.LatestMigration!,
                    _timeProvider.GetUtcNow()));
        }
        catch (OperationCanceledException) when (!swapStarted)
        {
            return LocalDataOperationResult<LocalDatabaseReplacement>.Failed(
                LocalDataFailureCategory.Cancelled,
                "Active database replacement was cancelled before the swap.");
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or System.Security.SecurityException
                  or SqliteException
                  or RestorePromotionException
                  or OperationCanceledException)
        {
            if (!swapStarted)
            {
                return LocalDataOperationResult<LocalDatabaseReplacement>.Failed(
                    LocalDataFailureCategory.IoFailure,
                    "Active database replacement could not begin the filesystem swap.");
            }

            _retainCandidate = true;
            var rolledBack = await RollBackAsync(
                supersededPath,
                liveMoved,
                candidatePromoted,
                liveBefore);

            var promotionDetail = exception is RestorePromotionException
                ? exception.Message + " "
                : string.Empty;

            return LocalDataOperationResult<LocalDatabaseReplacement>.Failed(
                LocalDataFailureCategory.RestoreFailure,
                rolledBack
                    ? promotionDetail
                      + "The previous database was restored and the failed candidate was retained."
                    : promotionDetail
                      + "The previous database could not be fully revalidated; recovery evidence was retained.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        var ownership = Interlocked.Exchange(ref _ownership, null);

        if (!_retainCandidate
            && _candidatePath is not null
            && _candidate is not null)
        {
            await DeleteOwnedCandidateAsync(
                _candidatePath,
                _candidate.SnapshotSha256);
        }

        if (ownership is not null)
        {
            await ownership.DisposeAsync();
        }
    }

    private static async Task DeleteOwnedCandidateAsync(
        string candidatePath,
        string expectedDigest)
    {
        try
        {
            if (!File.Exists(candidatePath))
            {
                return;
            }

            var currentDigest =
                await LocalBackupPackageReader.ComputeSha256Async(
                    candidatePath,
                    CancellationToken.None);

            if (string.Equals(
                    currentDigest,
                    expectedDigest,
                    StringComparison.OrdinalIgnoreCase))
            {
                LocalDatabaseFiles.DeleteDatabaseArtifacts(candidatePath);
            }
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or System.Security.SecurityException)
        {
            // A candidate whose identity cannot be proven is retained fail-closed.
        }
    }

    private async Task<LocalDataOperationResult<PreparedLiveDatabase>>
        PrepareLiveDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            var initialVerification = await _databaseVerifier.VerifyAsync(
                _databasePath,
                cancellationToken);

            if (!initialVerification.Succeeded
                || initialVerification.Value!.Compatibility
                    == LocalDatabaseCompatibility.Incompatible)
            {
                return LocalDataOperationResult<PreparedLiveDatabase>.Failed(
                    LocalDataFailureCategory.DatabaseNotReady,
                    "The authoritative database is not safe to replace.");
            }

            await using (var connection =
                         SqliteLocalDataConnectionFactory.CreateConnection(
                             _databasePath,
                             SqliteOpenMode.ReadWrite))
            {
                await connection.OpenAsync(cancellationToken);
                await using (var checkpoint = connection.CreateCommand())
                {
                    checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    await using var reader = await checkpoint.ExecuteReaderAsync(
                        cancellationToken);

                    if (!await reader.ReadAsync(cancellationToken)
                        || reader.GetInt64(0) != 0)
                    {
                        return LocalDataOperationResult<PreparedLiveDatabase>
                            .Failed(
                                LocalDataFailureCategory.OwnershipBusy,
                                "SQLite journal state is busy and cannot be prepared for replacement.");
                    }
                }

                await using var exclusive = connection.CreateCommand();
                exclusive.CommandText = "BEGIN EXCLUSIVE; COMMIT;";
                _ = await exclusive.ExecuteNonQueryAsync(cancellationToken);
            }

            LocalDatabaseFiles.DeleteCompanionArtifacts(_databasePath);

            if (LocalDatabaseFiles.AnyCompanionArtifactExists(_databasePath))
            {
                return LocalDataOperationResult<PreparedLiveDatabase>.Failed(
                    LocalDataFailureCategory.RestoreFailure,
                    "SQLite journal companions could not be safely separated from the live database.");
            }

            var preparedVerification = await _databaseVerifier.VerifyAsync(
                _databasePath,
                cancellationToken);

            if (!preparedVerification.Succeeded
                || !SqliteRestoreService.Equivalent(
                    initialVerification.Value,
                    preparedVerification.Value!))
            {
                return LocalDataOperationResult<PreparedLiveDatabase>.Failed(
                    LocalDataFailureCategory.RestoreFailure,
                    "The authoritative database changed while replacement was prepared.");
            }

            LocalDatabaseFiles.DeleteCompanionArtifacts(_databasePath);

            if (LocalDatabaseFiles.AnyCompanionArtifactExists(_databasePath))
            {
                return LocalDataOperationResult<PreparedLiveDatabase>.Failed(
                    LocalDataFailureCategory.RestoreFailure,
                    "SQLite journal companions could not be safely separated after verification.");
            }

            return LocalDataOperationResult<PreparedLiveDatabase>.Success(
                new PreparedLiveDatabase(preparedVerification.Value!));
        }
        catch (SqliteException exception)
            when (exception.SqliteErrorCode is 5 or 6)
        {
            return LocalDataOperationResult<PreparedLiveDatabase>.Failed(
                LocalDataFailureCategory.OwnershipBusy,
                "SQLite journal state is busy and cannot be prepared for replacement.");
        }
        catch (OperationCanceledException)
        {
            return LocalDataOperationResult<PreparedLiveDatabase>.Failed(
                LocalDataFailureCategory.Cancelled,
                "Active database replacement was cancelled before the swap.");
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or System.Security.SecurityException
                  or SqliteException)
        {
            return LocalDataOperationResult<PreparedLiveDatabase>.Failed(
                LocalDataFailureCategory.RestoreFailure,
                "SQLite journal state could not be prepared for replacement.");
        }
    }

    private async Task<bool> RollBackAsync(
        string supersededPath,
        bool liveMoved,
        bool candidatePromoted,
        SqliteDatabaseVerification? liveBefore)
    {
        try
        {
            LocalDatabaseFiles.DeleteCompanionArtifacts(_databasePath);

            if (candidatePromoted && File.Exists(_databasePath))
            {
                var failedCandidatePath = _candidatePath!;

                if (File.Exists(failedCandidatePath))
                {
                    failedCandidatePath =
                        LocalDatabaseFiles.CreateUniqueSiblingPath(
                            _databasePath,
                            ".wlrestore");
                }

                File.Move(
                    _databasePath,
                    failedCandidatePath,
                    overwrite: false);
                _candidatePath = failedCandidatePath;
            }

            if (liveMoved
                && File.Exists(supersededPath)
                && !File.Exists(_databasePath))
            {
                File.Move(
                    supersededPath,
                    _databasePath,
                    overwrite: false);
            }

            if (!File.Exists(_databasePath) || liveBefore is null)
            {
                return false;
            }

            var verification = await _databaseVerifier.VerifyAsync(
                _databasePath,
                CancellationToken.None);

            return verification.Succeeded
                   && SqliteRestoreService.Equivalent(
                       liveBefore,
                       verification.Value!);
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or System.Security.SecurityException
                  or SqliteException)
        {
            return false;
        }
    }

    private sealed class RestorePromotionException : Exception
    {
        internal RestorePromotionException(string message)
            : base(message)
        {
        }
    }
}

internal sealed record PreparedLiveDatabase(
    SqliteDatabaseVerification Verification);
