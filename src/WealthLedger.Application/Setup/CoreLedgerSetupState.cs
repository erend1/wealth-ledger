using WealthLedger.Application.LocalData;

namespace WealthLedger.Application.Setup
{
    public enum CoreLedgerSetupState
    {
        Empty,
        Complete,
        PartialOrConflicting
    }

    public sealed record CoreLedgerSetupStateSnapshot(
        CoreLedgerSetupState State);

    public interface ICoreLedgerSetupStateReader
    {
        Task<LocalDataOperationResult<CoreLedgerSetupStateSnapshot>> ReadAsync(
            CancellationToken cancellationToken = default);
    }
}
