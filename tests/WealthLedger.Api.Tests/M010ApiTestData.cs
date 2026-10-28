using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Api.Contracts;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Api.Tests;

/// <summary>
/// Synthetic M010 history posted through the real host, plus a direct
/// account in the same household and a second household for scope tests.
/// </summary>
internal sealed record M010ApiHistory(
    InitializeCoreLedgerResponse Setup,
    Guid DirectAccountId,
    Guid OtherHouseholdId,
    Guid OtherPortfolioId,
    Guid OtherAccountId,
    Guid FirstJanuaryId,
    Guid SecondJanuaryId,
    Guid DirectFebruaryId,
    Guid FundPurchaseId,
    Guid FundLotId,
    Guid ReversalId,
    Guid OtherHouseholdTransactionId)
{
    internal string HouseholdRoute => $"/api/households/{Setup.HouseholdId}";
}

internal static class M010ApiTestData
{
    internal const long Unit = 100_000_000L;

    internal static readonly Guid DirectAccountId =
        Guid.Parse("51000000-0000-0000-0000-0000000000a1");
    internal static readonly Guid OtherHouseholdId =
        Guid.Parse("10000000-0000-0000-0000-0000000000a2");
    internal static readonly Guid OtherPortfolioId =
        Guid.Parse("41000000-0000-0000-0000-0000000000a2");
    internal static readonly Guid OtherAccountId =
        Guid.Parse("51000000-0000-0000-0000-0000000000a2");

    internal static readonly DateOnly January = new(2026, 1, 10);
    internal static readonly DateOnly February = new(2026, 2, 5);
    internal static readonly DateOnly FundDate = new(2026, 2, 20);

    internal static async Task<M010ApiHistory> SeedAsync(
        WealthLedgerApiFactory factory,
        HttpClient client)
    {
        var setup = factory.ReadySetup;
        await SeedMastersAsync(factory, setup);

        var firstJanuary = await ContributeAsync(
            client,
            setup.HouseholdId,
            setup.PortfolioId,
            setup.AccountId,
            setup.CashAssetId,
            100_000,
            January,
            "SYNTH-JAN-001");
        var secondJanuary = await ContributeAsync(
            client,
            setup.HouseholdId,
            setup.PortfolioId,
            setup.AccountId,
            setup.CashAssetId,
            50_000,
            January,
            "SYNTH-JAN-002");
        var directFebruary = await ContributeAsync(
            client,
            setup.HouseholdId,
            setup.PortfolioId,
            DirectAccountId,
            setup.CashAssetId,
            7_000,
            February,
            externalReference: null);
        var fundPurchase = await PostAsync<RecordFundPurchaseResponse>(
            client,
            "/api/ledger/fund-purchases",
            new RecordFundPurchaseRequest(
                setup.HouseholdId,
                setup.PortfolioId,
                setup.AccountId,
                setup.FundAssetId,
                setup.CashAssetId,
                FundQuantityRawE8: 125_000_000,
                ExecutedUnitPriceRawE8: 20_000_000_000,
                PriceCurrencyCode: "TRY",
                CashConsiderationMinorUnits: 25_000,
                CashConsiderationCurrencyCode: "TRY",
                FundDate,
                ExternalReference: "SYNTH-FUND-001"));
        var reversal = await PostAsync<ReversePostedTransactionResponse>(
            client,
            $"/api/ledger/transactions/{directFebruary}/reversals",
            new ReversePostedTransactionRequest("Synthetic reversal."));
        var otherHousehold = await ContributeAsync(
            client,
            OtherHouseholdId,
            OtherPortfolioId,
            OtherAccountId,
            setup.CashAssetId,
            9_000,
            January,
            "SYNTH-OTHER-001");

        return new M010ApiHistory(
            setup,
            DirectAccountId,
            OtherHouseholdId,
            OtherPortfolioId,
            OtherAccountId,
            firstJanuary,
            secondJanuary,
            directFebruary,
            fundPurchase.TransactionId,
            fundPurchase.AssetLotId,
            reversal.ReversalTransactionId,
            otherHousehold);
    }

    internal static async Task<Guid> ContributeAsync(
        HttpClient client,
        Guid householdId,
        Guid portfolioId,
        Guid accountId,
        Guid cashAssetId,
        long amountMinorUnits,
        DateOnly executionDate,
        string? externalReference)
        => (await PostAsync<RecordContributionResponse>(
            client,
            "/api/ledger/contributions",
            new RecordContributionRequest(
                householdId,
                portfolioId,
                accountId,
                cashAssetId,
                amountMinorUnits,
                "TRY",
                "OTHER",
                executionDate,
                ExternalReference: externalReference))).TransactionId;

    internal static async Task<T> PostAsync<T>(
        HttpClient client,
        string route,
        object body,
        string? idempotencyKey = null)
    {
        using var response = await SendJsonAsync(
            client,
            route,
            body,
            idempotencyKey ?? $"m010-{Guid.NewGuid():N}");
        Assert.True(
            response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
            $"Expected success but got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return Assert.IsType<T>(await response.Content.ReadFromJsonAsync<T>());
    }

    internal static async Task<HttpResponseMessage> SendJsonAsync(
        HttpClient client,
        string route,
        object body,
        string? idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(body)
        };

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request);
    }

    internal static async Task<T> GetAsync<T>(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected 200 for {route} but got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return Assert.IsType<T>(await response.Content.ReadFromJsonAsync<T>());
    }

    /// <summary>
    /// Asserts a sanitized problem: the status, the stable code, and none of
    /// the given private markers or storage internals in the body.
    /// </summary>
    internal static async Task<string> AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode,
        params string[] privateMarkers)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == expectedStatus,
            $"Expected {(int)expectedStatus} {expectedCode} but got {(int)response.StatusCode}: {body}");
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(body);
        Assert.Equal(
            expectedCode,
            document.RootElement.GetProperty("code").GetString());

        foreach (var marker in privateMarkers.Append("SELECT").Append("SqliteException")
                     .Append("EntityFramework").Append("StackTrace").Append(" at WealthLedger"))
        {
            Assert.DoesNotContain(marker, body, StringComparison.OrdinalIgnoreCase);
        }

        return body;
    }

    internal static async Task<string> AssertGetProblemAsync(
        HttpClient client,
        string route,
        HttpStatusCode expectedStatus,
        string expectedCode,
        params string[] privateMarkers)
    {
        using var response = await client.GetAsync(route);
        return await AssertProblemAsync(response, expectedStatus, expectedCode, privateMarkers);
    }

    internal static WealthLedgerDbContext CreateContext(WealthLedgerApiFactory factory)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = factory.DatabasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();

        return new WealthLedgerDbContext(
            new DbContextOptionsBuilder<WealthLedgerDbContext>()
                .UseSqlite(connectionString)
                .Options);
    }

    /// <summary>
    /// Row counts of every ledger table, to prove reads and audit writes
    /// leave the ledger untouched.
    /// </summary>
    internal static async Task<int[]> ReadLedgerCountsAsync(WealthLedgerApiFactory factory)
    {
        await using var context = CreateContext(factory);

        return
        [
            await context.LedgerTransactions.CountAsync(),
            await context.TransactionEntries.CountAsync(),
            await context.CashFlowDetails.CountAsync(),
            await context.TransactionCostComponents.CountAsync(),
            await context.AssetLots.CountAsync(),
            await context.LotEntryAllocations.CountAsync(),
            await context.PhysicalGoldLotDetails.CountAsync(),
            await context.CommandReceipts.CountAsync()
        ];
    }

    private static async Task SeedMastersAsync(
        WealthLedgerApiFactory factory,
        InitializeCoreLedgerResponse setup)
    {
        await using var context = CreateContext(factory);
        var createdAtUtc = DateTime.UtcNow;

        context.Households.Add(new HouseholdRow
        {
            Id = OtherHouseholdId,
            Name = "Other Synthetic Household",
            BaseCurrencyCode = "TRY",
            CreatedAtUtc = createdAtUtc
        });
        context.Portfolios.Add(new PortfolioRow
        {
            Id = OtherPortfolioId,
            HouseholdId = OtherHouseholdId,
            Code = "OTHER_CORE",
            Name = "Other Synthetic Portfolio",
            Status = PortfolioStatus.Active,
            CreatedAtUtc = createdAtUtc
        });
        context.Accounts.AddRange(
            new AccountRow
            {
                Id = DirectAccountId,
                HouseholdId = setup.HouseholdId,
                InstitutionId = null,
                Code = "DIRECT_CASH",
                Name = "Synthetic Direct Account",
                Type = AccountType.Cash,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            },
            new AccountRow
            {
                Id = OtherAccountId,
                HouseholdId = OtherHouseholdId,
                InstitutionId = null,
                Code = "OTHER_CASH",
                Name = "Other Synthetic Account",
                Type = AccountType.Cash,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            });
        await context.SaveChangesAsync();
    }
}
