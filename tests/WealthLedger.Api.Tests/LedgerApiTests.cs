using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WealthLedger.Api.Contracts;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Infrastructure.Persistence;

namespace WealthLedger.Api.Tests;

public sealed class LedgerApiTests
{
    [Fact]
    public async Task ContributionPurchaseAndPositions_RoundTripExactValues()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var setup = await InitializeCoreLedgerAsync(client);

        var contributionResponse = await client.PostAsJsonAsync(
            "/api/ledger/contributions",
            CreateContributionRequest(setup));

        Assert.Equal(HttpStatusCode.Created, contributionResponse.StatusCode);

        var contribution = await contributionResponse.Content
            .ReadFromJsonAsync<RecordContributionResponse>();

        Assert.NotNull(contribution);
        Assert.NotEqual(Guid.Empty, contribution.TransactionId);
        Assert.Equal(
            $"/api/ledger/transactions/{contribution.TransactionId}",
            contributionResponse.Headers.Location?.OriginalString);

        var purchaseResponse = await client.PostAsJsonAsync(
            "/api/ledger/fund-purchases",
            CreateFundPurchaseRequest(setup));

        Assert.Equal(HttpStatusCode.Created, purchaseResponse.StatusCode);

        var purchase = await purchaseResponse.Content
            .ReadFromJsonAsync<RecordFundPurchaseResponse>();

        Assert.NotNull(purchase);
        Assert.NotEqual(Guid.Empty, purchase.TransactionId);
        Assert.NotEqual(Guid.Empty, purchase.AssetLotId);

        var fundPosition = await GetPositionAsync(
            client,
            setup,
            setup.FundAssetId);

        var cashPosition = await GetPositionAsync(
            client,
            setup,
            setup.CashAssetId);

        Assert.Equal(125_000_000, fundPosition.QuantityRawE8);
        Assert.Equal(1, fundPosition.SourceEntryCount);
        Assert.Equal(75_000_000_000, cashPosition.QuantityRawE8);
        Assert.Equal(2, cashPosition.SourceEntryCount);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider
            .GetRequiredService<WealthLedgerDbContext>();

        var cashFlow = await context.CashFlowDetails
            .AsNoTracking()
            .SingleAsync();

        var principalEntry = await context.TransactionEntries
            .AsNoTracking()
            .SingleAsync(x =>
                x.TransactionId == purchase.TransactionId
                && x.Role == EntryRole.Principal);

        var assetLot = await context.AssetLots
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(CashFlowCategory.AcademicIncome, cashFlow.Category);
        Assert.Equal(20_000_000_000, principalEntry.UnitPriceE8);
        Assert.Equal("TRY", principalEntry.PriceCurrencyCode);
        Assert.Equal(CostBasisStatus.Known, assetLot.CostBasisStatus);
        Assert.Equal(25_000, assetLot.OriginalCostBasisMinor);
        Assert.Equal("TRY", assetLot.CostBasisCurrencyCode);
    }

    [Fact]
    public async Task InvalidTransportCode_ReturnsBadRequestProblem()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var setup = await InitializeCoreLedgerAsync(client);

        var request = CreateContributionRequest(setup) with
        {
            CashFlowCategoryCode = "NOT_A_CATEGORY"
        };

        var response = await client.PostAsJsonAsync(
            "/api/ledger/contributions",
            request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content
            .ReadFromJsonAsync<ProblemDetails>();

        Assert.NotNull(problem);
        Assert.Equal("Invalid request", problem.Title);
        Assert.Contains("Cash flow category code", problem.Detail);
    }

    [Fact]
    public async Task ApplicationRuleViolation_ReturnsUnprocessableEntityProblem()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var setup = await InitializeCoreLedgerAsync(client);

        var request = CreateContributionRequest(setup) with
        {
            CashAssetId = setup.FundAssetId
        };

        var response = await client.PostAsJsonAsync(
            "/api/ledger/contributions",
            request);

        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            response.StatusCode);

        var problem = await response.Content
            .ReadFromJsonAsync<ProblemDetails>();

        Assert.NotNull(problem);
        Assert.Equal("Ledger rule violation", problem.Title);
        Assert.Contains("cash or currency asset", problem.Detail);
    }

    [Fact]
    public async Task PersistenceFailure_ReturnsSanitizedConflictProblem()
    {
        using var factory = new WealthLedgerApiFactory();
        using var setupClient = factory.CreateClient();
        var setup = await InitializeCoreLedgerAsync(setupClient);

        using var failingFactory = factory.WithWebHostBuilder(
            builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILedgerPostingStore>();
                services.AddScoped<ILedgerPostingStore>(
                    _ => new FailingPostingStore());
            }));

        using var client = failingFactory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/ledger/contributions",
            CreateContributionRequest(setup));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content
            .ReadFromJsonAsync<ProblemDetails>();

        Assert.NotNull(problem);
        Assert.Equal("Ledger persistence conflict", problem.Title);
        Assert.Equal(
            "The ledger write conflicted with persisted history.",
            problem.Detail);
        Assert.DoesNotContain("SQLite", problem.Detail);
        Assert.DoesNotContain("TransactionEntry", problem.Detail);
    }

    private static async Task<InitializeCoreLedgerResponse>
        InitializeCoreLedgerAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/setup/core-ledger",
            ApiTestData.CreateSetupRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var setup = await response.Content
            .ReadFromJsonAsync<InitializeCoreLedgerResponse>();

        return Assert.IsType<InitializeCoreLedgerResponse>(setup);
    }

    private static RecordContributionRequest CreateContributionRequest(
        InitializeCoreLedgerResponse setup)
        => new(
            setup.HouseholdId,
            setup.PortfolioId,
            setup.AccountId,
            setup.CashAssetId,
            AmountMinorUnits: 100_000,
            CurrencyCode: "TRY",
            CashFlowCategoryCode: "ACADEMIC_INCOME",
            ApiTestData.ExecutionDate,
            setup.HouseholdMemberId,
            ExternalReference: "CONTRIBUTION-TEST",
            Note: "Synthetic API test contribution");

    private static RecordFundPurchaseRequest CreateFundPurchaseRequest(
        InitializeCoreLedgerResponse setup)
        => new(
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
            ApiTestData.ExecutionDate,
            ExternalReference: "PURCHASE-TEST",
            Note: "Synthetic API test purchase");

    private static async Task<PositionResponse> GetPositionAsync(
        HttpClient client,
        InitializeCoreLedgerResponse setup,
        Guid assetId)
    {
        var response = await client.GetAsync(
            $"/api/households/{setup.HouseholdId}/portfolios/"
            + $"{setup.PortfolioId}/accounts/"
            + $"{setup.AccountId}/positions/{assetId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var position = await response.Content
            .ReadFromJsonAsync<PositionResponse>();

        return Assert.IsType<PositionResponse>(position);
    }

    private sealed class FailingPostingStore : ILedgerPostingStore
    {
        public Task SavePostedTransactionAsync(
            LedgerTransaction transaction,
            IReadOnlyCollection<AssetLot> newLots,
            CancellationToken cancellationToken = default)
            => Task.FromException(
                new CoreLedgerPersistenceException(
                    "SQLite TransactionEntry constraint details must stay private."));
    }
}
