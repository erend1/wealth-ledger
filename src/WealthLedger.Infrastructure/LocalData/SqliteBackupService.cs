using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using WealthLedger.Application.LocalData;

namespace WealthLedger.Infrastructure.LocalData;

internal enum LocalBackupPurpose
{
    Manual,
    PreMigration,
    PreRestore
}

internal sealed class SqliteBackupService
{
    private readonly LocalDataPathResolver _pathResolver;
    private readonly SqliteDatabaseVerifier _databaseVerifier;
    private readonly LocalBackupPackageReader _packageReader;
    private readonly TimeProvider _timeProvider;
    private readonly ILocalDataOperationHooks _hooks;

    internal SqliteBackupService(
        LocalDataPathResolver pathResolver,
        SqliteDatabaseVerifier databaseVerifier,
        LocalBackupPackageReader packageReader,
        TimeProvider timeProvider,
        ILocalDataOperationHooks? hooks = null)
    {
        _pathResolver = pathResolver
            ?? throw new ArgumentNullException(nameof(pathResolver));
        _databaseVerifier = databaseVerifier
            ?? throw new ArgumentNullException(nameof(databaseVerifier));
        _packageReader = packageReader
            ?? throw new ArgumentNullException(nameof(packageReader));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        _hooks = hooks ?? NoOpLocalDataOperationHooks.Instance;
    }

    internal async Task<LocalDataOperationResult<LocalBackupCreation>>
        CreateVerifiedBackupAsync(
            string databasePath,
            string backupDirectory,
            LocalBackupPurpose purpose,
            bool allowMigrationRequired,
            CancellationToken cancellationToken = default)
    {
        string? workingDirectory = null;
        string? snapshotPath = null;
        string? temporaryPackagePath = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceVerification = await _databaseVerifier.VerifyAsync(
                databasePath,
                cancellationToken);

            if (!sourceVerification.Succeeded)
            {
                return LocalDataResult<LocalBackupCreation>.FromFailure(
                    sourceVerification.Failure!);
            }

            if (sourceVerification.Value!.Compatibility
                == LocalDatabaseCompatibility.Incompatible
                || (!allowMigrationRequired
                    && sourceVerification.Value.Compatibility
                        != LocalDatabaseCompatibility.Compatible))
            {
                return LocalDataOperationResult<LocalBackupCreation>.Failed(
                    LocalDataFailureCategory.DatabaseNotReady,
                    "The database schema is not eligible for backup by this operation.");
            }

            Directory.CreateDirectory(backupDirectory);
            var repeatedPathCheck = _pathResolver.ResolveBackupDirectory(
                databasePath);

            if (!repeatedPathCheck.Succeeded
                || !_pathResolver.PathEquals(
                    repeatedPathCheck.Value!.FullPath,
                    backupDirectory))
            {
                return LocalDataOperationResult<LocalBackupCreation>.Failed(
                    LocalDataFailureCategory.UnsafePath,
                    "Backup destination safety changed before the operation began.");
            }

            workingDirectory = Path.Combine(
                backupDirectory,
                $"wealthledger-{Guid.NewGuid():N}.wlrestore");
            Directory.CreateDirectory(workingDirectory);
            snapshotPath = Path.Combine(
                workingDirectory,
                LocalBackupPackageReader.SnapshotEntryName);

            await using (var source =
                         SqliteLocalDataConnectionFactory.CreateConnection(
                             databasePath,
                             SqliteOpenMode.ReadOnly))
            await using (var destination =
                         SqliteLocalDataConnectionFactory.CreateConnection(
                             snapshotPath,
                             SqliteOpenMode.ReadWriteCreate))
            {
                await source.OpenAsync(cancellationToken);
                await destination.OpenAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                source.BackupDatabase(destination);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var snapshotVerification = await _databaseVerifier.VerifyAsync(
                snapshotPath,
                cancellationToken);

            if (!snapshotVerification.Succeeded)
            {
                return LocalDataResult<LocalBackupCreation>.FromFailure(
                    snapshotVerification.Failure!);
            }

            if (snapshotVerification.Value!.Compatibility
                == LocalDatabaseCompatibility.Incompatible
                || (!allowMigrationRequired
                    && snapshotVerification.Value.Compatibility
                        != LocalDatabaseCompatibility.Compatible))
            {
                return LocalDataOperationResult<LocalBackupCreation>.Failed(
                    LocalDataFailureCategory.DatabaseNotReady,
                    "The SQLite snapshot is not compatible with this application.");
            }

            var digest = await LocalBackupPackageReader.ComputeSha256Async(
                snapshotPath,
                cancellationToken);
            var createdAtUtc = _timeProvider.GetUtcNow();
            var verifiedAtUtc = _timeProvider.GetUtcNow();
            var manifest = new WealthLedgerBackupManifest
            {
                FormatVersion = WealthLedgerBackupManifest.CurrentFormatVersion,
                CreatedAtUtc = createdAtUtc,
                ApplicationVersion = GetApplicationVersion(),
                AppliedMigrations = snapshotVerification.Value
                    .AppliedMigrations
                    .ToArray(),
                LatestSchemaVersion = snapshotVerification.Value.LatestMigration!,
                SnapshotSha256 = digest,
                IntegrityCheckOutcome = WealthLedgerBackupManifest.PassedOutcome,
                CompatibilityCheckOutcome =
                    snapshotVerification.Value.Compatibility
                        == LocalDatabaseCompatibility.MigrationRequired
                            ? WealthLedgerBackupManifest.MigrationRequiredOutcome
                            : WealthLedgerBackupManifest.CompatibleOutcome,
                VerifiedAtUtc = verifiedAtUtc,
                VerificationStatus = WealthLedgerBackupManifest.VerifiedStatus,
                EncryptionMode =
                    WealthLedgerBackupManifest.PlaintextEncryptionMode,
                SourceWorkspaceId = snapshotVerification.Value.WorkspaceId
            };
            var finalPackagePath = CreatePackagePath(
                backupDirectory,
                purpose,
                createdAtUtc);
            temporaryPackagePath = finalPackagePath
                + $".{Guid.NewGuid():N}.wlrestore";

            await WritePackageAsync(
                temporaryPackagePath,
                snapshotPath,
                manifest,
                cancellationToken);

            var packageVerification = await _packageReader.OpenVerifiedAsync(
                temporaryPackagePath,
                cancellationToken);

            if (!packageVerification.Succeeded)
            {
                return LocalDataResult<LocalBackupCreation>.FromFailure(
                    packageVerification.Failure!);
            }

            var verifiedPackage = packageVerification.Value!;
            await using (verifiedPackage)
            {
                if (!string.Equals(
                        verifiedPackage.Manifest.SnapshotSha256,
                        digest,
                        StringComparison.Ordinal))
                {
                    return LocalDataOperationResult<LocalBackupCreation>.Failed(
                        LocalDataFailureCategory.InvalidBackup,
                        "Independent package verification produced a digest mismatch.");
                }
            }

            await _hooks.OnCheckpointAsync(
                LocalDataOperationCheckpoint.BeforeBackupPublish,
                temporaryPackagePath,
                finalPackagePath,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(
                temporaryPackagePath,
                finalPackagePath,
                overwrite: false);
            temporaryPackagePath = null;

            return LocalDataOperationResult<LocalBackupCreation>.Success(
                new LocalBackupCreation(
                    finalPackagePath,
                    createdAtUtc,
                    verifiedAtUtc,
                    digest[..12],
                    manifest.AppliedMigrations,
                    manifest.LatestSchemaVersion,
                    manifest.EncryptionMode));
        }
        catch (OperationCanceledException)
        {
            return LocalDataOperationResult<LocalBackupCreation>.Failed(
                LocalDataFailureCategory.Cancelled,
                "Backup creation was cancelled before publication.");
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or System.Security.SecurityException)
        {
            return LocalDataOperationResult<LocalBackupCreation>.Failed(
                LocalDataFailureCategory.IoFailure,
                "The backup could not be created or published.");
        }
        catch (SqliteException)
        {
            return LocalDataOperationResult<LocalBackupCreation>.Failed(
                LocalDataFailureCategory.IoFailure,
                "SQLite could not create a consistent backup snapshot.");
        }
        finally
        {
            if (temporaryPackagePath is not null)
            {
                TryDeleteFile(temporaryPackagePath);
            }

            if (snapshotPath is not null)
            {
                LocalDatabaseFiles.DeleteDatabaseArtifacts(snapshotPath);
            }

            if (workingDirectory is not null)
            {
                TryDeleteDirectory(workingDirectory);
            }
        }
    }

    private static async Task WritePackageAsync(
        string packagePath,
        string snapshotPath,
        WealthLedgerBackupManifest manifest,
        CancellationToken cancellationToken)
    {
        await using var packageStream = new FileStream(
            packagePath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        using var archive = new ZipArchive(
            packageStream,
            ZipArchiveMode.Create,
            leaveOpen: true);
        var snapshotEntry = archive.CreateEntry(
            LocalBackupPackageReader.SnapshotEntryName,
            CompressionLevel.NoCompression);
        snapshotEntry.LastWriteTime = manifest.CreatedAtUtc;

        await using (var snapshotInput = new FileStream(
                         snapshotPath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read,
                         bufferSize: 64 * 1024,
                         FileOptions.Asynchronous
                         | FileOptions.SequentialScan))
        await using (var snapshotOutput = snapshotEntry.Open())
        {
            await snapshotInput.CopyToAsync(
                snapshotOutput,
                cancellationToken);
        }

        var manifestEntry = archive.CreateEntry(
            LocalBackupPackageReader.ManifestEntryName,
            CompressionLevel.Optimal);
        manifestEntry.LastWriteTime = manifest.CreatedAtUtc;

        await using (var manifestOutput = manifestEntry.Open())
        {
            await JsonSerializer.SerializeAsync(
                manifestOutput,
                manifest,
                WealthLedgerBackupManifest.JsonOptions,
                cancellationToken);
        }
    }

    private static string CreatePackagePath(
        string backupDirectory,
        LocalBackupPurpose purpose,
        DateTimeOffset createdAtUtc)
    {
        var purposeCode = purpose switch
        {
            LocalBackupPurpose.Manual => "manual",
            LocalBackupPurpose.PreMigration => "pre-migration",
            LocalBackupPurpose.PreRestore => "pre-restore",
            _ => throw new ArgumentOutOfRangeException(nameof(purpose))
        };
        var timestamp = createdAtUtc.UtcDateTime.ToString(
            "yyyyMMdd'T'HHmmssfff'Z'",
            System.Globalization.CultureInfo.InvariantCulture);

        return Path.Combine(
            backupDirectory,
            $"wealthledger-{purposeCode}-{timestamp}-{Guid.NewGuid():N}.wlbackup");
    }

    private static string GetApplicationVersion()
        => typeof(SqliteBackupService).Assembly
               .GetName()
               .Version?
               .ToString()
           ?? "UNKNOWN";

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException)
        {
            // The primary operation result remains authoritative.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: false);
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException)
        {
            // The primary operation result remains authoritative.
        }
    }
}
