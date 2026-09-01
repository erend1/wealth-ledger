using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.LocalData;
using WealthLedger.Infrastructure.LocalData;

namespace WealthLedger.Infrastructure.Tests.LocalData;

internal sealed class LocalBackupTestHarness : IAsyncDisposable
{
    internal static readonly DateTimeOffset OperationTime =
        new(2026, 9, 1, 10, 15, 30, TimeSpan.Zero);

    private readonly string _allowedRoot;

    private LocalBackupTestHarness(
        string allowedRoot,
        string rootPath,
        string databasePath,
        string backupDirectory,
        LocalDataPathResolver resolver,
        LocalDatabaseOwnershipGuard ownershipGuard,
        SqliteDatabaseVerifier databaseVerifier,
        LocalBackupPackageReader packageReader,
        SqliteBackupService backupService,
        SqliteRestoreService restoreService,
        ILocalDataOperationHooks hooks)
    {
        _allowedRoot = allowedRoot;
        RootPath = rootPath;
        DatabasePath = databasePath;
        BackupDirectory = backupDirectory;
        Resolver = resolver;
        OwnershipGuard = ownershipGuard;
        DatabaseVerifier = databaseVerifier;
        PackageReader = packageReader;
        BackupService = backupService;
        RestoreService = restoreService;
        Hooks = hooks;
        Creator = new SqliteLocalBackupCreator(
            resolver,
            ownershipGuard,
            backupService);
        Verifier = new SqliteLocalBackupVerifier(resolver, packageReader);
        RestoreStager = new SqliteLocalRestoreStager(
            resolver,
            restoreService);
    }

    internal string RootPath { get; }

    internal string DatabasePath { get; }

    internal string BackupDirectory { get; }

    internal LocalDataPathResolver Resolver { get; }

    internal LocalDatabaseOwnershipGuard OwnershipGuard { get; }

    internal SqliteDatabaseVerifier DatabaseVerifier { get; }

    internal LocalBackupPackageReader PackageReader { get; }

    internal SqliteBackupService BackupService { get; }

    internal SqliteRestoreService RestoreService { get; }

    internal SqliteLocalBackupCreator Creator { get; }

    internal SqliteLocalBackupVerifier Verifier { get; }

    internal SqliteLocalRestoreStager RestoreStager { get; }

    internal ILocalDataOperationHooks Hooks { get; }

    internal static async Task<LocalBackupTestHarness> CreateAsync(
        ILocalDataOperationHooks? hooks = null,
        string? targetMigration = null,
        bool includeBackupConfiguration = true)
    {
        var allowedRoot = Path.Combine(
            Path.GetTempPath(),
            "WealthLedger.Infrastructure.Tests",
            "LocalBackup");
        var rootPath = Path.Combine(
            allowedRoot,
            Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(
            rootPath,
            "live",
            "wealthledger.db");
        var backupDirectory = Path.Combine(rootPath, "backups");
        var configuration = new Dictionary<string, string?>
        {
            [LocalDataPathResolver.DatabasePathConfigurationKey] =
                databasePath,
            [LocalDataPathResolver
                .DestinationSeparationConfigurationKey] = "true",
            [LocalDataPathResolver
                .DestinationEncryptionConfigurationKey] = "true"
        };

        if (includeBackupConfiguration)
        {
            configuration[LocalDataPathResolver
                .BackupDirectoryConfigurationKey] = backupDirectory;
        }

        var resolver = new LocalDataPathResolver(
            configuration,
            new LocalDataPathEnvironment(
                "Testing",
                FindRepositoryRoot(),
                AppContext.BaseDirectory,
                Directory.GetCurrentDirectory(),
                Path.Combine(rootPath, "local-app-data"),
                Path.Combine(rootPath, "profile")));
        var ownershipGuard = new LocalDatabaseOwnershipGuard(resolver);
        var databaseVerifier = new SqliteDatabaseVerifier();
        var packageReader = new LocalBackupPackageReader(databaseVerifier);
        var timeProvider = new FixedTimeProvider(OperationTime);
        var effectiveHooks = hooks ?? NoOpLocalDataOperationHooks.Instance;
        var backupService = new SqliteBackupService(
            resolver,
            databaseVerifier,
            packageReader,
            timeProvider,
            effectiveHooks);
        var restoreService = new SqliteRestoreService(
            packageReader,
            databaseVerifier,
            timeProvider,
            effectiveHooks);
        var harness = new LocalBackupTestHarness(
            allowedRoot,
            rootPath,
            databasePath,
            backupDirectory,
            resolver,
            ownershipGuard,
            databaseVerifier,
            packageReader,
            backupService,
            restoreService,
            effectiveHooks);

        if (targetMigration is null)
        {
            var initializer = new SqliteLocalDatabaseInitializer(
                resolver,
                ownershipGuard,
                databaseVerifier,
                timeProvider);
            var initialized = await initializer.InitializeAsync();

            if (!initialized.Succeeded)
            {
                await harness.DisposeAsync();
                throw new InvalidOperationException(
                    initialized.Failure!.Message);
            }
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            await using var context =
                SqliteLocalDataConnectionFactory.CreateContext(
                    databasePath,
                    SqliteOpenMode.ReadWriteCreate);
            await context.Database.MigrateAsync(targetMigration);
        }

        return harness;
    }

    internal SqliteLocalDatabaseReplacementSessionFactory
        CreateReplacementSessionFactory()
        => new(
            Resolver,
            OwnershipGuard,
            RestoreService,
            BackupService,
            PackageReader,
            DatabaseVerifier,
            new FixedTimeProvider(OperationTime),
            Hooks);

    internal SqliteLocalDatabaseMigrationSessionFactory
        CreateMigrationSessionFactory()
        => new(
            Resolver,
            OwnershipGuard,
            BackupService,
            PackageReader,
            DatabaseVerifier,
            new FixedTimeProvider(OperationTime),
            Hooks);

    internal async Task<LocalDataOperationResult<LocalBackupCreation>>
        CreateBackupAsync()
        => await Creator.CreateAsync();

    internal async Task InsertSyntheticHouseholdAsync(string name)
    {
        await using var connection =
            SqliteLocalDataConnectionFactory.CreateConnection(
                DatabasePath,
                SqliteOpenMode.ReadWrite);
        await connection.OpenAsync();
        await using (var currency = connection.CreateCommand())
        {
            currency.CommandText =
                """
                INSERT OR IGNORE INTO Currency (Code, Name, MinorUnitDigits)
                VALUES ('ZZZ', 'Synthetic Currency', 2);
                """;
            _ = await currency.ExecuteNonQueryAsync();
        }

        await using var household = connection.CreateCommand();
        household.CommandText =
            """
            INSERT INTO Household (Id, Name, BaseCurrencyCode, CreatedAtUtc)
            VALUES ($id, $name, 'ZZZ', '2026-09-01T10:15:30.0000000Z');
            """;
        household.Parameters.AddWithValue(
            "$id",
            Guid.NewGuid().ToString("D"));
        household.Parameters.AddWithValue("$name", name);
        _ = await household.ExecuteNonQueryAsync();
    }

    internal async Task CreateDetachedWalGenerationAsync(string name)
    {
        byte[] mainFile;
        byte[] walFile;
        byte[] sharedMemoryFile;

        await using (var connection =
                     SqliteLocalDataConnectionFactory.CreateConnection(
                         DatabasePath,
                         SqliteOpenMode.ReadWrite))
        {
            await connection.OpenAsync();
            await using (var mode = connection.CreateCommand())
            {
                mode.CommandText =
                    "PRAGMA journal_mode = WAL; PRAGMA wal_autocheckpoint = 0;";
                _ = await mode.ExecuteNonQueryAsync();
            }

            await using (var currency = connection.CreateCommand())
            {
                currency.CommandText =
                    """
                    INSERT OR IGNORE INTO Currency (Code, Name, MinorUnitDigits)
                    VALUES ('ZZZ', 'Synthetic Currency', 2);
                    """;
                _ = await currency.ExecuteNonQueryAsync();
            }

            await using (var household = connection.CreateCommand())
            {
                household.CommandText =
                    """
                    INSERT INTO Household (Id, Name, BaseCurrencyCode, CreatedAtUtc)
                    VALUES ($id, $name, 'ZZZ', '2026-09-01T10:15:30.0000000Z');
                    """;
                household.Parameters.AddWithValue(
                    "$id",
                    Guid.NewGuid().ToString("D"));
                household.Parameters.AddWithValue("$name", name);
                _ = await household.ExecuteNonQueryAsync();
            }

            mainFile = await ReadSharedBytesAsync(DatabasePath);
            walFile = await ReadSharedBytesAsync(DatabasePath + "-wal");
            sharedMemoryFile = await ReadSharedBytesAsync(
                DatabasePath + "-shm");
        }

        await File.WriteAllBytesAsync(DatabasePath, mainFile);
        await File.WriteAllBytesAsync(DatabasePath + "-wal", walFile);
        await File.WriteAllBytesAsync(
            DatabasePath + "-shm",
            sharedMemoryFile);
    }

    private static async Task<byte[]> ReadSharedBytesAsync(string path)
    {
        await using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new MemoryStream();
        await input.CopyToAsync(output);
        return output.ToArray();
    }

    internal static async Task<long> CountSyntheticHouseholdsAsync(
        string databasePath,
        string name)
    {
        await using var connection =
            SqliteLocalDataConnectionFactory.CreateConnection(
                databasePath,
                SqliteOpenMode.ReadOnly);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM Household WHERE Name = $name;";
        command.Parameters.AddWithValue("$name", name);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    internal static async Task<PackageParts> ReadPackagePartsAsync(
        string packagePath)
    {
        await using var stream = File.OpenRead(packagePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var snapshotEntry = archive.GetEntry(
            LocalBackupPackageReader.SnapshotEntryName)!;
        var manifestEntry = archive.GetEntry(
            LocalBackupPackageReader.ManifestEntryName)!;

        await using var snapshotInput = snapshotEntry.Open();
        await using var snapshotOutput = new MemoryStream();
        await snapshotInput.CopyToAsync(snapshotOutput);

        await using var manifestInput = manifestEntry.Open();
        await using var manifestOutput = new MemoryStream();
        await manifestInput.CopyToAsync(manifestOutput);

        return new PackageParts(
            snapshotOutput.ToArray(),
            manifestOutput.ToArray());
    }

    internal static WealthLedgerBackupManifest ReadManifest(byte[] json)
        => JsonSerializer.Deserialize<WealthLedgerBackupManifest>(
               json,
               WealthLedgerBackupManifest.JsonOptions)
           ?? throw new InvalidDataException("Synthetic manifest was invalid.");

    internal static async Task WritePackageAsync(
        string packagePath,
        IReadOnlyList<SyntheticArchiveEntry> entries)
    {
        await using var stream = new FileStream(
            packagePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        using var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Create,
            leaveOpen: false);

        foreach (var specification in entries)
        {
            var entry = archive.CreateEntry(
                specification.Name,
                specification.CompressionLevel);

            if (specification.ExternalAttributes is not null)
            {
                entry.ExternalAttributes = specification.ExternalAttributes.Value;
            }

            await using var output = entry.Open();
            await output.WriteAsync(specification.Content);
        }
    }

    internal static byte[] SerializeManifest(
        WealthLedgerBackupManifest manifest)
        => JsonSerializer.SerializeToUtf8Bytes(
            manifest,
            WealthLedgerBackupManifest.JsonOptions);

    internal static string ComputeSha256(byte[] content)
        => Convert.ToHexString(SHA256.HashData(content));

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        var root = Path.GetFullPath(RootPath);
        var allowed = Path.GetFullPath(_allowedRoot);

        if (Directory.Exists(root)
            && root.StartsWith(
                allowed + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            Directory.Delete(root, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            if (File.Exists(
                    Path.Combine(current.FullName, "WealthLedger.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("The repository root was not found.");
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        internal FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}

internal sealed record PackageParts(
    byte[] Snapshot,
    byte[] Manifest);

internal sealed record SyntheticArchiveEntry(
    string Name,
    byte[] Content,
    CompressionLevel CompressionLevel = CompressionLevel.NoCompression,
    int? ExternalAttributes = null);
