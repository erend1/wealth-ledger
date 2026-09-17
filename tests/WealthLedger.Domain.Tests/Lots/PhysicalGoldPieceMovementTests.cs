using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Common;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Tests.Lots;

public sealed class PhysicalGoldPieceMovementTests
{
    private static readonly Guid PortfolioId = Guid.NewGuid();
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 9, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_RecordsOriginalPieceEvidenceOnOpeningAllocation()
    {
        var (lot, _) = CreateGoldLot(25.25m, 2);

        var opening = Assert.Single(lot.Allocations);

        Assert.Equal(2, opening.PhysicalGoldDetail!.PieceDelta);
        Assert.Equal(2, lot.PhysicalGoldDetail!.PieceCount);
        Assert.Equal(2, lot.CurrentPieceCount);
    }

    [Fact]
    public void Allocate_PhysicalGoldRequiresPieceMovement()
    {
        var (lot, gold) = CreateGoldLot(25.25m, 2);
        var sale = CreateEntry(gold.Id, -10m, EntryRole.Principal);

        Assert.Throws<DomainRuleViolationException>(
            () => lot.Allocate(
                sale,
                QuantityDelta.FromDecimal(-10m)));
    }

    [Fact]
    public void Allocate_RejectsZeroPieceMovement()
    {
        var (lot, gold) = CreateGoldLot(25.25m, 2);
        var sale = CreateEntry(gold.Id, -10m, EntryRole.Principal);

        Assert.Throws<DomainRuleViolationException>(
            () => lot.Allocate(
                sale,
                QuantityDelta.FromDecimal(-10m),
                0));
    }

    [Fact]
    public void Allocate_RejectsPieceAndGrossSignMismatch()
    {
        var (lot, gold) = CreateGoldLot(25.25m, 2);
        var sale = CreateEntry(gold.Id, -10m, EntryRole.Principal);

        Assert.Throws<DomainRuleViolationException>(
            () => lot.Allocate(
                sale,
                QuantityDelta.FromDecimal(-10m),
                1));
    }

    [Fact]
    public void Allocate_RejectsNonReversiblePieceMinimum()
    {
        var (lot, gold) = CreateGoldLot(25.25m, 2);
        var sale = CreateEntry(gold.Id, -10m, EntryRole.Principal);

        Assert.Throws<DomainRuleViolationException>(
            () => lot.Allocate(
                sale,
                QuantityDelta.FromDecimal(-10m),
                int.MinValue));
    }

    [Fact]
    public void Allocate_PartialSaleMovesExactGrossAndPiecesIndependently()
    {
        var (lot, gold) = CreateGoldLot(25.25m, 2);
        var sale = CreateEntry(gold.Id, -9.4m, EntryRole.Principal);

        var allocation = lot.Allocate(
            sale,
            QuantityDelta.FromDecimal(-9.4m),
            -1);

        Assert.Equal(-9.4m, allocation.QuantityDelta.ToDecimal());
        Assert.Equal(-1, allocation.PhysicalGoldDetail!.PieceDelta);
        Assert.Equal(15.85m, lot.CurrentQuantity.ToDecimal());
        Assert.Equal(1, lot.CurrentPieceCount);
    }

    [Fact]
    public void Allocate_CannotMakePieceCountNegativeWhenGrossRemains()
    {
        var (lot, gold) = CreateGoldLot(25.25m, 2);
        var sale = CreateEntry(gold.Id, -1m, EntryRole.Principal);

        Assert.Throws<DomainRuleViolationException>(
            () => lot.Allocate(
                sale,
                QuantityDelta.FromDecimal(-1m),
                -3));
    }

    [Fact]
    public void Transfer_EqualAndOppositeMovementsPreserveGlobalFacts()
    {
        var (lot, gold) = CreateGoldLot(25.25m, 2);

        lot.Allocate(
            CreateEntry(gold.Id, -9.4m, EntryRole.Transfer),
            QuantityDelta.FromDecimal(-9.4m),
            -1);

        lot.Allocate(
            CreateEntry(
                gold.Id,
                9.4m,
                EntryRole.Transfer,
                accountId: Guid.NewGuid()),
            QuantityDelta.FromDecimal(9.4m),
            1);

        Assert.Equal(25.25m, lot.CurrentQuantity.ToDecimal());
        Assert.Equal(2, lot.CurrentPieceCount);
    }

    [Fact]
    public void SaleReversal_RestoresExactGrossAndPieces()
    {
        var (lot, gold) = CreateGoldLot(25.25m, 2);
        var sale = CreateEntry(gold.Id, -9.4m, EntryRole.Principal);

        var sold = lot.Allocate(
            sale,
            QuantityDelta.FromDecimal(-9.4m),
            -1);

        var reversal = CreateEntry(gold.Id, 9.4m, EntryRole.Principal);

        lot.Allocate(
            reversal,
            sold.QuantityDelta.Negate(),
            checked(-sold.PhysicalGoldDetail!.PieceDelta));

        Assert.Equal(25.25m, lot.CurrentQuantity.ToDecimal());
        Assert.Equal(2, lot.CurrentPieceCount);
    }

    [Fact]
    public void Reconstitute_RejectsGoldAllocationWithoutPieceEvidence()
    {
        var (lot, _) = CreateGoldLot(25.25m, 2);
        var opening = Assert.Single(lot.Allocations);

        Assert.Throws<DomainRuleViolationException>(
            () => AssetLot.Reconstitute(
                lot.Id,
                lot.AssetId,
                lot.OpeningTransactionEntryId,
                lot.AcquiredOn,
                lot.CostBasis,
                lot.PhysicalGoldDetail,
                lot.CreatedAtUtc,
                [
                    new AssetLotAllocationSnapshot(
                        opening.Id,
                        opening.TransactionEntryId,
                        opening.QuantityDelta)
                ]));
    }

    [Fact]
    public void Reconstitute_RejectsOpeningPieceMovementDifferentFromOriginalEvidence()
    {
        var (lot, _) = CreateGoldLot(25.25m, 2);
        var opening = Assert.Single(lot.Allocations);

        Assert.Throws<DomainRuleViolationException>(
            () => AssetLot.Reconstitute(
                lot.Id,
                lot.AssetId,
                lot.OpeningTransactionEntryId,
                lot.AcquiredOn,
                lot.CostBasis,
                lot.PhysicalGoldDetail,
                lot.CreatedAtUtc,
                [
                    new AssetLotAllocationSnapshot(
                        opening.Id,
                        opening.TransactionEntryId,
                        opening.QuantityDelta,
                        PhysicalGoldPieceDelta: 1)
                ]));
    }

    [Fact]
    public void Allocate_NonGoldLotRejectsPieceMovement()
    {
        var fund = Asset.Create(
            Guid.NewGuid(),
            "SYNTH_FUND",
            "Synthetic Fund",
            AssetType.Fund,
            AssetUnit.FundUnit,
            CurrencyCode.TRY,
            LotTrackingMode.Required);

        var opening = CreateEntry(fund.Id, 10m, EntryRole.Principal);
        var lot = AssetLot.Create(
            Guid.NewGuid(),
            fund,
            opening,
            Quantity.FromDecimal(10m),
            DateOnly.FromDateTime(CreatedAt.Date),
            CostBasis.Unknown(),
            CreatedAt);

        Assert.Throws<DomainRuleViolationException>(
            () => lot.Allocate(
                CreateEntry(fund.Id, -1m, EntryRole.Principal),
                QuantityDelta.FromDecimal(-1m),
                -1));
    }

    private static (AssetLot Lot, Asset Gold) CreateGoldLot(
        decimal grossWeight,
        int pieces)
    {
        var gold = Asset.Create(
            Guid.NewGuid(),
            "SYNTH_GOLD",
            "Synthetic Gold",
            AssetType.PhysicalGold,
            AssetUnit.GrossGram,
            CurrencyCode.TRY,
            LotTrackingMode.Required);

        var opening = CreateEntry(
            gold.Id,
            grossWeight,
            EntryRole.Principal);

        var lot = AssetLot.Create(
            Guid.NewGuid(),
            gold,
            opening,
            Quantity.FromDecimal(grossWeight),
            new DateOnly(2026, 9, 15),
            CostBasis.Known(
                Money.FromMinorUnits(100_00, CurrencyCode.TRY)),
            CreatedAt,
            new PhysicalGoldLotDetail(
                new Fineness(916_000),
                pieces));

        return (lot, gold);
    }

    private static TransactionEntry CreateEntry(
        Guid assetId,
        decimal quantity,
        EntryRole role,
        Guid? accountId = null)
        => new(
            Guid.NewGuid(),
            0,
            PortfolioId,
            accountId ?? AccountId,
            assetId,
            QuantityDelta.FromDecimal(quantity),
            role,
            null);
}
