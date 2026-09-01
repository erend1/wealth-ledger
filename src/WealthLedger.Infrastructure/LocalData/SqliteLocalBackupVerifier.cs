using WealthLedger.Application.LocalData;

namespace WealthLedger.Infrastructure.LocalData;

internal sealed class SqliteLocalBackupVerifier : ILocalBackupVerifier
{
    private readonly LocalDataPathResolver _pathResolver;
    private readonly LocalBackupPackageReader _packageReader;

    internal SqliteLocalBackupVerifier(
        LocalDataPathResolver pathResolver,
        LocalBackupPackageReader packageReader)
    {
        _pathResolver = pathResolver
            ?? throw new ArgumentNullException(nameof(pathResolver));
        _packageReader = packageReader
            ?? throw new ArgumentNullException(nameof(packageReader));
    }

    public async Task<LocalDataOperationResult<LocalBackupVerification>> VerifyAsync(
        string backupFilePath,
        CancellationToken cancellationToken = default)
    {
        var pathResult = _pathResolver.ValidateBackupFilePath(backupFilePath);

        if (!pathResult.Succeeded)
        {
            return LocalDataResult<LocalBackupVerification>.FromFailure(
                pathResult.Failure!);
        }

        var packageResult = await _packageReader.OpenVerifiedAsync(
            pathResult.Value!.FullPath,
            cancellationToken);

        if (!packageResult.Succeeded)
        {
            return LocalDataResult<LocalBackupVerification>.FromFailure(
                packageResult.Failure!);
        }

        await using var package = packageResult.Value!;
        var manifest = package.Manifest;

        return LocalDataOperationResult<LocalBackupVerification>.Success(
            new LocalBackupVerification(
                package.PackagePath,
                manifest.CreatedAtUtc,
                manifest.VerifiedAtUtc,
                manifest.SnapshotSha256[..12],
                manifest.AppliedMigrations,
                manifest.LatestSchemaVersion,
                package.DatabaseVerification.Compatibility,
                manifest.EncryptionMode));
    }
}
