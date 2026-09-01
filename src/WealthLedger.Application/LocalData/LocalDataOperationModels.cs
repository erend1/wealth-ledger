namespace WealthLedger.Application.LocalData;

public enum LocalDataFailureCategory
{
    InvalidInputOrConfiguration = 2,
    UnsafePath = 3,
    OwnershipBusy = 4,
    NotFound = 5,
    AlreadyExists = 6,
    InvalidBackup = 7,
    IncompatibleBackup = 8,
    IntegrityFailure = 9,
    IoFailure = 10,
    MigrationFailure = 11,
    RestoreFailure = 12,
    Cancelled = 13,
    DatabaseNotReady = 14
}

public enum LocalDatabaseCompatibility
{
    Uninitialized,
    Compatible,
    MigrationRequired,
    Incompatible
}

public enum LocalDataIntegrityStatus
{
    NotChecked,
    Passed,
    Failed
}

public sealed record LocalDataFailure(
    LocalDataFailureCategory Category,
    string Message);

public sealed record LocalDataOperationResult<T>(
    T? Value,
    LocalDataFailure? Failure)
    where T : class
{
    public bool Succeeded => Failure is null;

    public static LocalDataOperationResult<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new LocalDataOperationResult<T>(value, null);
    }

    public static LocalDataOperationResult<T> Failed(
        LocalDataFailureCategory category,
        string message,
        T? value = null)
        => new(
            value,
            new LocalDataFailure(
                category,
                string.IsNullOrWhiteSpace(message)
                    ? "The local data operation failed."
                    : message));
}

public sealed record LocalBackupSummary(
    string FilePath,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset VerifiedAtUtc,
    string DigestPrefix,
    string LatestMigration,
    string EncryptionMode);

public sealed record LocalDataStatus(
    string DatabasePath,
    string? BackupDirectory,
    string ApplicationVersion,
    bool DatabasePathSafe,
    bool DatabaseExists,
    bool BackupDirectoryConfigured,
    bool BackupDirectoryExists,
    bool OwnershipAvailable,
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyList<string> PendingMigrations,
    LocalDatabaseCompatibility Compatibility,
    LocalDataIntegrityStatus IntegrityStatus,
    LocalBackupSummary? LatestVerifiedBackup,
    bool DestinationSeparationConfirmed,
    bool DestinationEncryptionConfirmed,
    bool RealDataReady,
    string EncryptionMode);

public sealed record LocalDatabaseInitialization(
    string DatabasePath,
    IReadOnlyList<string> AppliedMigrations,
    DateTimeOffset CompletedAtUtc);

public sealed record LocalDatabaseMigrationPlan(
    string DatabasePath,
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyList<string> PendingMigrations);

public sealed record LocalDatabaseMigration(
    string DatabasePath,
    string? StartingMigration,
    string? EndingMigration,
    string? PreMigrationBackupPath,
    bool WasNoOp,
    DateTimeOffset CompletedAtUtc);

public sealed record LocalBackupCreation(
    string FilePath,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset VerifiedAtUtc,
    string DigestPrefix,
    IReadOnlyList<string> AppliedMigrations,
    string LatestMigration,
    string EncryptionMode);

public sealed record LocalBackupVerification(
    string FilePath,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset VerifiedAtUtc,
    string DigestPrefix,
    IReadOnlyList<string> AppliedMigrations,
    string LatestMigration,
    LocalDatabaseCompatibility Compatibility,
    string EncryptionMode);

public sealed record LocalRestoreStage(
    string BackupFilePath,
    string TargetDatabasePath,
    LocalDatabaseCompatibility Compatibility,
    string LatestMigration,
    DateTimeOffset CompletedAtUtc);

public sealed record LocalDatabaseReplacement(
    string DatabasePath,
    string PreRestoreBackupPath,
    string SupersededDatabasePath,
    string LatestMigration,
    DateTimeOffset CompletedAtUtc);
