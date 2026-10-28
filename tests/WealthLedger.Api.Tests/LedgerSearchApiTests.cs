using System.Net;
using WealthLedger.Api.Contracts;

namespace WealthLedger.Api.Tests;

public sealed class LedgerSearchApiTests
{
    [Fact]
    public async Task Search_AppliesEachFilterAndRepresentativeCombinations()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var history = await M010ApiTestData.SeedAsync(factory, client);
        var setup = history.Setup;
        var all = new[]
        {
            history.FirstJanuaryId,
            history.SecondJanuaryId,
            history.DirectFebruaryId,
            history.FundPurchaseId,
            history.ReversalId
        };

        async Task AssertMatchesAsync(string query, params Guid[] expected)
        {
            var page = await M010ApiTestData.GetAsync<
                NavigationPageResponse<LedgerSearchTransactionResponse>>(
                client,
                $"{history.HouseholdRoute}/ledger/search{query}");
            var ids = page.Items.Select(item => item.TransactionId).ToArray();

            // Entry-level filters select each transaction once.
            Assert.Equal(ids.Length, ids.Distinct().Count());
            Assert.Equal(
                expected.OrderBy(id => id).ToArray(),
                ids.OrderBy(id => id).ToArray());
            Assert.Null(page.NextCursor);
        }

        await AssertMatchesAsync(string.Empty, all);
        await AssertMatchesAsync("?status=POSTED", all);
        await AssertMatchesAsync(
            "?executedTo=2026-01-31",
            history.FirstJanuaryId,
            history.SecondJanuaryId);
        await AssertMatchesAsync(
            "?executedFrom=2026-01-10&executedTo=2026-01-10",
            history.FirstJanuaryId,
            history.SecondJanuaryId);
        await AssertMatchesAsync(
            "?executedFrom=2026-02-06&executedTo=2026-02-20",
            history.FundPurchaseId);
        await AssertMatchesAsync(
            "?type=CONTRIBUTION",
            history.FirstJanuaryId,
            history.SecondJanuaryId,
            history.DirectFebruaryId);
        await AssertMatchesAsync("?type=BUY", history.FundPurchaseId);
        await AssertMatchesAsync("?type=REVERSAL", history.ReversalId);
        await AssertMatchesAsync(
            "?type=CONTRIBUTION&type=BUY",
            history.FirstJanuaryId,
            history.SecondJanuaryId,
            history.DirectFebruaryId,
            history.FundPurchaseId);
        await AssertMatchesAsync($"?assetId={setup.FundAssetId}", history.FundPurchaseId);
        await AssertMatchesAsync($"?assetId={setup.CashAssetId}", all);
        await AssertMatchesAsync(
            $"?accountId={history.DirectAccountId}",
            history.DirectFebruaryId,
            history.ReversalId);

        // The fund purchase has a fund and a cash entry in the account.
        await AssertMatchesAsync(
            $"?accountId={setup.AccountId}",
            history.FirstJanuaryId,
            history.SecondJanuaryId,
            history.FundPurchaseId);
        await AssertMatchesAsync(
            $"?institutionId={setup.InstitutionId}",
            history.FirstJanuaryId,
            history.SecondJanuaryId,
            history.FundPurchaseId);
        await AssertMatchesAsync($"?portfolioId={setup.PortfolioId}", all);
        await AssertMatchesAsync(
            "?externalReference=jan-00",
            history.FirstJanuaryId,
            history.SecondJanuaryId);
        await AssertMatchesAsync(
            "?externalReference=%20%20synth-fund%20",
            history.FundPurchaseId);
        await AssertMatchesAsync("?reversal=ANY", all);
        await AssertMatchesAsync("?reversal=REVERSAL_ONLY", history.ReversalId);
        await AssertMatchesAsync("?reversal=REVERSED_ORIGINAL", history.DirectFebruaryId);
        await AssertMatchesAsync(
            "?reversal=ORIGINAL_ONLY",
            history.FirstJanuaryId,
            history.SecondJanuaryId,
            history.DirectFebruaryId,
            history.FundPurchaseId);
        await AssertMatchesAsync(
            $"?type=CONTRIBUTION&accountId={setup.AccountId}&executedTo=2026-01-31&externalReference=002",
            history.SecondJanuaryId);

        // Each identity exists in the household; no transaction touches both,
        // so the valid result is empty rather than a scope error.
        await AssertMatchesAsync(
            $"?accountId={history.DirectAccountId}&institutionId={setup.InstitutionId}");

        // The other household sees only its own history.
        var other = await M010ApiTestData.GetAsync<
            NavigationPageResponse<LedgerSearchTransactionResponse>>(
            client,
            $"/api/households/{history.OtherHouseholdId}/ledger/search");
        Assert.Equal(
            history.OtherHouseholdTransactionId,
            Assert.Single(other.Items).TransactionId);
    }

    [Fact]
    public async Task Search_PagesAcrossSameDateTiesAndBindsTheCursorToFiltersAndHousehold()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var history = await M010ApiTestData.SeedAsync(factory, client);
        var route = $"{history.HouseholdRoute}/ledger/search";

        var full = await M010ApiTestData.GetAsync<
            NavigationPageResponse<LedgerSearchTransactionResponse>>(client, route);
        var paged = new List<LedgerSearchTransactionResponse>();
        string? cursor = null;
        string? firstCursor = null;

        do
        {
            var page = await M010ApiTestData.GetAsync<
                NavigationPageResponse<LedgerSearchTransactionResponse>>(
                client,
                cursor is null
                    ? $"{route}?pageSize=1"
                    : $"{route}?pageSize=1&cursor={Uri.EscapeDataString(cursor)}");
            paged.Add(Assert.Single(page.Items));
            cursor = page.NextCursor;
            firstCursor ??= cursor;
        }
        while (cursor is not null);

        Assert.Equal(
            full.Items.Select(item => item.TransactionId),
            paged.Select(item => item.TransactionId));

        // Execution date, then posting time, then identity, all descending.
        Assert.Equal(
            full.Items
                .OrderByDescending(item => item.ExecutionDate)
                .ThenByDescending(item => item.PostedAtUtc)
                .ThenByDescending(item => item.TransactionId)
                .Select(item => item.TransactionId),
            full.Items.Select(item => item.TransactionId));
        Assert.Contains(
            full.Items.GroupBy(item => item.ExecutionDate),
            group => group.Count() > 1);

        var escaped = Uri.EscapeDataString(firstCursor!);
        await M010ApiTestData.AssertGetProblemAsync(
            client,
            $"{route}?pageSize=1&type=CONTRIBUTION&cursor={escaped}",
            HttpStatusCode.BadRequest,
            "LEDGER_SEARCH_CURSOR_SCOPE_MISMATCH",
            firstCursor!);
        await M010ApiTestData.AssertGetProblemAsync(
            client,
            $"/api/households/{history.OtherHouseholdId}/ledger/search?pageSize=1&cursor={escaped}",
            HttpStatusCode.BadRequest,
            "LEDGER_SEARCH_CURSOR_SCOPE_MISMATCH",
            firstCursor!);

        // The recent-ledger feed keeps its own cursor contract.
        using var recent = await client.GetAsync(
            $"{history.HouseholdRoute}/ledger/transactions?cursor={escaped}");
        Assert.Equal(HttpStatusCode.BadRequest, recent.StatusCode);
        var recentBody = await recent.Content.ReadAsStringAsync();
        Assert.Contains("\"code\":\"NAVIGATION_CURSOR_", recentBody, StringComparison.Ordinal);
        Assert.DoesNotContain(firstCursor!, recentBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_PreservesCurrentDisplayContextAndStableTransactionLinks()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var history = await M010ApiTestData.SeedAsync(factory, client);
        var setup = history.Setup;

        var page = await M010ApiTestData.GetAsync<
            NavigationPageResponse<LedgerSearchTransactionResponse>>(
            client,
            $"{history.HouseholdRoute}/ledger/search?type=BUY");
        var purchase = Assert.Single(page.Items);

        Assert.Equal(setup.HouseholdId, purchase.HouseholdId);
        Assert.Equal("BUY", purchase.TypeCode);
        Assert.Equal("POSTED", purchase.StatusCode);
        Assert.Equal(M010ApiTestData.FundDate, purchase.ExecutionDate);
        Assert.Equal("SYNTH-FUND-001", purchase.ExternalReference);
        Assert.Equal(2, purchase.EntryEffects.Count);

        var fund = Assert.Single(
            purchase.EntryEffects,
            effect => effect.AssetId == setup.FundAssetId);
        Assert.Equal(125_000_000, fund.QuantityDeltaRawE8);
        Assert.Equal("CORE", fund.PortfolioCode);
        Assert.Equal("PRIMARY", fund.AccountCode);
        Assert.Equal("INVESTMENT", fund.AccountTypeCode);
        Assert.Equal(setup.InstitutionId, fund.InstitutionId);
        Assert.Equal("SYNTHETIC_INSTITUTION", fund.InstitutionCode);
        Assert.Equal("BROKER", fund.InstitutionTypeCode);
        Assert.Equal("SYNTHETIC_FUND", fund.AssetCode);
        Assert.Equal("FUND_UNIT", fund.AssetBaseUnitCode);
        Assert.Equal("PRINCIPAL", fund.RoleCode);

        var reversal = Assert.Single(
            (await M010ApiTestData.GetAsync<
                NavigationPageResponse<LedgerSearchTransactionResponse>>(
                client,
                $"{history.HouseholdRoute}/ledger/search?reversal=REVERSAL_ONLY")).Items);
        Assert.Equal(history.DirectFebruaryId, reversal.ReversalOfTransactionId);
        var original = Assert.Single(
            (await M010ApiTestData.GetAsync<
                NavigationPageResponse<LedgerSearchTransactionResponse>>(
                client,
                $"{history.HouseholdRoute}/ledger/search?reversal=REVERSED_ORIGINAL")).Items);
        Assert.Equal(history.ReversalId, original.ReversedByTransactionId);

        // Every result opens the existing transaction readback.
        foreach (var item in (await M010ApiTestData.GetAsync<
                     NavigationPageResponse<LedgerSearchTransactionResponse>>(
                     client,
                     $"{history.HouseholdRoute}/ledger/search")).Items)
        {
            using var detail = await client.GetAsync($"/api/ledger/transactions/{item.TransactionId}");
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        }
    }

    [Fact]
    public async Task Search_RefusesMalformedUnknownAndCrossHouseholdInputWithSanitizedProblems()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var history = await M010ApiTestData.SeedAsync(factory, client);
        var route = $"{history.HouseholdRoute}/ledger/search";
        var longReference = new string('R', 257);

        (string Query, HttpStatusCode Status, string Code)[] cases =
        [
            ("?executedFrom=2026-13-01", HttpStatusCode.BadRequest, "LEDGER_SEARCH_FILTER_INVALID"),
            ("?executedFrom=01.02.2026", HttpStatusCode.BadRequest, "LEDGER_SEARCH_FILTER_INVALID"),
            ("?executedFrom=2026-03-01&executedTo=2026-02-01", HttpStatusCode.BadRequest, "LEDGER_SEARCH_FILTER_INVALID"),
            ("?type=NOPE", HttpStatusCode.BadRequest, "LEDGER_SEARCH_FILTER_INVALID"),
            ("?type=buy", HttpStatusCode.BadRequest, "LEDGER_SEARCH_FILTER_INVALID"),
            ("?status=DRAFT", HttpStatusCode.BadRequest, "LEDGER_SEARCH_FILTER_INVALID"),
            ("?status=CANCELLED", HttpStatusCode.BadRequest, "LEDGER_SEARCH_FILTER_INVALID"),
            ("?assetId=not-a-guid", HttpStatusCode.BadRequest, "LEDGER_SEARCH_FILTER_INVALID"),
            ($"?assetId={Guid.Empty}", HttpStatusCode.BadRequest, "LEDGER_SEARCH_FILTER_INVALID"),
            ($"?assetId={history.Setup.CashAssetId}&assetId={history.Setup.FundAssetId}", HttpStatusCode.BadRequest, "LEDGER_SEARCH_FILTER_INVALID"),
            ("?reversal=SOMETIMES", HttpStatusCode.BadRequest, "LEDGER_SEARCH_FILTER_INVALID"),
            ("?query=SYNTH", HttpStatusCode.BadRequest, "LEDGER_SEARCH_FILTER_INVALID"),
            ($"?externalReference={longReference}", HttpStatusCode.BadRequest, "LEDGER_SEARCH_FILTER_INVALID"),
            ("?pageSize=0", HttpStatusCode.BadRequest, "LEDGER_SEARCH_PAGE_SIZE_INVALID"),
            ("?pageSize=101", HttpStatusCode.BadRequest, "LEDGER_SEARCH_PAGE_SIZE_INVALID"),
            ("?pageSize=-1", HttpStatusCode.BadRequest, "LEDGER_SEARCH_PAGE_SIZE_INVALID"),
            ("?pageSize=ten", HttpStatusCode.BadRequest, "LEDGER_SEARCH_PAGE_SIZE_INVALID"),
            ("?cursor=PRIVATE%2BCURSOR%2FPAYLOAD", HttpStatusCode.BadRequest, "LEDGER_SEARCH_CURSOR_INVALID"),
            ("?cursor=a&cursor=b", HttpStatusCode.BadRequest, "LEDGER_SEARCH_CURSOR_INVALID"),
            ($"?accountId={history.OtherAccountId}", HttpStatusCode.NotFound, "LEDGER_SEARCH_FILTER_SCOPE_NOT_FOUND"),
            ($"?portfolioId={history.OtherPortfolioId}", HttpStatusCode.NotFound, "LEDGER_SEARCH_FILTER_SCOPE_NOT_FOUND"),
            ($"?assetId={Guid.NewGuid()}", HttpStatusCode.NotFound, "LEDGER_SEARCH_FILTER_SCOPE_NOT_FOUND"),
            ($"?institutionId={Guid.NewGuid()}", HttpStatusCode.NotFound, "LEDGER_SEARCH_FILTER_SCOPE_NOT_FOUND")
        ];

        foreach (var (query, status, code) in cases)
        {
            await M010ApiTestData.AssertGetProblemAsync(
                client,
                route + query,
                status,
                code,
                "PRIVATE+CURSOR/PAYLOAD",
                longReference,
                "SYNTH-");
        }

        await M010ApiTestData.AssertGetProblemAsync(
            client,
            $"/api/households/{Guid.NewGuid()}/ledger/search",
            HttpStatusCode.NotFound,
            "LEDGER_SEARCH_HOUSEHOLD_NOT_FOUND");
        await M010ApiTestData.AssertGetProblemAsync(
            client,
            $"/api/households/{Guid.Empty}/ledger/search",
            HttpStatusCode.BadRequest,
            "LEDGER_SEARCH_FILTER_INVALID");
    }

    [Fact]
    public async Task Search_ReadsWithoutWritingAndAddsNoGenericSearchSurface()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var history = await M010ApiTestData.SeedAsync(factory, client);
        var before = await M010ApiTestData.ReadLedgerCountsAsync(factory);

        using (var response = await client.GetAsync(
                   $"{history.HouseholdRoute}/ledger/search?type=CONTRIBUTION&pageSize=2"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using (var recent = await client.GetAsync($"{history.HouseholdRoute}/ledger/transactions"))
        {
            Assert.Equal(HttpStatusCode.OK, recent.StatusCode);
        }

        Assert.Equal(before, await M010ApiTestData.ReadLedgerCountsAsync(factory));

        using var post = await M010ApiTestData.SendJsonAsync(
            client,
            $"{history.HouseholdRoute}/ledger/search",
            new { query = "SYNTH" },
            idempotencyKey: null);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);

        foreach (var route in new[]
                 {
                     "/api/search?q=SYNTH",
                     $"{history.HouseholdRoute}/search?q=SYNTH",
                     "/api/odata/LedgerTransactions",
                     "/api/graphql"
                 })
        {
            using var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
