using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;

namespace WealthLedger.Application.CoreLedger
{
    public sealed record ReversalTargetIdentity(
        Guid TransactionId,
        Guid HouseholdId);

    public sealed record ReversalCandidate(
        Guid TransactionId,
        Guid HouseholdId,
        TransactionStatus Status,
        TransactionType Type,
        Guid? ExistingReversalTransactionId,
        IReadOnlyList<Guid> BlockingTransactionIds,
        LedgerTransaction? Original,
        IReadOnlyCollection<AssetLot> AffectedLots);
}
