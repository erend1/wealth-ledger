using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Lots
{
    public sealed record AssetLotAllocationSnapshot(
        Guid Id,
        Guid TransactionEntryId,
        QuantityDelta QuantityDelta);
}
