using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.PhysicalGold;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.Tests.PhysicalGold;

public sealed class PhysicalGoldUseCaseTests
{
    [Fact]
    public async Task PurchasePreview_DerivesFineWeightAndIndependentCashFacts()
    {
        var preview = await PurchasePreview().ExecuteAsync(
            PurchaseCommand() with
            {
                Costs =
                [
                    new PhysicalGoldCostInput(
                        CostType.MakingCharge,
                        CostTreatment.IncludedInConsideration,
                        Money.FromMinorUnits(5_000, GoldIds.Try)),
                    new PhysicalGoldCostInput(
                        CostType.Commission,
                        CostTreatment.AdditionalCashOutflow,
                        Money.FromMinorUnits(1_000, GoldIds.Try))
                ]
            });

        Assert.Equal(18.32m, preview.FineWeightGrams);
        Assert.Equal(201_000,
            preview.Economics.AcquisitionLotCost!.MinorUnits);
        Assert.Equal(-201_000, preview.Economics.NetCashEffect.MinorUnits);
        Assert.Contains(
            PhysicalGoldWarningCodes.ExecutedPriceUnavailable,
            preview.WarningCodes);
    }

    [Fact]
    public async Task PurchaseRecord_CreatesOneKnownPhysicalLotAndTradeDetail()
    {
        var references = GoldReferenceStoreFake.CreateValid();
        var submission = new GoldSubmissionStoreFake();
        var posting = new GoldPostingStoreFake();
        var result = await new RecordPhysicalGoldPurchaseUseCase(
            references,
            submission,
            posting,
            new GoldTimeProvider()).ExecuteAsync(
                "gold-purchase", PurchaseCommand());

        Assert.Equal(posting.Transaction!.Id, result.TransactionId);
        Assert.Equal(posting.Lot!.Id, result.AssetLotId);
        Assert.Equal(20_00000000L, posting.Lot.CurrentQuantity.RawE8);
        Assert.Equal(2, posting.Lot.CurrentPieceCount);
        Assert.Equal(916_000, posting.Lot.PhysicalGoldDetail!.Fineness.Ppm);
        Assert.Equal(CostBasisStatus.Known, posting.Lot.CostBasis.Status);
        Assert.Equal(
            GoldIds.Counterparty,
            posting.Transaction.PhysicalGoldTradeDetail!
                .CounterpartyInstitutionId);
    }

    [Theory]
    [InlineData(AssetType.Fund, AssetUnit.FundUnit, LotTrackingMode.Required)]
    [InlineData(AssetType.PhysicalGold, AssetUnit.Piece, LotTrackingMode.Required)]
    [InlineData(AssetType.PhysicalGold, AssetUnit.GrossGram, LotTrackingMode.Optional)]
    public async Task Purchase_RejectsWrongGoldAssetShape(
        AssetType type,
        AssetUnit unit,
        LotTrackingMode tracking)
    {
        var references = GoldReferenceStoreFake.CreateValid();
        references.Assets[GoldIds.GoldAsset] =
            references.Assets[GoldIds.GoldAsset] with
            {
                Type = type,
                BaseUnit = unit,
                LotTrackingMode = tracking
            };
        var exception = await Assert.ThrowsAsync<PhysicalGoldException>(
            () => PurchasePreview(references).ExecuteAsync(PurchaseCommand()));
        Assert.Equal(
            PhysicalGoldErrorCodes.ReferenceShapeInvalid,
            exception.ErrorCode);
    }

    [Fact]
    public async Task SalePreview_UsesOnlyExplicitSelectionAndProjectsCost()
    {
        var custody = new GoldCustodyStoreFake().AddLot();
        var costs = new GoldRealizedCostStoreFake().AddKnownLot();
        var command = SaleCommand(
            [new PhysicalGoldSelectedLot(
                GoldIds.Lot, Quantity.FromDecimal(8m), 1)]);
        var preview = await SalePreview(custody, costs).ExecuteAsync(command);

        Assert.Single(preview.Plan);
        Assert.Equal(8_00000000L, preview.Plan[0].MovedGrossWeightRawE8);
        Assert.Equal(1, preview.Plan[0].MovedPieceCount);
        Assert.Equal(80_000, preview.RealizedCost.KnownAmounts.Single().MinorUnits);
        Assert.StartsWith("sha256-1-", preview.PlanFingerprint);
    }

    [Theory]
    [InlineData(21, 1, PhysicalGoldErrorCodes.InsufficientGrossWeight)]
    [InlineData(10, 3, PhysicalGoldErrorCodes.InsufficientPieces)]
    public async Task SalePreview_RejectsIndependentAvailabilityOverrun(
        decimal gross,
        int pieces,
        string expectedCode)
    {
        var selection = new PhysicalGoldSelectedLot(
            GoldIds.Lot, Quantity.FromDecimal(gross), pieces);
        var exception = await Assert.ThrowsAsync<PhysicalGoldException>(
            () => SalePreview(
                    new GoldCustodyStoreFake().AddLot(),
                    new GoldRealizedCostStoreFake().AddKnownLot())
                .ExecuteAsync(SaleCommand([selection])));
        Assert.Equal(expectedCode, exception.ErrorCode);
    }

    [Fact]
    public async Task SalePreview_RejectsDuplicateLotSelection()
    {
        var selection = new PhysicalGoldSelectedLot(
            GoldIds.Lot, Quantity.FromDecimal(4m), 1);
        var command = SaleCommand([selection, selection]) with
        {
            GrossWeight = Quantity.FromDecimal(8m),
            PieceCount = 2
        };
        var exception = await Assert.ThrowsAsync<PhysicalGoldException>(
            () => SalePreview(
                    new GoldCustodyStoreFake().AddLot(),
                    new GoldRealizedCostStoreFake().AddKnownLot())
                .ExecuteAsync(command));
        Assert.Equal(
            PhysicalGoldErrorCodes.DuplicateSelection,
            exception.ErrorCode);
    }

    [Fact]
    public async Task SaleRecord_MapsStaleCommitToSanitizedConflict()
    {
        var posting = new GoldPostingStoreFake
        {
            Status = PhysicalGoldCommitStatus.StaleReviewedPlan
        };
        var command = SaleCommand(
            [new PhysicalGoldSelectedLot(
                GoldIds.Lot, Quantity.FromDecimal(8m), 1)]);
        command = command with
        {
            ReviewedPlanFingerprint = PhysicalGoldPlanFingerprint.ComputeSale(
                new PhysicalGoldTradeScope(
                    GoldIds.Household,
                    GoldIds.Portfolio,
                    GoldIds.Vault,
                    GoldIds.CashAccount,
                    GoldIds.GoldAsset,
                    GoldIds.CashAsset),
                command.GrossWeight.RawE8,
                command.PieceCount,
                command.SelectedLots)
        };
        var exception = await Assert.ThrowsAsync<PhysicalGoldException>(
            () => new RecordPhysicalGoldSaleUseCase(
                    GoldReferenceStoreFake.CreateValid(),
                    new GoldSubmissionStoreFake(),
                    posting,
                    new GoldTimeProvider())
                .ExecuteAsync("gold-sale", command));
        Assert.Equal(PhysicalGoldErrorCategory.Conflict, exception.Category);
        Assert.Equal(
            PhysicalGoldErrorCodes.StaleReviewedPlan,
            exception.ErrorCode);
        Assert.DoesNotContain(
            command.ReviewedPlanFingerprint,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PurchaseReplay_IsReceiptFirstAfterReferencesChange()
    {
        var command = PurchaseCommand();
        var scope = new LedgerSubmissionScope(
            command.HouseholdId,
            LedgerOperationCodes.RecordPhysicalGoldPurchase,
            "gold-replay");
        var receipt = new LedgerSubmissionReceipt(
            scope,
            PhysicalGoldCommandFingerprints.ComputeCurrent(command),
            Guid.NewGuid(),
            Guid.NewGuid(),
            GoldIds.Now);
        var references = GoldReferenceStoreFake.CreateValid();
        references.ThrowOnRead = true;
        var result = await new RecordPhysicalGoldPurchaseUseCase(
            references,
            new GoldSubmissionStoreFake { Receipt = receipt },
            new GoldPostingStoreFake(),
            new GoldTimeProvider()).ExecuteAsync("gold-replay", command);

        Assert.Equal(receipt.TransactionId, result.TransactionId);
        Assert.Equal(0, references.ReadCalls);
    }

    [Fact]
    public async Task TransferPreview_PreservesLineageWithExactOppositePlan()
    {
        var command = TransferCommand(
            [new PhysicalGoldSelectedLot(
                GoldIds.Lot, Quantity.FromDecimal(7.5m), 1)]);
        var preview = await TransferPreview(
            new GoldCustodyStoreFake().AddLot()).ExecuteAsync(command);
        Assert.Equal(6.87m, preview.FineWeightGrams);
        Assert.Equal(12_50000000L, preview.Plan[0].RemainingGrossWeightRawE8);
        Assert.Equal(1, preview.Plan[0].RemainingPieceCount);
    }

    [Fact]
    public async Task Transfer_RejectsSameCustodyScope()
    {
        var command = TransferCommand(
            [new PhysicalGoldSelectedLot(
                GoldIds.Lot, Quantity.FromDecimal(7.5m), 1)]) with
        {
            DestinationPortfolioId = GoldIds.Portfolio,
            DestinationGoldAccountId = GoldIds.Vault
        };
        var exception = await Assert.ThrowsAsync<PhysicalGoldException>(
            () => TransferPreview(
                    new GoldCustodyStoreFake().AddLot())
                .ExecuteAsync(command));
        Assert.Equal(
            PhysicalGoldErrorCodes.TransferScopeInvalid,
            exception.ErrorCode);
    }

    [Fact]
    public void SalePlanFingerprint_IsIndependentOfSelectionOrder()
    {
        var otherLot = Guid.Parse(
            "60000000-0000-0000-0000-000000000902");
        var first = new PhysicalGoldSelectedLot(
            GoldIds.Lot, Quantity.FromDecimal(2m), 1);
        var second = new PhysicalGoldSelectedLot(
            otherLot, Quantity.FromDecimal(3m), 1);
        var scope = new PhysicalGoldTradeScope(
            GoldIds.Household,
            GoldIds.Portfolio,
            GoldIds.Vault,
            GoldIds.CashAccount,
            GoldIds.GoldAsset,
            GoldIds.CashAsset);
        Assert.Equal(
            PhysicalGoldPlanFingerprint.ComputeSale(
                scope, 5_00000000L, 2, [first, second]),
            PhysicalGoldPlanFingerprint.ComputeSale(
                scope, 5_00000000L, 2, [second, first]));
    }

    private static PhysicalGoldPurchaseCommand PurchaseCommand()
        => new(
            GoldIds.Household,
            GoldIds.Portfolio,
            GoldIds.Vault,
            GoldIds.CashAccount,
            GoldIds.GoldAsset,
            GoldIds.CashAsset,
            Quantity.FromDecimal(20m),
            new Fineness(916_000),
            2,
            Money.FromMinorUnits(200_000, GoldIds.Try),
            GoldIds.ExecutionDate,
            CounterpartyInstitutionId: GoldIds.Counterparty,
            Hallmark: "916",
            CertificateReference: "CERT",
            ExternalReference: "GOLD-PURCHASE",
            Note: "Synthetic purchase.");

    private static PhysicalGoldSaleCommand SaleCommand(
        IReadOnlyList<PhysicalGoldSelectedLot> selected)
        => new(
            GoldIds.Household,
            GoldIds.Portfolio,
            GoldIds.Vault,
            GoldIds.CashAccount,
            GoldIds.GoldAsset,
            GoldIds.CashAsset,
            Quantity.FromRaw(selected.Sum(x => x.GrossWeight.RawE8)),
            selected.Sum(x => x.PieceCount),
            selected,
            Money.FromMinorUnits(100_000, GoldIds.Try),
            GoldIds.ExecutionDate,
            CounterpartyInstitutionId: GoldIds.Counterparty,
            ExternalReference: "GOLD-SALE",
            Note: "Synthetic sale.");

    private static PhysicalGoldTransferCommand TransferCommand(
        IReadOnlyList<PhysicalGoldSelectedLot> selected)
        => new(
            GoldIds.Household,
            GoldIds.Portfolio,
            GoldIds.Vault,
            GoldIds.OtherPortfolio,
            GoldIds.OtherVault,
            GoldIds.GoldAsset,
            Quantity.FromRaw(selected.Sum(x => x.GrossWeight.RawE8)),
            selected.Sum(x => x.PieceCount),
            selected,
            GoldIds.ExecutionDate,
            ExternalReference: "GOLD-TRANSFER",
            Note: "Synthetic transfer.");

    private static PreviewPhysicalGoldPurchaseUseCase PurchasePreview(
        GoldReferenceStoreFake? references = null)
        => new(
            references ?? GoldReferenceStoreFake.CreateValid(),
            new GoldCustodyStoreFake(),
            new GoldTimeProvider());

    private static PreviewPhysicalGoldSaleUseCase SalePreview(
        GoldCustodyStoreFake custody,
        GoldRealizedCostStoreFake costs)
        => new(
            GoldReferenceStoreFake.CreateValid(),
            custody,
            costs,
            new GoldTimeProvider());

    private static PreviewPhysicalGoldTransferUseCase TransferPreview(
        GoldCustodyStoreFake custody)
        => new(
            GoldReferenceStoreFake.CreateValid(),
            custody,
            new GoldTimeProvider());
}
