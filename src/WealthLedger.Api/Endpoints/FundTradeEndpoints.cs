using WealthLedger.Api.Contracts;
using WealthLedger.Api.Mapping;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.FundTrades;

namespace WealthLedger.Api.Endpoints;

/// <summary>
/// Ready-only JSON adapters for the fund-trade workflows.
/// </summary>
/// <remarks>
/// Preview routes write nothing and need no idempotency key. Posting routes
/// require exactly one key, because that is what makes a retry safe.
/// </remarks>
internal static class FundTradeEndpoints
{
    private const string IdempotencyKeyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 256;

    internal static IEndpointRouteBuilder MapFundTradeEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/ledger/fund-purchases/preview",
                PreviewPurchaseAsync)
            .WithName("PreviewFundPurchase");

        endpoints.MapPost(
                "/api/ledger/fund-sales/preview",
                PreviewSaleAsync)
            .WithName("PreviewFundSale");

        endpoints.MapPost(
                "/api/ledger/fund-sales",
                RecordSaleAsync)
            .WithName("RecordFundSale");

        endpoints.MapGet(
                "/api/households/{householdId:guid}/ledger/fund-trades/{transactionId:guid}/verification",
                GetVerificationAsync)
            .WithName("GetFundTradeVerification");

        return endpoints;
    }

    private static async Task<IResult> PreviewPurchaseAsync(
        FundPurchaseRequest request,
        PreviewFundPurchaseUseCase useCase,
        CancellationToken cancellationToken)
    {
        var preview =
            await useCase.ExecuteAsync(
                request.ToCommand(),
                cancellationToken);

        return Results.Ok(preview.ToResponse());
    }

    private static async Task<IResult> PreviewSaleAsync(
        FundSaleRequest request,
        PreviewFundSaleUseCase useCase,
        CancellationToken cancellationToken)
    {
        var preview =
            await useCase.ExecuteAsync(
                request.ToCommand(),
                cancellationToken);

        return Results.Ok(preview.ToResponse());
    }

    private static async Task<IResult> RecordSaleAsync(
        HttpRequest httpRequest,
        FundSaleRequest request,
        RecordFundSaleUseCase useCase,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdempotencyKey(
                httpRequest,
                out var idempotencyKey,
                out var error))
        {
            return error!;
        }

        try
        {
            var result =
                await useCase.ExecuteAsync(
                    idempotencyKey,
                    request.ToCommand(),
                    cancellationToken);

            return TypedResults.Created(
                $"/api/ledger/transactions/{result.TransactionId:D}",
                new RecordFundSaleResponse(
                    result.TransactionId,
                    BuildVerificationLocation(
                        request.HouseholdId,
                        result.TransactionId)));
        }
        catch (IdempotencyConflictException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Idempotency key conflict",
                detail: exception.Message,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = IdempotencyConflictException.ErrorCode
                });
        }
    }

    private static async Task<IResult> GetVerificationAsync(
        Guid householdId,
        Guid transactionId,
        GetFundTradeVerificationUseCase useCase,
        CancellationToken cancellationToken)
    {
        var verification =
            await useCase.ExecuteAsync(
                householdId,
                transactionId,
                cancellationToken);

        return Results.Ok(verification.ToResponse());
    }

    internal static string BuildVerificationLocation(
        Guid householdId,
        Guid transactionId)
        => $"/api/households/{householdId:D}/ledger/fund-trades/{transactionId:D}/verification";

    private static bool TryGetIdempotencyKey(
        HttpRequest httpRequest,
        out string idempotencyKey,
        out IResult? error)
    {
        idempotencyKey = string.Empty;
        error = null;

        if (!httpRequest.Headers.TryGetValue(
                IdempotencyKeyHeaderName,
                out var values)
            || values.Count != 1)
        {
            error = Problem(
                "Exactly one Idempotency-Key header is required.");

            return false;
        }

        var candidate = values[0];

        if (string.IsNullOrWhiteSpace(candidate)
            || candidate.Length > MaximumIdempotencyKeyLength
            || candidate.Any(char.IsControl))
        {
            error = Problem(
                "The Idempotency-Key header is not in a supported form.");

            return false;
        }

        idempotencyKey = candidate;

        return true;
    }

    private static IResult Problem(string detail)
        => Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid idempotency key",
            detail: detail);
}
