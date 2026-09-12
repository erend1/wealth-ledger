using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Lots
{
    /// <summary>
    /// One acquisition lot together with the exact quantity currently derived
    /// for a selected household, portfolio, account and asset.
    /// </summary>
    /// <remarks>
    /// <see cref="AssetLot"/> is acquisition lineage and deliberately carries
    /// no account or portfolio. Its <see cref="AssetLot.CurrentQuantity"/> is
    /// therefore a household-wide figure and must never be used as custody
    /// availability: doing so would let a sale in one account consume units
    /// actually held in another.
    ///
    /// This candidate keeps lineage and scope side by side without adding
    /// custody to the lot itself. <see cref="AvailableQuantity"/> is derived
    /// from signed allocations whose transaction entries fall inside the
    /// selected scope, and is never stored.
    /// </remarks>
    public sealed record ScopedLotCandidate(
        Guid AssetLotId,
        Guid AssetId,
        DateOnly? AcquiredOn,
        DateTimeOffset CreatedAtUtc,
        Quantity AvailableQuantity,
        CostBasis CostBasis)
    {
        /// <summary>
        /// True when the acquisition date is not recorded, which happens for
        /// lots established by an opening-balance cutover.
        /// </summary>
        public bool HasUnknownAcquisitionDate => AcquiredOn is null;
    }
}
