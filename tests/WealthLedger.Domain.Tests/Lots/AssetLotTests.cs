using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Common;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Tests.Lots
{
    public sealed class AssetLotTests
    {
        private static readonly Guid PortfolioId =
            Guid.NewGuid();

        private static readonly Guid AccountId =
            Guid.NewGuid();

        private static readonly DateTimeOffset CreatedAt =
            new(
                2026,
                8,
                19,
                10,
                0,
                0,
                TimeSpan.Zero);

        private static Asset CreateFund()
        {
            return Asset.Create(
                Guid.NewGuid(),
                "KPC",
                "KPC Fund",
                AssetType.Fund,
                AssetUnit.FundUnit,
                CurrencyCode.TRY,
                LotTrackingMode.Required);
        }

        private static TransactionEntry CreateEntry(
            Guid assetId,
            decimal quantity)
        {
            return new TransactionEntry(
                Guid.NewGuid(),
                0,
                PortfolioId,
                AccountId,
                assetId,
                QuantityDelta.FromDecimal(quantity),
                EntryRole.Principal,
                null);
        }

        [Fact]
        public void Create_OpensLotWithInitialQuantity()
        {
            var asset =
                CreateFund();

            var openingEntry =
                CreateEntry(
                    asset.Id,
                    1000m);

            var lot =
                AssetLot.Create(
                    Guid.NewGuid(),
                    asset,
                    openingEntry,
                    Quantity.FromDecimal(1000m),
                    new DateOnly(2026, 1, 10),
                    CostBasis.Known(
                        Money.FromMinorUnits(
                            1_000_000,
                            CurrencyCode.TRY)),
                    CreatedAt);

            Assert.Equal(
                1000m,
                lot.CurrentQuantity.ToDecimal());

            Assert.Single(
                lot.Allocations);

            Assert.False(
                lot.IsClosed);
        }

        [Fact]
        public void Allocate_SaleReducesCurrentQuantity()
        {
            var asset =
                CreateFund();

            var openingEntry =
                CreateEntry(
                    asset.Id,
                    1000m);

            var lot =
                AssetLot.Create(
                    Guid.NewGuid(),
                    asset,
                    openingEntry,
                    Quantity.FromDecimal(1000m),
                    new DateOnly(2026, 1, 10),
                    CostBasis.Unknown(),
                    CreatedAt);

            var saleEntry =
                CreateEntry(
                    asset.Id,
                    -400m);

            lot.Allocate(
                saleEntry,
                QuantityDelta.FromDecimal(-400m));

            Assert.Equal(
                600m,
                lot.CurrentQuantity.ToDecimal());
        }

        [Fact]
        public void Allocate_CannotMakeLotNegative()
        {
            var asset =
                CreateFund();

            var openingEntry =
                CreateEntry(
                    asset.Id,
                    500m);

            var lot =
                AssetLot.Create(
                    Guid.NewGuid(),
                    asset,
                    openingEntry,
                    Quantity.FromDecimal(500m),
                    null,
                    CostBasis.Unknown(),
                    CreatedAt);

            var saleEntry =
                CreateEntry(
                    asset.Id,
                    -700m);

            Assert.Throws<DomainRuleViolationException>(
                () => lot.Allocate(
                    saleEntry,
                    QuantityDelta.FromDecimal(-700m)));
        }

        [Fact]
        public void Allocate_RejectsDifferentAsset()
        {
            var asset =
                CreateFund();

            var openingEntry =
                CreateEntry(
                    asset.Id,
                    500m);

            var lot =
                AssetLot.Create(
                    Guid.NewGuid(),
                    asset,
                    openingEntry,
                    Quantity.FromDecimal(500m),
                    null,
                    CostBasis.Unknown(),
                    CreatedAt);

            var anotherAsset =
                CreateFund();

            var saleEntry =
                CreateEntry(
                    anotherAsset.Id,
                    -100m);

            Assert.Throws<DomainRuleViolationException>(
                () => lot.Allocate(
                    saleEntry,
                    QuantityDelta.FromDecimal(-100m)));
        }

        [Fact]
        public void Transfer_PreservesLotQuantity()
        {
            var asset =
                CreateFund();

            var openingEntry =
                CreateEntry(
                    asset.Id,
                    1000m);

            var lot =
                AssetLot.Create(
                    Guid.NewGuid(),
                    asset,
                    openingEntry,
                    Quantity.FromDecimal(1000m),
                    null,
                    CostBasis.Unknown(),
                    CreatedAt);

            var transferOut =
                new TransactionEntry(
                    Guid.NewGuid(),
                    0,
                    PortfolioId,
                    AccountId,
                    asset.Id,
                    QuantityDelta.FromDecimal(-200m),
                    EntryRole.Transfer,
                    null);

            var transferIn =
                new TransactionEntry(
                    Guid.NewGuid(),
                    1,
                    PortfolioId,
                    Guid.NewGuid(),
                    asset.Id,
                    QuantityDelta.FromDecimal(200m),
                    EntryRole.Transfer,
                    null);

            lot.Allocate(
                transferOut,
                QuantityDelta.FromDecimal(-200m));

            lot.Allocate(
                transferIn,
                QuantityDelta.FromDecimal(200m));

            Assert.Equal(
                1000m,
                lot.CurrentQuantity.ToDecimal());
        }

        [Fact]
        public void FullSale_ClosesLot()
        {
            var asset =
                CreateFund();

            var openingEntry =
                CreateEntry(
                    asset.Id,
                    500m);

            var lot =
                AssetLot.Create(
                    Guid.NewGuid(),
                    asset,
                    openingEntry,
                    Quantity.FromDecimal(500m),
                    null,
                    CostBasis.Unknown(),
                    CreatedAt);

            var saleEntry =
                CreateEntry(
                    asset.Id,
                    -500m);

            lot.Allocate(
                saleEntry,
                QuantityDelta.FromDecimal(-500m));

            Assert.True(
                lot.IsClosed);

            Assert.Equal(
                0m,
                lot.CurrentQuantity.ToDecimal());
        }

        [Fact]
        public void Reversal_OfSaleRestoresLotQuantity()
        {
            var asset =
                CreateFund();

            var openingEntry =
                CreateEntry(
                    asset.Id,
                    1000m);

            var lot =
                AssetLot.Create(
                    Guid.NewGuid(),
                    asset,
                    openingEntry,
                    Quantity.FromDecimal(1000m),
                    new DateOnly(2026, 1, 10),
                    CostBasis.Unknown(),
                    CreatedAt);

            var sale =
                LedgerTransaction.CreateDraft(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    TransactionType.Sell,
                    CreatedAt,
                    executionDate:
                        new DateOnly(2026, 8, 19));

            var saleEntry =
                sale.AddEntry(
                    PortfolioId,
                    AccountId,
                    asset.Id,
                    QuantityDelta.FromDecimal(-400m),
                    EntryRole.Principal);

            sale.AddEntry(
                PortfolioId,
                AccountId,
                Guid.NewGuid(),
                QuantityDelta.FromDecimal(5000m),
                EntryRole.Consideration);

            sale.Post(
                CreatedAt.AddMinutes(1));

            lot.Allocate(
                saleEntry,
                QuantityDelta.FromDecimal(-400m));

            Assert.Equal(
                600m,
                lot.CurrentQuantity.ToDecimal());

            var reversal =
                LedgerTransaction.CreateReversal(
                    Guid.NewGuid(),
                    sale,
                    CreatedAt.AddDays(1));

            var reversedPrincipal =
                reversal.Entries.Single(
                    x => x.Role == EntryRole.Principal);

            lot.Allocate(
                reversedPrincipal,
                reversedPrincipal.QuantityDelta);

            Assert.Equal(
                1000m,
                lot.CurrentQuantity.ToDecimal());
        }

        [Fact]
        public void PhysicalGoldLot_CanStoreLotSpecificDetails()
        {
            var gold =
                Asset.Create(
                    Guid.NewGuid(),
                    "GOLD_BRACELET_22K",
                    "22K Synthetic Bracelet",
                    AssetType.PhysicalGold,
                    AssetUnit.GrossGram,
                    CurrencyCode.TRY,
                    LotTrackingMode.Required);

            var openingEntry =
                CreateEntry(
                    gold.Id,
                    12.43m);

            var detail =
                new PhysicalGoldLotDetail(
                    new Fineness(916_000),
                    pieceCount: 1,
                    hallmark: "916");

            var lot =
                AssetLot.Create(
                    Guid.NewGuid(),
                    gold,
                    openingEntry,
                    Quantity.FromDecimal(12.43m),
                    new DateOnly(2026, 8, 19),
                    CostBasis.Known(
                        Money.FromMinorUnits(
                            7_950_000,
                            CurrencyCode.TRY)),
                    CreatedAt,
                    detail);

            Assert.NotNull(
                lot.PhysicalGoldDetail);

            Assert.Equal(
                916_000,
                lot.PhysicalGoldDetail!.Fineness.Ppm);

            Assert.Equal(
                1,
                lot.PhysicalGoldDetail.PieceCount);
        }

        [Fact]
        public void NonGoldAsset_CannotHavePhysicalGoldDetails()
        {
            var fund =
                CreateFund();

            var openingEntry =
                CreateEntry(
                    fund.Id,
                    100m);

            var detail =
                new PhysicalGoldLotDetail(
                    new Fineness(916_000),
                    1);

            Assert.Throws<DomainRuleViolationException>(
                () => AssetLot.Create(
                    Guid.NewGuid(),
                    fund,
                    openingEntry,
                    Quantity.FromDecimal(100m),
                    null,
                    CostBasis.Unknown(),
                    CreatedAt,
                    detail));
        }
    }
}
