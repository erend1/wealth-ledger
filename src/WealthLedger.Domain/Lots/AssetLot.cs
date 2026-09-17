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

        /// <summary>
        /// The current global number of pieces in a physical-gold lot.
        /// </summary>
        /// <remarks>
        /// This is derived from immutable signed allocation details. The
        /// original <see cref="PhysicalGoldLotDetail.PieceCount"/> remains
        /// acquisition evidence and is never mutated.
        /// </remarks>
        public int CurrentPieceCount
        {
            get
            {
                if (PhysicalGoldDetail is null)
                {
                    throw new InvalidOperationException(
                        "Only a physical-gold lot has a current piece count.");
                }

                var total = 0;

                foreach (var allocation in _allocations)
                {
                    var detail = allocation.PhysicalGoldDetail
                        ?? throw new InvalidOperationException(
                            "Every physical-gold allocation must contain piece movement.");

                    total = checked(total + detail.PieceDelta);
                }

                if (total < 0)
                {
                    throw new InvalidOperationException(
                        "Physical-gold lot piece count cannot be negative.");
                }

                return total;
            }
        }

        private AssetLot(
            Guid id,
            Guid assetId,
            Guid openingTransactionEntryId,
            DateOnly? acquiredOn,
            CostBasis costBasis,
            PhysicalGoldLotDetail? physicalGoldDetail,
            DateTimeOffset createdAtUtc)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentException(
                    "Asset lot ID cannot be empty.",
                    nameof(id));
            }

            if (assetId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Asset ID cannot be empty.",
                    nameof(assetId));
            }

            if (openingTransactionEntryId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Opening transaction entry ID cannot be empty.",
                    nameof(openingTransactionEntryId));
            }

            ArgumentNullException.ThrowIfNull(costBasis);

            Id = id;
            AssetId = assetId;
            OpeningTransactionEntryId =
                openingTransactionEntryId;
            AcquiredOn = acquiredOn;
            CostBasis = costBasis;
            PhysicalGoldDetail = physicalGoldDetail;
            CreatedAtUtc = createdAtUtc.ToUniversalTime();
        }

        private AssetLot(
            Guid id,
            Asset asset,
            TransactionEntry openingEntry,
            Quantity openingQuantity,
            DateOnly? acquiredOn,
            CostBasis costBasis,
            PhysicalGoldLotDetail? physicalGoldDetail,
            DateTimeOffset createdAtUtc)
            : this(
                id,
                asset?.Id ?? Guid.Empty,
                openingEntry?.Id ?? Guid.Empty,
                acquiredOn,
                costBasis,
                physicalGoldDetail,
                createdAtUtc)
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

            if (asset.Type == AssetType.PhysicalGold
                && physicalGoldDetail is null)
            {
                throw new DomainRuleViolationException(
                    "A physical gold lot must contain physical gold details.");
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
                        openingQuantity.RawE8),
                    physicalGoldDetail?.PieceCount));
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
            if (PhysicalGoldDetail is not null)
            {
                throw new DomainRuleViolationException(
                    "A physical-gold allocation requires an exact piece movement.");
            }

            return AllocateCore(
                entry,
                quantityDelta,
                physicalGoldPieceDelta: null);
        }

        public LotEntryAllocation Allocate(
            TransactionEntry entry,
            QuantityDelta quantityDelta,
            int physicalGoldPieceDelta)
        {
            if (PhysicalGoldDetail is null)
            {
                throw new DomainRuleViolationException(
                    "Piece movement can be attached only to a physical-gold allocation.");
            }

            return AllocateCore(
                entry,
                quantityDelta,
                physicalGoldPieceDelta);
        }

        private LotEntryAllocation AllocateCore(
            TransactionEntry entry,
            QuantityDelta quantityDelta,
            int? physicalGoldPieceDelta)
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

            PhysicalGoldLotAllocationDetail? pieceDetail = null;

            if (physicalGoldPieceDelta is int pieceDelta)
            {
                pieceDetail =
                    new PhysicalGoldLotAllocationDetail(
                        Guid.NewGuid(),
                        quantityDelta,
                        pieceDelta);

                var resultingPieces = checked(
                    CurrentPieceCount + pieceDetail.PieceDelta);

                if (resultingPieces < 0)
                {
                    throw new DomainRuleViolationException(
                        "Physical-gold allocation would make the lot piece count negative.");
                }
            }

            var allocationId = pieceDetail?.LotEntryAllocationId
                ?? Guid.NewGuid();

            var allocation =
                new LotEntryAllocation(
                    allocationId,
                    Id,
                    entry.Id,
                    quantityDelta,
                    physicalGoldPieceDelta);

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

        public static AssetLot Reconstitute(
            Guid id,
            Guid assetId,
            Guid openingTransactionEntryId,
            DateOnly? acquiredOn,
            CostBasis costBasis,
            PhysicalGoldLotDetail? physicalGoldDetail,
            DateTimeOffset createdAtUtc,
            IReadOnlyCollection<AssetLotAllocationSnapshot> allocations)
        {
            ArgumentNullException.ThrowIfNull(allocations);

            var lot =
                new AssetLot(
                    id,
                    assetId,
                    openingTransactionEntryId,
                    acquiredOn,
                    costBasis,
                    physicalGoldDetail,
                    createdAtUtc);

            if (allocations.Count == 0)
            {
                throw new DomainRuleViolationException(
                    "A reconstituted asset lot must contain allocation history.");
            }

            foreach (var snapshot in allocations)
            {
                if (lot._allocations.Any(
                        x => x.Id == snapshot.Id))
                {
                    throw new DomainRuleViolationException(
                        "Persisted lot allocations cannot contain duplicate IDs.");
                }

                if (lot._allocations.Any(
                        x => x.TransactionEntryId
                            == snapshot.TransactionEntryId))
                {
                    throw new DomainRuleViolationException(
                        "A transaction entry cannot be allocated to the same lot more than once.");
                }

                lot._allocations.Add(
                    new LotEntryAllocation(
                        snapshot.Id,
                        lot.Id,
                        snapshot.TransactionEntryId,
                        snapshot.QuantityDelta,
                        snapshot.PhysicalGoldPieceDelta));
            }

            var openingAllocation =
                lot._allocations.SingleOrDefault(
                    x => x.TransactionEntryId
                        == openingTransactionEntryId);

            if (openingAllocation is null
                || !openingAllocation.QuantityDelta.IsPositive)
            {
                throw new DomainRuleViolationException(
                    "A reconstituted asset lot must contain its positive opening allocation.");
            }

            if (physicalGoldDetail is not null)
            {
                if (lot._allocations.Any(
                        x => x.PhysicalGoldDetail is null))
                {
                    throw new DomainRuleViolationException(
                        "Every physical-gold allocation must contain piece movement.");
                }

                if (openingAllocation.PhysicalGoldDetail!.PieceDelta
                    != physicalGoldDetail.PieceCount)
                {
                    throw new DomainRuleViolationException(
                        "The opening physical-gold piece movement must equal the original piece count.");
                }
            }
            else if (lot._allocations.Any(
                         x => x.PhysicalGoldDetail is not null))
            {
                throw new DomainRuleViolationException(
                    "Only physical-gold allocations may contain piece movement.");
            }

            // Forces checked summation and negative-balance validation.
            _ = lot.CurrentQuantity;

            if (physicalGoldDetail is not null)
            {
                _ = lot.CurrentPieceCount;
            }

            return lot;
        }
    }
}
