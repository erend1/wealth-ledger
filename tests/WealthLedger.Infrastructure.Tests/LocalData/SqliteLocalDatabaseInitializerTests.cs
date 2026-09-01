using System.Security.Cryptography;
using WealthLedger.Application.LocalData;
using WealthLedger.Infrastructure.LocalData;

namespace WealthLedger.Infrastructure.Tests.LocalData;

public sealed class SqliteLocalDatabaseInitializerTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private readonly string _testRoot;
    private readonly string _databasePath;
    private readonly string _backupDirectory;

    public SqliteLocalDatabaseInitializerTests()
    {
        _testRoot = Path.Combine(
            Path.GetTempPath(),
            "WealthLedger.Infrastructure.Tests",
            nameof(SqliteLocalDatabaseInitializerTests),
            Guid.NewGuid().ToString("N"));
        _databasePath = Path.Combine(
            _testRoot,
            "live",
            "wealthledger.db");
        _backupDirectory = Path.Combine(_testRoot, "backups");
    }

    [Fact]
    public async Task Initialize_StagesMigratesPublishesAndVerifiesAfterRestart()
    {
        var components = CreateComponents();

        var result = await components.Initializer.InitializeAsync();
        var restarted = await components.Verifier.VerifyAsync(_databasePath);

        Assert.True(result.Succeeded);
        Assert.Equal(Path.GetFullPath(_databasePath), result.Value!.DatabasePath);
        Assert.Equal(3, result.Value.AppliedMigrations.Count);
        Assert.Equal(Now, result.Value.CompletedAtUtc);
        Assert.True(File.Exists(_databasePath));
        Assert.True(restarted.Succeeded);
        Assert.Equal(
            LocalDatabaseCompatibility.Compatible,
            restarted.Value!.Compatibility);
        AssertNoInitializationStageArtifacts();
    }

    [Fact]
    public async Task Initialize_ExistingDatabaseIsNeverRecreatedOrChanged()
    {
        var components = CreateComponents();
        var first = await components.Initializer.InitializeAsync();
        var digestBefore = await HashAsync(_databasePath);

        var second = await components.Initializer.InitializeAsync();
        var digestAfter = await HashAsync(_databasePath);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.AlreadyExists,
            second.Failure!.Category);
        Assert.Equal(digestBefore, digestAfter);
    }

    [Fact]
    public async Task Initialize_PartialCompanionIsRefusedAndPreserved()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        var companionPath = _databasePath + "-wal";
        await File.WriteAllTextAsync(companionPath, "synthetic-partial-state");
        var components = CreateComponents();

        var result = await components.Initializer.InitializeAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.AlreadyExists,
            result.Failure!.Category);
        Assert.True(File.Exists(companionPath));
        Assert.False(File.Exists(_databasePath));
    }

    [Fact]
    public async Task Initialize_InjectedIoFailureLeavesNoAuthoritativeFileOrStage()
    {
        var hooks = new ThrowingHooks(
            LocalDataOperationCheckpoint.BeforeInitializePublish,
            new IOException("Synthetic I/O failure."));
        var components = CreateComponents(hooks);

        var result = await components.Initializer.InitializeAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(LocalDataFailureCategory.IoFailure, result.Failure!.Category);
        Assert.False(File.Exists(_databasePath));
        AssertNoInitializationStageArtifacts();
        Assert.DoesNotContain("Synthetic", result.Failure.Message);
    }

    [Fact]
    public async Task Initialize_CancellationLeavesNoAuthoritativeFile()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var components = CreateComponents();

        var result = await components.Initializer.InitializeAsync(
            cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(LocalDataFailureCategory.Cancelled, result.Failure!.Category);
        Assert.False(File.Exists(_databasePath));
        AssertNoInitializationStageArtifacts();
    }

    public void Dispose()
    {
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

    private Components CreateComponents(ILocalDataOperationHooks? hooks = null)
    {
        var resolver = new LocalDataPathResolver(
            new Dictionary<string, string?>
            {
                [LocalDataPathResolver.DatabasePathConfigurationKey] =
                    _databasePath,
                [LocalDataPathResolver.BackupDirectoryConfigurationKey] =
                    _backupDirectory
            },
            new LocalDataPathEnvironment(
                "Testing",
                FindRepositoryRoot(),
                AppContext.BaseDirectory,
                Directory.GetCurrentDirectory(),
                Path.Combine(_testRoot, "local-app-data"),
                Path.Combine(_testRoot, "profile")));
        var guard = new LocalDatabaseOwnershipGuard(resolver);
        var verifier = new SqliteDatabaseVerifier();
        var initializer = new SqliteLocalDatabaseInitializer(
            resolver,
            guard,
            verifier,
            new FixedTimeProvider(Now),
            hooks);

        return new Components(initializer, verifier);
    }

    private void AssertNoInitializationStageArtifacts()
    {
        var databaseDirectory = Path.GetDirectoryName(_databasePath)!;

        Assert.DoesNotContain(
            Directory.EnumerateFiles(databaseDirectory),
            path => Path.GetFileName(path).Contains(
                ".wlrestore",
                StringComparison.Ordinal));
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(
            await SHA256.HashDataAsync(stream));
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
        SqliteLocalDatabaseInitializer Initializer,
        SqliteDatabaseVerifier Verifier);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        internal FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class ThrowingHooks : ILocalDataOperationHooks
    {
        private readonly LocalDataOperationCheckpoint _checkpoint;
        private readonly Exception _exception;

        internal ThrowingHooks(
            LocalDataOperationCheckpoint checkpoint,
            Exception exception)
        {
            _checkpoint = checkpoint;
            _exception = exception;
        }

        public ValueTask OnCheckpointAsync(
            LocalDataOperationCheckpoint checkpoint,
            string primaryPath,
            string? secondaryPath,
            CancellationToken cancellationToken)
        {
            if (checkpoint == _checkpoint)
            {
                return ValueTask.FromException(_exception);
            }

            return ValueTask.CompletedTask;
        }
    }
}
