using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.CoreLedger
{
    public sealed record ReversalPreviewEntry(
        int Sequence,
        Guid PortfolioId,
        Guid AccountId,
        Guid AssetId,
        QuantityDelta QuantityDelta,
        EntryRole Role,
        UnitPrice? UnitPrice);

    public sealed record ReversalPreviewLotAllocation(
        Guid AssetLotId,
        Guid OriginalTransactionEntryId,
        int EntrySequence,
        QuantityDelta QuantityDelta);

    public sealed record ReversalPreviewResult(
        Guid OriginalTransactionId,
        bool CanReverse,
        ReversalEligibilityCode EligibilityCode,
        Guid? ExistingReversalTransactionId,
        IReadOnlyList<Guid> BlockingTransactionIds,
        IReadOnlyList<ReversalPreviewEntry> InverseEntries,
        IReadOnlyList<ReversalPreviewLotAllocation>
            InverseLotAllocations);
}
