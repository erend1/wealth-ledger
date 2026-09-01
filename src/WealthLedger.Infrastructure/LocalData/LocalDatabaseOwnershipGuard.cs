using WealthLedger.Application.LocalData;

namespace WealthLedger.Infrastructure.LocalData;

internal sealed class LocalDatabaseOwnershipGuard
{
    private readonly LocalDataPathResolver _pathResolver;

    internal LocalDatabaseOwnershipGuard(LocalDataPathResolver pathResolver)
    {
        _pathResolver = pathResolver
            ?? throw new ArgumentNullException(nameof(pathResolver));
    }

    internal LocalDataOperationResult<LocalDatabaseOwnershipLease> Acquire(
        string databasePath,
        bool createDirectory)
    {
        var lockPath = _pathResolver.GetOwnershipLockPath(databasePath);
        var directoryPath = Path.GetDirectoryName(lockPath);

        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return LocalDataOperationResult<LocalDatabaseOwnershipLease>.Failed(
                LocalDataFailureCategory.UnsafePath,
                "The database ownership directory could not be resolved.");
        }

        try
        {
            if (!Directory.Exists(directoryPath))
            {
                if (!createDirectory)
                {
                    return LocalDataOperationResult<LocalDatabaseOwnershipLease>
                        .Failed(
                            LocalDataFailureCategory.NotFound,
                            "The local data directory does not exist.");
                }

                Directory.CreateDirectory(directoryPath);
            }

            var stream = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough);

            return LocalDataOperationResult<LocalDatabaseOwnershipLease>.Success(
                new LocalDatabaseOwnershipLease(stream, lockPath));
        }
        catch (IOException exception) when (IsOwnershipCollision(exception))
        {
            return LocalDataOperationResult<LocalDatabaseOwnershipLease>.Failed(
                LocalDataFailureCategory.OwnershipBusy,
                "Another process currently owns the authoritative database.");
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or System.Security.SecurityException)
        {
            return LocalDataOperationResult<LocalDatabaseOwnershipLease>.Failed(
                LocalDataFailureCategory.IoFailure,
                "The database ownership guard could not be acquired.");
        }
    }

    internal bool IsAvailable(string databasePath)
    {
        var directory = Path.GetDirectoryName(
            _pathResolver.GetOwnershipLockPath(databasePath));

        if (string.IsNullOrWhiteSpace(directory)
            || !Directory.Exists(directory))
        {
            return true;
        }

        var result = Acquire(databasePath, createDirectory: false);

        if (!result.Succeeded)
        {
            return false;
        }

        result.Value!.Dispose();
        return true;
    }

    private static bool IsOwnershipCollision(IOException exception)
    {
        var nativeCode = exception.HResult & 0xFFFF;

        return nativeCode is 11 or 13 or 32 or 33;
    }
}

internal sealed class LocalDatabaseOwnershipLease
    : IDisposable, IAsyncDisposable
{
    private FileStream? _stream;
    private readonly string _lockPath;

    internal LocalDatabaseOwnershipLease(
        FileStream stream,
        string lockPath)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _lockPath = lockPath
            ?? throw new ArgumentNullException(nameof(lockPath));
    }

    internal string LockPath => _lockPath;

    public void Dispose()
    {
        var stream = Interlocked.Exchange(ref _stream, null);

        if (stream is null)
        {
            return;
        }

        stream.Dispose();

        try
        {
            File.Delete(_lockPath);
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException)
        {
            // A stale unlocked marker is harmless. Ownership is the open handle.
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
