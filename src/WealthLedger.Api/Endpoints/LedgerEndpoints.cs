using WealthLedger.Api.Contracts;
using WealthLedger.Api.Mapping;
using WealthLedger.Application.CoreLedger;

namespace WealthLedger.Api.Endpoints;

internal static class LedgerEndpoints
{
    private const int MaxIdempotencyKeyLength = 256;
    private const string IdempotencyKeyHeaderName = "Idempotency-Key";

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

        group.MapGet(
                "/transactions/{transactionId:guid}",
                GetTransactionAsync)
            .WithName("GetLedgerTransaction");

        return endpoints;
    }

    private static async Task<IResult> RecordContributionAsync(
        HttpRequest httpRequest,
        RecordContributionRequest request,
        RecordContributionUseCase useCase,
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

            return Results.Created(
                $"/api/ledger/transactions/{result.TransactionId}",
                result);
        }
        catch (IdempotencyConflictException exception)
        {
            return Results.Problem(
                statusCode:
                    StatusCodes.Status409Conflict,

                title:
                    "Idempotency key conflict",

                detail:
                    exception.Message,

                extensions:
                    new Dictionary<string, object?>
                    {
                        ["code"] =
                            IdempotencyConflictException
                                .ErrorCode
                    });
        }

        // Keep your existing validation/domain/persistence
        // exception mappings below this.
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

    private static async Task<IResult> GetTransactionAsync(
        Guid transactionId,
        GetLedgerTransactionUseCase useCase,
        CancellationToken cancellationToken)
    {
        var result =
            await useCase.ExecuteAsync(
                transactionId,
                cancellationToken);

        if (result is null)
        {
            return Results.Problem(
                statusCode:
                    StatusCodes.Status404NotFound,
                title:
                    "Ledger transaction not found",
                detail:
                    "The requested ledger transaction does not exist.");
        }

        return Results.Ok(
            result.ToResponse());
    }

    private static bool TryGetIdempotencyKey(
        HttpRequest request,
        out string idempotencyKey,
        out IResult? error)
    {
        if (!request.Headers.TryGetValue(
                IdempotencyKeyHeaderName,
                out var values)
            || values.Count != 1)
        {
            idempotencyKey = string.Empty;

            error = Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid idempotency key",
                detail:
                    "Exactly one Idempotency-Key header is required.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] =
                        "IDEMPOTENCY_KEY_REQUIRED"
                });

            return false;
        }

        var value = values[0];

        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaxIdempotencyKeyLength
            || value.Any(char.IsControl))
        {
            idempotencyKey = string.Empty;

            error = Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid idempotency key",
                detail:
                    $"Idempotency-Key must contain between 1 and {MaxIdempotencyKeyLength} non-control characters.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] =
                        "IDEMPOTENCY_KEY_INVALID"
                });

            return false;
        }

        idempotencyKey = value;
        error = null;

        return true;
    }
}
