using Microsoft.Data.Sqlite;
using WealthLedger.Application.LocalData;

namespace WealthLedger.Infrastructure.LocalData;

internal sealed class SqliteRestoreService
{
    private readonly LocalBackupPackageReader _packageReader;
    private readonly SqliteDatabaseVerifier _databaseVerifier;
    private readonly TimeProvider _timeProvider;
    private readonly ILocalDataOperationHooks _hooks;

    internal SqliteRestoreService(
        LocalBackupPackageReader packageReader,
        SqliteDatabaseVerifier databaseVerifier,
        TimeProvider timeProvider,
        ILocalDataOperationHooks? hooks = null)
    {
        _packageReader = packageReader
            ?? throw new ArgumentNullException(nameof(packageReader));
        _databaseVerifier = databaseVerifier
            ?? throw new ArgumentNullException(nameof(databaseVerifier));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        _hooks = hooks ?? NoOpLocalDataOperationHooks.Instance;
    }

    internal async Task<LocalDataOperationResult<VerifiedRestoreDatabase>>
        CreateVerifiedDatabaseAsync(
            string backupFilePath,
            string targetDatabasePath,
            bool requireCurrentSchema,
            CancellationToken cancellationToken = default)
    {
        string? stagingPath = null;
        string? publishedDigest = null;
        var publishedTarget = false;
        var completed = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (LocalDatabaseFiles.AnyDatabaseArtifactExists(
                    targetDatabasePath))
            {
                return LocalDataOperationResult<VerifiedRestoreDatabase>.Failed(
                    LocalDataFailureCategory.AlreadyExists,
                    "The restore target already contains database artifacts.");
            }

            var packageResult = await _packageReader.OpenVerifiedAsync(
                backupFilePath,
                cancellationToken);

            if (!packageResult.Succeeded)
            {
                return LocalDataResult<VerifiedRestoreDatabase>.FromFailure(
                    packageResult.Failure!);
            }

            await using var package = packageResult.Value!;

            if (requireCurrentSchema
                && package.DatabaseVerification.Compatibility
                    != LocalDatabaseCompatibility.Compatible)
            {
                return LocalDataOperationResult<VerifiedRestoreDatabase>.Failed(
                    LocalDataFailureCategory.IncompatibleBackup,
                    "Active replacement requires a backup at the current schema version.");
            }

            stagingPath = LocalDatabaseFiles.CreateUniqueSiblingPath(
                targetDatabasePath,
                ".wlrestore");
            await CopySnapshotAsync(
                package.SnapshotPath,
                stagingPath,
                cancellationToken);

            var stagedDigest = await LocalBackupPackageReader.ComputeSha256Async(
                stagingPath,
                cancellationToken);

            if (!string.Equals(
                    stagedDigest,
                    package.Manifest.SnapshotSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return LocalDataOperationResult<VerifiedRestoreDatabase>.Failed(
                    LocalDataFailureCategory.IntegrityFailure,
                    "The staged restore digest changed during extraction.");
            }

            var stagedVerification = await _databaseVerifier.VerifyAsync(
                stagingPath,
                cancellationToken);

            if (!stagedVerification.Succeeded)
            {
                return LocalDataResult<VerifiedRestoreDatabase>.FromFailure(
                    stagedVerification.Failure!);
            }

            if (!Equivalent(
                    package.DatabaseVerification,
                    stagedVerification.Value!))
            {
                return LocalDataOperationResult<VerifiedRestoreDatabase>.Failed(
                    LocalDataFailureCategory.RestoreFailure,
                    "The staged restore does not match the verified backup snapshot.");
            }

            var normalizationResult = await NormalizeStandaloneAsync(
                stagingPath,
                cancellationToken);

            if (!normalizationResult.Succeeded)
            {
                return LocalDataResult<VerifiedRestoreDatabase>.FromFailure(
                    normalizationResult.Failure!);
            }

            if (!Equivalent(
                    package.DatabaseVerification,
                    normalizationResult.Value!.Verification))
            {
                return LocalDataOperationResult<VerifiedRestoreDatabase>.Failed(
                    LocalDataFailureCategory.RestoreFailure,
                    "The standalone restore changed during journal normalization.");
            }

            publishedDigest =
                await LocalBackupPackageReader.ComputeSha256Async(
                    stagingPath,
                    cancellationToken);

            await _hooks.OnCheckpointAsync(
                LocalDataOperationCheckpoint.BeforeRestoreStagePublish,
                stagingPath,
                targetDatabasePath,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (LocalDatabaseFiles.AnyDatabaseArtifactExists(
                    targetDatabasePath))
            {
                return LocalDataOperationResult<VerifiedRestoreDatabase>.Failed(
                    LocalDataFailureCategory.AlreadyExists,
                    "The restore target changed before publication.");
            }

            File.Move(stagingPath, targetDatabasePath, overwrite: false);
            stagingPath = null;
            publishedTarget = true;

            if (LocalDatabaseFiles.AnyCompanionArtifactExists(
                    targetDatabasePath))
            {
                return LocalDataOperationResult<VerifiedRestoreDatabase>.Failed(
                    LocalDataFailureCategory.RestoreFailure,
                    "The restore target acquired unsafe journal companions.");
            }

            var restartedVerification = await _databaseVerifier.VerifyAsync(
                targetDatabasePath,
                CancellationToken.None);

            if (!restartedVerification.Succeeded
                || !Equivalent(
                    package.DatabaseVerification,
                    restartedVerification.Value!))
            {
                return LocalDataOperationResult<VerifiedRestoreDatabase>.Failed(
                    LocalDataFailureCategory.RestoreFailure,
                    "The restored database failed restart verification.");
            }

            await _hooks.OnCheckpointAsync(
                LocalDataOperationCheckpoint.AfterRestoreStagePublish,
                targetDatabasePath,
                backupFilePath,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var stage = new LocalRestoreStage(
                Path.GetFullPath(backupFilePath),
                Path.GetFullPath(targetDatabasePath),
                restartedVerification.Value!.Compatibility,
                restartedVerification.Value.LatestMigration!,
                _timeProvider.GetUtcNow());
            completed = true;

            return LocalDataOperationResult<VerifiedRestoreDatabase>.Success(
                new VerifiedRestoreDatabase(
                    stage,
                    restartedVerification.Value,
                    publishedDigest));
        }
        catch (OperationCanceledException)
        {
            return LocalDataOperationResult<VerifiedRestoreDatabase>.Failed(
                LocalDataFailureCategory.Cancelled,
                "Restore staging was cancelled before completion.");
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or System.Security.SecurityException)
        {
            return LocalDataOperationResult<VerifiedRestoreDatabase>.Failed(
                LocalDataFailureCategory.IoFailure,
                "Restore staging could not complete its filesystem operation.");
        }
        finally
        {
            if (stagingPath is not null)
            {
                LocalDatabaseFiles.DeleteDatabaseArtifacts(stagingPath);
            }

            if (publishedTarget && !completed)
            {
                await DeleteOwnedPublishedTargetAsync(
                    targetDatabasePath,
                    publishedDigest);
            }
        }
    }

    private static async Task CopySnapshotAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await source.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    private async Task<LocalDataOperationResult<NormalizedRestoreDatabase>>
        NormalizeStandaloneAsync(
            string databasePath,
            CancellationToken cancellationToken)
    {
        try
        {
            await using (var connection =
                         SqliteLocalDataConnectionFactory.CreateConnection(
                             databasePath,
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
                        return LocalDataOperationResult<
                            NormalizedRestoreDatabase>.Failed(
                                LocalDataFailureCategory.RestoreFailure,
                                "The staged restore journal could not be checkpointed.");
                    }
                }

                await using var journalMode = connection.CreateCommand();
                journalMode.CommandText = "PRAGMA journal_mode = DELETE;";
                var selectedMode = await journalMode.ExecuteScalarAsync(
                    cancellationToken);

                if (!string.Equals(
                        Convert.ToString(
                            selectedMode,
                            System.Globalization.CultureInfo.InvariantCulture),
                        "delete",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return LocalDataOperationResult<NormalizedRestoreDatabase>
                        .Failed(
                            LocalDataFailureCategory.RestoreFailure,
                            "The staged restore could not enter standalone journal mode.");
                }

                await using var exclusive = connection.CreateCommand();
                exclusive.CommandText = "BEGIN EXCLUSIVE; COMMIT;";
                _ = await exclusive.ExecuteNonQueryAsync(cancellationToken);
            }

            LocalDatabaseFiles.DeleteCompanionArtifacts(databasePath);

            if (LocalDatabaseFiles.AnyCompanionArtifactExists(databasePath))
            {
                return LocalDataOperationResult<NormalizedRestoreDatabase>.Failed(
                    LocalDataFailureCategory.RestoreFailure,
                    "The staged restore retained unsafe journal companions.");
            }

            var verification = await _databaseVerifier.VerifyAsync(
                databasePath,
                cancellationToken);

            if (!verification.Succeeded)
            {
                return LocalDataResult<NormalizedRestoreDatabase>.FromFailure(
                    verification.Failure!);
            }

            return LocalDataOperationResult<NormalizedRestoreDatabase>.Success(
                new NormalizedRestoreDatabase(verification.Value!));
        }
        catch (OperationCanceledException)
        {
            return LocalDataOperationResult<NormalizedRestoreDatabase>.Failed(
                LocalDataFailureCategory.Cancelled,
                "Restore staging was cancelled during journal normalization.");
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or System.Security.SecurityException
                  or SqliteException)
        {
            return LocalDataOperationResult<NormalizedRestoreDatabase>.Failed(
                LocalDataFailureCategory.RestoreFailure,
                "The staged restore journal could not be normalized safely.");
        }
    }

    private static async Task DeleteOwnedPublishedTargetAsync(
        string targetDatabasePath,
        string? expectedDigest)
    {
        if (!File.Exists(targetDatabasePath)
            || string.IsNullOrWhiteSpace(expectedDigest))
        {
            return;
        }

        try
        {
            var currentDigest =
                await LocalBackupPackageReader.ComputeSha256Async(
                    targetDatabasePath,
                    CancellationToken.None);

            if (string.Equals(
                    currentDigest,
                    expectedDigest,
                    StringComparison.OrdinalIgnoreCase))
            {
                LocalDatabaseFiles.DeleteMainDatabaseFile(targetDatabasePath);
            }
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or System.Security.SecurityException)
        {
            // A target whose identity cannot be proven is retained fail-closed.
        }
    }

    internal static bool Equivalent(
        SqliteDatabaseVerification expected,
        SqliteDatabaseVerification actual)
        => expected.Compatibility == actual.Compatibility
           && expected.IntegrityStatus == actual.IntegrityStatus
           && expected.AppliedMigrations.SequenceEqual(
               actual.AppliedMigrations,
               StringComparer.Ordinal)
           && expected.PendingMigrations.SequenceEqual(
               actual.PendingMigrations,
               StringComparer.Ordinal)
           && string.Equals(
               expected.RepresentativeFingerprint,
               actual.RepresentativeFingerprint,
               StringComparison.Ordinal);
}

internal sealed record VerifiedRestoreDatabase(
    LocalRestoreStage Stage,
    SqliteDatabaseVerification DatabaseVerification,
    string SnapshotSha256);

internal sealed record NormalizedRestoreDatabase(
    SqliteDatabaseVerification Verification);
