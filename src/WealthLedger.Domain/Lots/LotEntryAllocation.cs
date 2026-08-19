using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Lots
{
    public sealed class LotEntryAllocation
    {
        public Guid Id { get; }

        public Guid AssetLotId { get; }

        public Guid TransactionEntryId { get; }

        public QuantityDelta QuantityDelta { get; }

        internal LotEntryAllocation(
            Guid id,
            Guid assetLotId,
            Guid transactionEntryId,
            QuantityDelta quantityDelta)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentException(
                    "Allocation ID cannot be empty.",
                    nameof(id));
            }

            if (assetLotId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Asset lot ID cannot be empty.",
                    nameof(assetLotId));
            }

            if (transactionEntryId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Transaction entry ID cannot be empty.",
                    nameof(transactionEntryId));
            }

            if (quantityDelta.IsZero)
            {
                throw new ArgumentException(
                    "Lot allocation quantity cannot be zero.",
                    nameof(quantityDelta));
            }

            Id = id;
            AssetLotId = assetLotId;
            TransactionEntryId = transactionEntryId;
            QuantityDelta = quantityDelta;
        }
    }
}
