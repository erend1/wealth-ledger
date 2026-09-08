using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using WealthLedger.Application.LocalData;

namespace WealthLedger.Infrastructure.LocalData;

internal sealed class LocalBackupPackageReader
{
    internal const string SnapshotEntryName = "database.sqlite";
    internal const string ManifestEntryName = "manifest.json";

    private readonly SqliteDatabaseVerifier _databaseVerifier;
    private readonly BackupPackageLimits _limits;

    internal LocalBackupPackageReader(
        SqliteDatabaseVerifier databaseVerifier,
        BackupPackageLimits? limits = null)
    {
        _databaseVerifier = databaseVerifier
            ?? throw new ArgumentNullException(nameof(databaseVerifier));
        _limits = limits ?? BackupPackageLimits.Default;
    }

    internal async Task<LocalDataOperationResult<VerifiedBackupPackage>>
        OpenVerifiedAsync(
            string backupFilePath,
            CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupFilePath))
        {
            return LocalDataOperationResult<VerifiedBackupPackage>.Failed(
                LocalDataFailureCategory.NotFound,
                "The backup package does not exist.");
        }

        string? temporaryDirectory = null;
        string? snapshotPath = null;
        var retained = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var packageInfo = new FileInfo(backupFilePath);

            if (packageInfo.Length <= 0
                || packageInfo.Length > _limits.MaxPackageBytes)
            {
                return InvalidPackage(
                    "The backup package size is outside supported limits.");
            }

            await using var packageStream = new FileStream(
                backupFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var archive = new ZipArchive(
                packageStream,
                ZipArchiveMode.Read,
                leaveOpen: false);

            if (archive.Entries.Count != 2
                || archive.Entries.Count > _limits.MaxEntryCount)
            {
                return InvalidPackage(
                    "A backup package must contain exactly two entries.");
            }

            var entries = new Dictionary<string, ZipArchiveEntry>(
                StringComparer.Ordinal);

            foreach (var entry in archive.Entries)
            {
                if (!IsSafeRequiredEntry(entry)
                    || !entries.TryAdd(entry.FullName, entry))
                {
                    return InvalidPackage(
                        "The backup package contains an unsafe, unknown, or duplicate entry.");
                }
            }

            if (!entries.TryGetValue(ManifestEntryName, out var manifestEntry)
                || !entries.TryGetValue(SnapshotEntryName, out var snapshotEntry))
            {
                return InvalidPackage(
                    "The backup package is missing a required entry.");
            }

            if (!EntryLengthIsAllowed(
                    manifestEntry,
                    _limits.MaxManifestBytes)
                || !EntryLengthIsAllowed(
                    snapshotEntry,
                    _limits.MaxSnapshotBytes))
            {
                return InvalidPackage(
                    "A backup package entry exceeds supported limits.");
            }

            var manifestBytes = await ReadBoundedEntryAsync(
                manifestEntry,
                _limits.MaxManifestBytes,
                cancellationToken);
            var manifest = JsonSerializer.Deserialize<WealthLedgerBackupManifest>(
                manifestBytes,
                WealthLedgerBackupManifest.JsonOptions);

            if (manifest is null)
            {
                return InvalidPackage(
                    "The backup manifest is missing or invalid.");
            }

            var manifestFailure = ValidateManifest(manifest);

            if (manifestFailure is not null)
            {
                return LocalDataOperationResult<VerifiedBackupPackage>.Failed(
                    manifestFailure.Category,
                    manifestFailure.Message);
            }

            temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "WealthLedger",
                "backup-verification",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);
            snapshotPath = Path.Combine(
                temporaryDirectory,
                SnapshotEntryName);

            await ExtractBoundedEntryAsync(
                snapshotEntry,
                snapshotPath,
                _limits.MaxSnapshotBytes,
                cancellationToken);

            var digest = await ComputeSha256Async(
                snapshotPath,
                cancellationToken);

            if (!string.Equals(
                    digest,
                    manifest.SnapshotSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return InvalidPackage(
                    "The backup snapshot digest does not match its manifest.");
            }

            var databaseResult = await _databaseVerifier.VerifyAsync(
                snapshotPath,
                cancellationToken);

            if (!databaseResult.Succeeded)
            {
                return DatabaseFailure(databaseResult.Failure!);
            }

            var databaseVerification = databaseResult.Value!;

            if (databaseVerification.Compatibility
                == LocalDatabaseCompatibility.Incompatible)
            {
                return LocalDataOperationResult<VerifiedBackupPackage>.Failed(
                    LocalDataFailureCategory.IncompatibleBackup,
                    "The backup uses an unsupported future or unknown schema.");
            }

            if (!databaseVerification.AppliedMigrations.SequenceEqual(
                    manifest.AppliedMigrations,
                    StringComparer.Ordinal)
                || !string.Equals(
                    databaseVerification.LatestMigration,
                    manifest.LatestSchemaVersion,
                    StringComparison.Ordinal))
            {
                return InvalidPackage(
                    "The backup manifest does not match the snapshot migration history.");
            }

            if (!string.Equals(
                    manifest.SourceWorkspaceId,
                    databaseVerification.WorkspaceId,
                    StringComparison.Ordinal))
            {
                return InvalidPackage(
                    "The backup manifest does not match the snapshot workspace identity.");
            }

            var expectedCompatibilityOutcome =
                databaseVerification.Compatibility
                    == LocalDatabaseCompatibility.MigrationRequired
                        ? WealthLedgerBackupManifest.MigrationRequiredOutcome
                        : WealthLedgerBackupManifest.CompatibleOutcome;

            if (!string.Equals(
                    manifest.CompatibilityCheckOutcome,
                    expectedCompatibilityOutcome,
                    StringComparison.Ordinal))
            {
                return InvalidPackage(
                    "The backup manifest does not match the snapshot compatibility result.");
            }

            retained = true;

            return LocalDataOperationResult<VerifiedBackupPackage>.Success(
                new VerifiedBackupPackage(
                    Path.GetFullPath(backupFilePath),
                    snapshotPath,
                    temporaryDirectory,
                    manifest,
                    databaseVerification));
        }
        catch (OperationCanceledException)
        {
            return LocalDataOperationResult<VerifiedBackupPackage>.Failed(
                LocalDataFailureCategory.Cancelled,
                "Backup verification was cancelled.");
        }
        catch (Exception exception)
            when (exception is InvalidDataException
                  or JsonException
                  or FormatException)
        {
            return InvalidPackage(
                "The backup package is corrupt or malformed.");
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or System.Security.SecurityException)
        {
            return LocalDataOperationResult<VerifiedBackupPackage>.Failed(
                LocalDataFailureCategory.IoFailure,
                "The backup package could not be read.");
        }
        finally
        {
            if (!retained)
            {
                DeleteTemporarySnapshot(snapshotPath, temporaryDirectory);
            }
        }
    }

    private LocalDataFailure? ValidateManifest(
        WealthLedgerBackupManifest manifest)
    {
        if (manifest.FormatVersion
            != WealthLedgerBackupManifest.CurrentFormatVersion)
        {
            return new LocalDataFailure(
                LocalDataFailureCategory.IncompatibleBackup,
                "The backup format version is not supported.");
        }

        if (manifest.CreatedAtUtc == default
            || manifest.VerifiedAtUtc == default
            || manifest.CreatedAtUtc.Offset != TimeSpan.Zero
            || manifest.VerifiedAtUtc.Offset != TimeSpan.Zero
            || manifest.VerifiedAtUtc < manifest.CreatedAtUtc)
        {
            return InvalidFailure(
                "The backup manifest contains invalid UTC timestamps.");
        }

        if (string.IsNullOrWhiteSpace(manifest.ApplicationVersion)
            || manifest.ApplicationVersion.Length > 128
            || manifest.AppliedMigrations is null
            || manifest.AppliedMigrations.Length == 0
            || manifest.AppliedMigrations.Length > _limits.MaxMigrationCount
            || manifest.AppliedMigrations.Any(
                migration => string.IsNullOrWhiteSpace(migration)
                             || migration.Length > 256)
            || !manifest.AppliedMigrations.SequenceEqual(
                manifest.AppliedMigrations.OrderBy(
                    migration => migration,
                    StringComparer.Ordinal),
                StringComparer.Ordinal)
            || manifest.AppliedMigrations.Distinct(
                    StringComparer.Ordinal).Count()
                != manifest.AppliedMigrations.Length
            || string.IsNullOrWhiteSpace(manifest.LatestSchemaVersion)
            || !string.Equals(
                manifest.LatestSchemaVersion,
                manifest.AppliedMigrations[^1],
                StringComparison.Ordinal))
        {
            return InvalidFailure(
                "The backup manifest contains invalid migration metadata.");
        }

        if (string.IsNullOrWhiteSpace(manifest.SnapshotSha256)
            || manifest.SnapshotSha256.Length != 64
            || manifest.SnapshotSha256.Any(character => !Uri.IsHexDigit(character)))
        {
            return InvalidFailure(
                "The backup manifest contains an invalid snapshot digest.");
        }

        if (manifest.SourceWorkspaceId is not null
            && !SqliteDatabaseVerifier.IsWellFormedWorkspaceId(
                manifest.SourceWorkspaceId))
        {
            return InvalidFailure(
                "The backup manifest contains an invalid workspace identity.");
        }

        if (!string.Equals(
                manifest.IntegrityCheckOutcome,
                WealthLedgerBackupManifest.PassedOutcome,
                StringComparison.Ordinal)
            || (manifest.CompatibilityCheckOutcome
                    is not WealthLedgerBackupManifest.CompatibleOutcome
                    and not WealthLedgerBackupManifest.MigrationRequiredOutcome)
            || !string.Equals(
                manifest.VerificationStatus,
                WealthLedgerBackupManifest.VerifiedStatus,
                StringComparison.Ordinal)
            || !string.Equals(
                manifest.EncryptionMode,
                WealthLedgerBackupManifest.PlaintextEncryptionMode,
                StringComparison.Ordinal))
        {
            return InvalidFailure(
                "The backup manifest does not record completed compatible plaintext verification.");
        }

        return null;
    }

    private bool EntryLengthIsAllowed(
        ZipArchiveEntry entry,
        long maximumLength)
    {
        if (entry.Length <= 0 || entry.Length > maximumLength)
        {
            return false;
        }

        return entry.CompressedLength > 0
               && entry.Length / (double)entry.CompressedLength
               <= _limits.MaxCompressionRatio;
    }

    private static bool IsSafeRequiredEntry(ZipArchiveEntry entry)
    {
        if (entry.FullName != entry.Name
            || (entry.FullName != SnapshotEntryName
                && entry.FullName != ManifestEntryName))
        {
            return false;
        }

        var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
        var windowsAttributes =
            (FileAttributes)(entry.ExternalAttributes & 0xFFFF);

        return (unixFileType == 0 || unixFileType == 0x8000)
               && (windowsAttributes & FileAttributes.ReparsePoint) == 0
               && (windowsAttributes & FileAttributes.Directory) == 0;
    }

    private static async Task<byte[]> ReadBoundedEntryAsync(
        ZipArchiveEntry entry,
        long maximumLength,
        CancellationToken cancellationToken)
    {
        if (entry.Length > int.MaxValue || entry.Length > maximumLength)
        {
            throw new InvalidDataException("Archive entry is too large.");
        }

        await using var input = entry.Open();
        using var output = new MemoryStream((int)entry.Length);
        await CopyBoundedAsync(
            input,
            output,
            maximumLength,
            cancellationToken);

        if (output.Length != entry.Length)
        {
            throw new InvalidDataException("Archive entry length mismatch.");
        }

        return output.ToArray();
    }

    private static async Task ExtractBoundedEntryAsync(
        ZipArchiveEntry entry,
        string destinationPath,
        long maximumLength,
        CancellationToken cancellationToken)
    {
        await using var input = entry.Open();
        await using var output = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var copied = await CopyBoundedAsync(
            input,
            output,
            maximumLength,
            cancellationToken);

        if (copied != entry.Length)
        {
            throw new InvalidDataException("Archive entry length mismatch.");
        }

        await output.FlushAsync(cancellationToken);
    }

    private static async Task<long> CopyBoundedAsync(
        Stream input,
        Stream output,
        long maximumLength,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;

        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);

            if (read == 0)
            {
                return total;
            }

            total = checked(total + read);

            if (total > maximumLength)
            {
                throw new InvalidDataException("Archive entry is too large.");
            }

            await output.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken);
        }
    }

    internal static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static LocalDataOperationResult<VerifiedBackupPackage>
        DatabaseFailure(LocalDataFailure failure)
        => LocalDataOperationResult<VerifiedBackupPackage>.Failed(
            failure.Category switch
            {
                LocalDataFailureCategory.IntegrityFailure =>
                    LocalDataFailureCategory.IntegrityFailure,
                LocalDataFailureCategory.Cancelled =>
                    LocalDataFailureCategory.Cancelled,
                LocalDataFailureCategory.IoFailure =>
                    LocalDataFailureCategory.IoFailure,
                _ => LocalDataFailureCategory.InvalidBackup
            },
            failure.Category switch
            {
                LocalDataFailureCategory.IntegrityFailure =>
                    "The backup snapshot failed SQLite integrity validation.",
                LocalDataFailureCategory.Cancelled =>
                    "Backup verification was cancelled.",
                LocalDataFailureCategory.IoFailure =>
                    "The backup snapshot could not be read.",
                _ =>
                    "The backup snapshot is not a complete WealthLedger database."
            });

    private static LocalDataOperationResult<VerifiedBackupPackage> InvalidPackage(
        string message)
        => LocalDataOperationResult<VerifiedBackupPackage>.Failed(
            LocalDataFailureCategory.InvalidBackup,
            message);

    private static LocalDataFailure InvalidFailure(string message)
        => new(LocalDataFailureCategory.InvalidBackup, message);

    private static void DeleteTemporarySnapshot(
        string? snapshotPath,
        string? temporaryDirectory)
    {
        if (!string.IsNullOrWhiteSpace(snapshotPath))
        {
            LocalDatabaseFiles.DeleteDatabaseArtifacts(snapshotPath);
        }

        if (!string.IsNullOrWhiteSpace(temporaryDirectory))
        {
            try
            {
                Directory.Delete(temporaryDirectory, recursive: false);
            }
            catch (Exception exception)
                when (exception is IOException
                      or UnauthorizedAccessException)
            {
                // The verification result remains the primary outcome.
            }
        }
    }
}

internal sealed record BackupPackageLimits(
    long MaxPackageBytes,
    long MaxManifestBytes,
    long MaxSnapshotBytes,
    int MaxEntryCount,
    int MaxMigrationCount,
    double MaxCompressionRatio)
{
    internal static BackupPackageLimits Default { get; } = new(
        MaxPackageBytes: 8L * 1024 * 1024 * 1024,
        MaxManifestBytes: 256L * 1024,
        MaxSnapshotBytes: 8L * 1024 * 1024 * 1024,
        MaxEntryCount: 2,
        MaxMigrationCount: 128,
        MaxCompressionRatio: 200);
}

internal sealed class VerifiedBackupPackage : IAsyncDisposable
{
    private string? _temporaryDirectory;

    internal VerifiedBackupPackage(
        string packagePath,
        string snapshotPath,
        string temporaryDirectory,
        WealthLedgerBackupManifest manifest,
        SqliteDatabaseVerification databaseVerification)
    {
        PackagePath = packagePath;
        SnapshotPath = snapshotPath;
        _temporaryDirectory = temporaryDirectory;
        Manifest = manifest;
        DatabaseVerification = databaseVerification;
    }

    internal string PackagePath { get; }

    internal string SnapshotPath { get; }

    internal WealthLedgerBackupManifest Manifest { get; }

    internal SqliteDatabaseVerification DatabaseVerification { get; }

    public ValueTask DisposeAsync()
    {
        var directory = Interlocked.Exchange(ref _temporaryDirectory, null);

        if (directory is not null)
        {
            LocalDatabaseFiles.DeleteDatabaseArtifacts(SnapshotPath);

            try
            {
                Directory.Delete(directory, recursive: false);
            }
            catch (Exception exception)
                when (exception is IOException
                      or UnauthorizedAccessException)
            {
                // A failed controlled-temp cleanup does not change verification.
            }
        }

        return ValueTask.CompletedTask;
    }
}
