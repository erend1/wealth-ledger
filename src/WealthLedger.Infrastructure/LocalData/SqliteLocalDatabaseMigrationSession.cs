using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.LocalData;

namespace WealthLedger.Infrastructure.LocalData;

internal sealed class SqliteLocalDatabaseMigrationSessionFactory
    : ILocalDatabaseMigrationSessionFactory
{
    private readonly LocalDataPathResolver _pathResolver;
    private readonly LocalDatabaseOwnershipGuard _ownershipGuard;
    private readonly SqliteBackupService _backupService;
    private readonly LocalBackupPackageReader _packageReader;
    private readonly SqliteDatabaseVerifier _databaseVerifier;
    private readonly TimeProvider _timeProvider;
    private readonly ILocalDataOperationHooks _hooks;

    internal SqliteLocalDatabaseMigrationSessionFactory(
        LocalDataPathResolver pathResolver,
        LocalDatabaseOwnershipGuard ownershipGuard,
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

    public Task<LocalDataOperationResult<ILocalDatabaseMigrationSession>>
        OpenAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(
                LocalDataOperationResult<ILocalDatabaseMigrationSession>
                    .Failed(
                        LocalDataFailureCategory.Cancelled,
                        "Database migration was cancelled before ownership was acquired."));
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
                LocalDataOperationResult<ILocalDatabaseMigrationSession>
                    .Failed(
                        LocalDataFailureCategory.NotFound,
                        "The database does not exist. Run database initialize first."));
        }

        var ownershipResult = _ownershipGuard.Acquire(
            databasePath,
            createDirectory: false);

        if (!ownershipResult.Succeeded)
        {
            return Failure(ownershipResult.Failure!);
        }

        ILocalDatabaseMigrationSession session =
            new SqliteLocalDatabaseMigrationSession(
                databasePath,
                ownershipResult.Value!,
                _pathResolver,
                _backupService,
                _packageReader,
                _databaseVerifier,
                _timeProvider,
                _hooks);

        return Task.FromResult(
            LocalDataOperationResult<ILocalDatabaseMigrationSession>.Success(
                session));
    }

    private static Task<
        LocalDataOperationResult<ILocalDatabaseMigrationSession>> Failure(
        LocalDataFailure failure)
        => Task.FromResult(
            LocalDataOperationResult<ILocalDatabaseMigrationSession>.Failed(
                failure.Category,
                failure.Message));
}

internal sealed class SqliteLocalDatabaseMigrationSession
    : ILocalDatabaseMigrationSession
{
    private readonly string _databasePath;
    private readonly LocalDataPathResolver _pathResolver;
    private readonly SqliteBackupService _backupService;
    private readonly LocalBackupPackageReader _packageReader;
    private readonly SqliteDatabaseVerifier _databaseVerifier;
    private readonly TimeProvider _timeProvider;
    private readonly ILocalDataOperationHooks _hooks;
    private LocalDatabaseOwnershipLease? _ownership;
    private LocalDatabaseMigrationPlan? _plan;
    private LocalBackupCreation? _preMigrationBackup;
    private bool _applyAttempted;

    internal SqliteLocalDatabaseMigrationSession(
        string databasePath,
        LocalDatabaseOwnershipLease ownership,
        LocalDataPathResolver pathResolver,
        SqliteBackupService backupService,
        LocalBackupPackageReader packageReader,
        SqliteDatabaseVerifier databaseVerifier,
        TimeProvider timeProvider,
        ILocalDataOperationHooks hooks)
    {
        _databasePath = databasePath;
        _ownership = ownership;
        _pathResolver = pathResolver;
        _backupService = backupService;
        _packageReader = packageReader;
        _databaseVerifier = databaseVerifier;
        _timeProvider = timeProvider;
        _hooks = hooks;
    }

    public async Task<LocalDataOperationResult<LocalDatabaseMigrationPlan>>
        InspectAsync(CancellationToken cancellationToken = default)
    {
        if (_plan is not null)
        {
            return LocalDataOperationResult<LocalDatabaseMigrationPlan>.Success(
                _plan);
        }

        var verificationResult = await _databaseVerifier.VerifyAsync(
            _databasePath,
            cancellationToken);

        if (!verificationResult.Succeeded)
        {
            return LocalDataResult<LocalDatabaseMigrationPlan>.FromFailure(
                verificationResult.Failure!);
        }

        var verification = verificationResult.Value!;

        if (verification.Compatibility
            == LocalDatabaseCompatibility.Incompatible)
        {
            return LocalDataOperationResult<LocalDatabaseMigrationPlan>.Failed(
                LocalDataFailureCategory.DatabaseNotReady,
                "The database migration history is not compatible with this application.");
        }

        _plan = new LocalDatabaseMigrationPlan(
            _databasePath,
            verification.AppliedMigrations,
            verification.PendingMigrations);

        return LocalDataOperationResult<LocalDatabaseMigrationPlan>.Success(
            _plan);
    }

    public async Task<LocalDataOperationResult<LocalBackupCreation>>
        CreateVerifiedPreMigrationBackupAsync(
            CancellationToken cancellationToken = default)
    {
        if (_plan is null || _plan.PendingMigrations.Count == 0)
        {
            return LocalDataOperationResult<LocalBackupCreation>.Failed(
                LocalDataFailureCategory.InvalidInputOrConfiguration,
                "A pending migration plan is required before creating a pre-migration backup.");
        }

        if (_preMigrationBackup is not null)
        {
            return LocalDataOperationResult<LocalBackupCreation>.Failed(
                LocalDataFailureCategory.AlreadyExists,
                "A pre-migration backup has already been created in this session.");
        }

        var backupDirectoryResult =
            _pathResolver.ResolveBackupDirectory(_databasePath);

        if (!backupDirectoryResult.Succeeded)
        {
            return LocalDataResult<LocalBackupCreation>.FromFailure(
                backupDirectoryResult.Failure!);
        }

        var result = await _backupService.CreateVerifiedBackupAsync(
            _databasePath,
            backupDirectoryResult.Value!.FullPath,
            LocalBackupPurpose.PreMigration,
            allowMigrationRequired: true,
            cancellationToken);

        if (result.Succeeded)
        {
            _preMigrationBackup = result.Value;
        }

        return result;
    }

    public async Task<LocalDataOperationResult<LocalDatabaseMigration>> ApplyAsync(
        LocalDatabaseMigrationPlan plan,
        LocalBackupCreation verifiedPreMigrationBackup,
        CancellationToken cancellationToken = default)
    {
        if (_applyAttempted)
        {
            return LocalDataOperationResult<LocalDatabaseMigration>.Failed(
                LocalDataFailureCategory.MigrationFailure,
                "This migration session has already attempted schema changes.");
        }

        _applyAttempted = true;

        if (_plan is null
            || _preMigrationBackup is null
            || plan != _plan
            || verifiedPreMigrationBackup != _preMigrationBackup
            || plan.PendingMigrations.Count == 0)
        {
            return LocalDataOperationResult<LocalDatabaseMigration>.Failed(
                LocalDataFailureCategory.InvalidInputOrConfiguration,
                "Migration requires the inspected plan and verified backup from this session.");
        }

        var migrationStarted = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var backupVerification = await _packageReader.OpenVerifiedAsync(
                _preMigrationBackup.FilePath,
                cancellationToken);

            if (!backupVerification.Succeeded)
            {
                return await FailureWithEvidenceAsync(
                    LocalDataFailureCategory.MigrationFailure,
                    "The pre-migration backup no longer passes independent verification.");
            }

            var verifiedPackage = backupVerification.Value!;
            SqliteDatabaseVerification backupDatabaseVerification;
            await using (verifiedPackage)
            {
                if (!string.Equals(
                        verifiedPackage.Manifest.SnapshotSha256[..12],
                        _preMigrationBackup.DigestPrefix,
                        StringComparison.Ordinal))
                {
                    return await FailureWithEvidenceAsync(
                        LocalDataFailureCategory.MigrationFailure,
                        "The pre-migration backup identity changed before migration.");
                }

                backupDatabaseVerification =
                    verifiedPackage.DatabaseVerification;
            }

            var repeatedInspection = await _databaseVerifier.VerifyAsync(
                _databasePath,
                cancellationToken);

            if (!repeatedInspection.Succeeded
                || !plan.AppliedMigrations.SequenceEqual(
                    repeatedInspection.Value!.AppliedMigrations,
                    StringComparer.Ordinal)
                || !plan.PendingMigrations.SequenceEqual(
                    repeatedInspection.Value.PendingMigrations,
                    StringComparer.Ordinal)
                || !SqliteRestoreService.Equivalent(
                    backupDatabaseVerification,
                    repeatedInspection.Value))
            {
                return await FailureWithEvidenceAsync(
                    LocalDataFailureCategory.MigrationFailure,
                    "The database migration state changed after backup creation.");
            }

            await _hooks.OnCheckpointAsync(
                LocalDataOperationCheckpoint.BeforeMigrationApply,
                _databasePath,
                _preMigrationBackup.FilePath,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            migrationStarted = true;

            await using (var context =
                         SqliteLocalDataConnectionFactory.CreateContext(
                             _databasePath,
                             SqliteOpenMode.ReadWrite))
            {
                await context.Database.MigrateAsync(cancellationToken);
            }

            await _hooks.OnCheckpointAsync(
                LocalDataOperationCheckpoint.AfterMigrationApply,
                _databasePath,
                _preMigrationBackup.FilePath,
                CancellationToken.None);

            var postMigrationVerification =
                await _databaseVerifier.VerifyAsync(
                    _databasePath,
                    CancellationToken.None);

            if (!postMigrationVerification.Succeeded
                || postMigrationVerification.Value!.Compatibility
                    != LocalDatabaseCompatibility.Compatible
                || postMigrationVerification.Value.PendingMigrations.Count != 0)
            {
                return await FailureWithEvidenceAsync(
                    LocalDataFailureCategory.MigrationFailure,
                    "Migration completed without a valid compatible post-migration state.");
            }

            return LocalDataOperationResult<LocalDatabaseMigration>.Success(
                new LocalDatabaseMigration(
                    _databasePath,
                    plan.AppliedMigrations.LastOrDefault(),
                    postMigrationVerification.Value.LatestMigration,
                    _preMigrationBackup.FilePath,
                    WasNoOp: false,
                    _timeProvider.GetUtcNow()));
        }
        catch (OperationCanceledException) when (!migrationStarted)
        {
            return await FailureWithEvidenceAsync(
                LocalDataFailureCategory.Cancelled,
                "Database migration was cancelled before schema changes began.");
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or System.Security.SecurityException
                  or SqliteException
                  or InvalidOperationException
                  or OperationCanceledException)
        {
            return await FailureWithEvidenceAsync(
                LocalDataFailureCategory.MigrationFailure,
                "Database migration did not complete successfully; use the verified pre-migration backup for staged recovery.");
        }
    }

    public ValueTask DisposeAsync()
    {
        var ownership = Interlocked.Exchange(ref _ownership, null);
        return ownership is null
            ? ValueTask.CompletedTask
            : ownership.DisposeAsync();
    }

    private async Task<LocalDataOperationResult<LocalDatabaseMigration>>
        FailureWithEvidenceAsync(
            LocalDataFailureCategory category,
            string message)
    {
        string? endingMigration = _plan?.AppliedMigrations.LastOrDefault();
        var verification = await _databaseVerifier.VerifyAsync(
            _databasePath,
            CancellationToken.None);

        if (verification.Succeeded)
        {
            endingMigration = verification.Value!.LatestMigration;
        }

        var evidence = new LocalDatabaseMigration(
            _databasePath,
            _plan?.AppliedMigrations.LastOrDefault(),
            endingMigration,
            _preMigrationBackup?.FilePath,
            WasNoOp: false,
            _timeProvider.GetUtcNow());

        return LocalDataOperationResult<LocalDatabaseMigration>.Failed(
            category,
            message,
            evidence);
    }
}
