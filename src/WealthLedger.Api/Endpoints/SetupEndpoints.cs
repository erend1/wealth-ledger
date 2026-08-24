using WealthLedger.Api.Contracts;
using WealthLedger.Api.Mapping;
using WealthLedger.Application.Setup;

namespace WealthLedger.Api.Endpoints;

internal static class SetupEndpoints
{
    internal static IEndpointRouteBuilder MapSetupEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/setup/core-ledger",
                InitializeCoreLedgerAsync)
            .WithName("InitializeCoreLedger");

        return endpoints;
    }

    private static async Task<IResult> InitializeCoreLedgerAsync(
        InitializeCoreLedgerRequest request,
        InitializeCoreLedgerUseCase useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            request.ToCommand(),
            cancellationToken);

        return TypedResults.Created(
            $"/api/households/{result.HouseholdId}",
            new InitializeCoreLedgerResponse(
                result.HouseholdId,
                result.HouseholdMemberId,
                result.InstitutionId,
                result.PortfolioId,
                result.AccountId,
                result.CashAssetId,
                result.FundAssetId));
    }
}
