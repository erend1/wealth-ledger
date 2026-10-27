using System.Globalization;
using WealthLedger.Application.Inventory;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.Tests.Inventory;

public sealed class InventoryUseCaseTests
{
    private const long Unit = 100_000_000L;

    private static readonly Guid HouseholdId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid PortfolioId =
        Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondPortfolioId =
        Guid.Parse("40000000-0000-0000-0000-000000000002");
    private static readonly Guid AccountId =
        Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondAccountId =
        Guid.Parse("50000000-0000-0000-0000-000000000002");
    private static readonly Guid InstitutionId =
        Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondInstitutionId =
        Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid AssetId =
        Guid.Parse("60000000-0000-0000-0000-000000000001");
    private static readonly Guid GoldAssetId =
        Guid.Parse("60000000-0000-0000-0000-000000000009");

    [Fact]
    public async Task PositionInventory_DerivesAsOfAndPreservesExactZero()
    {
        var scope = Scope(AssetId, "FUND_A", AssetType.Fund, AssetUnit.FundUnit);
        var store = new FakeInventoryReadStore
        {
            PositionFacts =
            [
                Entry(scope, 1, new DateOnly(2026, 1, 1), 500),
                Entry(scope, 2, new DateOnly(2026, 2, 1), -500),
                Entry(scope, 3, new DateOnly(2026, 3, 1), 250)
            ],
            ExactScope = scope
        };
        var useCase = new ListPositionInventoryUseCase(store);

        var january = await useCase.ExecuteAsync(
            new ListPositionInventoryQuery(
                HouseholdId,
                AsOf: new DateOnly(2026, 1, 31)));
        var februaryDefault = await useCase.ExecuteAsync(
            new ListPositionInventoryQuery(
                HouseholdId,
                AsOf: new DateOnly(2026, 2, 28)));
        var februaryWithZero = await useCase.ExecuteAsync(
            new ListPositionInventoryQuery(
                HouseholdId,
                PortfolioId,
                AccountId,
                InstitutionId,
                AssetId,
                AsOf: new DateOnly(2026, 2, 28),
                IncludeZero: true));
        var current = await useCase.ExecuteAsync(
            new ListPositionInventoryQuery(HouseholdId));

        Assert.Equal(500, Assert.Single(january.Items).QuantityRawE8);
        Assert.Empty(februaryDefault.Items);
        var zero = Assert.Single(februaryWithZero.Items);
        Assert.Equal(0, zero.QuantityRawE8);
        Assert.Equal(2, zero.SourceEntryCount);
        Assert.Equal(250, Assert.Single(current.Items).QuantityRawE8);
    }

    [Fact]
    public async Task PositionInventory_ValidEmptyExactScopeDiffersFromUnknownScope()
    {
        var scope = Scope(AssetId, "FUND_A", AssetType.Fund, AssetUnit.FundUnit);
        var store = new FakeInventoryReadStore
        {
            ExactScope = scope,
            PositionFacts = []
        };
        var useCase = new ListPositionInventoryUseCase(store);

        var validEmpty = await useCase.ExecuteAsync(
            new ListPositionInventoryQuery(
                HouseholdId,
                PortfolioId,
                AccountId,
                InstitutionId,
                AssetId,
                IncludeZero: true));

        Assert.Equal(0, Assert.Single(validEmpty.Items).QuantityRawE8);
        store.FilterScopeExists = false;

        await Assert.ThrowsAsync<InventoryScopeNotFoundException>(
            () => useCase.ExecuteAsync(
                new ListPositionInventoryQuery(
                    HouseholdId,
                    PortfolioId,
                    AccountId,
                    InstitutionId,
                    AssetId,
                    IncludeZero: true)));
    }

    [Fact]
    public async Task PositionInventory_ExactScopeDisagreeingWithFiltersFailsClosed()
    {
        var scope = Scope(AssetId, "FUND_A", AssetType.Fund, AssetUnit.FundUnit);
        var store = new FakeInventoryReadStore
        {
            ExactScope = scope with { InstitutionId = SecondInstitutionId }
        };

        // A validated scope must never come back as an empty inventory.
        await Assert.ThrowsAsync<InventoryPersistenceException>(
            () => new ListPositionInventoryUseCase(store).ExecuteAsync(
                new ListPositionInventoryQuery(
                    HouseholdId,
                    PortfolioId,
                    AccountId,
                    InstitutionId,
                    AssetId,
                    IncludeZero: true)));
    }

    [Fact]
    public async Task PositionInventory_CheckedAggregationRejectsOverflow()
    {
        var scope = Scope(AssetId, "FUND_A", AssetType.Fund, AssetUnit.FundUnit);
        var store = new FakeInventoryReadStore
        {
            PositionFacts =
            [
                Entry(scope, 1, new DateOnly(2026, 1, 1), long.MaxValue),
                Entry(scope, 2, new DateOnly(2026, 1, 2), 1)
            ]
        };

        await Assert.ThrowsAsync<OverflowException>(
            () => new ListPositionInventoryUseCase(store)
                .ExecuteAsync(new ListPositionInventoryQuery(HouseholdId)));
    }

    [Fact]
    public async Task LotInventory_DerivesCustodyExhaustionAndPhysicalGoldIndependently()
    {
        var goldScope = Scope(
            GoldAssetId,
            "GOLD",
            AssetType.PhysicalGold,
            AssetUnit.GrossGram);
        var lotId = Guid.Parse("80000000-0000-0000-0000-000000000001");
        var openingEntryId = Guid.Parse("81000000-0000-0000-0000-000000000001");
        var store = new FakeInventoryReadStore
        {
            LotFacts = new InventoryLotFactSet(
                [GoldLot(lotId, openingEntryId, 20 * Unit, pieceCount: 2)],
                [
                    Allocation(
                        goldScope,
                        lotId,
                        openingEntryId,
                        1,
                        new DateOnly(2026, 1, 1),
                        20 * Unit,
                        2,
                        TransactionType.OpeningBalance),
                    Allocation(
                        goldScope,
                        lotId,
                        Guid.Parse("81000000-0000-0000-0000-000000000002"),
                        2,
                        new DateOnly(2026, 2, 1),
                        -5 * Unit,
                        -1,
                        TransactionType.Sell)
                ])
        };
        var result = await new ListLotInventoryUseCase(store)
            .ExecuteAsync(new ListLotInventoryQuery(HouseholdId));

        var item = Assert.Single(result.Items);
        Assert.Equal(20 * Unit, item.OriginalQuantityRawE8);
        Assert.Equal(15 * Unit, item.GlobalQuantityRawE8);
        var physical = Assert.IsType<PhysicalGoldLotInventoryDetail>(
            item.PhysicalGold);
        Assert.Equal(1, physical.GlobalPieceCount);
        Assert.Equal(13.74m, physical.GlobalFineWeightGrams);
        // Same value and decimal scale as the M009 Domain derivation.
        var m009FineWeight = new PhysicalGoldLotDetail(new Fineness(916_000), 2)
            .CalculateFineWeightGrams(Quantity.FromRaw(15 * Unit));
        Assert.Equal(
            m009FineWeight.ToString(CultureInfo.InvariantCulture),
            physical.GlobalFineWeightGrams.ToString(CultureInfo.InvariantCulture));
        var custody = Assert.Single(item.Custody);
        Assert.Equal(1, custody.PieceCount);
        Assert.Equal(13.74m, custody.FineWeightGrams);
    }

    [Fact]
    public async Task LotInventory_PhysicalGoldPiecesKeepZeroGrossLotHeld()
    {
        // M009 bounds gross weight and pieces independently, so a sale of
        // all the weight with fewer pieces leaves whole pieces at zero grams.
        var goldScope = Scope(
            GoldAssetId,
            "GOLD",
            AssetType.PhysicalGold,
            AssetUnit.GrossGram);
        var lotId = Guid.Parse("80000000-0000-0000-0000-000000000009");
        var openingEntryId = Guid.Parse("81000000-0000-0000-0000-000000000070");
        var store = new FakeInventoryReadStore
        {
            LotFacts = new InventoryLotFactSet(
                [GoldLot(lotId, openingEntryId, 20 * Unit, pieceCount: 2)],
                [
                    Allocation(
                        goldScope,
                        lotId,
                        openingEntryId,
                        70,
                        new DateOnly(2026, 1, 1),
                        20 * Unit,
                        2,
                        TransactionType.Buy),
                    Allocation(
                        goldScope,
                        lotId,
                        Guid.Parse("81000000-0000-0000-0000-000000000071"),
                        71,
                        new DateOnly(2026, 2, 1),
                        -20 * Unit,
                        -1,
                        TransactionType.Sell)
                ])
        };
        var useCase = new ListLotInventoryUseCase(store);

        var current = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(HouseholdId));
        var heldInAccount = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(HouseholdId, AccountId: AccountId));

        var lot = Assert.Single(current.Items);
        Assert.Equal(0, lot.GlobalQuantityRawE8);
        var gold = Assert.IsType<PhysicalGoldLotInventoryDetail>(
            lot.PhysicalGold);
        Assert.Equal(1, gold.GlobalPieceCount);
        Assert.Equal(0m, gold.GlobalFineWeightGrams);
        var custody = Assert.Single(lot.Custody);
        Assert.Equal(0, custody.QuantityRawE8);
        Assert.Equal(1, custody.PieceCount);
        Assert.Equal(lotId, Assert.Single(heldInAccount.Items).AssetLotId);
    }

    [Fact]
    public async Task LotInventory_CheckedAggregationRejectsOverflow()
    {
        var scope = Scope(AssetId, "FUND_A", AssetType.Fund, AssetUnit.FundUnit);
        var lotId = Guid.Parse("80000000-0000-0000-0000-000000000003");
        var openingEntryId = Guid.Parse("81000000-0000-0000-0000-000000000020");
        var store = new FakeInventoryReadStore
        {
            LotFacts = new InventoryLotFactSet(
                [
                    FundLot(
                        lotId,
                        openingEntryId,
                        new DateOnly(2026, 1, 1),
                        long.MaxValue)
                ],
                [
                    Allocation(
                        scope,
                        lotId,
                        openingEntryId,
                        20,
                        new DateOnly(2026, 1, 1),
                        long.MaxValue,
                        null,
                        TransactionType.OpeningBalance),
                    Allocation(
                        scope,
                        lotId,
                        Guid.Parse("81000000-0000-0000-0000-000000000021"),
                        21,
                        new DateOnly(2026, 1, 2),
                        1,
                        null,
                        TransactionType.Adjustment)
                ])
        };

        await Assert.ThrowsAsync<OverflowException>(
            () => new ListLotInventoryUseCase(store)
                .ExecuteAsync(new ListLotInventoryQuery(HouseholdId)));
    }

    [Fact]
    public async Task LotInventory_AsOfExcludesLotAcquiredLater()
    {
        var scope = Scope(AssetId, "FUND_A", AssetType.Fund, AssetUnit.FundUnit);
        var lotId = Guid.Parse("80000000-0000-0000-0000-000000000004");
        var openingEntryId = Guid.Parse("81000000-0000-0000-0000-000000000030");
        var store = new FakeInventoryReadStore
        {
            LotFacts = new InventoryLotFactSet(
                [FundLot(lotId, openingEntryId, new DateOnly(2026, 3, 1), 100)],
                [
                    Allocation(
                        scope,
                        lotId,
                        openingEntryId,
                        30,
                        new DateOnly(2026, 3, 1),
                        100,
                        null,
                        TransactionType.OpeningBalance)
                ])
        };
        var useCase = new ListLotInventoryUseCase(store);

        var beforeAcquisition = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(
                HouseholdId,
                AsOf: new DateOnly(2026, 2, 28),
                IncludeExhausted: true));
        var onAcquisition = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(
                HouseholdId,
                AsOf: new DateOnly(2026, 3, 1)));

        Assert.Empty(beforeAcquisition.Items);
        Assert.Equal(
            100,
            Assert.Single(onAcquisition.Items).GlobalQuantityRawE8);
    }

    [Fact]
    public async Task LotInventory_AsOfReportsSignedCustodyFromBackdatedSale()
    {
        // M009 checks a sale against current custody only, so a sale from
        // account B may be dated before the transfer that brought the gold.
        var source = Scope(
            GoldAssetId,
            "GOLD",
            AssetType.PhysicalGold,
            AssetUnit.GrossGram);
        var destination = source with
        {
            AccountId = SecondAccountId,
            AccountCode = "VAULT_B",
            AccountName = "Vault B"
        };
        var lotId = Guid.Parse("80000000-0000-0000-0000-00000000000a");
        var openingEntryId = Guid.Parse("81000000-0000-0000-0000-000000000080");
        var store = new FakeInventoryReadStore
        {
            LotFacts = new InventoryLotFactSet(
                [GoldLot(lotId, openingEntryId, 20 * Unit, pieceCount: 2)],
                [
                    Allocation(
                        source,
                        lotId,
                        openingEntryId,
                        80,
                        new DateOnly(2026, 9, 1),
                        20 * Unit,
                        2,
                        TransactionType.Buy),
                    Allocation(
                        source,
                        lotId,
                        Guid.Parse("81000000-0000-0000-0000-000000000081"),
                        81,
                        new DateOnly(2026, 9, 10),
                        -20 * Unit,
                        -2,
                        TransactionType.Transfer),
                    Allocation(
                        destination,
                        lotId,
                        Guid.Parse("81000000-0000-0000-0000-000000000082"),
                        82,
                        new DateOnly(2026, 9, 10),
                        20 * Unit,
                        2,
                        TransactionType.Transfer),
                    Allocation(
                        destination,
                        lotId,
                        Guid.Parse("81000000-0000-0000-0000-000000000083"),
                        83,
                        new DateOnly(2026, 9, 5),
                        -10 * Unit,
                        -1,
                        TransactionType.Sell)
                ])
        };
        var useCase = new ListLotInventoryUseCase(store);

        var asOf = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(
                HouseholdId,
                AccountId: AccountId,
                AsOf: new DateOnly(2026, 9, 7)));
        var current = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(HouseholdId));

        var slice = Assert.Single(asOf.Items);
        Assert.Equal(10 * Unit, slice.GlobalQuantityRawE8);
        Assert.Equal(1, slice.PhysicalGold!.GlobalPieceCount);
        var heldInSource = Assert.Single(
            slice.Custody,
            item => item.AccountId == AccountId);
        Assert.Equal(20 * Unit, heldInSource.QuantityRawE8);
        Assert.Equal(2, heldInSource.PieceCount);
        var overdrawn = Assert.Single(
            slice.Custody,
            item => item.AccountId == SecondAccountId);
        Assert.Equal(-10 * Unit, overdrawn.QuantityRawE8);
        Assert.Equal(-1, overdrawn.PieceCount);
        Assert.Equal(-9.16m, overdrawn.FineWeightGrams);
        var held = Assert.Single(Assert.Single(current.Items).Custody);
        Assert.Equal(SecondAccountId, held.AccountId);
        Assert.Equal(10 * Unit, held.QuantityRawE8);
        Assert.Equal(1, held.PieceCount);
    }

    [Fact]
    public async Task LotInventory_AsOfIncludesBackdatedDisposalOfLaterLot()
    {
        // M008 plans a sale over current custody, so a sale dated 09-03 can
        // consume a lot that was first acquired on 09-05.
        var scope = Scope(AssetId, "FUND_A", AssetType.Fund, AssetUnit.FundUnit);
        var earlierLotId = Guid.Parse("80000000-0000-0000-0000-00000000000b");
        var earlierEntryId = Guid.Parse("81000000-0000-0000-0000-000000000090");
        var laterLotId = Guid.Parse("80000000-0000-0000-0000-00000000000c");
        var laterEntryId = Guid.Parse("81000000-0000-0000-0000-000000000091");
        var store = new FakeInventoryReadStore
        {
            LotFacts = new InventoryLotFactSet(
                [
                    FundLot(
                        earlierLotId,
                        earlierEntryId,
                        new DateOnly(2026, 9, 1),
                        10 * Unit),
                    FundLot(
                        laterLotId,
                        laterEntryId,
                        new DateOnly(2026, 9, 5),
                        20 * Unit)
                ],
                [
                    Allocation(
                        scope,
                        earlierLotId,
                        earlierEntryId,
                        90,
                        new DateOnly(2026, 9, 1),
                        10 * Unit,
                        null,
                        TransactionType.OpeningBalance),
                    Allocation(
                        scope,
                        laterLotId,
                        laterEntryId,
                        91,
                        new DateOnly(2026, 9, 5),
                        20 * Unit,
                        null,
                        TransactionType.Buy),
                    Allocation(
                        scope,
                        earlierLotId,
                        Guid.Parse("81000000-0000-0000-0000-000000000092"),
                        92,
                        new DateOnly(2026, 9, 3),
                        -10 * Unit,
                        null,
                        TransactionType.Sell),
                    Allocation(
                        scope,
                        laterLotId,
                        Guid.Parse("81000000-0000-0000-0000-000000000092"),
                        93,
                        new DateOnly(2026, 9, 3),
                        -5 * Unit,
                        null,
                        TransactionType.Sell)
                ])
        };
        var useCase = new ListLotInventoryUseCase(store);

        var asOf = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(
                HouseholdId,
                AsOf: new DateOnly(2026, 9, 4)));
        var asOfWithExhausted = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(
                HouseholdId,
                AsOf: new DateOnly(2026, 9, 4),
                IncludeExhausted: true));
        var current = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(HouseholdId));

        // The slice keeps the disposal, so lots still sum to the position.
        var later = Assert.Single(asOf.Items);
        Assert.Equal(laterLotId, later.AssetLotId);
        Assert.Equal(20 * Unit, later.OriginalQuantityRawE8);
        Assert.Equal(-5 * Unit, later.GlobalQuantityRawE8);
        Assert.Equal(-5 * Unit, Assert.Single(later.Custody).QuantityRawE8);
        Assert.Equal(
            -5 * Unit,
            asOfWithExhausted.Items.Sum(item => item.GlobalQuantityRawE8));
        Assert.Equal(2, asOfWithExhausted.Items.Count);
        Assert.Equal(
            15 * Unit,
            Assert.Single(current.Items).GlobalQuantityRawE8);
    }

    [Fact]
    public async Task LotInventory_CustodyFiltersFollowDerivedHoldings()
    {
        var source = Scope(AssetId, "FUND_A", AssetType.Fund, AssetUnit.FundUnit);
        var destination = source with
        {
            PortfolioId = SecondPortfolioId,
            PortfolioCode = "SIDE",
            PortfolioName = "Side Portfolio",
            AccountId = SecondAccountId,
            AccountCode = "SECOND",
            AccountName = "Second Account",
            InstitutionId = SecondInstitutionId,
            InstitutionCode = "BANK",
            InstitutionName = "Bank",
            InstitutionType = InstitutionType.Bank
        };
        var lotId = Guid.Parse("80000000-0000-0000-0000-000000000005");
        var openingEntryId = Guid.Parse("81000000-0000-0000-0000-000000000040");
        var store = new FakeInventoryReadStore
        {
            LotFacts = new InventoryLotFactSet(
                [FundLot(lotId, openingEntryId, new DateOnly(2026, 1, 1), 100)],
                [
                    Allocation(
                        source,
                        lotId,
                        openingEntryId,
                        40,
                        new DateOnly(2026, 1, 1),
                        100,
                        null,
                        TransactionType.OpeningBalance),
                    Allocation(
                        source,
                        lotId,
                        Guid.Parse("81000000-0000-0000-0000-000000000041"),
                        41,
                        new DateOnly(2026, 2, 1),
                        -100,
                        null,
                        TransactionType.Transfer),
                    Allocation(
                        destination,
                        lotId,
                        Guid.Parse("81000000-0000-0000-0000-000000000042"),
                        42,
                        new DateOnly(2026, 2, 1),
                        100,
                        null,
                        TransactionType.Transfer)
                ])
        };
        var useCase = new ListLotInventoryUseCase(store);

        var all = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(HouseholdId));
        var sourceHeld = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(HouseholdId, AccountId: AccountId));
        var sourceHistory = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(
                HouseholdId,
                AccountId: AccountId,
                IncludeExhausted: true));
        var destinationHeld = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(
                HouseholdId,
                AccountId: SecondAccountId));
        var sourcePortfolio = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(HouseholdId, PortfolioId: PortfolioId));
        var destinationPortfolio = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(
                HouseholdId,
                PortfolioId: SecondPortfolioId));
        var sourceInstitution = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(
                HouseholdId,
                InstitutionId: InstitutionId));
        var destinationInstitution = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(
                HouseholdId,
                InstitutionId: SecondInstitutionId));

        var lot = Assert.Single(all.Items);
        Assert.Equal(100, lot.GlobalQuantityRawE8);
        var custody = Assert.Single(lot.Custody);
        Assert.Equal(SecondPortfolioId, custody.PortfolioId);
        Assert.Equal(SecondAccountId, custody.AccountId);
        Assert.Equal(SecondInstitutionId, custody.InstitutionId);
        Assert.Equal(100, custody.QuantityRawE8);
        Assert.Empty(sourceHeld.Items);
        Assert.Equal(3, Assert.Single(sourceHistory.Items).Allocations.Count);
        Assert.Single(destinationHeld.Items);
        Assert.Empty(sourcePortfolio.Items);
        Assert.Single(destinationPortfolio.Items);
        Assert.Empty(sourceInstitution.Items);
        Assert.Single(destinationInstitution.Items);
    }

    [Fact]
    public async Task LotInventory_FailsClosedOnInconsistentLineageOrCustody()
    {
        var scope = Scope(AssetId, "FUND_A", AssetType.Fund, AssetUnit.FundUnit);
        var otherAccount = scope with
        {
            AccountId = SecondAccountId,
            AccountCode = "SECOND"
        };
        var lotId = Guid.Parse("80000000-0000-0000-0000-000000000006");
        var openingEntryId = Guid.Parse("81000000-0000-0000-0000-000000000050");
        var lot = FundLot(lotId, openingEntryId, new DateOnly(2026, 1, 1), 100);
        var opening = Allocation(
            scope,
            lotId,
            openingEntryId,
            50,
            new DateOnly(2026, 1, 1),
            100,
            null,
            TransactionType.OpeningBalance);

        var missingOriginalQuantity = new FakeInventoryReadStore
        {
            LotFacts = new InventoryLotFactSet(
                [lot with { OriginalQuantityRawE8 = null }],
                [opening])
        };
        var missingAllocations = new FakeInventoryReadStore
        {
            LotFacts = new InventoryLotFactSet([lot], [])
        };
        var missingOpening = new FakeInventoryReadStore
        {
            LotFacts = new InventoryLotFactSet(
                [lot],
                [
                    Allocation(
                        scope,
                        lotId,
                        Guid.Parse("81000000-0000-0000-0000-000000000051"),
                        51,
                        new DateOnly(2026, 1, 1),
                        100,
                        null,
                        TransactionType.Adjustment)
                ])
        };
        var negativeCustody = new FakeInventoryReadStore
        {
            LotFacts = new InventoryLotFactSet(
                [lot],
                [
                    opening,
                    Allocation(
                        otherAccount,
                        lotId,
                        Guid.Parse("81000000-0000-0000-0000-000000000052"),
                        52,
                        new DateOnly(2026, 2, 1),
                        -40,
                        null,
                        TransactionType.Sell)
                ])
        };
        var pieceMovementOnFund = new FakeInventoryReadStore
        {
            LotFacts = new InventoryLotFactSet(
                [lot],
                [opening with { PieceDelta = 1 }])
        };

        // Complete history is guarded at posting time, so each of these is a
        // persisted-shape fault rather than a derivable state.
        foreach (var store in new[]
                 {
                     missingOriginalQuantity,
                     missingAllocations,
                     missingOpening,
                     negativeCustody,
                     pieceMovementOnFund
                 })
        {
            await Assert.ThrowsAsync<InventoryPersistenceException>(
                () => new ListLotInventoryUseCase(store)
                    .ExecuteAsync(new ListLotInventoryQuery(HouseholdId)));
        }

        await Assert.ThrowsAsync<InventoryPersistenceException>(
            () => new ListLotInventoryUseCase(missingOriginalQuantity)
                .ExecuteAsync(
                    new ListLotInventoryQuery(
                        HouseholdId,
                        AsOf: new DateOnly(2026, 6, 30))));
    }

    [Fact]
    public async Task LotInventory_PreservesKnownAndUnknownCostShapes()
    {
        var scope = Scope(AssetId, "FUND_A", AssetType.Fund, AssetUnit.FundUnit);
        var knownLotId = Guid.Parse("80000000-0000-0000-0000-000000000007");
        var knownEntryId = Guid.Parse("81000000-0000-0000-0000-000000000060");
        var unknownLotId = Guid.Parse("80000000-0000-0000-0000-000000000008");
        var unknownEntryId = Guid.Parse("81000000-0000-0000-0000-000000000061");
        var store = new FakeInventoryReadStore
        {
            LotFacts = new InventoryLotFactSet(
                [
                    FundLot(
                        knownLotId,
                        knownEntryId,
                        new DateOnly(2026, 1, 1),
                        100,
                        CostBasisStatus.Known,
                        12_345,
                        "TRY"),
                    FundLot(
                        unknownLotId,
                        unknownEntryId,
                        new DateOnly(2026, 1, 2),
                        200)
                ],
                [
                    Allocation(
                        scope,
                        knownLotId,
                        knownEntryId,
                        60,
                        new DateOnly(2026, 1, 1),
                        100,
                        null,
                        TransactionType.OpeningBalance),
                    Allocation(
                        scope,
                        unknownLotId,
                        unknownEntryId,
                        61,
                        new DateOnly(2026, 1, 2),
                        200,
                        null,
                        TransactionType.OpeningBalance)
                ])
        };

        var result = await new ListLotInventoryUseCase(store)
            .ExecuteAsync(new ListLotInventoryQuery(HouseholdId));

        var known = Assert.Single(
            result.Items,
            item => item.AssetLotId == knownLotId);
        Assert.Equal(CostBasisStatus.Known, known.CostStatus);
        Assert.Equal(12_345, known.CostMinorUnits);
        Assert.Equal("TRY", known.CostCurrencyCode);
        var unknown = Assert.Single(
            result.Items,
            item => item.AssetLotId == unknownLotId);
        Assert.Equal(CostBasisStatus.Unknown, unknown.CostStatus);
        Assert.Null(unknown.CostMinorUnits);
        Assert.Null(unknown.CostCurrencyCode);
    }

    [Fact]
    public async Task LotInventory_HidesExhaustedByDefaultAndCanShowHistory()
    {
        var scope = Scope(AssetId, "FUND_A", AssetType.Fund, AssetUnit.FundUnit);
        var lotId = Guid.Parse("80000000-0000-0000-0000-000000000002");
        var openingEntryId = Guid.Parse("81000000-0000-0000-0000-000000000010");
        var store = new FakeInventoryReadStore
        {
            LotFacts = new InventoryLotFactSet(
                [FundLot(lotId, openingEntryId, new DateOnly(2026, 1, 1), 100)],
                [
                    Allocation(
                        scope,
                        lotId,
                        openingEntryId,
                        10,
                        new DateOnly(2026, 1, 1),
                        100,
                        null,
                        TransactionType.OpeningBalance),
                    Allocation(
                        scope,
                        lotId,
                        Guid.Parse("81000000-0000-0000-0000-000000000011"),
                        11,
                        new DateOnly(2026, 2, 1),
                        -100,
                        null,
                        TransactionType.Sell)
                ])
        };
        var useCase = new ListLotInventoryUseCase(store);

        var current = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(HouseholdId));
        var history = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(
                HouseholdId,
                AccountId: AccountId,
                IncludeExhausted: true));

        Assert.Empty(current.Items);
        var exhausted = Assert.Single(history.Items);
        Assert.Equal(0, exhausted.GlobalQuantityRawE8);
        Assert.Empty(exhausted.Custody);
        Assert.Equal(2, exhausted.Allocations.Count);
    }

    private static InventoryScopeContext Scope(
        Guid assetId,
        string assetCode,
        AssetType assetType,
        AssetUnit assetUnit)
        => new(
            PortfolioId,
            "CORE",
            "Core Portfolio",
            PortfolioStatus.Active,
            AccountId,
            "PRIMARY",
            "Primary Account",
            AccountType.Investment,
            true,
            InstitutionId,
            "BROKER",
            "Broker",
            InstitutionType.Broker,
            true,
            assetId,
            assetCode,
            assetCode,
            assetType,
            assetUnit,
            "TRY",
            LotTrackingMode.Required,
            true);

    private static InventoryLotDescriptor FundLot(
        Guid lotId,
        Guid openingEntryId,
        DateOnly acquiredOn,
        long? originalQuantityRawE8,
        CostBasisStatus costStatus = CostBasisStatus.Unknown,
        long? costMinorUnits = null,
        string? costCurrencyCode = null)
        => new(
            lotId,
            Guid.Parse("70000000-0000-0000-0000-000000000020"),
            openingEntryId,
            AssetId,
            "FUND_A",
            "Fund A",
            AssetType.Fund,
            AssetUnit.FundUnit,
            "TRY",
            LotTrackingMode.Required,
            true,
            acquiredOn,
            costStatus,
            costMinorUnits,
            costCurrencyCode,
            originalQuantityRawE8,
            null,
            null,
            null,
            null,
            null);

    private static InventoryLotDescriptor GoldLot(
        Guid lotId,
        Guid openingEntryId,
        long originalQuantityRawE8,
        int pieceCount)
        => new(
            lotId,
            Guid.Parse("70000000-0000-0000-0000-000000000001"),
            openingEntryId,
            GoldAssetId,
            "GOLD",
            "Physical Gold",
            AssetType.PhysicalGold,
            AssetUnit.GrossGram,
            "TRY",
            LotTrackingMode.Required,
            true,
            new DateOnly(2026, 1, 1),
            CostBasisStatus.Known,
            12_000,
            "TRY",
            originalQuantityRawE8,
            916_000,
            pieceCount,
            "TEST",
            "CERT",
            null);

    private static InventoryEntryFact Entry(
        InventoryScopeContext scope,
        int ordinal,
        DateOnly executionDate,
        long delta)
        => new(
            Guid.Parse($"91000000-0000-0000-0000-{ordinal:D12}"),
            Guid.Parse($"92000000-0000-0000-0000-{ordinal:D12}"),
            executionDate,
            new DateTimeOffset(
                executionDate.Year,
                executionDate.Month,
                executionDate.Day,
                8,
                0,
                0,
                TimeSpan.Zero),
            0,
            scope,
            delta);

    private static InventoryLotAllocationFact Allocation(
        InventoryScopeContext scope,
        Guid lotId,
        Guid entryId,
        int ordinal,
        DateOnly executionDate,
        long delta,
        int? pieceDelta,
        TransactionType transactionType)
        => new(
            lotId,
            Guid.Parse($"93000000-0000-0000-0000-{ordinal:D12}"),
            entryId,
            Guid.Parse($"94000000-0000-0000-0000-{ordinal:D12}"),
            transactionType,
            executionDate,
            new DateTimeOffset(
                executionDate.Year,
                executionDate.Month,
                executionDate.Day,
                8,
                0,
                0,
                TimeSpan.Zero),
            0,
            scope,
            delta,
            pieceDelta);

    // Mirrors EfCoreInventoryReadStore: lots are lineage and always returned,
    // while the as-of bound applies to allocations only.
    private sealed class FakeInventoryReadStore : IInventoryReadStore
    {
        public bool HouseholdExists { get; set; } = true;

        public bool FilterScopeExists { get; set; } = true;

        public InventoryScopeContext? ExactScope { get; set; }

        public IReadOnlyList<InventoryEntryFact> PositionFacts { get; set; } = [];

        public InventoryLotFactSet LotFacts { get; set; } = new([], []);

        public Task<bool> HouseholdExistsAsync(
            Guid householdId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(HouseholdExists && householdId == HouseholdId);

        public Task<bool> FilterScopeExistsAsync(
            Guid householdId,
            Guid? portfolioId,
            Guid? accountId,
            Guid? institutionId,
            Guid? assetId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(FilterScopeExists && householdId == HouseholdId);

        public Task<InventoryScopeContext?> FindExactPositionScopeAsync(
            Guid householdId,
            Guid portfolioId,
            Guid accountId,
            Guid assetId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ExactScope);

        public Task<IReadOnlyList<InventoryEntryFact>> ListPositionEntryFactsAsync(
            Guid householdId,
            Guid? portfolioId,
            Guid? accountId,
            Guid? institutionId,
            Guid? assetId,
            DateOnly? asOf,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<InventoryEntryFact>>(
                PositionFacts
                    .Where(fact => asOf is null || fact.ExecutionDate <= asOf)
                    .ToArray());

        public Task<InventoryLotFactSet> ListLotFactsAsync(
            Guid householdId,
            Guid? assetId,
            DateOnly? asOf,
            CancellationToken cancellationToken = default)
        {
            var allocations = LotFacts.Allocations
                .Where(
                    allocation => asOf is null
                                  || allocation.ExecutionDate <= asOf)
                .ToArray();

            return Task.FromResult(
                new InventoryLotFactSet(LotFacts.Lots, allocations));
        }
    }
}
