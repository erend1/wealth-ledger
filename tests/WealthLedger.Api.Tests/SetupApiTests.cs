using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using WealthLedger.Api.Contracts;

namespace WealthLedger.Api.Tests;

public sealed class SetupApiTests
{
    [Fact]
    public async Task Setup_AppliesOptInMigrationAndRejectsRepeatInitialization()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();

        var firstResponse = await client.PostAsJsonAsync(
            "/api/setup/core-ledger",
            ApiTestData.CreateSetupRequest());

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var setup = await firstResponse.Content
            .ReadFromJsonAsync<InitializeCoreLedgerResponse>();

        Assert.NotNull(setup);
        Assert.NotEqual(Guid.Empty, setup.HouseholdId);
        Assert.NotEqual(Guid.Empty, setup.InstitutionId);
        Assert.NotEqual(Guid.Empty, setup.PortfolioId);
        Assert.NotEqual(Guid.Empty, setup.AccountId);
        Assert.NotEqual(Guid.Empty, setup.CashAssetId);
        Assert.NotEqual(Guid.Empty, setup.FundAssetId);

        var secondResponse = await client.PostAsJsonAsync(
            "/api/setup/core-ledger",
            ApiTestData.CreateSetupRequest());

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        var problem = await secondResponse.Content
            .ReadFromJsonAsync<ProblemDetails>();

        Assert.NotNull(problem);
        Assert.Equal("Core ledger already initialized", problem.Title);
        Assert.Equal(
            "Core ledger setup has already been completed.",
            problem.Detail);
    }

    [Fact]
    public async Task Setup_WhenDisabled_IsNotMapped()
    {
        using var factory = new WealthLedgerApiFactory(
            setupEnabled: false,
            applyMigrationsOnStartup: false);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/setup/core-ledger",
            ApiTestData.CreateSetupRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Setup_WithInvalidStableCode_ReturnsBadRequestProblem()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var request = ApiTestData.CreateSetupRequest() with
        {
            Institution = new InitializeInstitutionRequest(
                "SYNTHETIC_INSTITUTION",
                "Synthetic Institution",
                TypeCode: "NOT_AN_INSTITUTION_TYPE")
        };

        var response = await client.PostAsJsonAsync(
            "/api/setup/core-ledger",
            request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content
            .ReadFromJsonAsync<ProblemDetails>();

        Assert.NotNull(problem);
        Assert.Equal("Invalid request", problem.Title);
        Assert.Contains("Institution type code", problem.Detail);
    }
}
