using WealthLedger.Application.LocalData;

namespace WealthLedger.Infrastructure.LocalData;

internal sealed class SqliteLocalDataStatusReader : ILocalDataStatusReader
{
    private const string EncryptionMode = "PLAINTEXT";
    private readonly LocalDataPathResolver _pathResolver;
    private readonly LocalDatabaseOwnershipGuard _ownershipGuard;
    private readonly SqliteDatabaseVerifier _verifier;

    internal SqliteLocalDataStatusReader(
        LocalDataPathResolver pathResolver,
        LocalDatabaseOwnershipGuard ownershipGuard,
        SqliteDatabaseVerifier verifier)
    {
        _pathResolver = pathResolver
            ?? throw new ArgumentNullException(nameof(pathResolver));
        _ownershipGuard = ownershipGuard
            ?? throw new ArgumentNullException(nameof(ownershipGuard));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
    }

    public async Task<LocalDataOperationResult<LocalDataStatus>> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        var databaseResult = _pathResolver.ResolveDatabasePath();

        if (!databaseResult.Succeeded)
        {
            return LocalDataResult<LocalDataStatus>.FromFailure(
                databaseResult.Failure!);
        }

        var databasePath = databaseResult.Value!.FullPath;
        var backupResult = _pathResolver.ResolveBackupDirectory(databasePath);
        var backupDirectory = backupResult.Succeeded
            ? backupResult.Value!.FullPath
            : null;
        var databaseExists = File.Exists(databasePath);
        var ownershipAvailable = _ownershipGuard.IsAvailable(databasePath);
        SqliteDatabaseVerification? verification = null;
        LocalDataFailure? databaseFailure = null;

        if (databaseExists)
        {
            var verificationResult = await _verifier.VerifyAsync(
                databasePath,
                cancellationToken);

            if (verificationResult.Succeeded)
            {
                verification = verificationResult.Value;
            }
            else
            {
                databaseFailure = verificationResult.Failure;
            }
        }

        var status = new LocalDataStatus(
            databasePath,
            backupDirectory,
            GetApplicationVersion(),
            DatabasePathSafe: true,
            databaseExists,
            BackupDirectoryConfigured: backupResult.Succeeded,
            BackupDirectoryExists: backupDirectory is not null
                                   && Directory.Exists(backupDirectory),
            ownershipAvailable,
            verification?.AppliedMigrations ?? [],
            verification?.PendingMigrations ?? [],
            verification?.Compatibility
                ?? LocalDatabaseCompatibility.Uninitialized,
            verification?.IntegrityStatus
                ?? LocalDataIntegrityStatus.NotChecked,
            LatestVerifiedBackup: null,
            _pathResolver.DestinationSeparationConfirmed,
            _pathResolver.DestinationEncryptionConfirmed,
            LocalProtectionReady: false,
            EncryptionMode);

        if (!databaseExists)
        {
            return LocalDataOperationResult<LocalDataStatus>.Failed(
                LocalDataFailureCategory.NotFound,
                "The database has not been initialized. Run database initialize.",
                status);
        }

        if (databaseFailure is not null)
        {
            return LocalDataOperationResult<LocalDataStatus>.Failed(
                databaseFailure.Category,
                databaseFailure.Message,
                status);
        }

        if (verification!.Compatibility
            == LocalDatabaseCompatibility.MigrationRequired)
        {
            return LocalDataOperationResult<LocalDataStatus>.Failed(
                LocalDataFailureCategory.DatabaseNotReady,
                "The database requires the explicit database migrate command.",
                status);
        }

        if (verification.Compatibility
            != LocalDatabaseCompatibility.Compatible)
        {
            return LocalDataOperationResult<LocalDataStatus>.Failed(
                LocalDataFailureCategory.DatabaseNotReady,
                "The database schema is incompatible with this application.",
                status);
        }

        if (!backupResult.Succeeded)
        {
            return LocalDataOperationResult<LocalDataStatus>.Failed(
                backupResult.Failure!.Category,
                backupResult.Failure.Message,
                status);
        }

        return LocalDataOperationResult<LocalDataStatus>.Success(status);
    }

    private static string GetApplicationVersion()
        => typeof(SqliteLocalDataStatusReader).Assembly
               .GetName()
               .Version?
               .ToString()
           ?? "UNKNOWN";
}
