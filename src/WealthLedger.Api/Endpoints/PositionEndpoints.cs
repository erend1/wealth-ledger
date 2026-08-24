using WealthLedger.Api.Contracts;
using WealthLedger.Application.Positions;

namespace WealthLedger.Api.Endpoints;

internal static class PositionEndpoints
{
    internal static IEndpointRouteBuilder MapPositionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/households/{householdId:guid}/portfolios/"
                + "{portfolioId:guid}/accounts/{accountId:guid}/positions/"
                + "{assetId:guid}",
                GetPositionAsync)
            .WithName("GetPosition");

        return endpoints;
    }

    private static async Task<IResult> GetPositionAsync(
        Guid householdId,
        Guid portfolioId,
        Guid accountId,
        Guid assetId,
        GetPositionUseCase useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            new GetPositionQuery(
                householdId,
                portfolioId,
                accountId,
                assetId),
            cancellationToken);

        return TypedResults.Ok(
            new PositionResponse(
                result.HouseholdId,
                result.PortfolioId,
                result.AccountId,
                result.AssetId,
                result.Quantity.RawE8,
                result.SourceEntryCount));
    }
}
