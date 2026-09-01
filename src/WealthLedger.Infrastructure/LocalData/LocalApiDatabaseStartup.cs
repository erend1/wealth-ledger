using WealthLedger.Application.LocalData;

namespace WealthLedger.Infrastructure.LocalData;

public sealed record LocalApiDatabaseStartupState(
    string DatabasePath,
    IReadOnlyList<string> AppliedMigrations);

public interface ILocalApiDatabaseStartup
{
    Task<LocalDataOperationResult<LocalApiDatabaseStartupState>> StartAsync(
        CancellationToken cancellationToken = default);
}

internal sealed class LocalApiDatabaseStartup
    : ILocalApiDatabaseStartup, IDisposable
{
    private readonly LocalDataPathResolver _pathResolver;
    private readonly LocalDatabaseOwnershipGuard _ownershipGuard;
    private readonly SqliteDatabaseVerifier _verifier;
    private LocalDatabaseOwnershipLease? _ownership;

    internal LocalApiDatabaseStartup(
        LocalDataPathResolver pathResolver,
        LocalDatabaseOwnershipGuard ownershipGuard,
        SqliteDatabaseVerifier verifier)
    {
        _pathResolver = pathResolver
            ?? throw new ArgumentNullException(nameof(pathResolver));
        _ownershipGuard = ownershipGuard
            ?? throw new ArgumentNullException(nameof(ownershipGuard));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
    }

    public async Task<LocalDataOperationResult<LocalApiDatabaseStartupState>>
        StartAsync(CancellationToken cancellationToken = default)
    {
        if (_ownership is not null)
        {
            return LocalDataOperationResult<LocalApiDatabaseStartupState>.Failed(
                LocalDataFailureCategory.OwnershipBusy,
                "This process already owns the authoritative database.");
        }

        var pathResult = _pathResolver.ResolveDatabasePath();

        if (!pathResult.Succeeded)
        {
            return LocalDataResult<LocalApiDatabaseStartupState>.FromFailure(
                pathResult.Failure!);
        }

        var databasePath = pathResult.Value!.FullPath;

        if (!File.Exists(databasePath))
        {
            return LocalDataOperationResult<LocalApiDatabaseStartupState>.Failed(
                LocalDataFailureCategory.NotFound,
                "The database is missing. Run database initialize before starting the API.");
        }

        var ownershipResult = _ownershipGuard.Acquire(
            databasePath,
            createDirectory: false);

        if (!ownershipResult.Succeeded)
        {
            return LocalDataResult<LocalApiDatabaseStartupState>.FromFailure(
                ownershipResult.Failure!);
        }

        _ownership = ownershipResult.Value!;

        try
        {
            var verificationResult = await _verifier.VerifyAsync(
                databasePath,
                cancellationToken);

            if (!verificationResult.Succeeded)
            {
                Dispose();
                return LocalDataResult<LocalApiDatabaseStartupState>.FromFailure(
                    verificationResult.Failure!);
            }

            var verification = verificationResult.Value!;

            if (verification.Compatibility
                == LocalDatabaseCompatibility.MigrationRequired)
            {
                Dispose();
                return LocalDataOperationResult<LocalApiDatabaseStartupState>
                    .Failed(
                        LocalDataFailureCategory.DatabaseNotReady,
                        "The database requires the explicit database migrate command.");
            }

            if (verification.Compatibility
                != LocalDatabaseCompatibility.Compatible)
            {
                Dispose();
                return LocalDataOperationResult<LocalApiDatabaseStartupState>
                    .Failed(
                        LocalDataFailureCategory.DatabaseNotReady,
                        "The database schema is incompatible with this application.");
            }

            return LocalDataOperationResult<LocalApiDatabaseStartupState>.Success(
                new LocalApiDatabaseStartupState(
                    databasePath,
                    verification.AppliedMigrations));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        var ownership = Interlocked.Exchange(ref _ownership, null);
        ownership?.Dispose();
    }
}
