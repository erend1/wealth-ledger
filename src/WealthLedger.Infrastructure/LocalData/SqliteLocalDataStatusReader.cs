using WealthLedger.Application.LocalData;

namespace WealthLedger.Infrastructure.LocalData;

internal sealed class SqliteLocalDataStatusReader : ILocalDataStatusReader
{
    private const string EncryptionMode = "PLAINTEXT";
    private const int MaximumStatusBackupCount = 256;
    private readonly LocalDataPathResolver _pathResolver;
    private readonly LocalDatabaseOwnershipGuard _ownershipGuard;
    private readonly SqliteDatabaseVerifier _verifier;
    private readonly LocalBackupPackageReader _packageReader;

    internal SqliteLocalDataStatusReader(
        LocalDataPathResolver pathResolver,
        LocalDatabaseOwnershipGuard ownershipGuard,
        SqliteDatabaseVerifier verifier,
        LocalBackupPackageReader? packageReader = null)
    {
        _pathResolver = pathResolver
            ?? throw new ArgumentNullException(nameof(pathResolver));
        _ownershipGuard = ownershipGuard
            ?? throw new ArgumentNullException(nameof(ownershipGuard));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _packageReader = packageReader ?? new LocalBackupPackageReader(verifier);
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
        LocalBackupSummary? latestVerifiedBackup = null;

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

        if (backupDirectory is not null && Directory.Exists(backupDirectory))
        {
            var latestResult = await FindLatestVerifiedBackupAsync(
                backupDirectory,
                cancellationToken);

            if (!latestResult.Succeeded)
            {
                return LocalDataOperationResult<LocalDataStatus>.Failed(
                    latestResult.Failure!.Category,
                    latestResult.Failure.Message);
            }

            latestVerifiedBackup = latestResult.Value!.Summary;
        }

        var localProtectionReady = databaseExists
                                   && verification?.Compatibility
                                       == LocalDatabaseCompatibility.Compatible
                                   && backupDirectory is not null
                                   && Directory.Exists(backupDirectory)
                                   && latestVerifiedBackup is not null
                                   && _pathResolver
                                       .DestinationSeparationConfirmed
                                   && _pathResolver
                                       .DestinationEncryptionConfirmed;

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
            latestVerifiedBackup,
            _pathResolver.DestinationSeparationConfirmed,
            _pathResolver.DestinationEncryptionConfirmed,
            localProtectionReady,
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

    private async Task<LocalDataOperationResult<LatestBackupDiscovery>>
        FindLatestVerifiedBackupAsync(
            string backupDirectory,
            CancellationToken cancellationToken)
    {
        try
        {
            var candidates = Directory
                .EnumerateFiles(
                    backupDirectory,
                    "*.wlbackup",
                    SearchOption.TopDirectoryOnly)
                .Take(MaximumStatusBackupCount + 1)
                .ToArray();

            if (candidates.Length > MaximumStatusBackupCount)
            {
                return LocalDataOperationResult<LatestBackupDiscovery>.Failed(
                    LocalDataFailureCategory.InvalidInputOrConfiguration,
                    "The backup directory contains too many packages for bounded status inspection.");
            }

            LocalBackupSummary? latest = null;

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await _packageReader.OpenVerifiedAsync(
                    candidate,
                    cancellationToken);

                if (!result.Succeeded)
                {
                    if (result.Failure!.Category
                        == LocalDataFailureCategory.Cancelled)
                    {
                        return LocalDataResult<LatestBackupDiscovery>.FromFailure(
                            result.Failure);
                    }

                    continue;
                }

                await using var package = result.Value!;
                var manifest = package.Manifest;

                if (latest is null
                    || manifest.CreatedAtUtc > latest.CreatedAtUtc)
                {
                    latest = new LocalBackupSummary(
                        package.PackagePath,
                        manifest.CreatedAtUtc,
                        manifest.VerifiedAtUtc,
                        manifest.SnapshotSha256[..12],
                        manifest.LatestSchemaVersion,
                        manifest.EncryptionMode);
                }
            }

            return LocalDataOperationResult<LatestBackupDiscovery>.Success(
                new LatestBackupDiscovery(latest));
        }
        catch (OperationCanceledException)
        {
            return LocalDataOperationResult<LatestBackupDiscovery>.Failed(
                LocalDataFailureCategory.Cancelled,
                "Local data status inspection was cancelled.");
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or System.Security.SecurityException)
        {
            return LocalDataOperationResult<LatestBackupDiscovery>.Failed(
                LocalDataFailureCategory.IoFailure,
                "The configured backup directory could not be inspected.");
        }
    }

    private static string GetApplicationVersion()
        => typeof(SqliteLocalDataStatusReader).Assembly
               .GetName()
               .Version?
               .ToString()
           ?? "UNKNOWN";
}

internal sealed record LatestBackupDiscovery(LocalBackupSummary? Summary);
