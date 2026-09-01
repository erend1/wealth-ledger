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
        SqliteBackupService backupService)
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
        Creator = new SqliteLocalBackupCreator(
            resolver,
            ownershipGuard,
            backupService);
        Verifier = new SqliteLocalBackupVerifier(resolver, packageReader);
    }

    internal string RootPath { get; }

    internal string DatabasePath { get; }

    internal string BackupDirectory { get; }

    internal LocalDataPathResolver Resolver { get; }

    internal LocalDatabaseOwnershipGuard OwnershipGuard { get; }

    internal SqliteDatabaseVerifier DatabaseVerifier { get; }

    internal LocalBackupPackageReader PackageReader { get; }

    internal SqliteBackupService BackupService { get; }

    internal SqliteLocalBackupCreator Creator { get; }

    internal SqliteLocalBackupVerifier Verifier { get; }

    internal static async Task<LocalBackupTestHarness> CreateAsync(
        ILocalDataOperationHooks? hooks = null,
        string? targetMigration = null)
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
        var resolver = new LocalDataPathResolver(
            new Dictionary<string, string?>
            {
                [LocalDataPathResolver.DatabasePathConfigurationKey] =
                    databasePath,
                [LocalDataPathResolver.BackupDirectoryConfigurationKey] =
                    backupDirectory,
                [LocalDataPathResolver
                    .DestinationSeparationConfigurationKey] = "true",
                [LocalDataPathResolver
                    .DestinationEncryptionConfigurationKey] = "true"
            },
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
        var backupService = new SqliteBackupService(
            resolver,
            databaseVerifier,
            packageReader,
            timeProvider,
            hooks);
        var harness = new LocalBackupTestHarness(
            allowedRoot,
            rootPath,
            databasePath,
            backupDirectory,
            resolver,
            ownershipGuard,
            databaseVerifier,
            packageReader,
            backupService);

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

    internal async Task<LocalDataOperationResult<LocalBackupCreation>>
        CreateBackupAsync()
        => await Creator.CreateAsync();

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
