using System.Globalization;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.FundTrades;
using WealthLedger.Application.Inventory;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Application.PhysicalGold;
using WealthLedger.Application.Positions;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Tests.Persistence;

/// <summary>
/// Derives M010 inventory from history posted through the real M007 opening,
/// M008 Fund-trade, M009 physical-gold and M003 reversal use cases, and
/// cross-checks it against the verified M005 position and M009 custody reads.
/// </summary>
public sealed class InventoryLifecycleTests
{
    private static readonly Guid SourceVaultId =
        Guid.Parse("50000000-0000-0000-0000-0000000000a1");
    private static readonly Guid DestinationVaultId =
        Guid.Parse("50000000-0000-0000-0000-0000000000a2");
    private static readonly Guid EquityAssetId =
        Guid.Parse("60000000-0000-0000-0000-0000000000a1");
    private static readonly Guid UsdAssetId =
        Guid.Parse("60000000-0000-0000-0000-0000000000a2");
    private static readonly DateTimeOffset Now =
        new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly OpeningDate = new(2026, 9, 1);
    private static readonly DateOnly FundPurchaseDate = new(2026, 9, 5);
    private static readonly DateOnly FundSaleDate = new(2026, 9, 10);
    private static readonly DateOnly GoldPurchaseDate = new(2026, 9, 12);
    private static readonly DateOnly GoldSaleDate = new(2026, 9, 15);
    private static readonly DateOnly GoldTransferDate = new(2026, 9, 18);
    private const long Unit = 100_000_000L;

    [Fact]
    public async Task PositionInventory_MixedLifecycle_MatchesPostedHistoryCurrentAndAsOf()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var history = await SeedLifecycleAsync(database);

        await using var context = database.CreateContext();
        var useCase = new ListPositionInventoryUseCase(
            new EfCoreInventoryReadStore(context));
        var current = await useCase.ExecuteAsync(
            new ListPositionInventoryQuery(CoreLedgerTestData.HouseholdId));
        var afterOpening = await useCase.ExecuteAsync(
            new ListPositionInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                AsOf: FundPurchaseDate.AddDays(-1)));
        var beforeGoldSale = await useCase.ExecuteAsync(
            new ListPositionInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                AsOf: GoldSaleDate.AddDays(-1)));
        var onGoldSaleAfterReversal = await useCase.ExecuteAsync(
            new ListPositionInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                AsOf: GoldSaleDate));
        var institutionScoped = await useCase.ExecuteAsync(
            new ListPositionInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                InstitutionId: CoreLedgerTestData.InstitutionId));

        // Cash: +1,000.00 opening, -300.00 Fund purchase, +150.00 Fund
        // sale, -200.00 gold purchase, +100.00 gold sale, -100.00 reversal.
        Assert.Equal(650 * Unit, QuantityOf(current, CashScope));
        Assert.Equal(250 * Unit, QuantityOf(current, UsdScope));
        Assert.Equal(15 * Unit, QuantityOf(current, FundScope));
        Assert.Equal(5 * Unit, QuantityOf(current, EquityScope));
        Assert.Equal(18 * Unit, QuantityOf(current, SourceGoldScope));
        Assert.Equal(12 * Unit, QuantityOf(current, DestinationGoldScope));
        Assert.Equal(6, current.Items.Count);

        var fund = Single(current, FundScope);
        Assert.Equal(3, fund.SourceEntryCount);
        Assert.Equal(
            new[]
            {
                history.FundOpeningTransactionId,
                history.FundPurchaseTransactionId,
                history.FundSaleTransactionId
            },
            fund.SourceTransactionIds);
        Assert.Equal(CoreLedgerTestData.InstitutionId, fund.InstitutionId);
        Assert.Equal("Test Institution", fund.InstitutionName);
        Assert.Equal(AssetType.Fund, fund.AssetType);
        Assert.Null(Single(current, SourceGoldScope).InstitutionId);

        Assert.Equal(1_000 * Unit, QuantityOf(afterOpening, CashScope));
        Assert.Equal(10 * Unit, QuantityOf(afterOpening, FundScope));
        Assert.Equal(10 * Unit, QuantityOf(afterOpening, SourceGoldScope));
        Assert.Equal(5, afterOpening.Items.Count);
        Assert.Equal(FundPurchaseDate.AddDays(-1), afterOpening.AsOf);

        Assert.Equal(650 * Unit, QuantityOf(beforeGoldSale, CashScope));
        Assert.Equal(30 * Unit, QuantityOf(beforeGoldSale, SourceGoldScope));

        // The reversal keeps the sale's ExecutionDate, so the reconstruction
        // for that date now includes both and nets the sale out. The transfer
        // dated after it remains excluded.
        Assert.Equal(650 * Unit, QuantityOf(onGoldSaleAfterReversal, CashScope));
        Assert.Equal(
            30 * Unit,
            QuantityOf(onGoldSaleAfterReversal, SourceGoldScope));
        Assert.DoesNotContain(
            onGoldSaleAfterReversal.Items,
            item => item.AccountId == DestinationVaultId);
        Assert.Equal(
            22 * Unit,
            QuantityOf(history.GoldSaleDateBeforeReversal, SourceGoldScope));
        Assert.Equal(
            750 * Unit,
            QuantityOf(history.GoldSaleDateBeforeReversal, CashScope));

        Assert.Equal(4, institutionScoped.Items.Count);
        Assert.All(
            institutionScoped.Items,
            item => Assert.Equal(
                CoreLedgerTestData.InstitutionId,
                item.InstitutionId));

        var positions = new GetPositionUseCase(
            new EfCorePostedEntrySource(context),
            new EfCoreNavigationScopeReadStore(context));

        foreach (var item in current.Items)
        {
            var verified = await positions.ExecuteAsync(
                new GetPositionQuery(
                    CoreLedgerTestData.HouseholdId,
                    item.PortfolioId,
                    item.AccountId,
                    item.AssetId));

            Assert.Equal(verified.Quantity.RawE8, item.QuantityRawE8);
            Assert.Equal(verified.SourceEntryCount, item.SourceEntryCount);
        }
    }

    [Fact]
    public async Task LotInventory_MixedLifecycle_DerivesLineageCostAndCustody()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var history = await SeedLifecycleAsync(database);

        await using var context = database.CreateContext();
        var useCase = new ListLotInventoryUseCase(
            new EfCoreInventoryReadStore(context));
        var current = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(CoreLedgerTestData.HouseholdId));
        var withExhausted = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                IncludeExhausted: true));
        var afterOpening = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                AsOf: FundPurchaseDate.AddDays(-1)));
        var institutionScoped = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                InstitutionId: CoreLedgerTestData.InstitutionId));
        var destinationVault = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                AccountId: DestinationVaultId));

        Assert.Equal(
            new[]
            {
                history.EquityLotId,
                history.FundPurchaseLotId,
                history.GoldOpeningLotId,
                history.GoldPurchaseLotId
            }.Order().ToArray(),
            current.Items.Select(item => item.AssetLotId).Order().ToArray());

        // FIFO consumed the whole opening lot before the purchase lot.
        var exhausted = Assert.Single(
            withExhausted.Items,
            item => item.AssetLotId == history.FundOpeningLotId);
        Assert.Equal(10 * Unit, exhausted.OriginalQuantityRawE8);
        Assert.Equal(0, exhausted.GlobalQuantityRawE8);
        Assert.Empty(exhausted.Custody);
        Assert.Equal(CostBasisStatus.Known, exhausted.CostStatus);
        Assert.Equal(10_000, exhausted.CostMinorUnits);
        Assert.Equal(
            [TransactionType.OpeningBalance, TransactionType.Sell],
            exhausted.Allocations
                .Select(allocation => allocation.TransactionType)
                .ToArray());
        Assert.Equal(5, withExhausted.Items.Count);

        var purchased = Single(current, history.FundPurchaseLotId);
        Assert.Equal(history.FundPurchaseTransactionId, purchased.CreatingTransactionId);
        Assert.Equal(20 * Unit, purchased.OriginalQuantityRawE8);
        Assert.Equal(15 * Unit, purchased.GlobalQuantityRawE8);
        Assert.Equal(CostBasisStatus.Known, purchased.CostStatus);
        Assert.Equal(30_000, purchased.CostMinorUnits);
        Assert.Equal("TRY", purchased.CostCurrencyCode);
        var purchasedCustody = Assert.Single(purchased.Custody);
        Assert.Equal(CoreLedgerTestData.AccountId, purchasedCustody.AccountId);
        Assert.Equal(15 * Unit, purchasedCustody.QuantityRawE8);
        Assert.Null(purchasedCustody.PieceCount);
        Assert.Null(purchased.PhysicalGold);

        var equity = Single(current, history.EquityLotId);
        Assert.Equal(AssetType.Equity, equity.AssetType);
        Assert.Equal(5 * Unit, equity.GlobalQuantityRawE8);
        Assert.Equal(CostBasisStatus.Unknown, equity.CostStatus);
        Assert.Null(equity.CostMinorUnits);
        Assert.Null(equity.CostCurrencyCode);

        var goldOpening = Single(current, history.GoldOpeningLotId);
        Assert.Equal(CostBasisStatus.Unknown, goldOpening.CostStatus);
        Assert.Equal(10 * Unit, goldOpening.GlobalQuantityRawE8);
        Assert.Equal(1, goldOpening.PhysicalGold!.GlobalPieceCount);
        Assert.Equal(9.16m, goldOpening.PhysicalGold.GlobalFineWeightGrams);

        var goldPurchase = Single(current, history.GoldPurchaseLotId);
        Assert.Equal(20 * Unit, goldPurchase.OriginalQuantityRawE8);
        Assert.Equal(20 * Unit, goldPurchase.GlobalQuantityRawE8);
        var gold = Assert.IsType<PhysicalGoldLotInventoryDetail>(
            goldPurchase.PhysicalGold);
        Assert.Equal(916_000, gold.FinenessPpm);
        Assert.Equal(2, gold.OriginalPieceCount);
        Assert.Equal(2, gold.GlobalPieceCount);
        Assert.Equal(18.32m, gold.GlobalFineWeightGrams);
        Assert.Equal(
            [
                TransactionType.Buy,
                TransactionType.Sell,
                TransactionType.Reversal,
                TransactionType.Transfer,
                TransactionType.Transfer
            ],
            goldPurchase.Allocations
                .Select(allocation => allocation.TransactionType)
                .ToArray());
        var source = Assert.Single(
            goldPurchase.Custody,
            item => item.AccountId == SourceVaultId);
        Assert.Equal(8 * Unit, source.QuantityRawE8);
        Assert.Equal(1, source.PieceCount);
        Assert.Equal(7.328m, source.FineWeightGrams);
        var destination = Assert.Single(
            goldPurchase.Custody,
            item => item.AccountId == DestinationVaultId);
        Assert.Equal(12 * Unit, destination.QuantityRawE8);
        Assert.Equal(1, destination.PieceCount);
        Assert.Equal(10.992m, destination.FineWeightGrams);

        Assert.Equal(
            new[]
            {
                history.EquityLotId,
                history.FundOpeningLotId,
                history.GoldOpeningLotId
            }.Order().ToArray(),
            afterOpening.Items.Select(item => item.AssetLotId).Order().ToArray());
        Assert.Equal(
            10 * Unit,
            Single(afterOpening, history.FundOpeningLotId).GlobalQuantityRawE8);

        Assert.Equal(
            new[] { history.EquityLotId, history.FundPurchaseLotId }.Order().ToArray(),
            institutionScoped.Items.Select(item => item.AssetLotId).Order().ToArray());
        Assert.Equal(
            history.GoldPurchaseLotId,
            Assert.Single(destinationVault.Items).AssetLotId);

        var m009Custody = await new GetPhysicalGoldCustodyInventoryUseCase(
                new EfCorePhysicalGoldVerificationReadStore(context))
            .ExecuteAsync(CoreLedgerTestData.HouseholdId);
        var m010Custody = current.Items
            .Where(item => item.PhysicalGold is not null)
            .SelectMany(
                item => item.Custody.Select(
                    custody => (item.AssetLotId, Custody: custody)))
            .ToArray();

        Assert.Equal(m009Custody.Items.Count, m010Custody.Length);

        foreach (var expected in m009Custody.Items)
        {
            var (_, actual) = Assert.Single(
                m010Custody,
                pair => pair.AssetLotId == expected.AssetLotId
                        && pair.Custody.AccountId == expected.AccountId);

            Assert.Equal(expected.GrossWeightRawE8, actual.QuantityRawE8);
            Assert.Equal(expected.PieceCount, actual.PieceCount);
            Assert.Equal(
                expected.FineWeightGrams.ToString(CultureInfo.InvariantCulture),
                actual.FineWeightGrams!.Value.ToString(
                    CultureInfo.InvariantCulture));
        }
    }

    private static (Guid AccountId, Guid AssetId) CashScope
        => (CoreLedgerTestData.DestinationAccountId, CoreLedgerTestData.CashAssetId);

    private static (Guid AccountId, Guid AssetId) UsdScope
        => (CoreLedgerTestData.AccountId, UsdAssetId);

    private static (Guid AccountId, Guid AssetId) FundScope
        => (CoreLedgerTestData.AccountId, CoreLedgerTestData.FundAssetId);

    private static (Guid AccountId, Guid AssetId) EquityScope
        => (CoreLedgerTestData.AccountId, EquityAssetId);

    private static (Guid AccountId, Guid AssetId) SourceGoldScope
        => (SourceVaultId, CoreLedgerTestData.GoldAssetId);

    private static (Guid AccountId, Guid AssetId) DestinationGoldScope
        => (DestinationVaultId, CoreLedgerTestData.GoldAssetId);

    private static PositionInventoryItem Single(
        PositionInventory inventory,
        (Guid AccountId, Guid AssetId) scope)
        => Assert.Single(
            inventory.Items,
            item => item.PortfolioId == CoreLedgerTestData.PortfolioId
                    && item.AccountId == scope.AccountId
                    && item.AssetId == scope.AssetId);

    private static long QuantityOf(
        PositionInventory inventory,
        (Guid AccountId, Guid AssetId) scope)
        => Single(inventory, scope).QuantityRawE8;

    private static LotInventoryItem Single(LotInventory inventory, Guid lotId)
        => Assert.Single(inventory.Items, item => item.AssetLotId == lotId);

    private static async Task<LifecycleHistory> SeedLifecycleAsync(
        SqliteTestDatabase database)
    {
        await SeedMasterDataAsync(database);

        Guid fundOpeningTransactionId;
        Guid fundOpeningLotId;
        Guid equityLotId;
        Guid goldOpeningLotId;

        await using (var context = database.CreateContext())
        {
            var opening = OpeningRecorder(context);
            await opening.ExecuteAsync(
                "inventory-cash-opening",
                Opening(
                    CoreLedgerTestData.DestinationAccountId,
                    CoreLedgerTestData.CashAssetId,
                    1_000m,
                    []));
            await opening.ExecuteAsync(
                "inventory-usd-opening",
                Opening(CoreLedgerTestData.AccountId, UsdAssetId, 250m, []));
            var fundOpening = await opening.ExecuteAsync(
                "inventory-fund-opening",
                Opening(
                    CoreLedgerTestData.AccountId,
                    CoreLedgerTestData.FundAssetId,
                    10m,
                    [
                        new OpeningBalanceLotCommand(
                            Quantity.FromDecimal(10m),
                            new DateOnly(2026, 8, 15),
                            CostBasis.Known(
                                Money.FromMinorUnits(10_000, CurrencyCode.TRY)))
                    ]));
            fundOpeningTransactionId = fundOpening.TransactionId;
            fundOpeningLotId = Assert.Single(fundOpening.AssetLotIds);
            equityLotId = Assert.Single(
                (await opening.ExecuteAsync(
                    "inventory-equity-opening",
                    Opening(
                        CoreLedgerTestData.AccountId,
                        EquityAssetId,
                        5m,
                        [
                            new OpeningBalanceLotCommand(
                                Quantity.FromDecimal(5m),
                                AcquiredOn: null,
                                CostBasis.Unknown())
                        ]))).AssetLotIds);
            goldOpeningLotId = Assert.Single(
                (await opening.ExecuteAsync(
                    "inventory-gold-opening",
                    Opening(
                        SourceVaultId,
                        CoreLedgerTestData.GoldAssetId,
                        10m,
                        [
                            new OpeningBalanceLotCommand(
                                Quantity.FromDecimal(10m),
                                AcquiredOn: null,
                                CostBasis.Unknown(),
                                new PhysicalGoldLotDetail(
                                    new Fineness(916_000),
                                    pieceCount: 1))
                        ]))).AssetLotIds);
        }

        Guid fundPurchaseTransactionId;
        Guid fundPurchaseLotId;

        await using (var context = database.CreateContext())
        {
            var posting = new EfCoreLedgerPostingStore(context);
            var purchase = await new RecordFundPurchaseUseCase(
                    new EfCoreOpeningBalanceReferenceStore(context),
                    posting,
                    posting,
                    new FixedTimeProvider(Now))
                .ExecuteAsync(
                    "inventory-fund-purchase",
                    new RecordFundPurchaseCommand(
                        CoreLedgerTestData.HouseholdId,
                        CoreLedgerTestData.PortfolioId,
                        CoreLedgerTestData.AccountId,
                        CoreLedgerTestData.FundAssetId,
                        CoreLedgerTestData.CashAssetId,
                        Quantity.FromDecimal(20m),
                        UnitPrice.FromDecimal(15m, CurrencyCode.TRY),
                        Money.FromMinorUnits(30_000, CurrencyCode.TRY),
                        FundPurchaseDate,
                        ExternalReference: "SYNTHETIC-INVENTORY-FUND-BUY",
                        Note: "Synthetic inventory fund purchase.",
                        CashAccountId: CoreLedgerTestData.DestinationAccountId));
            fundPurchaseTransactionId = purchase.TransactionId;
            fundPurchaseLotId = purchase.AssetLotId;
        }

        Guid fundSaleTransactionId;

        await using (var context = database.CreateContext())
        {
            var posting = new EfCoreLedgerPostingStore(context);
            var command = new FundSaleCommand(
                CoreLedgerTestData.HouseholdId,
                CoreLedgerTestData.PortfolioId,
                CoreLedgerTestData.AccountId,
                CoreLedgerTestData.DestinationAccountId,
                CoreLedgerTestData.FundAssetId,
                CoreLedgerTestData.CashAssetId,
                Quantity.FromDecimal(15m),
                UnitPrice.FromDecimal(10m, CurrencyCode.TRY),
                Money.FromMinorUnits(15_000, CurrencyCode.TRY),
                FundSaleDate,
                ExternalReference: "SYNTHETIC-INVENTORY-FUND-SELL",
                Note: "Synthetic inventory fund sale.");
            var preview = await new PreviewFundSaleUseCase(
                    new EfCoreOpeningBalanceReferenceStore(context),
                    new EfCoreFundLotCustodyReadStore(posting),
                    new EfCoreFundRealizedCostReadStore(context),
                    new LotAllocationService(),
                    new FixedTimeProvider(Now))
                .ExecuteAsync(command);

            Assert.Equal(
                [fundOpeningLotId, fundPurchaseLotId],
                preview.Plan.Select(line => line.AssetLotId).ToArray());

            fundSaleTransactionId = (await new RecordFundSaleUseCase(
                    new EfCoreOpeningBalanceReferenceStore(context),
                    posting,
                    posting,
                    new FixedTimeProvider(Now))
                .ExecuteAsync(
                    "inventory-fund-sale",
                    command with
                    {
                        ReviewedPlan = preview.Plan
                            .Select(
                                line => new ReviewedLotAllocation(
                                    line.AssetLotId,
                                    Quantity.FromRaw(
                                        line.ConsumedQuantityRawE8)))
                            .ToArray(),
                        ReviewedPlanFingerprint = preview.PlanFingerprint
                    })).TransactionId;
        }

        Guid goldPurchaseLotId;

        await using (var context = database.CreateContext())
        {
            var posting = new EfCoreLedgerPostingStore(context);
            goldPurchaseLotId = (await new RecordPhysicalGoldPurchaseUseCase(
                    new EfCoreOpeningBalanceReferenceStore(context),
                    posting,
                    posting,
                    new FixedTimeProvider(Now))
                .ExecuteAsync(
                    "inventory-gold-purchase",
                    new PhysicalGoldPurchaseCommand(
                        CoreLedgerTestData.HouseholdId,
                        CoreLedgerTestData.PortfolioId,
                        SourceVaultId,
                        CoreLedgerTestData.DestinationAccountId,
                        CoreLedgerTestData.GoldAssetId,
                        CoreLedgerTestData.CashAssetId,
                        Quantity.FromDecimal(20m),
                        new Fineness(916_000),
                        2,
                        Money.FromMinorUnits(20_000, CurrencyCode.TRY),
                        GoldPurchaseDate,
                        ExternalReference: "SYNTHETIC-INVENTORY-GOLD-BUY",
                        Note: "Synthetic inventory gold purchase.")))
                .AssetLotId;
        }

        Guid goldSaleTransactionId;

        await using (var context = database.CreateContext())
        {
            var posting = new EfCoreLedgerPostingStore(context);
            var command = new PhysicalGoldSaleCommand(
                CoreLedgerTestData.HouseholdId,
                CoreLedgerTestData.PortfolioId,
                SourceVaultId,
                CoreLedgerTestData.DestinationAccountId,
                CoreLedgerTestData.GoldAssetId,
                CoreLedgerTestData.CashAssetId,
                Quantity.FromDecimal(8m),
                1,
                [
                    new PhysicalGoldSelectedLot(
                        goldPurchaseLotId,
                        Quantity.FromDecimal(8m),
                        1)
                ],
                Money.FromMinorUnits(10_000, CurrencyCode.TRY),
                GoldSaleDate,
                ExternalReference: "SYNTHETIC-INVENTORY-GOLD-SELL",
                Note: "Synthetic inventory gold sale.");
            var preview = await new PreviewPhysicalGoldSaleUseCase(
                    new EfCoreOpeningBalanceReferenceStore(context),
                    new EfCorePhysicalGoldCustodyReadStore(posting),
                    new EfCorePhysicalGoldRealizedCostReadStore(context),
                    new FixedTimeProvider(Now))
                .ExecuteAsync(command);
            goldSaleTransactionId = (await new RecordPhysicalGoldSaleUseCase(
                    new EfCoreOpeningBalanceReferenceStore(context),
                    posting,
                    posting,
                    new FixedTimeProvider(Now))
                .ExecuteAsync(
                    "inventory-gold-sale",
                    command with
                    {
                        ReviewedPlanFingerprint = preview.PlanFingerprint
                    })).TransactionId;
        }

        await using (var context = database.CreateContext())
        {
            var posting = new EfCoreLedgerPostingStore(context);
            var command = new PhysicalGoldTransferCommand(
                CoreLedgerTestData.HouseholdId,
                CoreLedgerTestData.PortfolioId,
                SourceVaultId,
                CoreLedgerTestData.PortfolioId,
                DestinationVaultId,
                CoreLedgerTestData.GoldAssetId,
                Quantity.FromDecimal(12m),
                1,
                [
                    new PhysicalGoldSelectedLot(
                        goldPurchaseLotId,
                        Quantity.FromDecimal(12m),
                        1)
                ],
                GoldTransferDate,
                ExternalReference: "SYNTHETIC-INVENTORY-GOLD-MOVE",
                Note: "Synthetic inventory custody transfer.");
            var preview = await new PreviewPhysicalGoldTransferUseCase(
                    new EfCoreOpeningBalanceReferenceStore(context),
                    new EfCorePhysicalGoldCustodyReadStore(posting),
                    new FixedTimeProvider(Now))
                .ExecuteAsync(command);
            await new RecordPhysicalGoldTransferUseCase(
                    new EfCoreOpeningBalanceReferenceStore(context),
                    posting,
                    posting,
                    new FixedTimeProvider(Now))
                .ExecuteAsync(
                    "inventory-gold-transfer",
                    command with
                    {
                        ReviewedPlanFingerprint = preview.PlanFingerprint
                    });
        }

        PositionInventory goldSaleDateBeforeReversal;

        await using (var context = database.CreateContext())
        {
            goldSaleDateBeforeReversal = await new ListPositionInventoryUseCase(
                    new EfCoreInventoryReadStore(context))
                .ExecuteAsync(
                    new ListPositionInventoryQuery(
                        CoreLedgerTestData.HouseholdId,
                        AsOf: GoldSaleDate));
        }

        await using (var context = database.CreateContext())
        {
            var reversal = await new ReversePostedTransactionUseCase(
                    new EfCoreLedgerReversalStore(context),
                    new FixedTimeProvider(Now.AddMinutes(1)))
                .ExecuteAsync(
                    "inventory-gold-sale-reversal",
                    new ReversePostedTransactionCommand(
                        goldSaleTransactionId,
                        "Synthetic incorrect gold sale."));
            Assert.NotNull(reversal);
        }

        return new LifecycleHistory(
            fundOpeningTransactionId,
            fundOpeningLotId,
            fundPurchaseTransactionId,
            fundPurchaseLotId,
            fundSaleTransactionId,
            equityLotId,
            goldOpeningLotId,
            goldPurchaseLotId,
            goldSaleDateBeforeReversal);
    }

    private static RecordOpeningBalanceCommand Opening(
        Guid accountId,
        Guid assetId,
        decimal quantity,
        IReadOnlyList<OpeningBalanceLotCommand> lots)
        => new(
            CoreLedgerTestData.HouseholdId,
            CoreLedgerTestData.PortfolioId,
            accountId,
            assetId,
            OpeningDate,
            Quantity.FromDecimal(quantity),
            "SYNTHETIC-INVENTORY-OPENING",
            "Synthetic inventory opening source.",
            lots);

    private static RecordOpeningBalanceUseCase OpeningRecorder(
        WealthLedgerDbContext context)
        => new(
            new EfCoreOpeningBalanceReferenceStore(context),
            new EfCoreOpeningBalanceEffectiveHistoryReadStore(context),
            new EfCoreLedgerPostingStore(context),
            new EfCoreLedgerTransactionReadStore(context),
            new FixedTimeProvider(Now));

    private static async Task SeedMasterDataAsync(SqliteTestDatabase database)
    {
        await using var context = database.CreateContext();
        await CoreLedgerTestData.SeedMasterDataAsync(context);
        context.Accounts.AddRange(
            new AccountRow
            {
                Id = SourceVaultId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                Code = "INVENTORY_VAULT_A",
                Name = "Synthetic Inventory Vault A",
                Type = AccountType.PhysicalVault,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            },
            new AccountRow
            {
                Id = DestinationVaultId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                Code = "INVENTORY_VAULT_B",
                Name = "Synthetic Inventory Vault B",
                Type = AccountType.PhysicalVault,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            });
        context.Assets.AddRange(
            new AssetRow
            {
                Id = EquityAssetId,
                Code = "EQUITY_INVENTORY",
                Name = "Synthetic Inventory Equity",
                Type = AssetType.Equity,
                BaseUnit = AssetUnit.Share,
                BaseCurrencyCode = "TRY",
                LotTrackingMode = LotTrackingMode.Required,
                IsActive = true,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            },
            new AssetRow
            {
                Id = UsdAssetId,
                Code = "USD_CASH",
                Name = "Synthetic Foreign Currency",
                Type = AssetType.Currency,
                BaseUnit = AssetUnit.CurrencyUnit,
                BaseCurrencyCode = "USD",
                LotTrackingMode = LotTrackingMode.None,
                IsActive = true,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
        await context.SaveChangesAsync();
    }

    private sealed record LifecycleHistory(
        Guid FundOpeningTransactionId,
        Guid FundOpeningLotId,
        Guid FundPurchaseTransactionId,
        Guid FundPurchaseLotId,
        Guid FundSaleTransactionId,
        Guid EquityLotId,
        Guid GoldOpeningLotId,
        Guid GoldPurchaseLotId,
        PositionInventory GoldSaleDateBeforeReversal);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        internal FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
