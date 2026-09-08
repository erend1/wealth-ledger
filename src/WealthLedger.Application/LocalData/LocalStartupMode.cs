using WealthLedger.Application.Setup;

namespace WealthLedger.Application.LocalData
{
    public enum LocalStartupMode
    {
        Blocked,
        StorageUninitialized,
        WorkspaceUninitialized,
        InitialBackupRequired,
        Ready
    }

    public sealed record LocalStartupSelection(
        LocalStartupMode Mode,
        LocalDataStatus? Status,
        LocalDataFailure? Failure,
        CoreLedgerSetupState? WorkspaceState);
}
