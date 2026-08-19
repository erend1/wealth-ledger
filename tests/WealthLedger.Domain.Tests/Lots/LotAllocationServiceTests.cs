using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Common;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Tests.Lots
{
    public sealed class LotAllocationServiceTests
    {
        private readonly LotAllocationService _service =
            new();

        [Fact]
        public void PlanFifo_AllocatesOldestLotsFirst()
        {
            var asset =
                CreateFund();

            var first =
                CreateLot(
                    asset,
                    600m,
                    new DateOnly(2026, 1, 1));

            var second =
                CreateLot(
                    asset,
                    700m,
                    new DateOnly(2026, 2, 1));

            var plan =
                _service.PlanFifo(
                    asset.Id,
                    Quantity.FromDecimal(1000m),
                    [second, first]);

            Assert.Equal(
                2,
                plan.Count);

            Assert.Equal(
                first.Id,
                plan[0].AssetLotId);

            Assert.Equal(
                600m,
                plan[0].Quantity.ToDecimal());

            Assert.Equal(
                second.Id,
                plan[1].AssetLotId);

            Assert.Equal(
                400m,
                plan[1].Quantity.ToDecimal());
        }

        [Fact]
        public void PlanFifo_RejectsInsufficientQuantity()
        {
            var asset =
                CreateFund();

            var lot =
                CreateLot(
                    asset,
                    600m,
                    new DateOnly(2026, 1, 1));

            Assert.Throws<DomainRuleViolationException>(
                () => _service.PlanFifo(
                    asset.Id,
                    Quantity.FromDecimal(1000m),
                    [lot]));
        }

        [Fact]
        public void PlanFifo_IgnoresClosedLots()
        {
            var asset =
                CreateFund();

            var closed =
                CreateLot(
                    asset,
                    500m,
                    new DateOnly(2025, 12, 1));

            var closeEntry =
                CreateEntry(
                    asset.Id,
                    -500m);

            closed.Allocate(
                closeEntry,
                QuantityDelta.FromDecimal(-500m));

            var open =
                CreateLot(
                    asset,
                    700m,
                    new DateOnly(2026, 1, 1));

            var plan =
                _service.PlanFifo(
                    asset.Id,
                    Quantity.FromDecimal(400m),
                    [closed, open]);

            Assert.Single(plan);

            Assert.Equal(
                open.Id,
                plan[0].AssetLotId);

            Assert.Equal(
                400m,
                plan[0].Quantity.ToDecimal());
        }

        private static Asset CreateFund()
        {
            return Asset.Create(
                Guid.NewGuid(),
                $"FUND_{Guid.NewGuid():N}",
                "Test Fund",
                AssetType.Fund,
                AssetUnit.FundUnit,
                CurrencyCode.TRY,
                LotTrackingMode.Required);
        }

        private static AssetLot CreateLot(
            Asset asset,
            decimal quantity,
            DateOnly acquiredOn)
        {
            var entry =
                CreateEntry(
                    asset.Id,
                    quantity);

            return AssetLot.Create(
                Guid.NewGuid(),
                asset,
                entry,
                Quantity.FromDecimal(quantity),
                acquiredOn,
                CostBasis.Unknown(),
                DateTimeOffset.UtcNow);
        }

        private static TransactionEntry CreateEntry(
            Guid assetId,
            decimal quantity)
        {
            return new TransactionEntry(
                Guid.NewGuid(),
                0,
                Guid.NewGuid(),
                Guid.NewGuid(),
                assetId,
                QuantityDelta.FromDecimal(quantity),
                EntryRole.Principal,
                null);
        }

        [Fact]
        public void OpeningGoldLot_CanHaveUnknownHistoricalCost()
        {
            var gold =
                Asset.Create(
                    Guid.NewGuid(),
                    "GOLD_OPENING_22K",
                    "Existing 22K Gold",
                    AssetType.PhysicalGold,
                    AssetUnit.GrossGram,
                    CurrencyCode.TRY,
                    LotTrackingMode.Required);

            var openingEntry =
                new TransactionEntry(
                    Guid.NewGuid(),
                    0,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    gold.Id,
                    QuantityDelta.FromDecimal(100m),
                    EntryRole.Principal,
                    null);

            var lot =
                AssetLot.Create(
                    Guid.NewGuid(),
                    gold,
                    openingEntry,
                    Quantity.FromDecimal(100m),
                    acquiredOn: null,
                    CostBasis.Unknown(),
                    DateTimeOffset.UtcNow,
                    new PhysicalGoldLotDetail(
                        new Fineness(916_000),
                        pieceCount: 1));

            Assert.Equal(
                CostBasisStatus.Unknown,
                lot.CostBasis.Status);

            Assert.Null(
                lot.CostBasis.Amount);
        }
    }
}
