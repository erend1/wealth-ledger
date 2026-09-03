using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using WealthLedger.Application.LocalData;
using WealthLedger.Infrastructure.LocalData;

namespace WealthLedger.Infrastructure.Tests.LocalData;

public sealed class SqliteLocalBackupTests
{
    private const string PrivateMarker = "SYNTHETIC_PRIVATE_MARKER_7D4E";

    [Fact]
    public async Task Backup_WalWithOpenConnection_CreatesStandaloneVerifiedPackage()
    {
        await using var harness = await LocalBackupTestHarness.CreateAsync();
        await using var writer = SqliteLocalDataConnectionFactory.CreateConnection(
            harness.DatabasePath,
            SqliteOpenMode.ReadWrite);
        await writer.OpenAsync();
        await ExecuteAsync(writer, "PRAGMA journal_mode = WAL;");
        await ExecuteAsync(writer, "PRAGMA wal_autocheckpoint = 0;");
        await InsertHouseholdAsync(writer, PrivateMarker);

        Assert.True(File.Exists(harness.DatabasePath + "-wal"));

        var result = await harness.CreateBackupAsync();

        Assert.True(result.Succeeded);
        Assert.EndsWith(".wlbackup", result.Value!.FilePath);
        Assert.Equal(LocalBackupTestHarness.OperationTime, result.Value.CreatedAtUtc);
        Assert.Equal(5, result.Value.AppliedMigrations.Count);
        Assert.Equal("PLAINTEXT", result.Value.EncryptionMode);
        Assert.Equal(12, result.Value.DigestPrefix.Length);

        await using (var packageStream = File.OpenRead(result.Value.FilePath))
        using (var archive = new ZipArchive(
                   packageStream,
                   ZipArchiveMode.Read))
        {
            Assert.Equal(
                ["database.sqlite", "manifest.json"],
                archive.Entries.Select(entry => entry.FullName)
                    .OrderBy(name => name, StringComparer.Ordinal));
        }

        var parts = await LocalBackupTestHarness.ReadPackagePartsAsync(
            result.Value.FilePath);
        var manifestJson = Encoding.UTF8.GetString(parts.Manifest);
        var manifest = LocalBackupTestHarness.ReadManifest(parts.Manifest);

        Assert.DoesNotContain(PrivateMarker, manifestJson);
        Assert.DoesNotContain(
            PrivateMarker,
            Path.GetFileName(result.Value.FilePath));
        Assert.Equal(
            LocalBackupTestHarness.ComputeSha256(parts.Snapshot),
            manifest.SnapshotSha256);
        Assert.Equal(
            WealthLedgerBackupManifest.CompatibleOutcome,
            manifest.CompatibilityCheckOutcome);

        var restartedReader = new LocalBackupPackageReader(
            new SqliteDatabaseVerifier());
        var restarted = await restartedReader.OpenVerifiedAsync(
            result.Value.FilePath);

        Assert.True(restarted.Succeeded);
        await using (var verifiedPackage = restarted.Value!)
        await using (var snapshot =
                     SqliteLocalDataConnectionFactory.CreateConnection(
                         verifiedPackage.SnapshotPath,
                         SqliteOpenMode.ReadOnly))
        {
            await snapshot.OpenAsync();
            Assert.Equal(
                1L,
                await CountHouseholdsAsync(snapshot, PrivateMarker));
        }
    }

    [Fact]
    public async Task Backup_RollbackJournalExcludesUncommittedWriterState()
    {
        await using var harness = await LocalBackupTestHarness.CreateAsync();
        await using var writer = SqliteLocalDataConnectionFactory.CreateConnection(
            harness.DatabasePath,
            SqliteOpenMode.ReadWrite);
        await writer.OpenAsync();
        await ExecuteAsync(writer, "PRAGMA journal_mode = DELETE;");
        await ExecuteAsync(writer, "BEGIN IMMEDIATE;");
        await InsertHouseholdAsync(writer, PrivateMarker);

        Assert.True(File.Exists(harness.DatabasePath + "-journal"));

        var result = await harness.CreateBackupAsync();

        Assert.True(result.Succeeded);
        var package = await harness.PackageReader.OpenVerifiedAsync(
            result.Value!.FilePath);
        Assert.True(package.Succeeded);

        await using (var verifiedPackage = package.Value!)
        await using (var snapshot =
                     SqliteLocalDataConnectionFactory.CreateConnection(
                         verifiedPackage.SnapshotPath,
                         SqliteOpenMode.ReadOnly))
        {
            await snapshot.OpenAsync();
            Assert.Equal(
                0L,
                await CountHouseholdsAsync(snapshot, PrivateMarker));
        }

        await ExecuteAsync(writer, "ROLLBACK;");
    }

    [Fact]
    public async Task Backup_RetryCreatesImmutableCollisionSafeGenerations()
    {
        await using var harness = await LocalBackupTestHarness.CreateAsync();

        var first = await harness.CreateBackupAsync();
        var second = await harness.CreateBackupAsync();

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.NotEqual(first.Value!.FilePath, second.Value!.FilePath);
        Assert.True(File.Exists(first.Value.FilePath));
        Assert.True(File.Exists(second.Value.FilePath));
        Assert.Equal(2, Directory.GetFiles(
            harness.BackupDirectory,
            "*.wlbackup").Length);
    }

    [Fact]
    public async Task Backup_ActiveOwnerReturnsStableBusyWithoutPackage()
    {
        await using var harness = await LocalBackupTestHarness.CreateAsync();
        var ownership = harness.OwnershipGuard.Acquire(
            harness.DatabasePath,
            createDirectory: false);
        Assert.True(ownership.Succeeded);
        await using var lease = ownership.Value!;

        var result = await harness.CreateBackupAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.OwnershipBusy,
            result.Failure!.Category);
        Assert.False(Directory.Exists(harness.BackupDirectory));
    }

    [Fact]
    public async Task Backup_CancellationBeforePublishLeavesNoPackageAndSourceValid()
    {
        var hooks = new ThrowingBackupHooks(
            new OperationCanceledException("Synthetic cancellation."));
        await using var harness = await LocalBackupTestHarness.CreateAsync(hooks);

        var result = await harness.CreateBackupAsync();
        var source = await harness.DatabaseVerifier.VerifyAsync(
            harness.DatabasePath);

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.Cancelled,
            result.Failure!.Category);
        Assert.True(source.Succeeded);
        Assert.Empty(Directory.GetFileSystemEntries(harness.BackupDirectory));
        Assert.DoesNotContain("Synthetic", result.Failure.Message);
    }

    [Fact]
    public async Task Backup_InjectedIoFailureLeavesNoPackageAndSourceValid()
    {
        var hooks = new ThrowingBackupHooks(
            new IOException("Synthetic private I/O detail."));
        await using var harness = await LocalBackupTestHarness.CreateAsync(hooks);

        var result = await harness.CreateBackupAsync();
        var source = await harness.DatabaseVerifier.VerifyAsync(
            harness.DatabasePath);

        Assert.False(result.Succeeded);
        Assert.Equal(LocalDataFailureCategory.IoFailure, result.Failure!.Category);
        Assert.True(source.Succeeded);
        Assert.Empty(Directory.GetFileSystemEntries(harness.BackupDirectory));
        Assert.DoesNotContain("Synthetic", result.Failure.Message);
    }

    [Fact]
    public async Task Backup_PublishCollisionNeverOverwritesExistingPath()
    {
        var hooks = new PublishCollisionHooks();
        await using var harness = await LocalBackupTestHarness.CreateAsync(hooks);

        var result = await harness.CreateBackupAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(LocalDataFailureCategory.IoFailure, result.Failure!.Category);
        Assert.NotNull(hooks.CollisionPath);
        Assert.Equal(
            PublishCollisionHooks.Marker,
            await File.ReadAllTextAsync(hooks.CollisionPath!));
        Assert.Single(Directory.GetFiles(
            harness.BackupDirectory,
            "*.wlbackup"));
        Assert.Empty(Directory.GetFileSystemEntries(
            harness.BackupDirectory,
            "*.wlrestore"));
    }

    [Fact]
    public async Task Backup_OldSupportedSchemaRecordsMigrationRequired()
    {
        await using var harness = await LocalBackupTestHarness.CreateAsync(
            targetMigration: "20260827072019_002_CommandReceipt");
        Directory.CreateDirectory(harness.BackupDirectory);

        var result = await harness.BackupService.CreateVerifiedBackupAsync(
            harness.DatabasePath,
            harness.BackupDirectory,
            LocalBackupPurpose.PreMigration,
            allowMigrationRequired: true);

        Assert.True(result.Succeeded);

        var verification = await harness.Verifier.VerifyAsync(
            result.Value!.FilePath);
        var parts = await LocalBackupTestHarness.ReadPackagePartsAsync(
            result.Value.FilePath);
        var manifest = LocalBackupTestHarness.ReadManifest(parts.Manifest);

        Assert.True(verification.Succeeded);
        Assert.Equal(
            LocalDatabaseCompatibility.MigrationRequired,
            verification.Value!.Compatibility);
        Assert.Equal(
            WealthLedgerBackupManifest.MigrationRequiredOutcome,
            manifest.CompatibilityCheckOutcome);
    }

    [Fact]
    public async Task Backup_StatusFindsLatestVerifiedGenerationAndReadiness()
    {
        await using var harness = await LocalBackupTestHarness.CreateAsync();
        var created = await harness.CreateBackupAsync();
        var reader = new SqliteLocalDataStatusReader(
            harness.Resolver,
            harness.OwnershipGuard,
            harness.DatabaseVerifier,
            harness.PackageReader);

        var result = await reader.ReadAsync();

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.BackupDirectoryExists);
        Assert.True(result.Value.LocalProtectionReady);
        Assert.NotNull(result.Value.LatestVerifiedBackup);
        Assert.Equal(
            created.Value!.FilePath,
            result.Value.LatestVerifiedBackup!.FilePath);
        Assert.Equal(
            created.Value.DigestPrefix,
            result.Value.LatestVerifiedBackup.DigestPrefix);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        _ = await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertHouseholdAsync(
        SqliteConnection connection,
        string name)
    {
        await using (var currencyCommand = connection.CreateCommand())
        {
            currencyCommand.CommandText =
                """
                INSERT OR IGNORE INTO Currency (Code, Name, MinorUnitDigits)
                VALUES ('ZZZ', 'Synthetic Currency', 2);
                """;
            _ = await currencyCommand.ExecuteNonQueryAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Household (Id, Name, BaseCurrencyCode, CreatedAtUtc)
            VALUES ($id, $name, 'ZZZ', '2026-09-01T10:15:30.0000000Z');
            """;
        command.Parameters.AddWithValue(
            "$id",
            Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$name", name);
        _ = await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountHouseholdsAsync(
        SqliteConnection connection,
        string name)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM Household WHERE Name = $name;";
        command.Parameters.AddWithValue("$name", name);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private sealed class ThrowingBackupHooks : ILocalDataOperationHooks
    {
        private readonly Exception _exception;

        internal ThrowingBackupHooks(Exception exception)
        {
            _exception = exception;
        }

        public ValueTask OnCheckpointAsync(
            LocalDataOperationCheckpoint checkpoint,
            string primaryPath,
            string? secondaryPath,
            CancellationToken cancellationToken)
            => checkpoint == LocalDataOperationCheckpoint.BeforeBackupPublish
                ? ValueTask.FromException(_exception)
                : ValueTask.CompletedTask;
    }

    private sealed class PublishCollisionHooks : ILocalDataOperationHooks
    {
        internal const string Marker = "preexisting synthetic collision";

        internal string? CollisionPath { get; private set; }

        public async ValueTask OnCheckpointAsync(
            LocalDataOperationCheckpoint checkpoint,
            string primaryPath,
            string? secondaryPath,
            CancellationToken cancellationToken)
        {
            if (checkpoint != LocalDataOperationCheckpoint.BeforeBackupPublish)
            {
                return;
            }

            CollisionPath = secondaryPath
                ?? throw new InvalidOperationException(
                    "The synthetic final path was missing.");
            await File.WriteAllTextAsync(
                CollisionPath,
                Marker,
                cancellationToken);
        }
    }
}
