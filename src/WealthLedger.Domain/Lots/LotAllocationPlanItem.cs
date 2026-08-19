using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Lots
{
    public sealed record LotAllocationPlanItem(
        Guid AssetLotId,
        Quantity Quantity);
}
