using WealthLedger.Application.FundTrades;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.Tests.FundTrades;

public sealed class FundPurchasePreviewTests
{
    private static readonly DateOnly ExecutionDate =
        new(2026, 3, 10);

    [Fact]
    public async Task Preview_SeparateAccounts_ShowsBothSides()
    {
        var preview =
            await PreviewAsync(
                Command());

        Assert.Equal(
            FundTradeIds.FundAccount,
            preview.Scope.FundAccountId);

        Assert.Equal(
            FundTradeIds.CashAccount,
            preview.Scope.CashAccountId);

        Assert.Equal(
            "Investment account",
            preview.FundAccountName);

        Assert.Equal(
            "Cash account",
            preview.CashAccountName);
    }

    /*
     * The central double-counting guard. A cost already inside the entered
     * consideration must not also be deducted again, and an additional
     * outflow must be deducted exactly once.
     */
    [Fact]
    public async Task Preview_IncludedCost_DoesNotMoveCashTwice()
    {
        var preview =
            await PreviewAsync(
                Command() with
                {
                    Costs =
                    [
                        new FundTradeCostInput(
                            CostType.Commission,
                            CostTreatment.IncludedInConsideration,
                            Money.FromMinorUnits(
                                5_00,
                                FundTradeIds.Try))
                    ]
                });

        // Cash moves by the consideration alone.
        Assert.Equal(
            -1_000_00,
            preview.Economics.NetCashEffect.MinorUnits);

        // The lot cost is the consideration, not consideration plus the
        // commission that is already inside it.
        Assert.Equal(
            1_000_00,
            preview.AcquisitionLotCostMinorUnits);

        Assert.Equal(
            0,
            preview.Economics
                .AdditionalCashOutflowTotal.MinorUnits);
    }

    [Fact]
    public async Task Preview_AdditionalCost_MovesCashOnceAndRaisesLotCost()
    {
        var preview =
            await PreviewAsync(
                Command() with
                {
                    Costs =
                    [
                        new FundTradeCostInput(
                            CostType.Commission,
                            CostTreatment.AdditionalCashOutflow,
                            Money.FromMinorUnits(
                                5_00,
                                FundTradeIds.Try))
                    ],

                    // The entered consideration now equals the gross price
                    // exactly, because the commission is settled separately.
                    CashConsideration =
                        Money.FromMinorUnits(
                            1_000_00,
                            FundTradeIds.Try)
                });

        Assert.Equal(
            -1_005_00,
            preview.Economics.NetCashEffect.MinorUnits);

        Assert.Equal(
            1_005_00,
            preview.AcquisitionLotCostMinorUnits);

        Assert.Equal(
            5_00,
            preview.Economics.AdditionalFeeTotal.MinorUnits);

        Assert.Equal(
            0,
            preview.Economics.AdditionalTaxTotal.MinorUnits);
    }

    [Fact]
    public async Task Preview_InformationalCost_ChangesNothing()
    {
        var preview =
            await PreviewAsync(
                Command() with
                {
                    Costs =
                    [
                        new FundTradeCostInput(
                            CostType.Other,
                            CostTreatment.InformationalOnly,
                            Money.FromMinorUnits(
                                7_00,
                                FundTradeIds.Try))
                    ]
                });

        Assert.Equal(
            -1_000_00,
            preview.Economics.NetCashEffect.MinorUnits);

        Assert.Equal(
            1_000_00,
            preview.AcquisitionLotCostMinorUnits);
    }

    [Fact]
    public async Task Preview_TaxCost_AggregatesIntoTaxTotal()
    {
        var preview =
            await PreviewAsync(
                Command() with
                {
                    Costs =
                    [
                        new FundTradeCostInput(
                            CostType.OtherTax,
                            CostTreatment.AdditionalCashOutflow,
                            Money.FromMinorUnits(
                                2_00,
                                FundTradeIds.Try))
                    ]
                });

        Assert.Equal(
            2_00,
            preview.Economics.AdditionalTaxTotal.MinorUnits);

        Assert.Equal(
            0,
            preview.Economics.AdditionalFeeTotal.MinorUnits);
    }

    [Fact]
    public async Task Preview_PurchaseWithheldFromProceeds_IsRejected()
    {
        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () =>
                    PreviewAsync(
                        Command() with
                        {
                            Costs =
                            [
                                new FundTradeCostInput(
                                    CostType.Commission,
                                    CostTreatment.WithheldFromProceeds,
                                    Money.FromMinorUnits(
                                        1_00,
                                        FundTradeIds.Try))
                            ]
                        }));

        Assert.Equal(
            FundTradeErrorCodes.CostTreatmentNotSupported,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Preview_NonFundCostType_IsRejected()
    {
        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () =>
                    PreviewAsync(
                        Command() with
                        {
                            Costs =
                            [
                                new FundTradeCostInput(
                                    CostType.MakingCharge,
                                    CostTreatment.AdditionalCashOutflow,
                                    Money.FromMinorUnits(
                                        1_00,
                                        FundTradeIds.Try))
                            ]
                        }));

        Assert.Equal(
            FundTradeErrorCodes.CostTypeNotSupported,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Preview_DuplicateCostComponent_IsRejected()
    {
        var duplicate =
            new FundTradeCostInput(
                CostType.Commission,
                CostTreatment.AdditionalCashOutflow,
                Money.FromMinorUnits(
                    1_00,
                    FundTradeIds.Try));

        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () =>
                    PreviewAsync(
                        Command() with
                        {
                            Costs = [duplicate, duplicate]
                        }));

        Assert.Equal(
            FundTradeErrorCodes.CostDuplicate,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Preview_CostOrder_DoesNotChangeCanonicalForm()
    {
        var commission =
            new FundTradeCostInput(
                CostType.Commission,
                CostTreatment.AdditionalCashOutflow,
                Money.FromMinorUnits(1_00, FundTradeIds.Try));

        var tax =
            new FundTradeCostInput(
                CostType.OtherTax,
                CostTreatment.AdditionalCashOutflow,
                Money.FromMinorUnits(2_00, FundTradeIds.Try));

        var first =
            await PreviewAsync(
                Command() with { Costs = [commission, tax] });

        var second =
            await PreviewAsync(
                Command() with { Costs = [tax, commission] });

        Assert.Equal(
            first.Costs.Select(x => x.Type),
            second.Costs.Select(x => x.Type));
    }

    [Fact]
    public async Task Preview_TooManyCosts_IsRejected()
    {
        var costs =
            Enumerable.Range(1, 17)
                .Select(index =>
                    new FundTradeCostInput(
                        CostType.Other,
                        CostTreatment.AdditionalCashOutflow,
                        Money.FromMinorUnits(
                            index,
                            FundTradeIds.Try)))
                .ToList();

        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () =>
                    PreviewAsync(
                        Command() with { Costs = costs }));

        Assert.Equal(
            FundTradeErrorCodes.CostLimitExceeded,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Preview_OneMinorUnitDifference_IsRoundingConsistent()
    {
        var preview =
            await PreviewAsync(
                Command() with
                {
                    CashConsideration =
                        Money.FromMinorUnits(
                            1_000_01,
                            FundTradeIds.Try)
                });

        Assert.Equal(
            DiscrepancyClassification.RoundingConsistent,
            preview.Economics.Discrepancy);

        Assert.Contains(
            FundTradeWarningCodes.RoundingConsistent,
            preview.WarningCodes);
    }

    [Fact]
    public async Task Preview_MaterialDifferenceWithoutNote_IsRejected()
    {
        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () =>
                    PreviewAsync(
                        Command() with
                        {
                            CashConsideration =
                                Money.FromMinorUnits(
                                    1_500_00,
                                    FundTradeIds.Try),
                            Note = null,
                            ExternalReference = "REF-1"
                        }));

        Assert.Equal(
            FundTradeErrorCodes.UnexplainedDiscrepancy,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Preview_MaterialDifferenceWithNote_PostsSourceFacts()
    {
        var preview =
            await PreviewAsync(
                Command() with
                {
                    CashConsideration =
                        Money.FromMinorUnits(
                            1_500_00,
                            FundTradeIds.Try),
                    Note = "Broker applied a correction on the statement."
                });

        Assert.Contains(
            FundTradeWarningCodes.ExplainedDiscrepancy,
            preview.WarningCodes);

        // The entered fact is preserved, not normalized towards the price.
        Assert.Equal(
            1_500_00,
            preview.Economics.CashConsideration.MinorUnits);

        Assert.Equal(
            1_000_00,
            preview.Economics.PriceImpliedGross.MinorUnits);
    }

    [Fact]
    public async Task Preview_NoReferenceOrNote_IsRejected()
    {
        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () =>
                    PreviewAsync(
                        Command() with
                        {
                            ExternalReference = null,
                            Note = null
                        }));

        Assert.Equal(
            FundTradeErrorCodes.ProvenanceRequired,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Preview_FutureExecutionDate_IsRejected()
    {
        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () =>
                    PreviewAsync(
                        Command() with
                        {
                            ExecutionDate =
                                ExecutionDate.AddDays(30)
                        }));

        Assert.Equal(
            FundTradeErrorCodes.DateInFuture,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Preview_OrderDateAfterExecution_IsRejected()
    {
        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () =>
                    PreviewAsync(
                        Command() with
                        {
                            OrderDate = ExecutionDate.AddDays(1)
                        }));

        Assert.Equal(
            FundTradeErrorCodes.DateOrderInvalid,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Preview_SettlementBeforeExecution_IsRejected()
    {
        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () =>
                    PreviewAsync(
                        Command() with
                        {
                            SettlementDate =
                                ExecutionDate.AddDays(-1)
                        }));

        Assert.Equal(
            FundTradeErrorCodes.DateOrderInvalid,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Preview_ExecutionBeforeAccountOpening_IsRejected()
    {
        var store =
            FundTradeReferenceStoreFake.CreateValid();

        store.Accounts[FundTradeIds.FundAccount] =
            store.Accounts[FundTradeIds.FundAccount] with
            {
                OpenedOn = new DateOnly(2026, 6, 1)
            };

        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () =>
                    PreviewAsync(
                        Command(),
                        store));

        Assert.Equal(
            FundTradeErrorCodes.DateBeforeAccountOpening,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Preview_CashAccountInAnotherHousehold_IsRejected()
    {
        var store =
            FundTradeReferenceStoreFake.CreateValid();

        store.Accounts[FundTradeIds.CashAccount] =
            store.Accounts[FundTradeIds.CashAccount] with
            {
                HouseholdId =
                    Guid.Parse("99999999-0000-0000-0000-000000000001")
            };

        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () =>
                    PreviewAsync(Command(), store));

        Assert.Equal(
            FundTradeErrorCodes.HouseholdMismatch,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Preview_SavingsAccountForFund_IsRejected()
    {
        var store =
            FundTradeReferenceStoreFake.CreateValid();

        store.Accounts[FundTradeIds.FundAccount] =
            store.Accounts[FundTradeIds.FundAccount] with
            {
                Type = WealthLedger.Domain.Portfolios.AccountType.Cash
            };

        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () =>
                    PreviewAsync(Command(), store));

        Assert.Equal(
            FundTradeErrorCodes.ReferenceShapeInvalid,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Preview_MismatchedFundCurrency_IsRejected()
    {
        var store =
            FundTradeReferenceStoreFake.CreateValid();

        store.Assets[FundTradeIds.FundAsset] =
            store.Assets[FundTradeIds.FundAsset] with
            {
                BaseCurrency = new CurrencyCode("USD")
            };

        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () =>
                    PreviewAsync(Command(), store));

        Assert.Equal(
            FundTradeErrorCodes.CurrencyMismatch,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Preview_FundWithoutBaseCurrency_IsRejected()
    {
        var store =
            FundTradeReferenceStoreFake.CreateValid();

        store.Assets[FundTradeIds.FundAsset] =
            store.Assets[FundTradeIds.FundAsset] with
            {
                BaseCurrency = null
            };

        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () =>
                    PreviewAsync(Command(), store));

        Assert.Equal(
            FundTradeErrorCodes.ReferenceShapeInvalid,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Preview_NegativeProjectedCash_WarnsWithoutRejecting()
    {
        var custody =
            new FundLotCustodyStoreFake
            {
                CashPositionRawE8 = 0
            };

        var preview =
            await PreviewAsync(
                Command(),
                FundTradeReferenceStoreFake.CreateValid(),
                custody);

        Assert.Contains(
            FundTradeWarningCodes.NegativeProjectedCash,
            preview.WarningCodes);
    }

    [Fact]
    public async Task Preview_WritesNothing()
    {
        var custody = new FundLotCustodyStoreFake();

        await PreviewAsync(
            Command(),
            FundTradeReferenceStoreFake.CreateValid(),
            custody);

        // The custody fake is read-only by construction; the meaningful
        // assertion is that preview needs no submission store at all, which
        // the constructor signature already enforces.
        Assert.Empty(custody.Candidates);
    }

    internal static FundPurchaseCommand Command()
        => new(
            FundTradeIds.Household,
            FundTradeIds.Portfolio,
            FundTradeIds.FundAccount,
            FundTradeIds.CashAccount,
            FundTradeIds.FundAsset,
            FundTradeIds.CashAsset,
            Quantity.FromDecimal(100m),
            UnitPrice.FromDecimal(10m, FundTradeIds.Try),
            Money.FromMinorUnits(1_000_00, FundTradeIds.Try),
            ExecutionDate,
            ExternalReference: "REF-1",
            Note: "Monthly fund purchase.");

    private static Task<FundPurchasePreview> PreviewAsync(
        FundPurchaseCommand command,
        FundTradeReferenceStoreFake? references = null,
        FundLotCustodyStoreFake? custody = null)
    {
        var useCase =
            new PreviewFundPurchaseUseCase(
                references
                    ?? FundTradeReferenceStoreFake.CreateValid(),
                custody ?? new FundLotCustodyStoreFake(),
                new FundTradeTimeProvider(
                    new DateTimeOffset(
                        2026, 3, 12, 9, 0, 0, TimeSpan.Zero)));

        return useCase.ExecuteAsync(command);
    }
}

public sealed class FundSalePreviewTests
{
    [Fact]
    public async Task Preview_ConsumesOldestScopedLotsFirst()
    {
        var custody =
            new FundLotCustodyStoreFake()
                .WithLot(
                    "aaaaaaaa-0000-0000-0000-000000000002",
                    available: 60m,
                    acquiredOn: new DateOnly(2026, 2, 1),
                    CostBasis.Known(
                        Money.FromMinorUnits(
                            600_00,
                            FundTradeIds.Try)))
                .WithLot(
                    "aaaaaaaa-0000-0000-0000-000000000001",
                    available: 40m,
                    acquiredOn: new DateOnly(2026, 1, 1),
                    CostBasis.Known(
                        Money.FromMinorUnits(
                            400_00,
                            FundTradeIds.Try)));

        var preview =
            await PreviewAsync(
                Command(quantity: 70m),
                custody);

        Assert.Equal(2, preview.Plan.Count);

        Assert.Equal(
            new DateOnly(2026, 1, 1),
            preview.Plan[0].AcquiredOn);

        Assert.Equal(
            40_00000000L,
            preview.Plan[0].ConsumedQuantityRawE8);

        Assert.Equal(
            30_00000000L,
            preview.Plan[1].ConsumedQuantityRawE8);
    }

    [Fact]
    public async Task Preview_InsufficientScopedQuantity_IsRejected()
    {
        var custody =
            new FundLotCustodyStoreFake()
                .WithLot(
                    "aaaaaaaa-0000-0000-0000-000000000001",
                    available: 10m,
                    acquiredOn: new DateOnly(2026, 1, 1),
                    CostBasis.Unknown());

        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () =>
                    PreviewAsync(
                        Command(quantity: 11m),
                        custody));

        Assert.Equal(
            FundTradeErrorCodes.InsufficientFundQuantity,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Preview_UnknownCostLot_ReportsUnknownCompleteness()
    {
        var custody =
            new FundLotCustodyStoreFake()
                .WithLot(
                    "aaaaaaaa-0000-0000-0000-000000000001",
                    available: 100m,
                    acquiredOn: null,
                    CostBasis.Unknown());

        var preview =
            await PreviewAsync(
                Command(quantity: 50m),
                custody);

        Assert.Equal(
            RealizedCostCompleteness.Unknown,
            preview.RealizedCost.Completeness);

        Assert.Empty(
            preview.RealizedCost.KnownAmounts);

        Assert.Contains(
            FundTradeWarningCodes.IncompleteRealizedCost,
            preview.WarningCodes);

        Assert.Contains(
            FundTradeWarningCodes
                .UnknownAcquisitionDateOrdering,
            preview.WarningCodes);
    }

    [Fact]
    public async Task Preview_MixedLots_ReportsPartiallyKnown()
    {
        var custody =
            new FundLotCustodyStoreFake()
                .WithLot(
                    "aaaaaaaa-0000-0000-0000-000000000001",
                    available: 40m,
                    acquiredOn: null,
                    CostBasis.Unknown())
                .WithLot(
                    "aaaaaaaa-0000-0000-0000-000000000002",
                    available: 60m,
                    acquiredOn: new DateOnly(2026, 2, 1),
                    CostBasis.Known(
                        Money.FromMinorUnits(
                            600_00,
                            FundTradeIds.Try)));

        var preview =
            await PreviewAsync(
                Command(quantity: 70m),
                custody);

        Assert.Equal(
            RealizedCostCompleteness.PartiallyKnown,
            preview.RealizedCost.Completeness);

        Assert.Equal(
            40_00000000L,
            preview.RealizedCost.UnknownQuantityRawE8);

        Assert.Equal(
            30_00000000L,
            preview.RealizedCost.KnownQuantityRawE8);
    }

    [Fact]
    public async Task Preview_SamePlan_ProducesStableFingerprint()
    {
        var custody =
            new FundLotCustodyStoreFake()
                .WithLot(
                    "aaaaaaaa-0000-0000-0000-000000000001",
                    available: 100m,
                    acquiredOn: new DateOnly(2026, 1, 1),
                    CostBasis.Known(
                        Money.FromMinorUnits(
                            1_000_00,
                            FundTradeIds.Try)));

        var first =
            await PreviewAsync(Command(quantity: 10m), custody);

        var second =
            await PreviewAsync(Command(quantity: 10m), custody);

        Assert.Equal(
            first.PlanFingerprint,
            second.PlanFingerprint);
    }

    [Fact]
    public async Task Preview_DifferentPlan_ProducesDifferentFingerprint()
    {
        var custody =
            new FundLotCustodyStoreFake()
                .WithLot(
                    "aaaaaaaa-0000-0000-0000-000000000001",
                    available: 100m,
                    acquiredOn: new DateOnly(2026, 1, 1),
                    CostBasis.Known(
                        Money.FromMinorUnits(
                            1_000_00,
                            FundTradeIds.Try)));

        var first =
            await PreviewAsync(Command(quantity: 10m), custody);

        var second =
            await PreviewAsync(Command(quantity: 20m), custody);

        Assert.NotEqual(
            first.PlanFingerprint,
            second.PlanFingerprint);
    }

    [Fact]
    public async Task Preview_SaleWithheldFromProceeds_ReducesExpectation()
    {
        var custody =
            new FundLotCustodyStoreFake()
                .WithLot(
                    "aaaaaaaa-0000-0000-0000-000000000001",
                    available: 100m,
                    acquiredOn: new DateOnly(2026, 1, 1),
                    CostBasis.Known(
                        Money.FromMinorUnits(
                            1_000_00,
                            FundTradeIds.Try)));

        var preview =
            await PreviewAsync(
                Command(quantity: 10m) with
                {
                    CashConsideration =
                        Money.FromMinorUnits(
                            95_00,
                            FundTradeIds.Try),

                    Costs =
                    [
                        new FundTradeCostInput(
                            CostType.WithholdingTax,
                            CostTreatment.WithheldFromProceeds,
                            Money.FromMinorUnits(
                                5_00,
                                FundTradeIds.Try))
                    ]
                },
                custody);

        Assert.Equal(
            DiscrepancyClassification.Exact,
            preview.Economics.Discrepancy);

        // Withheld tax never creates a second cash entry.
        Assert.Equal(
            95_00,
            preview.Economics.NetCashEffect.MinorUnits);
    }

    [Fact]
    public async Task Preview_CostsExceedingProceeds_IsRejected()
    {
        var custody =
            new FundLotCustodyStoreFake()
                .WithLot(
                    "aaaaaaaa-0000-0000-0000-000000000001",
                    available: 100m,
                    acquiredOn: new DateOnly(2026, 1, 1),
                    CostBasis.Unknown());

        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () =>
                    PreviewAsync(
                        Command(quantity: 10m) with
                        {
                            Note = "Explained.",
                            Costs =
                            [
                                new FundTradeCostInput(
                                    CostType.WithholdingTax,
                                    CostTreatment.WithheldFromProceeds,
                                    Money.FromMinorUnits(
                                        200_00,
                                        FundTradeIds.Try))
                            ]
                        },
                        custody));

        Assert.Equal(
            FundTradeErrorCodes.NegativeExpectedProceeds,
            exception.ErrorCode);
    }

    internal static FundSaleCommand Command(decimal quantity)
        => new(
            FundTradeIds.Household,
            FundTradeIds.Portfolio,
            FundTradeIds.FundAccount,
            FundTradeIds.CashAccount,
            FundTradeIds.FundAsset,
            FundTradeIds.CashAsset,
            Quantity.FromDecimal(quantity),
            UnitPrice.FromDecimal(10m, FundTradeIds.Try),
            Money.FromMinorUnits(
                (long)(quantity * 10m * 100m),
                FundTradeIds.Try),
            new DateOnly(2026, 3, 10),
            ExternalReference: "SALE-1",
            Note: "Partial liquidation.");

    private static Task<FundSalePreview> PreviewAsync(
        FundSaleCommand command,
        FundLotCustodyStoreFake custody)
    {
        var useCase =
            new PreviewFundSaleUseCase(
                FundTradeReferenceStoreFake.CreateValid(),
                custody,
                new LotAllocationService(),
                new FundTradeTimeProvider(
                    new DateTimeOffset(
                        2026, 3, 12, 9, 0, 0, TimeSpan.Zero)));

        return useCase.ExecuteAsync(command);
    }
}
