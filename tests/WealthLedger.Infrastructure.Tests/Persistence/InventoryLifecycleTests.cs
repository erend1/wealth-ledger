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
    private static readonly Guid SidePortfolioId =
        Guid.Parse("40000000-0000-0000-0000-0000000000b1");
    private static readonly Guid SideAccountId =
        Guid.Parse("50000000-0000-0000-0000-0000000000b1");
    private static readonly Guid SideInstitutionId =
        Guid.Parse("30000000-0000-0000-0000-0000000000b1");
    private static readonly Guid OtherHouseholdAccountId =
        Guid.Parse("50000000-0000-0000-0000-0000000000c1");
    private static readonly Guid UnknownId =
        Guid.Parse("99999999-0000-0000-0000-0000000000ff");
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
        var onFundSaleDate = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                AsOf: FundSaleDate));
        var onGoldPurchaseDate = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                AsOf: GoldPurchaseDate));
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

        // The as-of bound is inclusive: an allocation dated on the as-of day,
        // including a lot's opening, belongs to that day's inventory.
        Assert.Equal(
            15 * Unit,
            Single(onFundSaleDate, history.FundPurchaseLotId).GlobalQuantityRawE8);
        Assert.Equal(
            20 * Unit,
            Single(onGoldPurchaseDate, history.GoldPurchaseLotId)
                .GlobalQuantityRawE8);

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

    [Fact]
    public async Task LotInventory_GoldPiecesLeftAtZeroGramsStayHeldAsInM009()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedMasterDataAsync(database);
        var lotId = await RecordGoldPurchaseAsync(
            database,
            "zero-gram-gold-purchase",
            SourceVaultId,
            20m,
            2,
            20_000,
            new DateOnly(2026, 9, 1));

        // M009 bounds gross weight and pieces separately, so it accepts a
        // sale of all the weight with only one of the two pieces.
        await RecordGoldSaleAsync(
            database,
            "zero-gram-gold-sale",
            SourceVaultId,
            lotId,
            20m,
            1,
            20_000,
            new DateOnly(2026, 9, 5));

        await using var context = database.CreateContext();
        var inventory = await new ListLotInventoryUseCase(
                new EfCoreInventoryReadStore(context))
            .ExecuteAsync(
                new ListLotInventoryQuery(CoreLedgerTestData.HouseholdId));
        var m009Custody = await new GetPhysicalGoldCustodyInventoryUseCase(
                new EfCorePhysicalGoldVerificationReadStore(context))
            .ExecuteAsync(CoreLedgerTestData.HouseholdId);

        var lot = Assert.Single(inventory.Items);
        Assert.Equal(0, lot.GlobalQuantityRawE8);
        Assert.Equal(1, lot.PhysicalGold!.GlobalPieceCount);
        var custody = Assert.Single(lot.Custody);
        var expected = Assert.Single(m009Custody.Items);
        Assert.Equal(expected.AccountId, custody.AccountId);
        Assert.Equal(expected.GrossWeightRawE8, custody.QuantityRawE8);
        Assert.Equal(expected.PieceCount, custody.PieceCount);
        Assert.Equal(1, custody.PieceCount);
    }

    [Fact]
    public async Task Inventory_BackdatedGoldSaleBeforeTransfer_ReportsSignedAsOfCustody()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedMasterDataAsync(database);
        var lotId = await RecordGoldPurchaseAsync(
            database,
            "backdated-gold-purchase",
            SourceVaultId,
            20m,
            2,
            20_000,
            new DateOnly(2026, 9, 1));
        await RecordGoldTransferAsync(
            database,
            "backdated-gold-transfer",
            SourceVaultId,
            DestinationVaultId,
            lotId,
            20m,
            2,
            new DateOnly(2026, 9, 10));

        // M009 checks the sale against current custody in vault B only, so
        // it accepts a date before the transfer that filled vault B.
        await RecordGoldSaleAsync(
            database,
            "backdated-gold-sale",
            DestinationVaultId,
            lotId,
            10m,
            1,
            10_000,
            new DateOnly(2026, 9, 5));

        await using var context = database.CreateContext();
        var store = new EfCoreInventoryReadStore(context);
        var asOf = new DateOnly(2026, 9, 7);
        var slice = await new ListLotInventoryUseCase(store).ExecuteAsync(
            new ListLotInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                AsOf: asOf));
        var positions = await new ListPositionInventoryUseCase(store)
            .ExecuteAsync(
                new ListPositionInventoryQuery(
                    CoreLedgerTestData.HouseholdId,
                    AssetId: CoreLedgerTestData.GoldAssetId,
                    AsOf: asOf));
        var current = await new ListLotInventoryUseCase(store).ExecuteAsync(
            new ListLotInventoryQuery(CoreLedgerTestData.HouseholdId));

        var lot = Assert.Single(slice.Items);
        Assert.Equal(10 * Unit, lot.GlobalQuantityRawE8);
        Assert.Equal(1, lot.PhysicalGold!.GlobalPieceCount);
        var overdrawn = Assert.Single(
            lot.Custody,
            item => item.AccountId == DestinationVaultId);
        Assert.Equal(-10 * Unit, overdrawn.QuantityRawE8);
        Assert.Equal(-1, overdrawn.PieceCount);
        Assert.Equal(2, positions.Items.Count);

        foreach (var custody in lot.Custody)
        {
            Assert.Equal(
                QuantityOf(
                    positions,
                    (custody.AccountId, CoreLedgerTestData.GoldAssetId)),
                custody.QuantityRawE8);
        }

        var held = Assert.Single(Assert.Single(current.Items).Custody);
        Assert.Equal(DestinationVaultId, held.AccountId);
        Assert.Equal(10 * Unit, held.QuantityRawE8);
        Assert.Equal(1, held.PieceCount);
    }

    [Fact]
    public async Task Inventory_BackdatedFundSaleOfLaterLot_KeepsLotsEqualToPosition()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedMasterDataAsync(database);
        var openingLotId = Assert.Single(
            (await RecordOpeningAsync(
                database,
                "backdated-fund-opening",
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
                    ]))).AssetLotIds);
        var (_, laterLotId) = await RecordFundPurchaseAsync(
            database,
            "backdated-fund-purchase",
            20m,
            15m,
            30_000,
            new DateOnly(2026, 9, 5));

        // M008 plans the sale over current custody, so a sale dated before
        // the purchase still consumes the purchased lot.
        var (_, plannedLotIds) = await RecordFundSaleAsync(
            database,
            "backdated-fund-sale",
            15m,
            10m,
            15_000,
            new DateOnly(2026, 9, 3));
        Assert.Equal([openingLotId, laterLotId], plannedLotIds);

        await using var context = database.CreateContext();
        var store = new EfCoreInventoryReadStore(context);
        var asOf = new DateOnly(2026, 9, 4);
        var lots = await new ListLotInventoryUseCase(store).ExecuteAsync(
            new ListLotInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                AssetId: CoreLedgerTestData.FundAssetId,
                AsOf: asOf,
                IncludeExhausted: true));
        var position = await new ListPositionInventoryUseCase(store)
            .ExecuteAsync(
                new ListPositionInventoryQuery(
                    CoreLedgerTestData.HouseholdId,
                    AssetId: CoreLedgerTestData.FundAssetId,
                    AsOf: asOf));

        Assert.Equal(-5 * Unit, QuantityOf(position, FundScope));
        Assert.Equal(
            QuantityOf(position, FundScope),
            lots.Items.Sum(item => item.GlobalQuantityRawE8));
        Assert.Equal(0, Single(lots, openingLotId).GlobalQuantityRawE8);
        var later = Single(lots, laterLotId);
        Assert.Equal(20 * Unit, later.OriginalQuantityRawE8);
        Assert.Equal(-5 * Unit, later.GlobalQuantityRawE8);
    }

    [Fact]
    public async Task Inventory_FiltersSelectExactScopesAndRejectUnknownScopes()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var history = await SeedLifecycleAsync(database);
        var sideFundLotId = await SeedSideScopeAsync(database);
        var otherHouseholdLotId = await SeedOtherHouseholdAsync(database);

        await using var context = database.CreateContext();
        var store = new EfCoreInventoryReadStore(context);
        var positions = new ListPositionInventoryUseCase(store);
        var lots = new ListLotInventoryUseCase(store);
        var sidePortfolio = await positions.ExecuteAsync(
            new ListPositionInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                PortfolioId: SidePortfolioId));
        var primaryAccount = await positions.ExecuteAsync(
            new ListPositionInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                AccountId: CoreLedgerTestData.AccountId));
        var crossedScope = await positions.ExecuteAsync(
            new ListPositionInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                PortfolioId: CoreLedgerTestData.PortfolioId,
                AccountId: SideAccountId));
        var sideInstitution = await positions.ExecuteAsync(
            new ListPositionInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                InstitutionId: SideInstitutionId));
        var primaryInstitution = await positions.ExecuteAsync(
            new ListPositionInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                InstitutionId: CoreLedgerTestData.InstitutionId));
        var exactEmpty = await positions.ExecuteAsync(
            new ListPositionInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                CoreLedgerTestData.PortfolioId,
                CoreLedgerTestData.DestinationAccountId,
                AssetId: EquityAssetId,
                IncludeZero: true));

        Assert.Equal(
            new[] { CoreLedgerTestData.CashAssetId, CoreLedgerTestData.FundAssetId }
                .Order()
                .ToArray(),
            sidePortfolio.Items.Select(item => item.AssetId).Order().ToArray());
        Assert.All(
            sidePortfolio.Items,
            item => Assert.Equal(SideAccountId, item.AccountId));
        Assert.Equal(
            new[] { CoreLedgerTestData.FundAssetId, EquityAssetId, UsdAssetId }
                .Order()
                .ToArray(),
            primaryAccount.Items.Select(item => item.AssetId).Order().ToArray());
        Assert.All(
            primaryAccount.Items,
            item => Assert.Equal(CoreLedgerTestData.PortfolioId, item.PortfolioId));
        Assert.Empty(crossedScope.Items);
        Assert.Equal(
            sidePortfolio.Items.Select(item => (item.AccountId, item.AssetId)),
            sideInstitution.Items.Select(item => (item.AccountId, item.AssetId)));
        Assert.Equal(4, primaryInstitution.Items.Count);
        Assert.DoesNotContain(
            primaryInstitution.Items,
            item => item.PortfolioId == SidePortfolioId);

        var sideCash = Assert.Single(
            sidePortfolio.Items,
            item => item.AssetId == CoreLedgerTestData.CashAssetId);
        Assert.Equal(50 * Unit, sideCash.QuantityRawE8);
        Assert.Equal(SideScopeView, ScopeView.Of(sideCash));
        Assert.Equal(
            new AssetView(
                CoreLedgerTestData.CashAssetId,
                "TRY_CASH",
                "Test Cash Asset",
                AssetType.Cash,
                AssetUnit.CurrencyUnit,
                "TRY",
                LotTrackingMode.None,
                true),
            AssetView.Of(sideCash));

        var empty = Assert.Single(exactEmpty.Items);
        Assert.Equal(0, empty.QuantityRawE8);
        Assert.Equal(0, empty.SourceEntryCount);
        Assert.Equal(
            new ScopeView(
                CoreLedgerTestData.PortfolioId,
                "CORE",
                "Core Portfolio",
                PortfolioStatus.Active,
                CoreLedgerTestData.DestinationAccountId,
                "SECONDARY",
                "Secondary Account",
                AccountType.Investment,
                true,
                CoreLedgerTestData.InstitutionId,
                "TEST_INSTITUTION",
                "Test Institution",
                InstitutionType.Broker,
                true),
            ScopeView.Of(empty));
        Assert.Equal(
            new AssetView(
                EquityAssetId,
                "EQUITY_INVENTORY",
                "Synthetic Inventory Equity",
                AssetType.Equity,
                AssetUnit.Share,
                "TRY",
                LotTrackingMode.Required,
                true),
            AssetView.Of(empty));

        var sideLots = await lots.ExecuteAsync(
            new ListLotInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                PortfolioId: SidePortfolioId));
        var primaryLots = await lots.ExecuteAsync(
            new ListLotInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                PortfolioId: CoreLedgerTestData.PortfolioId));
        var sideLot = Assert.Single(sideLots.Items);
        Assert.Equal(sideFundLotId, sideLot.AssetLotId);
        Assert.Equal(SideScopeView, ScopeView.Of(Assert.Single(sideLot.Custody)));
        Assert.Equal(
            new[]
            {
                history.EquityLotId,
                history.FundPurchaseLotId,
                history.GoldOpeningLotId,
                history.GoldPurchaseLotId
            }.Order().ToArray(),
            primaryLots.Items.Select(item => item.AssetLotId).Order().ToArray());

        // Another household's Posted history never leaks into this one.
        var householdPositions = await positions.ExecuteAsync(
            new ListPositionInventoryQuery(CoreLedgerTestData.HouseholdId));
        var householdLots = await lots.ExecuteAsync(
            new ListLotInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                IncludeExhausted: true));
        var otherHouseholdLots = await lots.ExecuteAsync(
            new ListLotInventoryQuery(CoreLedgerTestData.OtherHouseholdId));
        Assert.DoesNotContain(
            householdPositions.Items,
            item => item.PortfolioId == CoreLedgerTestData.OtherPortfolioId);
        Assert.Equal(15 * Unit, QuantityOf(householdPositions, FundScope));
        Assert.DoesNotContain(
            householdLots.Items,
            lot => lot.AssetLotId == otherHouseholdLotId);
        Assert.Equal(
            otherHouseholdLotId,
            Assert.Single(otherHouseholdLots.Items).AssetLotId);

        // Unknown, cross-household and mismatched scopes never read as empty.
        (Guid? Portfolio, Guid? Account, Guid? Institution, Guid? Asset)[] unknownScopes =
        [
            (UnknownId, null, null, null),
            (CoreLedgerTestData.OtherPortfolioId, null, null, null),
            (null, UnknownId, null, null),
            (null, CoreLedgerTestData.OtherAccountId, null, null),
            (null, CoreLedgerTestData.AccountId, SideInstitutionId, null),
            (null, null, UnknownId, null),
            (null, null, null, UnknownId)
        ];

        foreach (var (portfolio, account, institution, asset) in unknownScopes)
        {
            await Assert.ThrowsAsync<InventoryScopeNotFoundException>(
                () => positions.ExecuteAsync(
                    new ListPositionInventoryQuery(
                        CoreLedgerTestData.HouseholdId,
                        portfolio,
                        account,
                        institution,
                        asset)));
            await Assert.ThrowsAsync<InventoryScopeNotFoundException>(
                () => lots.ExecuteAsync(
                    new ListLotInventoryQuery(
                        CoreLedgerTestData.HouseholdId,
                        portfolio,
                        account,
                        institution,
                        asset)));
        }
    }

    [Fact]
    public async Task LotInventory_SplitOpeningKeepsEachLotsOriginalQuantity()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedMasterDataAsync(database);

        // M007 may split one opening entry into lots with different cost.
        var opening = await RecordOpeningAsync(
            database,
            "split-fund-opening",
            Opening(
                CoreLedgerTestData.AccountId,
                CoreLedgerTestData.FundAssetId,
                10m,
                [
                    new OpeningBalanceLotCommand(
                        Quantity.FromDecimal(6m),
                        new DateOnly(2026, 8, 10),
                        CostBasis.Known(
                            Money.FromMinorUnits(6_000, CurrencyCode.TRY))),
                    new OpeningBalanceLotCommand(
                        Quantity.FromDecimal(4m),
                        AcquiredOn: null,
                        CostBasis.Unknown())
                ]));

        await using var context = database.CreateContext();
        var inventory = await new ListLotInventoryUseCase(
                new EfCoreInventoryReadStore(context))
            .ExecuteAsync(
                new ListLotInventoryQuery(CoreLedgerTestData.HouseholdId));

        Assert.Equal(
            opening.AssetLotIds.Order().ToArray(),
            inventory.Items.Select(lot => lot.AssetLotId).Order().ToArray());
        Assert.Single(
            inventory.Items.Select(lot => lot.OpeningTransactionEntryId).Distinct());
        Assert.All(
            inventory.Items,
            lot => Assert.Equal(opening.TransactionId, lot.CreatingTransactionId));
        Assert.All(
            inventory.Items,
            lot => Assert.Equal(lot.OriginalQuantityRawE8, lot.GlobalQuantityRawE8));
        var known = Assert.Single(
            inventory.Items,
            lot => lot.CostStatus == CostBasisStatus.Known);
        Assert.Equal(6 * Unit, known.OriginalQuantityRawE8);
        Assert.Equal(6_000, known.CostMinorUnits);
        var unknown = Assert.Single(
            inventory.Items,
            lot => lot.CostStatus == CostBasisStatus.Unknown);
        Assert.Equal(4 * Unit, unknown.OriginalQuantityRawE8);
        Assert.Null(unknown.CostMinorUnits);
    }

    private static ScopeView SideScopeView
        => new(
            SidePortfolioId,
            "SIDE",
            "Synthetic Side Portfolio",
            PortfolioStatus.Active,
            SideAccountId,
            "SIDE_ACCOUNT",
            "Synthetic Side Account",
            AccountType.Investment,
            true,
            SideInstitutionId,
            "SIDE_BANK",
            "Synthetic Side Bank",
            InstitutionType.Bank,
            true);

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
        await RecordOpeningAsync(
            database,
            "inventory-cash-opening",
            Opening(
                CoreLedgerTestData.DestinationAccountId,
                CoreLedgerTestData.CashAssetId,
                1_000m,
                []));
        await RecordOpeningAsync(
            database,
            "inventory-usd-opening",
            Opening(CoreLedgerTestData.AccountId, UsdAssetId, 250m, []));
        var fundOpening = await RecordOpeningAsync(
            database,
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
        var fundOpeningLotId = Assert.Single(fundOpening.AssetLotIds);
        var equityLotId = Assert.Single(
            (await RecordOpeningAsync(
                database,
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
        var goldOpeningLotId = Assert.Single(
            (await RecordOpeningAsync(
                database,
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

        var (fundPurchaseTransactionId, fundPurchaseLotId) =
            await RecordFundPurchaseAsync(
                database,
                "inventory-fund-purchase",
                20m,
                15m,
                30_000,
                FundPurchaseDate);
        var (fundSaleTransactionId, plannedLotIds) = await RecordFundSaleAsync(
            database,
            "inventory-fund-sale",
            15m,
            10m,
            15_000,
            FundSaleDate);

        // FIFO consumes the whole opening lot before the purchase lot.
        Assert.Equal([fundOpeningLotId, fundPurchaseLotId], plannedLotIds);

        var goldPurchaseLotId = await RecordGoldPurchaseAsync(
            database,
            "inventory-gold-purchase",
            SourceVaultId,
            20m,
            2,
            20_000,
            GoldPurchaseDate);
        var goldSaleTransactionId = await RecordGoldSaleAsync(
            database,
            "inventory-gold-sale",
            SourceVaultId,
            goldPurchaseLotId,
            8m,
            1,
            10_000,
            GoldSaleDate);
        await RecordGoldTransferAsync(
            database,
            "inventory-gold-transfer",
            SourceVaultId,
            DestinationVaultId,
            goldPurchaseLotId,
            12m,
            1,
            GoldTransferDate);

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

        await ReverseAsync(
            database,
            "inventory-gold-sale-reversal",
            goldSaleTransactionId);

        return new LifecycleHistory(
            fundOpening.TransactionId,
            fundOpeningLotId,
            fundPurchaseTransactionId,
            fundPurchaseLotId,
            fundSaleTransactionId,
            equityLotId,
            goldOpeningLotId,
            goldPurchaseLotId,
            goldSaleDateBeforeReversal);
    }

    private static async Task<Guid> SeedSideScopeAsync(SqliteTestDatabase database)
    {
        await using (var context = database.CreateContext())
        {
            context.Institutions.Add(
                new InstitutionRow
                {
                    Id = SideInstitutionId,
                    Code = "SIDE_BANK",
                    Name = "Synthetic Side Bank",
                    Type = InstitutionType.Bank,
                    IsActive = true
                });
            context.Portfolios.Add(
                new PortfolioRow
                {
                    Id = SidePortfolioId,
                    HouseholdId = CoreLedgerTestData.HouseholdId,
                    Code = "SIDE",
                    Name = "Synthetic Side Portfolio",
                    Status = PortfolioStatus.Active,
                    CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
                });
            context.Accounts.Add(
                new AccountRow
                {
                    Id = SideAccountId,
                    HouseholdId = CoreLedgerTestData.HouseholdId,
                    InstitutionId = SideInstitutionId,
                    Code = "SIDE_ACCOUNT",
                    Name = "Synthetic Side Account",
                    Type = AccountType.Investment,
                    IsActive = true,
                    OpenedOn = new DateOnly(2026, 1, 1)
                });
            await context.SaveChangesAsync();
        }

        await RecordOpeningAsync(
            database,
            "inventory-side-cash-opening",
            Opening(
                SideAccountId,
                CoreLedgerTestData.CashAssetId,
                50m,
                [],
                SidePortfolioId));

        return Assert.Single(
            (await RecordOpeningAsync(
                database,
                "inventory-side-fund-opening",
                Opening(
                    SideAccountId,
                    CoreLedgerTestData.FundAssetId,
                    5m,
                    [
                        new OpeningBalanceLotCommand(
                            Quantity.FromDecimal(5m),
                            new DateOnly(2026, 8, 20),
                            CostBasis.Known(
                                Money.FromMinorUnits(5_000, CurrencyCode.TRY)))
                    ],
                    SidePortfolioId))).AssetLotIds);
    }

    private static async Task<Guid> SeedOtherHouseholdAsync(
        SqliteTestDatabase database)
    {
        await using (var context = database.CreateContext())
        {
            context.Accounts.Add(
                new AccountRow
                {
                    Id = OtherHouseholdAccountId,
                    HouseholdId = CoreLedgerTestData.OtherHouseholdId,
                    InstitutionId = CoreLedgerTestData.InstitutionId,
                    Code = "OTHER_INVESTMENT",
                    Name = "Synthetic Other Investment Account",
                    Type = AccountType.Investment,
                    IsActive = true,
                    OpenedOn = new DateOnly(2026, 1, 1)
                });
            await context.SaveChangesAsync();
        }

        return Assert.Single(
            (await RecordOpeningAsync(
                database,
                "inventory-other-household-opening",
                new RecordOpeningBalanceCommand(
                    CoreLedgerTestData.OtherHouseholdId,
                    CoreLedgerTestData.OtherPortfolioId,
                    OtherHouseholdAccountId,
                    CoreLedgerTestData.FundAssetId,
                    OpeningDate,
                    Quantity.FromDecimal(7m),
                    "SYNTHETIC-OTHER-HOUSEHOLD-OPENING",
                    "Synthetic other-household opening source.",
                    [
                        new OpeningBalanceLotCommand(
                            Quantity.FromDecimal(7m),
                            AcquiredOn: null,
                            CostBasis.Unknown())
                    ]))).AssetLotIds);
    }

    private static RecordOpeningBalanceCommand Opening(
        Guid accountId,
        Guid assetId,
        decimal quantity,
        IReadOnlyList<OpeningBalanceLotCommand> lots,
        Guid? portfolioId = null)
        => new(
            CoreLedgerTestData.HouseholdId,
            portfolioId ?? CoreLedgerTestData.PortfolioId,
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

    private static async Task<RecordOpeningBalanceResult> RecordOpeningAsync(
        SqliteTestDatabase database,
        string idempotencyKey,
        RecordOpeningBalanceCommand command)
    {
        await using var context = database.CreateContext();

        return await OpeningRecorder(context).ExecuteAsync(
            idempotencyKey,
            command);
    }

    private static async Task<(Guid TransactionId, Guid LotId)> RecordFundPurchaseAsync(
        SqliteTestDatabase database,
        string idempotencyKey,
        decimal units,
        decimal unitPrice,
        long considerationMinor,
        DateOnly executionDate)
    {
        await using var context = database.CreateContext();
        var posting = new EfCoreLedgerPostingStore(context);
        var purchase = await new RecordFundPurchaseUseCase(
                new EfCoreOpeningBalanceReferenceStore(context),
                posting,
                posting,
                new FixedTimeProvider(Now))
            .ExecuteAsync(
                idempotencyKey,
                new RecordFundPurchaseCommand(
                    CoreLedgerTestData.HouseholdId,
                    CoreLedgerTestData.PortfolioId,
                    CoreLedgerTestData.AccountId,
                    CoreLedgerTestData.FundAssetId,
                    CoreLedgerTestData.CashAssetId,
                    Quantity.FromDecimal(units),
                    UnitPrice.FromDecimal(unitPrice, CurrencyCode.TRY),
                    Money.FromMinorUnits(considerationMinor, CurrencyCode.TRY),
                    executionDate,
                    ExternalReference: "SYNTHETIC-INVENTORY-FUND-BUY",
                    Note: "Synthetic inventory fund purchase.",
                    CashAccountId: CoreLedgerTestData.DestinationAccountId));

        return (purchase.TransactionId, purchase.AssetLotId);
    }

    private static async Task<(Guid TransactionId, Guid[] PlannedLotIds)> RecordFundSaleAsync(
        SqliteTestDatabase database,
        string idempotencyKey,
        decimal units,
        decimal unitPrice,
        long considerationMinor,
        DateOnly executionDate)
    {
        await using var context = database.CreateContext();
        var posting = new EfCoreLedgerPostingStore(context);
        var command = new FundSaleCommand(
            CoreLedgerTestData.HouseholdId,
            CoreLedgerTestData.PortfolioId,
            CoreLedgerTestData.AccountId,
            CoreLedgerTestData.DestinationAccountId,
            CoreLedgerTestData.FundAssetId,
            CoreLedgerTestData.CashAssetId,
            Quantity.FromDecimal(units),
            UnitPrice.FromDecimal(unitPrice, CurrencyCode.TRY),
            Money.FromMinorUnits(considerationMinor, CurrencyCode.TRY),
            executionDate,
            ExternalReference: "SYNTHETIC-INVENTORY-FUND-SELL",
            Note: "Synthetic inventory fund sale.");
        var preview = await new PreviewFundSaleUseCase(
                new EfCoreOpeningBalanceReferenceStore(context),
                new EfCoreFundLotCustodyReadStore(posting),
                new EfCoreFundRealizedCostReadStore(context),
                new LotAllocationService(),
                new FixedTimeProvider(Now))
            .ExecuteAsync(command);
        var sale = await new RecordFundSaleUseCase(
                new EfCoreOpeningBalanceReferenceStore(context),
                posting,
                posting,
                new FixedTimeProvider(Now))
            .ExecuteAsync(
                idempotencyKey,
                command with
                {
                    ReviewedPlan = preview.Plan
                        .Select(
                            line => new ReviewedLotAllocation(
                                line.AssetLotId,
                                Quantity.FromRaw(line.ConsumedQuantityRawE8)))
                        .ToArray(),
                    ReviewedPlanFingerprint = preview.PlanFingerprint
                });

        return (
            sale.TransactionId,
            preview.Plan.Select(line => line.AssetLotId).ToArray());
    }

    private static async Task<Guid> RecordGoldPurchaseAsync(
        SqliteTestDatabase database,
        string idempotencyKey,
        Guid vaultId,
        decimal grams,
        int pieces,
        long considerationMinor,
        DateOnly executionDate)
    {
        await using var context = database.CreateContext();
        var posting = new EfCoreLedgerPostingStore(context);

        return (await new RecordPhysicalGoldPurchaseUseCase(
                new EfCoreOpeningBalanceReferenceStore(context),
                posting,
                posting,
                new FixedTimeProvider(Now))
            .ExecuteAsync(
                idempotencyKey,
                new PhysicalGoldPurchaseCommand(
                    CoreLedgerTestData.HouseholdId,
                    CoreLedgerTestData.PortfolioId,
                    vaultId,
                    CoreLedgerTestData.DestinationAccountId,
                    CoreLedgerTestData.GoldAssetId,
                    CoreLedgerTestData.CashAssetId,
                    Quantity.FromDecimal(grams),
                    new Fineness(916_000),
                    pieces,
                    Money.FromMinorUnits(considerationMinor, CurrencyCode.TRY),
                    executionDate,
                    ExternalReference: "SYNTHETIC-INVENTORY-GOLD-BUY",
                    Note: "Synthetic inventory gold purchase.")))
            .AssetLotId;
    }

    private static async Task<Guid> RecordGoldSaleAsync(
        SqliteTestDatabase database,
        string idempotencyKey,
        Guid vaultId,
        Guid lotId,
        decimal grams,
        int pieces,
        long considerationMinor,
        DateOnly executionDate)
    {
        await using var context = database.CreateContext();
        var posting = new EfCoreLedgerPostingStore(context);
        var command = new PhysicalGoldSaleCommand(
            CoreLedgerTestData.HouseholdId,
            CoreLedgerTestData.PortfolioId,
            vaultId,
            CoreLedgerTestData.DestinationAccountId,
            CoreLedgerTestData.GoldAssetId,
            CoreLedgerTestData.CashAssetId,
            Quantity.FromDecimal(grams),
            pieces,
            [
                new PhysicalGoldSelectedLot(
                    lotId,
                    Quantity.FromDecimal(grams),
                    pieces)
            ],
            Money.FromMinorUnits(considerationMinor, CurrencyCode.TRY),
            executionDate,
            ExternalReference: "SYNTHETIC-INVENTORY-GOLD-SELL",
            Note: "Synthetic inventory gold sale.");
        var preview = await new PreviewPhysicalGoldSaleUseCase(
                new EfCoreOpeningBalanceReferenceStore(context),
                new EfCorePhysicalGoldCustodyReadStore(posting),
                new EfCorePhysicalGoldRealizedCostReadStore(context),
                new FixedTimeProvider(Now))
            .ExecuteAsync(command);

        return (await new RecordPhysicalGoldSaleUseCase(
                new EfCoreOpeningBalanceReferenceStore(context),
                posting,
                posting,
                new FixedTimeProvider(Now))
            .ExecuteAsync(
                idempotencyKey,
                command with
                {
                    ReviewedPlanFingerprint = preview.PlanFingerprint
                })).TransactionId;
    }

    private static async Task RecordGoldTransferAsync(
        SqliteTestDatabase database,
        string idempotencyKey,
        Guid sourceVaultId,
        Guid destinationVaultId,
        Guid lotId,
        decimal grams,
        int pieces,
        DateOnly executionDate)
    {
        await using var context = database.CreateContext();
        var posting = new EfCoreLedgerPostingStore(context);
        var command = new PhysicalGoldTransferCommand(
            CoreLedgerTestData.HouseholdId,
            CoreLedgerTestData.PortfolioId,
            sourceVaultId,
            CoreLedgerTestData.PortfolioId,
            destinationVaultId,
            CoreLedgerTestData.GoldAssetId,
            Quantity.FromDecimal(grams),
            pieces,
            [
                new PhysicalGoldSelectedLot(
                    lotId,
                    Quantity.FromDecimal(grams),
                    pieces)
            ],
            executionDate,
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
                idempotencyKey,
                command with
                {
                    ReviewedPlanFingerprint = preview.PlanFingerprint
                });
    }

    private static async Task ReverseAsync(
        SqliteTestDatabase database,
        string idempotencyKey,
        Guid transactionId)
    {
        await using var context = database.CreateContext();
        var reversal = await new ReversePostedTransactionUseCase(
                new EfCoreLedgerReversalStore(context),
                new FixedTimeProvider(Now.AddMinutes(1)))
            .ExecuteAsync(
                idempotencyKey,
                new ReversePostedTransactionCommand(
                    transactionId,
                    "Synthetic incorrect transaction."));
        Assert.NotNull(reversal);
    }

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

    private sealed record ScopeView(
        Guid PortfolioId,
        string PortfolioCode,
        string PortfolioName,
        PortfolioStatus PortfolioStatus,
        Guid AccountId,
        string AccountCode,
        string AccountName,
        AccountType AccountType,
        bool AccountIsActive,
        Guid? InstitutionId,
        string? InstitutionCode,
        string? InstitutionName,
        InstitutionType? InstitutionType,
        bool? InstitutionIsActive)
    {
        internal static ScopeView Of(PositionInventoryItem item)
            => new(
                item.PortfolioId,
                item.PortfolioCode,
                item.PortfolioName,
                item.PortfolioStatus,
                item.AccountId,
                item.AccountCode,
                item.AccountName,
                item.AccountType,
                item.AccountIsActive,
                item.InstitutionId,
                item.InstitutionCode,
                item.InstitutionName,
                item.InstitutionType,
                item.InstitutionIsActive);

        internal static ScopeView Of(LotCustodyInventoryItem item)
            => new(
                item.PortfolioId,
                item.PortfolioCode,
                item.PortfolioName,
                item.PortfolioStatus,
                item.AccountId,
                item.AccountCode,
                item.AccountName,
                item.AccountType,
                item.AccountIsActive,
                item.InstitutionId,
                item.InstitutionCode,
                item.InstitutionName,
                item.InstitutionType,
                item.InstitutionIsActive);
    }

    private sealed record AssetView(
        Guid AssetId,
        string AssetCode,
        string AssetName,
        AssetType AssetType,
        AssetUnit AssetBaseUnit,
        string? AssetBaseCurrencyCode,
        LotTrackingMode AssetLotTrackingMode,
        bool AssetIsActive)
    {
        internal static AssetView Of(PositionInventoryItem item)
            => new(
                item.AssetId,
                item.AssetCode,
                item.AssetName,
                item.AssetType,
                item.AssetBaseUnit,
                item.AssetBaseCurrencyCode,
                item.AssetLotTrackingMode,
                item.AssetIsActive);
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
