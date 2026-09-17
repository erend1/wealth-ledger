using WealthLedger.Domain.Common;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Lots
{
    /// <summary>
    /// The exact whole-piece movement beside one physical-gold lot
    /// allocation.
    /// </summary>
    /// <remarks>
    /// Gross weight and pieces are independent evidence. Neither value is
    /// inferred from the other, and current pieces remain a derived sum.
    /// </remarks>
    public sealed class PhysicalGoldLotAllocationDetail
    {
        public Guid LotEntryAllocationId { get; }

        public int PieceDelta { get; }

        internal PhysicalGoldLotAllocationDetail(
            Guid lotEntryAllocationId,
            QuantityDelta quantityDelta,
            int pieceDelta)
        {
            if (lotEntryAllocationId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Allocation ID cannot be empty.",
                    nameof(lotEntryAllocationId));
            }

            if (pieceDelta == 0)
            {
                throw new DomainRuleViolationException(
                    "Physical-gold piece movement cannot be zero.");
            }

            if (pieceDelta == int.MinValue)
            {
                throw new DomainRuleViolationException(
                    "Physical-gold piece movement exceeds the reversible range.");
            }

            var signsMatch =
                quantityDelta.IsPositive && pieceDelta > 0
                || quantityDelta.IsNegative && pieceDelta < 0;

            if (!signsMatch)
            {
                throw new DomainRuleViolationException(
                    "Physical-gold piece movement sign must match the gross-quantity movement sign.");
            }

            LotEntryAllocationId = lotEntryAllocationId;
            PieceDelta = pieceDelta;
        }

        public int Negate()
            => checked(-PieceDelta);
    }
}
