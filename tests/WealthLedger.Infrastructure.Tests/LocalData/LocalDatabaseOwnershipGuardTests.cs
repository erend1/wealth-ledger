using WealthLedger.Application.LocalData;
using WealthLedger.Infrastructure.LocalData;

namespace WealthLedger.Infrastructure.Tests.LocalData;

public sealed class LocalDatabaseOwnershipGuardTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _databasePath;
    private readonly LocalDatabaseOwnershipGuard _guard;

    public LocalDatabaseOwnershipGuardTests()
    {
        _testRoot = Path.Combine(
            Path.GetTempPath(),
            "WealthLedger.Infrastructure.Tests",
            nameof(LocalDatabaseOwnershipGuardTests),
            Guid.NewGuid().ToString("N"));
        _databasePath = Path.Combine(
            _testRoot,
            "live",
            "wealthledger.db");
        var resolver = new LocalDataPathResolver(
            new Dictionary<string, string?>
            {
                [LocalDataPathResolver.DatabasePathConfigurationKey] =
                    _databasePath
            },
            new LocalDataPathEnvironment(
                "Testing",
                FindRepositoryRoot(),
                AppContext.BaseDirectory,
                Directory.GetCurrentDirectory(),
                Path.Combine(_testRoot, "local-app-data"),
                Path.Combine(_testRoot, "profile")));
        _guard = new LocalDatabaseOwnershipGuard(resolver);
    }

    [Fact]
    public void Acquire_RejectsConcurrentOwnerAndAllowsReacquireAfterRelease()
    {
        var first = _guard.Acquire(
            _databasePath,
            createDirectory: true);

        Assert.True(first.Succeeded);

        var collision = _guard.Acquire(
            _databasePath,
            createDirectory: false);

        Assert.False(collision.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.OwnershipBusy,
            collision.Failure!.Category);
        Assert.False(File.Exists(_databasePath));

        first.Value!.Dispose();

        var afterRelease = _guard.Acquire(
            _databasePath,
            createDirectory: false);

        Assert.True(afterRelease.Succeeded);
        afterRelease.Value!.Dispose();
    }

    [Fact]
    public void Acquire_StaleUnlockedMarkerDoesNotBlockRestart()
    {
        var first = _guard.Acquire(
            _databasePath,
            createDirectory: true);
        var lockPath = first.Value!.LockPath;
        first.Value.Dispose();
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        File.WriteAllText(lockPath, "stale");

        var restarted = _guard.Acquire(
            _databasePath,
            createDirectory: false);

        Assert.True(restarted.Succeeded);
        restarted.Value!.Dispose();
    }

    [Fact]
    public void Acquire_UniqueDatabasePathsDoNotCollide()
    {
        var first = _guard.Acquire(
            _databasePath,
            createDirectory: true);
        var otherPath = Path.Combine(
            _testRoot,
            "other",
            "wealthledger.db");
        var second = _guard.Acquire(
            otherPath,
            createDirectory: true);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);

        first.Value!.Dispose();
        second.Value!.Dispose();
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
}
