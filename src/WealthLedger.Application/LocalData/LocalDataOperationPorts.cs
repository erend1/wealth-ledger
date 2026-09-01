namespace WealthLedger.Application.LocalData;

public interface ILocalDataStatusReader
{
    Task<LocalDataOperationResult<LocalDataStatus>> ReadAsync(
        CancellationToken cancellationToken = default);
}

public interface ILocalDatabaseInitializer
{
    Task<LocalDataOperationResult<LocalDatabaseInitialization>> InitializeAsync(
        CancellationToken cancellationToken = default);
}

public interface ILocalDatabaseMigrationSessionFactory
{
    Task<LocalDataOperationResult<ILocalDatabaseMigrationSession>> OpenAsync(
        CancellationToken cancellationToken = default);
}

public interface ILocalDatabaseMigrationSession : IAsyncDisposable
{
    Task<LocalDataOperationResult<LocalDatabaseMigrationPlan>> InspectAsync(
        CancellationToken cancellationToken = default);

    Task<LocalDataOperationResult<LocalBackupCreation>>
        CreateVerifiedPreMigrationBackupAsync(
            CancellationToken cancellationToken = default);

    Task<LocalDataOperationResult<LocalDatabaseMigration>> ApplyAsync(
        LocalDatabaseMigrationPlan plan,
        LocalBackupCreation verifiedPreMigrationBackup,
        CancellationToken cancellationToken = default);
}

public interface ILocalBackupCreator
{
    Task<LocalDataOperationResult<LocalBackupCreation>> CreateAsync(
        CancellationToken cancellationToken = default);
}

public interface ILocalBackupVerifier
{
    Task<LocalDataOperationResult<LocalBackupVerification>> VerifyAsync(
        string backupFilePath,
        CancellationToken cancellationToken = default);
}

public interface ILocalRestoreStager
{
    Task<LocalDataOperationResult<LocalRestoreStage>> StageAsync(
        string backupFilePath,
        string targetDatabasePath,
        CancellationToken cancellationToken = default);
}

public interface ILocalDatabaseReplacementSessionFactory
{
    Task<LocalDataOperationResult<ILocalDatabaseReplacementSession>> OpenAsync(
        string backupFilePath,
        CancellationToken cancellationToken = default);
}

public interface ILocalDatabaseReplacementSession : IAsyncDisposable
{
    Task<LocalDataOperationResult<LocalRestoreStage>> StageCandidateAsync(
        CancellationToken cancellationToken = default);

    Task<LocalDataOperationResult<LocalBackupCreation>>
        CreateVerifiedPreRestoreBackupAsync(
            CancellationToken cancellationToken = default);

    Task<LocalDataOperationResult<LocalDatabaseReplacement>> PromoteAsync(
        LocalRestoreStage verifiedStage,
        LocalBackupCreation verifiedPreRestoreBackup,
        CancellationToken cancellationToken = default);
}
