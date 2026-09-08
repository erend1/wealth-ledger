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

/// <summary>
/// Describes whether a verified backup package belongs to the workspace it is
/// being considered as protection for.
/// </summary>
public enum LocalBackupWorkspaceBinding
{
    /// <summary>
    /// The package predates durable workspace lineage, so its origin cannot be
    /// proved. It is never protection for a live database.
    /// </summary>
    Unknown,

    /// <summary>
    /// The package carries the same durable workspace lineage as the live
    /// database.
    /// </summary>
    Matched,

    /// <summary>
    /// The package is internally valid but belongs to a different workspace.
    /// </summary>
    Unrelated
}

public sealed record LocalBackupSummary(
    string FilePath,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset VerifiedAtUtc,
    string DigestPrefix,
    string LatestMigration,
    string EncryptionMode,
    LocalBackupWorkspaceBinding WorkspaceBinding);

/// <summary>
/// Local data readiness for one configured live database.
/// </summary>
/// <remarks>
/// <para>
/// <c>LatestVerifiedBackup</c> is the newest verified package proved to belong
/// to this live database. A package from another workspace never appears here,
/// however recent it is, and it is the readiness signal a guided first run must
/// consume.
/// </para>
/// <para>
/// <c>UnrelatedVerifiedBackupCount</c> counts verified packages in the
/// configured directory that belong to a different workspace or whose lineage
/// cannot be proved. It is reported so an operator with a populated backup
/// directory and no protection is told why rather than being shown an
/// unexplained empty result.
/// </para>
/// <para>
/// <c>LiveWorkspaceId</c> is the durable lineage identity of the live database,
/// absent only for a database that predates the introducing migration.
/// </para>
/// </remarks>
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
    int UnrelatedVerifiedBackupCount,
    string? LiveWorkspaceId,
    bool DestinationSeparationConfirmed,
    bool DestinationEncryptionConfirmed,
    bool LocalProtectionReady,
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
