namespace WealthLedger.Application.LocalData;

public sealed class GetLocalDataStatusUseCase
{
    private readonly ILocalDataStatusReader _reader;

    public GetLocalDataStatusUseCase(ILocalDataStatusReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public Task<LocalDataOperationResult<LocalDataStatus>> ExecuteAsync(
        CancellationToken cancellationToken = default)
        => _reader.ReadAsync(cancellationToken);
}

public sealed class InitializeLocalDatabaseUseCase
{
    private readonly ILocalDatabaseInitializer _initializer;

    public InitializeLocalDatabaseUseCase(ILocalDatabaseInitializer initializer)
    {
        _initializer = initializer
            ?? throw new ArgumentNullException(nameof(initializer));
    }

    public Task<LocalDataOperationResult<LocalDatabaseInitialization>> ExecuteAsync(
        CancellationToken cancellationToken = default)
        => _initializer.InitializeAsync(cancellationToken);
}

public sealed class MigrateLocalDatabaseUseCase
{
    private readonly ILocalDatabaseMigrationSessionFactory _sessionFactory;
    private readonly TimeProvider _timeProvider;

    public MigrateLocalDatabaseUseCase(
        ILocalDatabaseMigrationSessionFactory sessionFactory,
        TimeProvider timeProvider)
    {
        _sessionFactory = sessionFactory
            ?? throw new ArgumentNullException(nameof(sessionFactory));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<LocalDataOperationResult<LocalDatabaseMigration>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionResult = await _sessionFactory.OpenAsync(
                cancellationToken);

            if (!sessionResult.Succeeded)
            {
                return LocalDataResultConversion.FailureFrom<LocalDatabaseMigration>(
                    sessionResult.Failure!);
            }

            await using var session = sessionResult.Value!;

            var planResult = await session.InspectAsync(cancellationToken);

            if (!planResult.Succeeded)
            {
                return LocalDataResultConversion.FailureFrom<LocalDatabaseMigration>(
                    planResult.Failure!);
            }

            var plan = planResult.Value!;
            var startingMigration = plan.AppliedMigrations.LastOrDefault();

            if (plan.PendingMigrations.Count == 0)
            {
                return LocalDataOperationResult<LocalDatabaseMigration>.Success(
                    new LocalDatabaseMigration(
                        plan.DatabasePath,
                        startingMigration,
                        startingMigration,
                        PreMigrationBackupPath: null,
                        WasNoOp: true,
                        _timeProvider.GetUtcNow()));
            }

            var backupResult =
                await session.CreateVerifiedPreMigrationBackupAsync(
                    cancellationToken);

            if (!backupResult.Succeeded)
            {
                return LocalDataResultConversion.FailureFrom<LocalDatabaseMigration>(
                    backupResult.Failure!);
            }

            return await session.ApplyAsync(
                plan,
                backupResult.Value!,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return LocalDataOperationResult<LocalDatabaseMigration>.Failed(
                LocalDataFailureCategory.Cancelled,
                "Database migration was cancelled before completion.");
        }
    }
}

public sealed class CreateLocalBackupUseCase
{
    private readonly ILocalBackupCreator _creator;

    public CreateLocalBackupUseCase(ILocalBackupCreator creator)
    {
        _creator = creator ?? throw new ArgumentNullException(nameof(creator));
    }

    public Task<LocalDataOperationResult<LocalBackupCreation>> ExecuteAsync(
        CancellationToken cancellationToken = default)
        => _creator.CreateAsync(cancellationToken);
}

public sealed class VerifyLocalBackupUseCase
{
    private readonly ILocalBackupVerifier _verifier;

    public VerifyLocalBackupUseCase(ILocalBackupVerifier verifier)
    {
        _verifier = verifier
            ?? throw new ArgumentNullException(nameof(verifier));
    }

    public Task<LocalDataOperationResult<LocalBackupVerification>> ExecuteAsync(
        string backupFilePath,
        CancellationToken cancellationToken = default)
    {
        var failure = LocalDataPathInput.ValidateAbsoluteFile(
            backupFilePath,
            "Backup file");

        return failure is null
            ? _verifier.VerifyAsync(backupFilePath, cancellationToken)
            : Task.FromResult(
                LocalDataOperationResult<LocalBackupVerification>.Failed(
                    failure.Category,
                    failure.Message));
    }
}

public sealed class StageLocalRestoreUseCase
{
    private readonly ILocalRestoreStager _stager;

    public StageLocalRestoreUseCase(ILocalRestoreStager stager)
    {
        _stager = stager ?? throw new ArgumentNullException(nameof(stager));
    }

    public Task<LocalDataOperationResult<LocalRestoreStage>> ExecuteAsync(
        string backupFilePath,
        string targetDatabasePath,
        CancellationToken cancellationToken = default)
    {
        var backupFailure = LocalDataPathInput.ValidateAbsoluteFile(
            backupFilePath,
            "Backup file");

        if (backupFailure is not null)
        {
            return Task.FromResult(
                LocalDataOperationResult<LocalRestoreStage>.Failed(
                    backupFailure.Category,
                    backupFailure.Message));
        }

        var targetFailure = LocalDataPathInput.ValidateAbsoluteFile(
            targetDatabasePath,
            "Restore target");

        if (targetFailure is not null)
        {
            return Task.FromResult(
                LocalDataOperationResult<LocalRestoreStage>.Failed(
                    targetFailure.Category,
                    targetFailure.Message));
        }

        if (string.Equals(
                Path.GetFullPath(backupFilePath),
                Path.GetFullPath(targetDatabasePath),
                LocalDataPathInput.PathComparison))
        {
            return Task.FromResult(
                LocalDataOperationResult<LocalRestoreStage>.Failed(
                    LocalDataFailureCategory.InvalidInputOrConfiguration,
                    "The backup file and restore target must be different paths."));
        }

        return _stager.StageAsync(
            backupFilePath,
            targetDatabasePath,
            cancellationToken);
    }
}

public sealed class ReplaceLocalDatabaseUseCase
{
    private readonly ILocalDatabaseReplacementSessionFactory _sessionFactory;

    public ReplaceLocalDatabaseUseCase(
        ILocalDatabaseReplacementSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory
            ?? throw new ArgumentNullException(nameof(sessionFactory));
    }

    public async Task<LocalDataOperationResult<LocalDatabaseReplacement>> ExecuteAsync(
        string backupFilePath,
        bool confirmReplaceActive,
        CancellationToken cancellationToken = default)
    {
        if (!confirmReplaceActive)
        {
            return LocalDataOperationResult<LocalDatabaseReplacement>.Failed(
                LocalDataFailureCategory.InvalidInputOrConfiguration,
                "Active replacement requires --confirm-replace-active.");
        }

        var failure = LocalDataPathInput.ValidateAbsoluteFile(
            backupFilePath,
            "Backup file");

        if (failure is not null)
        {
            return LocalDataOperationResult<LocalDatabaseReplacement>.Failed(
                failure.Category,
                failure.Message);
        }

        try
        {
            var sessionResult = await _sessionFactory.OpenAsync(
                backupFilePath,
                cancellationToken);

            if (!sessionResult.Succeeded)
            {
                return LocalDataResultConversion.FailureFrom<LocalDatabaseReplacement>(
                    sessionResult.Failure!);
            }

            await using var session = sessionResult.Value!;

            var stageResult = await session.StageCandidateAsync(
                cancellationToken);

            if (!stageResult.Succeeded)
            {
                return LocalDataResultConversion.FailureFrom<LocalDatabaseReplacement>(
                    stageResult.Failure!);
            }

            var backupResult =
                await session.CreateVerifiedPreRestoreBackupAsync(
                    cancellationToken);

            if (!backupResult.Succeeded)
            {
                return LocalDataResultConversion.FailureFrom<LocalDatabaseReplacement>(
                    backupResult.Failure!);
            }

            return await session.PromoteAsync(
                stageResult.Value!,
                backupResult.Value!,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return LocalDataOperationResult<LocalDatabaseReplacement>.Failed(
                LocalDataFailureCategory.Cancelled,
                "Active database replacement was cancelled before completion.");
        }
    }
}

internal static class LocalDataResultConversion
{
    internal static LocalDataOperationResult<T> FailureFrom<T>(
        LocalDataFailure failure)
        where T : class
        => LocalDataOperationResult<T>.Failed(
            failure.Category,
            failure.Message);
}

internal static class LocalDataPathInput
{
    internal static StringComparison PathComparison
        => OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    internal static LocalDataFailure? ValidateAbsoluteFile(
        string? path,
        string label)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new LocalDataFailure(
                LocalDataFailureCategory.InvalidInputOrConfiguration,
                $"{label} is required.");
        }

        if (!Path.IsPathFullyQualified(path))
        {
            return new LocalDataFailure(
                LocalDataFailureCategory.InvalidInputOrConfiguration,
                $"{label} must be an absolute path.");
        }

        try
        {
            _ = Path.GetFullPath(path);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                  or NotSupportedException
                  or PathTooLongException)
        {
            return new LocalDataFailure(
                LocalDataFailureCategory.InvalidInputOrConfiguration,
                $"{label} is not a valid absolute path.");
        }

        return null;
    }
}
