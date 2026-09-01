using WealthLedger.Application.LocalData;

namespace WealthLedger.Infrastructure.LocalData;

internal sealed class SqliteLocalBackupCreator : ILocalBackupCreator
{
    private readonly LocalDataPathResolver _pathResolver;
    private readonly LocalDatabaseOwnershipGuard _ownershipGuard;
    private readonly SqliteBackupService _backupService;

    internal SqliteLocalBackupCreator(
        LocalDataPathResolver pathResolver,
        LocalDatabaseOwnershipGuard ownershipGuard,
        SqliteBackupService backupService)
    {
        _pathResolver = pathResolver
            ?? throw new ArgumentNullException(nameof(pathResolver));
        _ownershipGuard = ownershipGuard
            ?? throw new ArgumentNullException(nameof(ownershipGuard));
        _backupService = backupService
            ?? throw new ArgumentNullException(nameof(backupService));
    }

    public async Task<LocalDataOperationResult<LocalBackupCreation>> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        var databaseResult = _pathResolver.ResolveDatabasePath();

        if (!databaseResult.Succeeded)
        {
            return LocalDataResult<LocalBackupCreation>.FromFailure(
                databaseResult.Failure!);
        }

        var databasePath = databaseResult.Value!.FullPath;

        if (!File.Exists(databasePath))
        {
            return LocalDataOperationResult<LocalBackupCreation>.Failed(
                LocalDataFailureCategory.NotFound,
                "The database does not exist. Run database initialize first.");
        }

        var backupResult = _pathResolver.ResolveBackupDirectory(databasePath);

        if (!backupResult.Succeeded)
        {
            return LocalDataResult<LocalBackupCreation>.FromFailure(
                backupResult.Failure!);
        }

        var ownershipResult = _ownershipGuard.Acquire(
            databasePath,
            createDirectory: false);

        if (!ownershipResult.Succeeded)
        {
            return LocalDataResult<LocalBackupCreation>.FromFailure(
                ownershipResult.Failure!);
        }

        await using var ownership = ownershipResult.Value!;

        return await _backupService.CreateVerifiedBackupAsync(
            databasePath,
            backupResult.Value!.FullPath,
            LocalBackupPurpose.Manual,
            allowMigrationRequired: false,
            cancellationToken);
    }
}
