using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Json;
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

        client.DefaultRequestHeaders.Add(
            "Idempotency-Key",
            $"test-{Guid.NewGuid():N}");

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

        Assert.NotNull(
    contributionResponse.Headers.Location);

        var contributionReadResponse =
            await client.GetAsync(
                contributionResponse.Headers.Location);

        Assert.Equal(
            HttpStatusCode.OK,
            contributionReadResponse.StatusCode);

        var contributionRead =
            await contributionReadResponse.Content
                .ReadFromJsonAsync<LedgerTransactionResponse>();

        Assert.NotNull(contributionRead);

        Assert.Equal(
            contribution.TransactionId,
            contributionRead.TransactionId);

        Assert.Equal(
            setup.HouseholdId,
            contributionRead.HouseholdId);

        Assert.Equal(
            "CONTRIBUTION",
            contributionRead.TypeCode);

        Assert.Equal(
            "POSTED",
            contributionRead.StatusCode);

        Assert.Equal(
            ApiTestData.ExecutionDate,
            contributionRead.ExecutionDate);

        Assert.Equal(
            "CONTRIBUTION-TEST",
            contributionRead.ExternalReference);

        Assert.Equal(
            "Synthetic API test contribution",
            contributionRead.Note);

        var contributionEntry =
            Assert.Single(
                contributionRead.Entries);

        Assert.Equal(
            setup.PortfolioId,
            contributionEntry.PortfolioId);

        Assert.Equal(
            setup.AccountId,
            contributionEntry.AccountId);

        Assert.Equal(
            setup.CashAssetId,
            contributionEntry.AssetId);

        Assert.Equal(
            "PRINCIPAL",
            contributionEntry.RoleCode);

        Assert.Equal(
            100_000_000_000,
            contributionEntry.QuantityDeltaRawE8);

        Assert.Null(
            contributionEntry.UnitPriceRawE8);

        Assert.Null(
            contributionEntry.PriceCurrencyCode);

        Assert.NotNull(
            contributionRead.CashFlow);

        Assert.Equal(
            "ACADEMIC_INCOME",
            contributionRead.CashFlow.CategoryCode);

        Assert.Equal(
            setup.HouseholdMemberId,
            contributionRead.CashFlow.HouseholdMemberId);

        Assert.Empty(
            contributionRead.Costs);

        Assert.Empty(
            contributionRead.CreatedLots);


        var purchaseResponse = await client.PostAsJsonAsync(
            "/api/ledger/fund-purchases",
            CreateFundPurchaseRequest(setup));

        Assert.Equal(HttpStatusCode.Created, purchaseResponse.StatusCode);

        var purchase = await purchaseResponse.Content
            .ReadFromJsonAsync<RecordFundPurchaseResponse>();

        Assert.NotNull(purchase);
        Assert.NotEqual(Guid.Empty, purchase.TransactionId);
        Assert.NotEqual(Guid.Empty, purchase.AssetLotId);

        Assert.NotNull(purchaseResponse.Headers.Location);

        Assert.Equal(
            $"/api/ledger/transactions/{purchase.TransactionId}",
            purchaseResponse.Headers.Location.OriginalString);

        var purchaseReadResponse =
            await client.GetAsync(
                purchaseResponse.Headers.Location);

        Assert.Equal(
            HttpStatusCode.OK,
            purchaseReadResponse.StatusCode);

        var purchaseRead =
            await purchaseReadResponse.Content
                .ReadFromJsonAsync<LedgerTransactionResponse>();

        Assert.NotNull(purchaseRead);

        Assert.Equal(
            purchase.TransactionId,
            purchaseRead.TransactionId);

        Assert.Equal(
            setup.HouseholdId,
            purchaseRead.HouseholdId);

        Assert.Equal(
            "BUY",
            purchaseRead.TypeCode);

        Assert.Equal(
            "POSTED",
            purchaseRead.StatusCode);

        Assert.Equal(
            ApiTestData.ExecutionDate,
            purchaseRead.ExecutionDate);

        Assert.Equal(
            "PURCHASE-TEST",
            purchaseRead.ExternalReference);

        Assert.Equal(
            "Synthetic API test purchase",
            purchaseRead.Note);

        Assert.Equal(
            2,
            purchaseRead.Entries.Count);

        var purchasePrincipal =
            Assert.Single(
                purchaseRead.Entries,
                    entry =>
                        entry.RoleCode
                            == "PRINCIPAL");

        Assert.Equal(
            setup.FundAssetId,
            purchasePrincipal.AssetId);

        Assert.Equal(
            125_000_000,
            purchasePrincipal.QuantityDeltaRawE8);

        Assert.Equal(
            20_000_000_000,
            purchasePrincipal.UnitPriceRawE8);

        Assert.Equal(
            "TRY",
            purchasePrincipal.PriceCurrencyCode);

        var purchaseConsideration =
            Assert.Single(
                purchaseRead.Entries,
                    entry =>
                        entry.RoleCode
                            == "CONSIDERATION");

        Assert.Equal(
            setup.CashAssetId,
            purchaseConsideration.AssetId);

        Assert.Equal(
            -25_000_000_000,
            purchaseConsideration.QuantityDeltaRawE8);

        Assert.Null(
            purchaseRead.CashFlow);

        Assert.Empty(
            purchaseRead.Costs);

        var createdLot =
            Assert.Single(
                purchaseRead.CreatedLots);

        Assert.Equal(
            purchase.AssetLotId,
            createdLot.AssetLotId);

        Assert.Equal(
            setup.FundAssetId,
            createdLot.AssetId);

        Assert.Equal(
            purchasePrincipal.EntryId,
            createdLot.OpeningTransactionEntryId);

        Assert.Equal(
            25_000,
            createdLot.OriginalCostBasisMinorUnits);

        Assert.Equal(
            "TRY",
            createdLot.CostBasisCurrencyCode);

        Assert.Equal(
            "KNOWN",
            createdLot.CostBasisStatusCode);

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

        client.DefaultRequestHeaders.Add(
            "Idempotency-Key",
            $"test-{Guid.NewGuid():N}");

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


        client.DefaultRequestHeaders.Add(
            "Idempotency-Key",
            $"test-{Guid.NewGuid():N}");

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
                services.RemoveAll<ILedgerSubmissionStore>();

                services.AddScoped<FailingPostingStore>();

                services.AddScoped<ILedgerPostingStore>(
                    serviceProvider =>
                        serviceProvider.GetRequiredService<
                            FailingPostingStore>());

                services.AddScoped<ILedgerSubmissionStore>(
                    serviceProvider =>
                        serviceProvider.GetRequiredService<
                            FailingPostingStore>());
            }));

        using var client = failingFactory.CreateClient();

        client.DefaultRequestHeaders.Add(
            "Idempotency-Key",
            $"test-{Guid.NewGuid():N}");

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

    [Fact]
    public async Task Contribution_WithoutIdempotencyKey_ReturnsBadRequest()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var setup = await InitializeCoreLedgerAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/ledger/contributions",
            CreateContributionRequest(setup));

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        var problem =
            await response.Content
                .ReadFromJsonAsync<ProblemDetails>();

        Assert.NotNull(problem);

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            problem.Status);
    }

    [Fact]
    public async Task Contribution_WithOverlongIdempotencyKey_ReturnsBadRequest()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var setup = await InitializeCoreLedgerAsync(client);

        client.DefaultRequestHeaders.Add(
            "Idempotency-Key",
            new string('x', 257));

        var response = await client.PostAsJsonAsync(
            "/api/ledger/contributions",
            CreateContributionRequest(setup));

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task Contribution_WithNewIdempotencyKey_ReturnsCreated()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var setup = await InitializeCoreLedgerAsync(client);
        var request = CreateContributionRequest(setup);

        client.DefaultRequestHeaders.Add(
            "Idempotency-Key",
            "api-contribution-001");

        var response = await client.PostAsJsonAsync(
            "/api/ledger/contributions",
            CreateContributionRequest(setup));

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);
    }

    [Fact]
    public async Task Contribution_EquivalentReplay_ReturnsSameTransaction()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var setup = await InitializeCoreLedgerAsync(client);

        const string key =
            "api-contribution-replay-001";

        client.DefaultRequestHeaders.Add(
            "Idempotency-Key",
            key);

        var firstResponse = await client.PostAsJsonAsync(
            "/api/ledger/contributions",
            CreateContributionRequest(setup));

        Assert.Equal(
            HttpStatusCode.Created,
            firstResponse.StatusCode);

        var firstBody =
            await firstResponse.Content
                .ReadFromJsonAsync<RecordContributionResponse>();

        Assert.NotNull(firstBody);

        client.DefaultRequestHeaders.Remove(
            "Idempotency-Key");
        client.DefaultRequestHeaders.Add(
            "Idempotency-Key",
            key);

        var secondResponse = await client.PostAsJsonAsync(
            "/api/ledger/contributions",
            CreateContributionRequest(setup));

        Assert.Equal(
            HttpStatusCode.Created,
            secondResponse.StatusCode);

        var secondBody =
            await secondResponse.Content
                .ReadFromJsonAsync<RecordContributionResponse>();

        Assert.NotNull(secondBody);

        Assert.Equal(
            firstBody.TransactionId,
            secondBody.TransactionId);

        Assert.Equal(
            firstResponse.Headers.Location,
            secondResponse.Headers.Location);
    }

    [Fact]
    public async Task Contribution_ReusedKeyForDifferentCommand_ReturnsConflict()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var setup = await InitializeCoreLedgerAsync(client);

        const string key =
            "api-contribution-conflict-001";

        client.DefaultRequestHeaders.Add(
            "Idempotency-Key",
            key);

        var firstResponse = await client.PostAsJsonAsync(
            "/api/ledger/contributions",
            CreateContributionRequest(setup, 30_000_00));

        Assert.Equal(
            HttpStatusCode.Created,
            firstResponse.StatusCode);

        var changedResponse = await client.PostAsJsonAsync(
            "/api/ledger/contributions",
            CreateContributionRequest(setup, 31_000_00));

        Assert.Equal(
            HttpStatusCode.Conflict,
            changedResponse.StatusCode);
    }

    [Fact]
    public async Task Contribution_SameCommandWithDifferentKey_CreatesDifferentTransaction()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var setup = await InitializeCoreLedgerAsync(client);

        client.DefaultRequestHeaders.Add(
            "Idempotency-Key",
            "api-new-command-001");

        var firstResponse = await client.PostAsJsonAsync(
            "/api/ledger/contributions",
            CreateContributionRequest(setup));

        var firstBody =
            await firstResponse.Content
                .ReadFromJsonAsync<RecordContributionResponse>();

        client.DefaultRequestHeaders.Remove(
            "Idempotency-Key");
        client.DefaultRequestHeaders.Add(
            "Idempotency-Key",
            "api-new-command-002");

        var secondResponse = await client.PostAsJsonAsync(
            "/api/ledger/contributions",
            CreateContributionRequest(setup));

        var secondBody =
            await secondResponse.Content
                .ReadFromJsonAsync<RecordContributionResponse>();

        Assert.NotNull(firstBody);
        Assert.NotNull(secondBody);

        Assert.NotEqual(
            firstBody.TransactionId,
            secondBody.TransactionId);
    }

    [Fact]
    public async Task Transaction_UnknownId_ReturnsNotFoundProblem()
    {
        using var factory =
            new WealthLedgerApiFactory();

        using var client =
            factory.CreateClient();

        var response =
            await client.GetAsync(
                $"/api/ledger/transactions/{Guid.NewGuid()}");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);

        var problem =
            await response.Content
                .ReadFromJsonAsync<ProblemDetails>();

        Assert.NotNull(problem);

        Assert.Equal(
            "Ledger transaction not found",
            problem.Title);
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
        InitializeCoreLedgerResponse setup, long amountMinorUnits = 100_000)
        => new(
            setup.HouseholdId,
            setup.PortfolioId,
            setup.AccountId,
            setup.CashAssetId,
            AmountMinorUnits: amountMinorUnits,
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

    private sealed class FailingPostingStore : ILedgerPostingStore, ILedgerSubmissionStore
    {
        public Task SavePostedTransactionAsync(
            LedgerTransaction transaction,
            IReadOnlyCollection<AssetLot> newLots,
            CancellationToken cancellationToken = default)
            => Task.FromException(
                new CoreLedgerPersistenceException(
                    "SQLite TransactionEntry constraint details must stay private."));

        public Task<LedgerSubmissionReceipt?> FindReceiptAsync(
            LedgerSubmissionScope scope,
            CancellationToken cancellationToken = default)
            => Task.FromResult<LedgerSubmissionReceipt?>(null);

        public Task<LedgerSubmissionCommitResult> TryCommitAsync(
            LedgerSubmissionReceipt receipt,
            LedgerTransaction transaction,
            IReadOnlyCollection<AssetLot> newLots,
            CancellationToken cancellationToken = default)
            => Task.FromException<LedgerSubmissionCommitResult>(
                new CoreLedgerPersistenceException(
                    "SQLite TransactionEntry constraint details must stay private."));
    }
}
