namespace WealthLedger.Infrastructure.LocalData;

internal enum LocalDataOperationCheckpoint
{
    BeforeInitializePublish,
    BeforeBackupPublish,
    BeforeMigrationApply,
    AfterMigrationApply,
    BeforeRestoreStagePublish,
    AfterRestoreStagePublish,
    BeforeRestorePromotion,
    AfterRestorePromotion
}

internal interface ILocalDataOperationHooks
{
    ValueTask OnCheckpointAsync(
        LocalDataOperationCheckpoint checkpoint,
        string primaryPath,
        string? secondaryPath,
        CancellationToken cancellationToken);
}

internal sealed class NoOpLocalDataOperationHooks : ILocalDataOperationHooks
{
    internal static readonly NoOpLocalDataOperationHooks Instance = new();

    private NoOpLocalDataOperationHooks()
    {
    }

    public ValueTask OnCheckpointAsync(
        LocalDataOperationCheckpoint checkpoint,
        string primaryPath,
        string? secondaryPath,
        CancellationToken cancellationToken)
        => ValueTask.CompletedTask;
}
