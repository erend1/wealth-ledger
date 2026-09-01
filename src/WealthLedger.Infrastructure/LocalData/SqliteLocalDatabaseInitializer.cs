using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.LocalData;

namespace WealthLedger.Infrastructure.LocalData;

internal sealed class SqliteLocalDatabaseInitializer
    : ILocalDatabaseInitializer
{
    private readonly LocalDataPathResolver _pathResolver;
    private readonly LocalDatabaseOwnershipGuard _ownershipGuard;
    private readonly SqliteDatabaseVerifier _verifier;
    private readonly TimeProvider _timeProvider;
    private readonly ILocalDataOperationHooks _hooks;

    internal SqliteLocalDatabaseInitializer(
        LocalDataPathResolver pathResolver,
        LocalDatabaseOwnershipGuard ownershipGuard,
        SqliteDatabaseVerifier verifier,
        TimeProvider timeProvider,
        ILocalDataOperationHooks? hooks = null)
    {
        _pathResolver = pathResolver
            ?? throw new ArgumentNullException(nameof(pathResolver));
        _ownershipGuard = ownershipGuard
            ?? throw new ArgumentNullException(nameof(ownershipGuard));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        _hooks = hooks ?? NoOpLocalDataOperationHooks.Instance;
    }

    public async Task<LocalDataOperationResult<LocalDatabaseInitialization>>
        InitializeAsync(CancellationToken cancellationToken = default)
    {
        var pathResult = _pathResolver.ResolveDatabasePath();

        if (!pathResult.Succeeded)
        {
            return LocalDataResult<LocalDatabaseInitialization>.FromFailure(
                pathResult.Failure!);
        }

        var databasePath = pathResult.Value!.FullPath;
        var ownershipResult = _ownershipGuard.Acquire(
            databasePath,
            createDirectory: true);

        if (!ownershipResult.Succeeded)
        {
            return LocalDataResult<LocalDatabaseInitialization>.FromFailure(
                ownershipResult.Failure!);
        }

        await using var ownership = ownershipResult.Value!;
        string? stagedPath = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (LocalDatabaseFiles.AnyDatabaseArtifactExists(databasePath))
            {
                return LocalDataOperationResult<LocalDatabaseInitialization>
                    .Failed(
                        LocalDataFailureCategory.AlreadyExists,
                        "The database destination already contains database artifacts.");
            }

            stagedPath = LocalDatabaseFiles.CreateUniqueSiblingPath(
                databasePath,
                ".wlrestore");

            await using (var context =
                         SqliteLocalDataConnectionFactory.CreateContext(
                             stagedPath,
                             SqliteOpenMode.ReadWriteCreate))
            {
                await context.Database.MigrateAsync(cancellationToken);
            }

            var stagedVerification = await _verifier.VerifyAsync(
                stagedPath,
                cancellationToken);

            if (!stagedVerification.Succeeded)
            {
                return LocalDataResult<LocalDatabaseInitialization>.FromFailure(
                    stagedVerification.Failure!);
            }

            if (stagedVerification.Value!.Compatibility
                != LocalDatabaseCompatibility.Compatible)
            {
                return LocalDataOperationResult<LocalDatabaseInitialization>
                    .Failed(
                        LocalDataFailureCategory.DatabaseNotReady,
                        "The initialized database is not compatible with this application.");
            }

            await _hooks.OnCheckpointAsync(
                LocalDataOperationCheckpoint.BeforeInitializePublish,
                stagedPath,
                databasePath,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            File.Move(stagedPath, databasePath, overwrite: false);

            var publishedVerification = await _verifier.VerifyAsync(
                databasePath,
                CancellationToken.None);

            if (!publishedVerification.Succeeded
                || publishedVerification.Value!.Compatibility
                    != LocalDatabaseCompatibility.Compatible)
            {
                LocalDatabaseFiles.DeleteDatabaseArtifacts(databasePath);

                return LocalDataOperationResult<LocalDatabaseInitialization>
                    .Failed(
                        LocalDataFailureCategory.IntegrityFailure,
                        "The published database failed restart verification.");
            }

            return LocalDataOperationResult<LocalDatabaseInitialization>.Success(
                new LocalDatabaseInitialization(
                    databasePath,
                    publishedVerification.Value.AppliedMigrations,
                    _timeProvider.GetUtcNow()));
        }
        catch (OperationCanceledException)
        {
            return LocalDataOperationResult<LocalDatabaseInitialization>.Failed(
                LocalDataFailureCategory.Cancelled,
                "Database initialization was cancelled before publication.");
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or System.Security.SecurityException)
        {
            return LocalDataOperationResult<LocalDatabaseInitialization>.Failed(
                LocalDataFailureCategory.IoFailure,
                "Database initialization could not complete its filesystem operation.");
        }
        catch (Exception exception)
            when (exception is SqliteException
                  or InvalidOperationException)
        {
            return LocalDataOperationResult<LocalDatabaseInitialization>.Failed(
                LocalDataFailureCategory.MigrationFailure,
                "Database initialization could not apply the accepted migration chain.");
        }
        finally
        {
            if (stagedPath is not null && File.Exists(stagedPath))
            {
                LocalDatabaseFiles.DeleteDatabaseArtifacts(stagedPath);
            }
        }
    }
}

internal static class LocalDatabaseFiles
{
    private static readonly string[] CompanionSuffixes =
    [
        string.Empty,
        "-journal",
        "-wal",
        "-shm"
    ];

    internal static bool AnyDatabaseArtifactExists(string databasePath)
        => CompanionSuffixes.Any(
            suffix => File.Exists(databasePath + suffix));

    internal static string CreateUniqueSiblingPath(
        string path,
        string extension)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new InvalidOperationException(
                "The database directory could not be resolved.");
        var fileName = Path.GetFileNameWithoutExtension(path);

        return Path.Combine(
            directory,
            $"{fileName}.{Guid.NewGuid():N}{extension}");
    }

    internal static void DeleteDatabaseArtifacts(string databasePath)
    {
        SqliteConnection.ClearAllPools();

        foreach (var suffix in CompanionSuffixes)
        {
            try
            {
                File.Delete(databasePath + suffix);
            }
            catch (Exception exception)
                when (exception is IOException
                      or UnauthorizedAccessException)
            {
                // The caller reports the primary failure. Cleanup is best effort.
            }
        }
    }
}

internal static class LocalDataResult<T>
    where T : class
{
    internal static LocalDataOperationResult<T> FromFailure(
        LocalDataFailure failure)
        => LocalDataOperationResult<T>.Failed(
            failure.Category,
            failure.Message);
}
