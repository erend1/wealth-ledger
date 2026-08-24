using WealthLedger.Api.Contracts;
using WealthLedger.Api.Mapping;
using WealthLedger.Application.CoreLedger;

namespace WealthLedger.Api.Endpoints;

internal static class LedgerEndpoints
{
    internal static IEndpointRouteBuilder MapLedgerEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/ledger");

        group.MapPost(
                "/contributions",
                RecordContributionAsync)
            .WithName("RecordContribution");

        group.MapPost(
                "/fund-purchases",
                RecordFundPurchaseAsync)
            .WithName("RecordFundPurchase");

        return endpoints;
    }

    private static async Task<IResult> RecordContributionAsync(
        RecordContributionRequest request,
        RecordContributionUseCase useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            request.ToCommand(),
            cancellationToken);

        return TypedResults.Created(
            $"/api/ledger/transactions/{result.TransactionId}",
            new RecordContributionResponse(result.TransactionId));
    }

    private static async Task<IResult> RecordFundPurchaseAsync(
        RecordFundPurchaseRequest request,
        RecordFundPurchaseUseCase useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            request.ToCommand(),
            cancellationToken);

        return TypedResults.Created(
            $"/api/ledger/transactions/{result.TransactionId}",
            new RecordFundPurchaseResponse(
                result.TransactionId,
                result.AssetLotId));
    }
}
