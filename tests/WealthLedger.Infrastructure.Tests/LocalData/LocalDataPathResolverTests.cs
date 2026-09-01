using WealthLedger.Application.LocalData;
using WealthLedger.Infrastructure.LocalData;

namespace WealthLedger.Infrastructure.Tests.LocalData;

public sealed class LocalDataPathResolverTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _localApplicationDataPath;
    private readonly string _userProfilePath;
    private readonly string _repositoryRoot;

    public LocalDataPathResolverTests()
    {
        _testRoot = Path.Combine(
            Path.GetTempPath(),
            "WealthLedger.Infrastructure.Tests",
            nameof(LocalDataPathResolverTests),
            Guid.NewGuid().ToString("N"));
        _localApplicationDataPath = Path.Combine(
            _testRoot,
            "local-app-data");
        _userProfilePath = Path.Combine(_testRoot, "profile");
        _repositoryRoot = FindRepositoryRoot();

        Directory.CreateDirectory(_localApplicationDataPath);
        Directory.CreateDirectory(_userProfilePath);
    }

    [Fact]
    public void DefaultDatabasePath_UsesLocalApplicationDataAndDoesNotCreateIt()
    {
        var firstResolver = CreateResolver(
            currentDirectory: Path.Combine(_testRoot, "working-one"));
        var secondResolver = CreateResolver(
            currentDirectory: Path.Combine(_testRoot, "working-two"));
        var expected = Path.Combine(
            _localApplicationDataPath,
            "WealthLedger",
            "data",
            "wealthledger.db");

        var first = firstResolver.ResolveDatabasePath();
        var second = secondResolver.ResolveDatabasePath();

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(Path.GetFullPath(expected), first.Value!.FullPath);
        Assert.Equal(first.Value.FullPath, second.Value!.FullPath);
        Assert.False(Directory.Exists(Path.GetDirectoryName(expected)));
        Assert.False(File.Exists(expected));
    }

    [Fact]
    public void AbsoluteOverride_IsNormalizedIndependentlyOfCurrentDirectory()
    {
        var configured = Path.Combine(
            _testRoot,
            "storage",
            "nested",
            "..",
            "wealthledger.db");
        var resolver = CreateResolver(
            databasePath: configured,
            currentDirectory: Path.Combine(_testRoot, "unrelated"));

        var result = resolver.ResolveDatabasePath();

        Assert.True(result.Succeeded);
        Assert.Equal(
            Path.GetFullPath(
                Path.Combine(_testRoot, "storage", "wealthledger.db")),
            result.Value!.FullPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative.db")]
    public void InvalidDatabaseOverride_FailsClosed(string configuredPath)
    {
        var resolver = CreateResolver(databasePath: configuredPath);

        var result = resolver.ResolveDatabasePath();

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failure!.Category,
            new[]
            {
                LocalDataFailureCategory.InvalidInputOrConfiguration,
                LocalDataFailureCategory.UnsafePath
            });
    }

    [Fact]
    public void RepositoryAndBuildPaths_AreRejected()
    {
        var repositoryResult = CreateResolver(
                databasePath: Path.Combine(_repositoryRoot, "unsafe.db"))
            .ResolveDatabasePath();
        var buildResult = CreateResolver(
                databasePath: Path.Combine(AppContext.BaseDirectory, "unsafe.db"))
            .ResolveDatabasePath();

        Assert.False(repositoryResult.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.UnsafePath,
            repositoryResult.Failure!.Category);
        Assert.False(buildResult.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.UnsafePath,
            buildResult.Failure!.Category);
    }

    [Fact]
    public void ExistingDirectory_CannotBeUsedAsDatabaseFile()
    {
        var directory = Path.Combine(_testRoot, "directory-target");
        Directory.CreateDirectory(directory);
        var resolver = CreateResolver(databasePath: directory);

        var result = resolver.ResolveDatabasePath();

        Assert.False(result.Succeeded);
        Assert.Equal(LocalDataFailureCategory.UnsafePath, result.Failure!.Category);
    }

    [Fact]
    public void BackupDirectory_MustBeAbsoluteDistinctAndNarrow()
    {
        var databasePath = Path.Combine(
            _testRoot,
            "live",
            "wealthledger.db");
        var missing = CreateResolver(databasePath: databasePath)
            .ResolveBackupDirectory(databasePath);
        var relative = CreateResolver(
                databasePath,
                backupDirectory: "relative-backups")
            .ResolveBackupDirectory(databasePath);
        var overlapping = CreateResolver(
                databasePath,
                backupDirectory: Path.Combine(_testRoot, "live", "backups"))
            .ResolveBackupDirectory(databasePath);
        var root = CreateResolver(
                databasePath,
                backupDirectory: Path.GetPathRoot(_testRoot))
            .ResolveBackupDirectory(databasePath);
        var validPath = Path.Combine(_testRoot, "backups");
        var valid = CreateResolver(
                databasePath,
                backupDirectory: validPath)
            .ResolveBackupDirectory(databasePath);

        Assert.Equal(
            LocalDataFailureCategory.InvalidInputOrConfiguration,
            missing.Failure!.Category);
        Assert.False(relative.Succeeded);
        Assert.False(overlapping.Succeeded);
        Assert.False(root.Succeeded);
        Assert.True(valid.Succeeded);
        Assert.Equal(Path.GetFullPath(validPath), valid.Value!.FullPath);
        Assert.False(Directory.Exists(validPath));
    }

    [Fact]
    public void RestoreTarget_CannotBeLiveOrInsideBackupDirectory()
    {
        var databasePath = Path.Combine(
            _testRoot,
            "live",
            "wealthledger.db");
        var backupDirectory = Path.Combine(_testRoot, "backups");
        var resolver = CreateResolver(
            databasePath,
            backupDirectory);

        var liveResult = resolver.ValidateRestoreTargetPath(
            databasePath,
            databasePath,
            backupDirectory);
        var backupResult = resolver.ValidateRestoreTargetPath(
            Path.Combine(backupDirectory, "restored.db"),
            databasePath,
            backupDirectory);
        var isolatedPath = Path.Combine(
            _testRoot,
            "restore-drill",
            "restored.db");
        var isolatedResult = resolver.ValidateRestoreTargetPath(
            isolatedPath,
            databasePath,
            backupDirectory);

        Assert.False(liveResult.Succeeded);
        Assert.False(backupResult.Succeeded);
        Assert.True(isolatedResult.Succeeded);
        Assert.Equal(
            Path.GetFullPath(isolatedPath),
            isolatedResult.Value!.FullPath);
    }

    [Fact]
    public void SymbolicLinkOrReparseTraversal_IsRejectedWhenSupported()
    {
        var target = Path.Combine(_testRoot, "link-target");
        var link = Path.Combine(_testRoot, "link");
        Directory.CreateDirectory(target);

        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or PlatformNotSupportedException)
        {
            return;
        }

        var resolver = CreateResolver(
            databasePath: Path.Combine(link, "wealthledger.db"));

        var result = resolver.ResolveDatabasePath();

        Assert.False(result.Succeeded);
        Assert.Equal(LocalDataFailureCategory.UnsafePath, result.Failure!.Category);

        Directory.Delete(link);
    }

    [Fact]
    public void OwnershipLockName_IsAdjacentAndStable()
    {
        var databasePath = Path.Combine(
            _testRoot,
            "live",
            "wealthledger.db");
        var resolver = CreateResolver(databasePath: databasePath);

        var lockPath = resolver.GetOwnershipLockPath(databasePath);

        Assert.Equal(
            Path.Combine(_testRoot, "live", "wealthledger.wloperation.lock"),
            lockPath);
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

    private LocalDataPathResolver CreateResolver(
        string? databasePath = null,
        string? backupDirectory = null,
        string? currentDirectory = null)
    {
        var values = new Dictionary<string, string?>();

        if (databasePath is not null)
        {
            values[LocalDataPathResolver.DatabasePathConfigurationKey] =
                databasePath;
        }

        if (backupDirectory is not null)
        {
            values[LocalDataPathResolver.BackupDirectoryConfigurationKey] =
                backupDirectory;
        }

        var environment = new LocalDataPathEnvironment(
            "Testing",
            _repositoryRoot,
            AppContext.BaseDirectory,
            currentDirectory ?? _repositoryRoot,
            _localApplicationDataPath,
            _userProfilePath);

        return new LocalDataPathResolver(values, environment);
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
