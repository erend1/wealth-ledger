using System.Net;
using WealthLedger.Api.Contracts;

namespace WealthLedger.Api.Tests;

public sealed class InventoryApiTests
{
    private const long Unit = M010ApiTestData.Unit;

    [Fact]
    public async Task Positions_DeriveCurrentAndAsOfFromPostedHistoryWithSourceCounts()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var history = await M010ApiTestData.SeedAsync(factory, client);
        var setup = history.Setup;
        var route = $"{history.HouseholdRoute}/inventory/positions";
        var before = await M010ApiTestData.ReadLedgerCountsAsync(factory);

        var current = await M010ApiTestData.GetAsync<PositionInventoryResponse>(client, route);

        Assert.Equal(setup.HouseholdId, current.HouseholdId);
        Assert.Null(current.AsOf);

        // 1,000 + 500 contributed, 250 paid for 1.25 fund units. The reversed
        // direct contribution nets to an exact zero and is hidden by default.
        Assert.Equal(2, current.Items.Count);
        var cash = Single(current, setup.AccountId, setup.CashAssetId);
        Assert.Equal(1_250 * Unit, cash.QuantityRawE8);
        Assert.Equal((3, 3), (cash.SourceEntryCount, cash.SourceTransactionCount));
        Assert.Equal("PRIMARY", cash.AccountCode);
        Assert.Equal("SYNTHETIC_INSTITUTION", cash.InstitutionCode);
        Assert.Equal("CURRENCY_UNIT", cash.AssetBaseUnitCode);
        var fund = Single(current, setup.AccountId, setup.FundAssetId);
        Assert.Equal(125_000_000, fund.QuantityRawE8);
        Assert.Equal((1, 1), (fund.SourceEntryCount, fund.SourceTransactionCount));

        var withZero = await M010ApiTestData.GetAsync<PositionInventoryResponse>(
            client,
            $"{route}?includeZero=true");
        var direct = Single(withZero, history.DirectAccountId, setup.CashAssetId);
        Assert.Equal(0, direct.QuantityRawE8);
        Assert.Equal((2, 2), (direct.SourceEntryCount, direct.SourceTransactionCount));
        Assert.Null(direct.InstitutionId);

        var january = await M010ApiTestData.GetAsync<PositionInventoryResponse>(
            client,
            $"{route}?asOf=2026-01-31");
        Assert.Equal(new DateOnly(2026, 1, 31), january.AsOf);
        Assert.Equal(1_500 * Unit, Assert.Single(january.Items).QuantityRawE8);

        // The reversal keeps its own effective date; the as-of slice follows it.
        var reversal = Assert.Single(
            (await M010ApiTestData.GetAsync<
                NavigationPageResponse<LedgerSearchTransactionResponse>>(
                client,
                $"{history.HouseholdRoute}/ledger/search?reversal=REVERSAL_ONLY")).Items);
        var afterDirect = await M010ApiTestData.GetAsync<PositionInventoryResponse>(
            client,
            $"{route}?asOf=2026-02-10&includeZero=true&accountId={history.DirectAccountId}");
        Assert.Equal(
            reversal.ExecutionDate <= new DateOnly(2026, 2, 10) ? 0 : 70 * Unit,
            Assert.Single(afterDirect.Items).QuantityRawE8);

        // A valid exact scope without effective entries is a genuine zero.
        var emptyExact = await M010ApiTestData.GetAsync<PositionInventoryResponse>(
            client,
            $"{route}?portfolioId={setup.PortfolioId}&accountId={setup.AccountId}"
            + $"&assetId={setup.FundAssetId}&asOf=2026-01-31&includeZero=true");
        var zero = Assert.Single(emptyExact.Items);
        Assert.Equal((0L, 0, 0), (zero.QuantityRawE8, zero.SourceEntryCount, zero.SourceTransactionCount));

        // The other household derives only its own history.
        var other = await M010ApiTestData.GetAsync<PositionInventoryResponse>(
            client,
            $"/api/households/{history.OtherHouseholdId}/inventory/positions");
        Assert.Equal(90 * Unit, Assert.Single(other.Items).QuantityRawE8);

        Assert.Equal(before, await M010ApiTestData.ReadLedgerCountsAsync(factory));
    }

    [Fact]
    public async Task Positions_UnknownCrossHouseholdOrMismatchedScopeIsNotZero()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var history = await M010ApiTestData.SeedAsync(factory, client);
        var setup = history.Setup;
        var route = $"{history.HouseholdRoute}/inventory/positions";

        foreach (var query in new[]
                 {
                     $"?accountId={history.OtherAccountId}",
                     $"?portfolioId={history.OtherPortfolioId}",
                     $"?assetId={Guid.NewGuid()}",
                     $"?institutionId={Guid.NewGuid()}",

                     // The direct account has no institution, so this pair
                     // names no exact scope.
                     $"?accountId={history.DirectAccountId}&institutionId={setup.InstitutionId}",
                     $"?portfolioId={setup.PortfolioId}&accountId={history.OtherAccountId}"
                     + $"&assetId={setup.CashAssetId}&includeZero=true"
                 })
        {
            await M010ApiTestData.AssertGetProblemAsync(
                client,
                route + query,
                HttpStatusCode.NotFound,
                "INVENTORY_SCOPE_NOT_FOUND",
                "Synthetic");
        }

        await M010ApiTestData.AssertGetProblemAsync(
            client,
            $"/api/households/{Guid.NewGuid()}/inventory/positions",
            HttpStatusCode.NotFound,
            "INVENTORY_SCOPE_NOT_FOUND");
        await M010ApiTestData.AssertGetProblemAsync(
            client,
            $"/api/households/{Guid.NewGuid()}/inventory/lots",
            HttpStatusCode.NotFound,
            "INVENTORY_SCOPE_NOT_FOUND");
    }

    [Fact]
    public async Task Lots_DeriveLineageCustodyAndPhysicalGoldAndPageWithABoundCursor()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var history = await M010ApiTestData.SeedAsync(factory, client);
        var gold = await M010ApiTestData.SeedGoldAsync(factory, client);
        var setup = history.Setup;
        var route = $"{history.HouseholdRoute}/inventory/lots";

        var all = await M010ApiTestData.GetAsync<LotInventoryResponse>(client, route);
        Assert.Null(all.NextCursor);
        Assert.Equal(2, all.Items.Count);

        var fund = Assert.Single(all.Items, item => item.AssetLotId == history.FundLotId);
        Assert.Equal(history.FundPurchaseId, fund.CreatingTransactionId);
        Assert.Equal(M010ApiTestData.FundDate, fund.AcquiredOn);
        Assert.Equal((125_000_000L, 125_000_000L), (fund.OriginalQuantityRawE8, fund.GlobalQuantityRawE8));
        Assert.Equal("FUND", fund.AssetTypeCode);
        Assert.Equal(
            ("KNOWN", (long?)25_000, "TRY"),
            (fund.CostStatusCode, fund.CostMinorUnits, fund.CostCurrencyCode));
        Assert.Null(fund.PhysicalGold);
        var fundCustody = Assert.Single(fund.Custody);
        Assert.Equal((setup.AccountId, 125_000_000L), (fundCustody.AccountId, fundCustody.QuantityRawE8));
        Assert.Null(fundCustody.PieceCount);
        Assert.Null(fundCustody.FineWeightGramsExact);
        var opening = Assert.Single(fund.Allocations);
        Assert.Equal(("BUY", history.FundPurchaseId), (opening.TransactionTypeCode, opening.TransactionId));

        var goldLot = Assert.Single(all.Items, item => item.AssetLotId == gold.LotId);
        Assert.Equal("PHYSICAL_GOLD", goldLot.AssetTypeCode);
        Assert.Equal((20 * Unit, 20 * Unit), (goldLot.OriginalQuantityRawE8, goldLot.GlobalQuantityRawE8));
        var physical = Assert.IsType<PhysicalGoldLotInventoryResponse>(goldLot.PhysicalGold);
        Assert.Equal((916_000, 2, 2), (physical.FinenessPpm, physical.OriginalPieceCount, physical.GlobalPieceCount));
        Assert.Equal("18.32", physical.GlobalFineWeightGramsExact);
        Assert.Equal("SYNTH-CERT-001", physical.CertificateReference);
        var source = Assert.Single(goldLot.Custody, item => item.AccountId == gold.SourceVaultId);
        Assert.Equal((15 * Unit, 1, "13.74"), (source.QuantityRawE8, source.PieceCount, source.FineWeightGramsExact));
        var destination = Assert.Single(goldLot.Custody, item => item.AccountId == gold.DestinationVaultId);
        Assert.Equal((5 * Unit, 1, "4.58"), (destination.QuantityRawE8, destination.PieceCount, destination.FineWeightGramsExact));
        // Purchase, then the transfer's paired allocations; signed pieces sum
        // to the two still held.
        Assert.Equal(3, goldLot.Allocations.Count);
        Assert.Equal(2, goldLot.Allocations.Sum(item => item.PieceDelta ?? 0));

        // Custody filters follow the derived holding.
        var inDestination = await M010ApiTestData.GetAsync<LotInventoryResponse>(
            client,
            $"{route}?accountId={gold.DestinationVaultId}");
        Assert.Equal(gold.LotId, Assert.Single(inDestination.Items).AssetLotId);
        var beforeGold = await M010ApiTestData.GetAsync<LotInventoryResponse>(
            client,
            $"{route}?asOf=2026-09-13");
        Assert.Equal(history.FundLotId, Assert.Single(beforeGold.Items).AssetLotId);

        // Pages of one reproduce the full order without gaps.
        var first = await M010ApiTestData.GetAsync<LotInventoryResponse>(client, $"{route}?pageSize=1");
        var cursor = Assert.IsType<string>(first.NextCursor);
        var second = await M010ApiTestData.GetAsync<LotInventoryResponse>(
            client,
            $"{route}?pageSize=1&cursor={Uri.EscapeDataString(cursor)}");
        Assert.Null(second.NextCursor);
        Assert.Equal(
            all.Items.Select(item => item.AssetLotId),
            first.Items.Concat(second.Items).Select(item => item.AssetLotId));

        await M010ApiTestData.AssertGetProblemAsync(
            client,
            $"{route}?pageSize=1&includeExhausted=true&cursor={Uri.EscapeDataString(cursor)}",
            HttpStatusCode.BadRequest,
            "INVENTORY_CURSOR_SCOPE_MISMATCH",
            cursor);
        await M010ApiTestData.AssertGetProblemAsync(
            client,
            $"/api/households/{history.OtherHouseholdId}/inventory/lots?pageSize=1&cursor={Uri.EscapeDataString(cursor)}",
            HttpStatusCode.BadRequest,
            "INVENTORY_CURSOR_SCOPE_MISMATCH",
            cursor);
    }

    [Fact]
    public async Task Assets_TraceAPositionToItsLotsAndTransactions()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var history = await M010ApiTestData.SeedAsync(factory, client);
        var gold = await M010ApiTestData.SeedGoldAsync(factory, client);
        var setup = history.Setup;

        var position = Assert.Single(
            (await M010ApiTestData.GetAsync<PositionInventoryResponse>(
                client,
                $"{history.HouseholdRoute}/inventory/positions?accountId={gold.DestinationVaultId}")).Items);
        Assert.Equal((5 * Unit, 1), (position.QuantityRawE8, position.SourceTransactionCount));

        // Position -> lots in the same exact scope.
        var lots = await M010ApiTestData.GetAsync<LotInventoryResponse>(
            client,
            $"{history.HouseholdRoute}/inventory/lots?portfolioId={position.PortfolioId}"
            + $"&accountId={position.AccountId}&assetId={position.AssetId}");
        var lot = Assert.Single(lots.Items);
        Assert.Equal(
            position.QuantityRawE8,
            lot.Custody.Single(item => item.AccountId == position.AccountId).QuantityRawE8);

        // Position -> its source transactions through search on the same scope.
        var sources = await M010ApiTestData.GetAsync<
            NavigationPageResponse<LedgerSearchTransactionResponse>>(
            client,
            $"{history.HouseholdRoute}/ledger/search?portfolioId={position.PortfolioId}"
            + $"&accountId={position.AccountId}&assetId={position.AssetId}");
        Assert.Equal(position.SourceTransactionCount, sources.Items.Count);
        Assert.Equal(gold.TransferId, Assert.Single(sources.Items).TransactionId);

        // Lot -> each allocation's transaction readback.
        foreach (var allocation in lot.Allocations)
        {
            using var detail = await client.GetAsync($"/api/ledger/transactions/{allocation.TransactionId}");
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        }

        Assert.Contains(lot.Allocations, item => item.TransactionId == gold.PurchaseId);
        Assert.Equal(setup.PortfolioId, position.PortfolioId);
    }

    [Fact]
    public async Task Inventory_RefusesMalformedInputWithSanitizedProblems()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var history = await M010ApiTestData.SeedAsync(factory, client);
        var positions = $"{history.HouseholdRoute}/inventory/positions";
        var lots = $"{history.HouseholdRoute}/inventory/lots";

        (string Route, string Code)[] cases =
        [
            ($"{positions}?asOf=2026-02-30", "INVENTORY_FILTER_INVALID"),
            ($"{positions}?asOf=today", "INVENTORY_FILTER_INVALID"),
            ($"{positions}?includeZero=yes", "INVENTORY_FILTER_INVALID"),
            ($"{positions}?accountId=not-a-guid", "INVENTORY_FILTER_INVALID"),
            ($"{positions}?accountId={Guid.Empty}", "INVENTORY_FILTER_INVALID"),
            ($"{positions}?accountId={history.DirectAccountId}&accountId={history.DirectAccountId}", "INVENTORY_FILTER_INVALID"),
            ($"{positions}?pageSize=10", "INVENTORY_FILTER_INVALID"),
            ($"{positions}?includeExhausted=true", "INVENTORY_FILTER_INVALID"),
            ($"{lots}?includeZero=true", "INVENTORY_FILTER_INVALID"),
            ($"{lots}?includeExhausted=1", "INVENTORY_FILTER_INVALID"),
            ($"{lots}?pageSize=0", "INVENTORY_PAGE_SIZE_INVALID"),
            ($"{lots}?pageSize=101", "INVENTORY_PAGE_SIZE_INVALID"),
            ($"{lots}?cursor=PRIVATE%2BCURSOR%2FPAYLOAD", "INVENTORY_CURSOR_INVALID"),
            ($"/api/households/{Guid.Empty}/inventory/positions", "INVENTORY_FILTER_INVALID"),
            ($"/api/households/{Guid.Empty}/inventory/lots", "INVENTORY_FILTER_INVALID")
        ];

        foreach (var (route, code) in cases)
        {
            await M010ApiTestData.AssertGetProblemAsync(
                client,
                route,
                HttpStatusCode.BadRequest,
                code,
                "PRIVATE+CURSOR/PAYLOAD");
        }
    }

    private static PositionInventoryItemResponse Single(
        PositionInventoryResponse inventory,
        Guid accountId,
        Guid assetId)
        => Assert.Single(
            inventory.Items,
            item => item.AccountId == accountId && item.AssetId == assetId);
}
