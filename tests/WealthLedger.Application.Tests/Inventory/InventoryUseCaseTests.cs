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
    private static readonly Guid HouseholdId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid PortfolioId =
        Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid AccountId =
        Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid InstitutionId =
        Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid AssetId =
        Guid.Parse("60000000-0000-0000-0000-000000000001");

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
        var goldAssetId = Guid.Parse("60000000-0000-0000-0000-000000000009");
        var goldScope = Scope(
            goldAssetId,
            "GOLD",
            AssetType.PhysicalGold,
            AssetUnit.GrossGram);
        var lotId = Guid.Parse("80000000-0000-0000-0000-000000000001");
        var openingEntryId = Guid.Parse("81000000-0000-0000-0000-000000000001");
        var descriptor = new InventoryLotDescriptor(
            lotId,
            Guid.Parse("70000000-0000-0000-0000-000000000001"),
            openingEntryId,
            goldAssetId,
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
            new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero),
            916_000,
            2,
            "TEST",
            "CERT",
            null);
        var store = new FakeInventoryReadStore
        {
            LotFacts = new InventoryLotFactSet(
                [descriptor],
                [
                    Allocation(
                        goldScope,
                        lotId,
                        openingEntryId,
                        1,
                        new DateOnly(2026, 1, 1),
                        20 * 100_000_000L,
                        2),
                    Allocation(
                        goldScope,
                        lotId,
                        Guid.Parse("81000000-0000-0000-0000-000000000002"),
                        2,
                        new DateOnly(2026, 2, 1),
                        -5 * 100_000_000L,
                        -1)
                ])
        };
        var result = await new ListLotInventoryUseCase(store)
            .ExecuteAsync(new ListLotInventoryQuery(HouseholdId));

        var item = Assert.Single(result.Items);
        Assert.Equal(20 * 100_000_000L, item.OriginalQuantityRawE8);
        Assert.Equal(15 * 100_000_000L, item.GlobalQuantityRawE8);
        var physical = Assert.IsType<PhysicalGoldLotInventoryDetail>(
            item.PhysicalGold);
        Assert.Equal(1, physical.GlobalPieceCount);
        Assert.Equal(13.74m, physical.GlobalFineWeightGrams);
        // Same value and decimal scale as the M009 Domain derivation.
        var m009FineWeight = new PhysicalGoldLotDetail(new Fineness(916_000), 2)
            .CalculateFineWeightGrams(Quantity.FromRaw(15 * 100_000_000L));
        Assert.Equal(
            m009FineWeight.ToString(CultureInfo.InvariantCulture),
            physical.GlobalFineWeightGrams.ToString(CultureInfo.InvariantCulture));
        var custody = Assert.Single(item.Custody);
        Assert.Equal(1, custody.PieceCount);
        Assert.Equal(13.74m, custody.FineWeightGrams);
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
                [FundLot(lotId, openingEntryId, new DateOnly(2026, 1, 1))],
                [
                    Allocation(
                        scope,
                        lotId,
                        openingEntryId,
                        20,
                        new DateOnly(2026, 1, 1),
                        long.MaxValue,
                        null),
                    Allocation(
                        scope,
                        lotId,
                        Guid.Parse("81000000-0000-0000-0000-000000000021"),
                        21,
                        new DateOnly(2026, 1, 2),
                        1,
                        null)
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
                [FundLot(lotId, openingEntryId, new DateOnly(2026, 3, 1))],
                [
                    Allocation(
                        scope,
                        lotId,
                        openingEntryId,
                        30,
                        new DateOnly(2026, 3, 1),
                        100,
                        null)
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
    public async Task LotInventory_CustodyFiltersFollowDerivedHoldings()
    {
        var secondAccountId = Guid.Parse("50000000-0000-0000-0000-000000000002");
        var secondInstitutionId = Guid.Parse("30000000-0000-0000-0000-000000000002");
        var source = Scope(AssetId, "FUND_A", AssetType.Fund, AssetUnit.FundUnit);
        var destination = source with
        {
            AccountId = secondAccountId,
            AccountCode = "SECOND",
            AccountName = "Second Account",
            InstitutionId = secondInstitutionId,
            InstitutionCode = "BANK",
            InstitutionName = "Bank",
            InstitutionType = InstitutionType.Bank
        };
        var lotId = Guid.Parse("80000000-0000-0000-0000-000000000005");
        var openingEntryId = Guid.Parse("81000000-0000-0000-0000-000000000040");
        var store = new FakeInventoryReadStore
        {
            LotFacts = new InventoryLotFactSet(
                [FundLot(lotId, openingEntryId, new DateOnly(2026, 1, 1))],
                [
                    Allocation(
                        source,
                        lotId,
                        openingEntryId,
                        40,
                        new DateOnly(2026, 1, 1),
                        100,
                        null),
                    Allocation(
                        source,
                        lotId,
                        Guid.Parse("81000000-0000-0000-0000-000000000041"),
                        41,
                        new DateOnly(2026, 2, 1),
                        -100,
                        null),
                    Allocation(
                        destination,
                        lotId,
                        Guid.Parse("81000000-0000-0000-0000-000000000042"),
                        42,
                        new DateOnly(2026, 2, 1),
                        100,
                        null)
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
                AccountId: secondAccountId));
        var sourceInstitution = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(
                HouseholdId,
                InstitutionId: InstitutionId));
        var destinationInstitution = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(
                HouseholdId,
                InstitutionId: secondInstitutionId));

        var lot = Assert.Single(all.Items);
        Assert.Equal(100, lot.GlobalQuantityRawE8);
        var custody = Assert.Single(lot.Custody);
        Assert.Equal(secondAccountId, custody.AccountId);
        Assert.Equal(secondInstitutionId, custody.InstitutionId);
        Assert.Equal(100, custody.QuantityRawE8);
        Assert.Empty(sourceHeld.Items);
        Assert.Equal(3, Assert.Single(sourceHistory.Items).Allocations.Count);
        Assert.Single(destinationHeld.Items);
        Assert.Empty(sourceInstitution.Items);
        Assert.Single(destinationInstitution.Items);
    }

    [Fact]
    public async Task LotInventory_FailsClosedOnInconsistentLineageOrCustody()
    {
        var scope = Scope(AssetId, "FUND_A", AssetType.Fund, AssetUnit.FundUnit);
        var otherAccount = scope with
        {
            AccountId = Guid.Parse("50000000-0000-0000-0000-000000000002"),
            AccountCode = "SECOND"
        };
        var lotId = Guid.Parse("80000000-0000-0000-0000-000000000006");
        var openingEntryId = Guid.Parse("81000000-0000-0000-0000-000000000050");
        var lot = FundLot(lotId, openingEntryId, new DateOnly(2026, 1, 1));
        var opening = Allocation(
            scope,
            lotId,
            openingEntryId,
            50,
            new DateOnly(2026, 1, 1),
            100,
            null);

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
                        null)
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
                        null)
                ])
        };
        var pieceMovementOnFund = new FakeInventoryReadStore
        {
            LotFacts = new InventoryLotFactSet(
                [lot],
                [opening with { PieceDelta = 1 }])
        };

        foreach (var store in new[]
                 {
                     missingOpening,
                     negativeCustody,
                     pieceMovementOnFund
                 })
        {
            await Assert.ThrowsAsync<InventoryPersistenceException>(
                () => new ListLotInventoryUseCase(store)
                    .ExecuteAsync(new ListLotInventoryQuery(HouseholdId)));
        }
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
                        CostBasisStatus.Known,
                        12_345,
                        "TRY"),
                    FundLot(
                        unknownLotId,
                        unknownEntryId,
                        new DateOnly(2026, 1, 2))
                ],
                [
                    Allocation(
                        scope,
                        knownLotId,
                        knownEntryId,
                        60,
                        new DateOnly(2026, 1, 1),
                        100,
                        null),
                    Allocation(
                        scope,
                        unknownLotId,
                        unknownEntryId,
                        61,
                        new DateOnly(2026, 1, 2),
                        200,
                        null)
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
        var descriptor = new InventoryLotDescriptor(
            lotId,
            Guid.Parse("70000000-0000-0000-0000-000000000010"),
            openingEntryId,
            AssetId,
            "FUND_A",
            "Fund A",
            AssetType.Fund,
            AssetUnit.FundUnit,
            "TRY",
            LotTrackingMode.Required,
            true,
            new DateOnly(2026, 1, 1),
            CostBasisStatus.Unknown,
            null,
            null,
            new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero),
            null,
            null,
            null,
            null,
            null);
        var store = new FakeInventoryReadStore
        {
            LotFacts = new InventoryLotFactSet(
                [descriptor],
                [
                    Allocation(
                        scope,
                        lotId,
                        openingEntryId,
                        10,
                        new DateOnly(2026, 1, 1),
                        100,
                        null),
                    Allocation(
                        scope,
                        lotId,
                        Guid.Parse("81000000-0000-0000-0000-000000000011"),
                        11,
                        new DateOnly(2026, 2, 1),
                        -100,
                        null)
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
            new DateTimeOffset(
                acquiredOn.Year,
                acquiredOn.Month,
                acquiredOn.Day,
                8,
                0,
                0,
                TimeSpan.Zero),
            null,
            null,
            null,
            null,
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
        int? pieceDelta)
        => new(
            lotId,
            Guid.Parse($"93000000-0000-0000-0000-{ordinal:D12}"),
            entryId,
            Guid.Parse($"94000000-0000-0000-0000-{ordinal:D12}"),
            ordinal == 1 || ordinal == 10
                ? TransactionType.OpeningBalance
                : TransactionType.Sell,
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
