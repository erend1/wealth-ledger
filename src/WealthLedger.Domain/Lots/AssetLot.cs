using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Common;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Lots
{
    public sealed class AssetLot
    {
        private readonly List<LotEntryAllocation> _allocations = [];

        public Guid Id { get; }

        public Guid AssetId { get; }

        public Guid OpeningTransactionEntryId { get; }

        public DateOnly? AcquiredOn { get; }

        public CostBasis CostBasis { get; }

        public PhysicalGoldLotDetail? PhysicalGoldDetail { get; }

        public DateTimeOffset CreatedAtUtc { get; }

        public IReadOnlyCollection<LotEntryAllocation> Allocations
            => _allocations;

        public Quantity CurrentQuantity
        {
            get
            {
                long total = 0;

                foreach (var allocation in _allocations)
                {
                    total = checked(
                        total + allocation.QuantityDelta.RawE8);
                }

                if (total < 0)
                {
                    throw new InvalidOperationException(
                        "Asset lot quantity cannot be negative.");
                }

                return Quantity.FromRaw(total);
            }
        }

        public bool IsClosed
            => CurrentQuantity.RawE8 == 0;

        private AssetLot(
            Guid id,
            Asset asset,
            TransactionEntry openingEntry,
            Quantity openingQuantity,
            DateOnly? acquiredOn,
            CostBasis costBasis,
            PhysicalGoldLotDetail? physicalGoldDetail,
            DateTimeOffset createdAtUtc)
        {
            ArgumentNullException.ThrowIfNull(asset);
            ArgumentNullException.ThrowIfNull(openingEntry);
            ArgumentNullException.ThrowIfNull(costBasis);

            if (id == Guid.Empty)
            {
                throw new ArgumentException(
                    "Asset lot ID cannot be empty.",
                    nameof(id));
            }

            if (asset.Id != openingEntry.AssetId)
            {
                throw new DomainRuleViolationException(
                    "Opening transaction entry does not belong to the lot asset.");
            }

            if (asset.LotTrackingMode == LotTrackingMode.None)
            {
                throw new DomainRuleViolationException(
                    "A lot cannot be created for an asset that does not use lot tracking.");
            }

            if (!openingEntry.QuantityDelta.IsPositive)
            {
                throw new DomainRuleViolationException(
                    "A lot must be opened by a positive transaction entry.");
            }

            if (openingQuantity.RawE8 == 0)
            {
                throw new DomainRuleViolationException(
                    "Opening lot quantity must be greater than zero.");
            }

            if (openingQuantity.RawE8
                > openingEntry.QuantityDelta.RawE8)
            {
                throw new DomainRuleViolationException(
                    "Opening lot quantity cannot exceed the transaction entry quantity.");
            }

            if (physicalGoldDetail is not null
                && asset.Type != AssetType.PhysicalGold)
            {
                throw new DomainRuleViolationException(
                    "Physical gold details can be attached only to physical gold lots.");
            }

            Id = id;
            AssetId = asset.Id;
            OpeningTransactionEntryId = openingEntry.Id;
            AcquiredOn = acquiredOn;
            CostBasis = costBasis;
            PhysicalGoldDetail = physicalGoldDetail;
            CreatedAtUtc = createdAtUtc.ToUniversalTime();

            _allocations.Add(
                new LotEntryAllocation(
                    Guid.NewGuid(),
                    Id,
                    openingEntry.Id,
                    QuantityDelta.FromRaw(
                        openingQuantity.RawE8)));
        }

        public static AssetLot Create(
            Guid id,
            Asset asset,
            TransactionEntry openingEntry,
            Quantity openingQuantity,
            DateOnly? acquiredOn,
            CostBasis costBasis,
            DateTimeOffset createdAtUtc,
            PhysicalGoldLotDetail? physicalGoldDetail = null)
        {
            return new AssetLot(
                id,
                asset,
                openingEntry,
                openingQuantity,
                acquiredOn,
                costBasis,
                physicalGoldDetail,
                createdAtUtc);
        }

        public LotEntryAllocation Allocate(
            TransactionEntry entry,
            QuantityDelta quantityDelta)
        {
            ArgumentNullException.ThrowIfNull(entry);

            if (entry.AssetId != AssetId)
            {
                throw new DomainRuleViolationException(
                    "Transaction entry asset does not match the lot asset.");
            }

            if (quantityDelta.IsZero)
            {
                throw new DomainRuleViolationException(
                    "Lot allocation quantity cannot be zero.");
            }

            EnsureSameSign(
                entry.QuantityDelta,
                quantityDelta);

            EnsureAllocationDoesNotExceedEntry(
                entry.QuantityDelta,
                quantityDelta);

            if (_allocations.Any(x =>
                    x.TransactionEntryId == entry.Id))
            {
                throw new DomainRuleViolationException(
                    "This transaction entry has already been allocated to the lot.");
            }

            var resultingQuantity = checked(
                CurrentQuantity.RawE8
                + quantityDelta.RawE8);

            if (resultingQuantity < 0)
            {
                throw new DomainRuleViolationException(
                    "Lot allocation would make the lot quantity negative.");
            }

            var allocation =
                new LotEntryAllocation(
                    Guid.NewGuid(),
                    Id,
                    entry.Id,
                    quantityDelta);

            _allocations.Add(allocation);

            return allocation;
        }

        private static void EnsureSameSign(
            QuantityDelta entryQuantity,
            QuantityDelta allocationQuantity)
        {
            var signsMatch =
                entryQuantity.IsPositive
                    && allocationQuantity.IsPositive
                ||
                entryQuantity.IsNegative
                    && allocationQuantity.IsNegative;

            if (!signsMatch)
            {
                throw new DomainRuleViolationException(
                    "Lot allocation sign must match the transaction entry sign.");
            }
        }

        private static void EnsureAllocationDoesNotExceedEntry(
            QuantityDelta entryQuantity,
            QuantityDelta allocationQuantity)
        {
            if (entryQuantity.IsPositive)
            {
                if (allocationQuantity.RawE8
                    > entryQuantity.RawE8)
                {
                    throw new DomainRuleViolationException(
                        "Lot allocation cannot exceed the transaction entry quantity.");
                }

                return;
            }

            // Example:
            // entry      = -1000
            // allocation = -600   => valid
            // allocation = -1200  => invalid
            if (allocationQuantity.RawE8
                < entryQuantity.RawE8)
            {
                throw new DomainRuleViolationException(
                    "Lot allocation cannot exceed the transaction entry quantity.");
            }
        }
    }
}
