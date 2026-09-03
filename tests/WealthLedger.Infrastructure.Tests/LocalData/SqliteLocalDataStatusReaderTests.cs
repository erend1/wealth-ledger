using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.LocalData;
using WealthLedger.Infrastructure.LocalData;

namespace WealthLedger.Infrastructure.Tests.LocalData;

public sealed class SqliteLocalDataStatusReaderTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _databasePath;
    private readonly string _backupDirectory;

    public SqliteLocalDataStatusReaderTests()
    {
        _testRoot = Path.Combine(
            Path.GetTempPath(),
            "WealthLedger.Infrastructure.Tests",
            nameof(SqliteLocalDataStatusReaderTests),
            Guid.NewGuid().ToString("N"));
        _databasePath = Path.Combine(
            _testRoot,
            "live",
            "wealthledger.db");
        _backupDirectory = Path.Combine(_testRoot, "backups");
    }

    [Fact]
    public async Task Status_CurrentDatabaseReportsPathsStateAndNoSideEffects()
    {
        await MigrateAsync();
        var components = CreateComponents(includeBackupDirectory: true);

        var result = await components.Reader.ReadAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(Path.GetFullPath(_databasePath), result.Value!.DatabasePath);
        Assert.Equal(
            Path.GetFullPath(_backupDirectory),
            result.Value.BackupDirectory);
        Assert.True(result.Value.DatabaseExists);
        Assert.True(result.Value.DatabasePathSafe);
        Assert.True(result.Value.OwnershipAvailable);
        Assert.Equal(LocalDataIntegrityStatus.Passed, result.Value.IntegrityStatus);
        Assert.Equal(
            LocalDatabaseCompatibility.Compatible,
            result.Value.Compatibility);
        Assert.Equal(5, result.Value.AppliedMigrations.Count);
        Assert.False(result.Value.BackupDirectoryExists);
        Assert.False(result.Value.LocalProtectionReady);
        Assert.Equal("PLAINTEXT", result.Value.EncryptionMode);
        Assert.False(Directory.Exists(_backupDirectory));
    }

    [Fact]
    public async Task Status_MissingBackupConfigurationIsActionableAndNonZero()
    {
        await MigrateAsync();
        var components = CreateComponents(includeBackupDirectory: false);

        var result = await components.Reader.ReadAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.InvalidInputOrConfiguration,
            result.Failure!.Category);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.DatabaseExists);
        Assert.False(result.Value.BackupDirectoryConfigured);
    }

    [Fact]
    public async Task Status_PendingMigrationRequiresExplicitCommand()
    {
        await MigrateAsync("20260827072019_002_CommandReceipt");
        var components = CreateComponents(includeBackupDirectory: true);

        var result = await components.Reader.ReadAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.DatabaseNotReady,
            result.Failure!.Category);
        Assert.Equal(
            LocalDatabaseCompatibility.MigrationRequired,
            result.Value!.Compatibility);
        Assert.Equal(3, result.Value.PendingMigrations.Count);
    }

    [Fact]
    public async Task Status_ActiveOwnerIsReportedWithoutChangingDatabase()
    {
        await MigrateAsync();
        var components = CreateComponents(includeBackupDirectory: true);
        var ownership = components.Guard.Acquire(
            _databasePath,
            createDirectory: false);
        Assert.True(ownership.Succeeded);

        try
        {
            var result = await components.Reader.ReadAsync();

            Assert.True(result.Succeeded);
            Assert.False(result.Value!.OwnershipAvailable);
        }
        finally
        {
            ownership.Value!.Dispose();
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        var resolvedRoot = Path.GetFullPath(_testRoot);
        var allowedRoot = Path.GetFullPath(
            Path.Combine(
                Path.GetTempPath(),
                "WealthLedger.Infrastructure.Tests"));

        if (Directory.Exists(resolvedRoot)
            && resolvedRoot.StartsWith(
                allowedRoot + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            Directory.Delete(resolvedRoot, recursive: true);
        }
    }

    private Components CreateComponents(bool includeBackupDirectory)
    {
        var values = new Dictionary<string, string?>
        {
            [LocalDataPathResolver.DatabasePathConfigurationKey] = _databasePath
        };

        if (includeBackupDirectory)
        {
            values[LocalDataPathResolver.BackupDirectoryConfigurationKey] =
                _backupDirectory;
        }

        var resolver = new LocalDataPathResolver(
            values,
            new LocalDataPathEnvironment(
                "Testing",
                FindRepositoryRoot(),
                AppContext.BaseDirectory,
                Directory.GetCurrentDirectory(),
                Path.Combine(_testRoot, "local-app-data"),
                Path.Combine(_testRoot, "profile")));
        var guard = new LocalDatabaseOwnershipGuard(resolver);
        var verifier = new SqliteDatabaseVerifier();

        return new Components(
            new SqliteLocalDataStatusReader(resolver, guard, verifier),
            guard);
    }

    private async Task MigrateAsync(string? targetMigration = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        var options = new DbContextOptionsBuilder<
                WealthLedger.Infrastructure.Persistence.WealthLedgerDbContext>()
            .UseSqlite(
                new SqliteConnectionStringBuilder
                {
                    DataSource = _databasePath,
                    ForeignKeys = true,
                    Pooling = false
                }.ToString())
            .Options;
        await using var context =
            new WealthLedger.Infrastructure.Persistence.WealthLedgerDbContext(
                options);

        if (targetMigration is null)
        {
            await context.Database.MigrateAsync();
        }
        else
        {
            await context.Database.MigrateAsync(targetMigration);
        }
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

    private sealed record Components(
        SqliteLocalDataStatusReader Reader,
        LocalDatabaseOwnershipGuard Guard);
}
