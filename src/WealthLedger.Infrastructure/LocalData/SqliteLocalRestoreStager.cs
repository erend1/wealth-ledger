using WealthLedger.Application.LocalData;

namespace WealthLedger.Infrastructure.LocalData;

internal sealed class SqliteLocalRestoreStager : ILocalRestoreStager
{
    private readonly LocalDataPathResolver _pathResolver;
    private readonly SqliteRestoreService _restoreService;

    internal SqliteLocalRestoreStager(
        LocalDataPathResolver pathResolver,
        SqliteRestoreService restoreService)
    {
        _pathResolver = pathResolver
            ?? throw new ArgumentNullException(nameof(pathResolver));
        _restoreService = restoreService
            ?? throw new ArgumentNullException(nameof(restoreService));
    }

    public async Task<LocalDataOperationResult<LocalRestoreStage>> StageAsync(
        string backupFilePath,
        string targetDatabasePath,
        CancellationToken cancellationToken = default)
    {
        var databaseResult = _pathResolver.ResolveDatabasePath();

        if (!databaseResult.Succeeded)
        {
            return LocalDataResult<LocalRestoreStage>.FromFailure(
                databaseResult.Failure!);
        }

        var databasePath = databaseResult.Value!.FullPath;
        var backupDirectoryResult =
            _pathResolver.ResolveBackupDirectory(databasePath);

        if (!backupDirectoryResult.Succeeded)
        {
            return LocalDataResult<LocalRestoreStage>.FromFailure(
                backupDirectoryResult.Failure!);
        }

        var packagePathResult =
            _pathResolver.ValidateBackupFilePath(backupFilePath);

        if (!packagePathResult.Succeeded)
        {
            return LocalDataResult<LocalRestoreStage>.FromFailure(
                packagePathResult.Failure!);
        }

        var targetPathResult = _pathResolver.ValidateRestoreTargetPath(
            targetDatabasePath,
            databasePath,
            backupDirectoryResult.Value!.FullPath);

        if (!targetPathResult.Succeeded)
        {
            return LocalDataResult<LocalRestoreStage>.FromFailure(
                targetPathResult.Failure!);
        }

        var packagePath = packagePathResult.Value!.FullPath;
        var targetPath = targetPathResult.Value!.FullPath;

        if (_pathResolver.PathEquals(packagePath, targetPath))
        {
            return LocalDataOperationResult<LocalRestoreStage>.Failed(
                LocalDataFailureCategory.InvalidInputOrConfiguration,
                "The backup file and restore target must be different paths.");
        }

        var targetDirectory = Path.GetDirectoryName(targetPath);

        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            return LocalDataOperationResult<LocalRestoreStage>.Failed(
                LocalDataFailureCategory.UnsafePath,
                "The restore target directory could not be resolved.");
        }

        try
        {
            Directory.CreateDirectory(targetDirectory);
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or System.Security.SecurityException)
        {
            return LocalDataOperationResult<LocalRestoreStage>.Failed(
                LocalDataFailureCategory.IoFailure,
                "The restore target directory could not be created.");
        }

        var repeatedTargetCheck = _pathResolver.ValidateRestoreTargetPath(
            targetPath,
            databasePath,
            backupDirectoryResult.Value.FullPath);

        if (!repeatedTargetCheck.Succeeded
            || !_pathResolver.PathEquals(
                repeatedTargetCheck.Value!.FullPath,
                targetPath))
        {
            return LocalDataOperationResult<LocalRestoreStage>.Failed(
                LocalDataFailureCategory.UnsafePath,
                "Restore target safety changed before staging began.");
        }

        var result = await _restoreService.CreateVerifiedDatabaseAsync(
            packagePath,
            targetPath,
            requireCurrentSchema: false,
            cancellationToken);

        return result.Succeeded
            ? LocalDataOperationResult<LocalRestoreStage>.Success(
                result.Value!.Stage)
            : LocalDataResult<LocalRestoreStage>.FromFailure(
                result.Failure!);
    }
}
