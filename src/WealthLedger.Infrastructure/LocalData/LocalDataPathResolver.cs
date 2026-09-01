using Microsoft.Extensions.Configuration;
using WealthLedger.Application.LocalData;

namespace WealthLedger.Infrastructure.LocalData;

internal sealed class LocalDataPathResolver
{
    internal const string DatabasePathConfigurationKey = "Storage:DatabasePath";
    internal const string BackupDirectoryConfigurationKey = "Backup:Directory";
    internal const string DestinationSeparationConfigurationKey =
        "Backup:DestinationSeparationConfirmed";
    internal const string DestinationEncryptionConfigurationKey =
        "Backup:DestinationEncryptionConfirmed";

    private readonly Func<string, string?> _getConfigurationValue;
    private readonly LocalDataPathEnvironment _environment;
    private readonly IReadOnlyList<string> _protectedRoots;

    internal LocalDataPathResolver(
        IConfiguration configuration,
        LocalDataPathEnvironment environment)
        : this(
            key => configuration?[key],
            environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
    }

    internal LocalDataPathResolver(
        IReadOnlyDictionary<string, string?> configuration,
        LocalDataPathEnvironment environment)
        : this(
            key => configuration.TryGetValue(key, out var value)
                ? value
                : null,
            environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
    }

    private LocalDataPathResolver(
        Func<string, string?> getConfigurationValue,
        LocalDataPathEnvironment environment)
    {
        _getConfigurationValue = getConfigurationValue
            ?? throw new ArgumentNullException(nameof(getConfigurationValue));
        _environment = environment
            ?? throw new ArgumentNullException(nameof(environment));
        _protectedRoots = DiscoverProtectedRoots(environment);
    }

    internal bool DestinationSeparationConfirmed
        => GetConfiguredBoolean(DestinationSeparationConfigurationKey);

    internal bool DestinationEncryptionConfirmed
        => GetConfiguredBoolean(DestinationEncryptionConfigurationKey);

    internal LocalDataOperationResult<ResolvedLocalDataPath> ResolveDatabasePath()
    {
        var configuredValue = _getConfigurationValue(
            DatabasePathConfigurationKey);
        string candidate;

        if (configuredValue is not null)
        {
            if (string.IsNullOrWhiteSpace(configuredValue))
            {
                return InvalidPath(
                    "The configured database path cannot be empty.");
            }

            candidate = configuredValue;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(
                    _environment.LocalApplicationDataPath))
            {
                return InvalidPath(
                    "The local application-data directory could not be resolved.");
            }

            candidate = Path.Combine(
                _environment.LocalApplicationDataPath,
                "WealthLedger",
                "data",
                "wealthledger.db");
        }

        return ValidateFilePath(candidate, "database");
    }

    internal LocalDataOperationResult<ResolvedLocalDataPath>
        ResolveBackupDirectory(string databasePath)
    {
        var configuredValue = _getConfigurationValue(
            BackupDirectoryConfigurationKey);

        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return LocalDataOperationResult<ResolvedLocalDataPath>.Failed(
                LocalDataFailureCategory.InvalidInputOrConfiguration,
                "Backup:Directory must be configured as an absolute directory.");
        }

        var directoryResult = ValidateDirectoryPath(
            configuredValue,
            "backup directory");

        if (!directoryResult.Succeeded)
        {
            return directoryResult;
        }

        var databaseDirectory = Path.GetDirectoryName(
            Path.GetFullPath(databasePath));

        if (databaseDirectory is null)
        {
            return InvalidPath(
                "The database directory could not be resolved.");
        }

        var backupDirectory = directoryResult.Value!.FullPath;

        if (PathsOverlap(backupDirectory, databaseDirectory))
        {
            return InvalidPath(
                "The backup directory must be separate from the live-data directory.");
        }

        return directoryResult;
    }

    internal LocalDataOperationResult<ResolvedLocalDataPath>
        ValidateBackupFilePath(string backupFilePath)
    {
        var result = ValidateFilePath(backupFilePath, "backup file");

        if (!result.Succeeded)
        {
            return result;
        }

        if (!string.Equals(
                Path.GetExtension(result.Value!.FullPath),
                ".wlbackup",
                StringComparison.OrdinalIgnoreCase))
        {
            return LocalDataOperationResult<ResolvedLocalDataPath>.Failed(
                LocalDataFailureCategory.InvalidInputOrConfiguration,
                "The backup file must use the .wlbackup extension.");
        }

        return result;
    }

    internal LocalDataOperationResult<ResolvedLocalDataPath>
        ValidateRestoreTargetPath(
            string targetDatabasePath,
            string databasePath,
            string? backupDirectory)
    {
        var result = ValidateFilePath(
            targetDatabasePath,
            "restore target");

        if (!result.Succeeded)
        {
            return result;
        }

        var target = result.Value!.FullPath;
        var live = Path.GetFullPath(databasePath);

        if (PathEquals(target, live))
        {
            return InvalidPath(
                "Restore staging cannot target the authoritative database path.");
        }

        if (!string.IsNullOrWhiteSpace(backupDirectory)
            && IsWithin(target, Path.GetFullPath(backupDirectory)))
        {
            return InvalidPath(
                "Restore staging must be outside the configured backup directory.");
        }

        return result;
    }

    internal string GetOwnershipLockPath(string databasePath)
        => Path.ChangeExtension(
            Path.GetFullPath(databasePath),
            ".wloperation.lock");

    internal bool PathEquals(string left, string right)
        => string.Equals(
            TrimEndingSeparator(Path.GetFullPath(left)),
            TrimEndingSeparator(Path.GetFullPath(right)),
            PathComparison);

    private LocalDataOperationResult<ResolvedLocalDataPath> ValidateFilePath(
        string candidate,
        string label)
    {
        var normalizedResult = NormalizeAbsolutePath(candidate, label);

        if (!normalizedResult.Succeeded)
        {
            return normalizedResult;
        }

        var path = normalizedResult.Value!.FullPath;

        if (Directory.Exists(path))
        {
            return InvalidPath(
                $"The {label} path identifies a directory, not a file.");
        }

        return ValidateSafeLocation(path, label);
    }

    private LocalDataOperationResult<ResolvedLocalDataPath> ValidateDirectoryPath(
        string candidate,
        string label)
    {
        var normalizedResult = NormalizeAbsolutePath(candidate, label);

        if (!normalizedResult.Succeeded)
        {
            return normalizedResult;
        }

        var path = normalizedResult.Value!.FullPath;

        if (File.Exists(path))
        {
            return InvalidPath(
                $"The {label} path identifies a file, not a directory.");
        }

        if (IsBroadDirectory(path))
        {
            return InvalidPath(
                $"The {label} cannot be a filesystem or user-profile root.");
        }

        return ValidateSafeLocation(path, label);
    }

    private LocalDataOperationResult<ResolvedLocalDataPath> NormalizeAbsolutePath(
        string candidate,
        string label)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return LocalDataOperationResult<ResolvedLocalDataPath>.Failed(
                LocalDataFailureCategory.InvalidInputOrConfiguration,
                $"The {label} path is required.");
        }

        if (!Path.IsPathFullyQualified(candidate))
        {
            return LocalDataOperationResult<ResolvedLocalDataPath>.Failed(
                LocalDataFailureCategory.InvalidInputOrConfiguration,
                $"The {label} path must be absolute.");
        }

        try
        {
            return LocalDataOperationResult<ResolvedLocalDataPath>.Success(
                new ResolvedLocalDataPath(
                    TrimEndingSeparator(Path.GetFullPath(candidate))));
        }
        catch (Exception exception)
            when (exception is ArgumentException
                  or NotSupportedException
                  or PathTooLongException)
        {
            return LocalDataOperationResult<ResolvedLocalDataPath>.Failed(
                LocalDataFailureCategory.InvalidInputOrConfiguration,
                $"The {label} path is invalid.");
        }
    }

    private LocalDataOperationResult<ResolvedLocalDataPath> ValidateSafeLocation(
        string path,
        string label)
    {
        if (_protectedRoots.Any(root => IsWithin(path, root)))
        {
            return InvalidPath(
                $"The {label} must be outside source and application build directories.");
        }

        var reparseResult = HasExistingReparsePoint(path);

        if (!reparseResult.Succeeded)
        {
            return LocalDataOperationResult<ResolvedLocalDataPath>.Failed(
                reparseResult.Failure!.Category,
                reparseResult.Failure.Message);
        }

        if (reparseResult.Value!.HasReparsePoint)
        {
            return InvalidPath(
                $"The {label} cannot traverse a symbolic link or reparse point.");
        }

        return LocalDataOperationResult<ResolvedLocalDataPath>.Success(
            new ResolvedLocalDataPath(path));
    }

    private LocalDataOperationResult<ReparsePointCheck> HasExistingReparsePoint(
        string path)
    {
        try
        {
            string? current = path;

            while (!string.IsNullOrWhiteSpace(current))
            {
                if (File.Exists(current) || Directory.Exists(current))
                {
                    var attributes = File.GetAttributes(current);

                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        return LocalDataOperationResult<ReparsePointCheck>.Success(
                            new ReparsePointCheck(true));
                    }
                }

                var parent = Path.GetDirectoryName(current);

                if (string.IsNullOrWhiteSpace(parent)
                    || PathEquals(parent, current))
                {
                    break;
                }

                current = parent;
            }

            return LocalDataOperationResult<ReparsePointCheck>.Success(
                new ReparsePointCheck(false));
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or System.Security.SecurityException)
        {
            return LocalDataOperationResult<ReparsePointCheck>.Failed(
                LocalDataFailureCategory.UnsafePath,
                "Path safety could not be proven for an existing path component.");
        }
    }

    private bool IsBroadDirectory(string path)
    {
        var normalized = TrimEndingSeparator(path);
        var root = Path.GetPathRoot(normalized);

        if (!string.IsNullOrWhiteSpace(root)
            && PathEquals(normalized, root))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(_environment.UserProfilePath)
               && PathEquals(normalized, _environment.UserProfilePath);
    }

    private bool PathsOverlap(string first, string second)
        => IsWithin(first, second) || IsWithin(second, first);

    private bool IsWithin(string candidate, string root)
    {
        var normalizedCandidate = TrimEndingSeparator(
            Path.GetFullPath(candidate));
        var normalizedRoot = TrimEndingSeparator(
            Path.GetFullPath(root));

        if (string.Equals(
                normalizedCandidate,
                normalizedRoot,
                PathComparison))
        {
            return true;
        }

        return normalizedCandidate.StartsWith(
            normalizedRoot + Path.DirectorySeparatorChar,
            PathComparison);
    }

    private static IReadOnlyList<string> DiscoverProtectedRoots(
        LocalDataPathEnvironment environment)
    {
        var roots = new HashSet<string>(PathComparer);

        AddExistingPath(roots, environment.ContentRootPath);
        AddExistingPath(roots, environment.ApplicationBasePath);

        foreach (var start in new[]
                 {
                     environment.ContentRootPath,
                     environment.ApplicationBasePath,
                     environment.CurrentDirectory
                 })
        {
            var repositoryRoot = FindRepositoryRoot(start);

            if (repositoryRoot is not null)
            {
                roots.Add(repositoryRoot);
            }
        }

        return roots.ToArray();
    }

    private static string? FindRepositoryRoot(string startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return null;
        }

        try
        {
            var current = new DirectoryInfo(Path.GetFullPath(startPath));

            if (!current.Exists)
            {
                current = current.Parent;
            }

            while (current is not null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, ".git"))
                    || File.Exists(Path.Combine(current.FullName, ".git"))
                    || File.Exists(
                        Path.Combine(current.FullName, "WealthLedger.slnx")))
                {
                    return TrimEndingSeparator(current.FullName);
                }

                current = current.Parent;
            }
        }
        catch (Exception exception)
            when (exception is ArgumentException
                  or IOException
                  or UnauthorizedAccessException
                  or NotSupportedException)
        {
            return null;
        }

        return null;
    }

    private static void AddExistingPath(
        ISet<string> roots,
        string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            roots.Add(TrimEndingSeparator(Path.GetFullPath(path)));
        }
    }

    private static LocalDataOperationResult<ResolvedLocalDataPath> InvalidPath(
        string message)
        => LocalDataOperationResult<ResolvedLocalDataPath>.Failed(
            LocalDataFailureCategory.UnsafePath,
            message);

    private bool GetConfiguredBoolean(string key)
        => bool.TryParse(_getConfigurationValue(key), out var value) && value;

    private static string TrimEndingSeparator(string path)
        => Path.TrimEndingDirectorySeparator(path);

    private static StringComparer PathComparer
        => OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}

internal sealed record ResolvedLocalDataPath(string FullPath);

internal sealed record ReparsePointCheck(bool HasReparsePoint);
