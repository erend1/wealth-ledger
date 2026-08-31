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

        group.MapGet(
                "/transactions/{transactionId:guid}/reversal-preview",
                GetReversalPreviewAsync)
            .WithName("GetLedgerTransactionReversalPreview");

        group.MapPost(
                "/transactions/{transactionId:guid}/reversals",
                ReverseTransactionAsync)
            .WithName("ReverseLedgerTransaction");

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

            return TypedResults.Created(
                $"/api/ledger/transactions/{result.TransactionId}",
                new RecordContributionResponse(
                    result.TransactionId));
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
    }

    private static async Task<IResult> RecordFundPurchaseAsync(
        HttpRequest httpRequest,
        RecordFundPurchaseRequest request,
        RecordFundPurchaseUseCase useCase,
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
                $"/api/ledger/transactions/{result.TransactionId}",
                new RecordFundPurchaseResponse(
                    result.TransactionId,
                    result.AssetLotId));
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

    private static async Task<IResult> GetReversalPreviewAsync(
        Guid transactionId,
        PreviewPostedTransactionReversalUseCase useCase,
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
                    "The requested ledger transaction does not exist.",

                extensions:
                    new Dictionary<string, object?>
                    {
                        ["code"] =
                            "LEDGER_TRANSACTION_NOT_FOUND"
                    });
        }

        return Results.Ok(
            result.ToResponse());
    }

    private static async Task<IResult> ReverseTransactionAsync(
        HttpRequest httpRequest,
        Guid transactionId,
        ReversePostedTransactionRequest request,
        ReversePostedTransactionUseCase useCase,
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
                    new ReversePostedTransactionCommand(
                        transactionId,
                        request.Reason),
                    cancellationToken);

            if (result is null)
            {
                return Results.Problem(
                    statusCode:
                        StatusCodes.Status404NotFound,

                    title:
                        "Ledger transaction not found",

                    detail:
                        "The requested ledger transaction does not exist.",

                    extensions:
                        new Dictionary<string, object?>
                        {
                            ["code"] =
                                "LEDGER_TRANSACTION_NOT_FOUND"
                        });
            }

            return TypedResults.Created(
                $"/api/ledger/transactions/{result.ReversalTransactionId}",
                new ReversePostedTransactionResponse(
                    result.ReversalTransactionId,
                    result.ReversalOfTransactionId));
        }
        catch (ReversalReasonValidationException exception)
        {
            return Results.Problem(
                statusCode:
                    StatusCodes.Status400BadRequest,

                title:
                    "Invalid reversal reason",

                detail:
                    exception.Message,

                extensions:
                    new Dictionary<string, object?>
                    {
                        ["code"] =
                            exception.ErrorCode
                    });
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
        catch (ReversalCommandRejectedException exception)
        {
            return ToReversalRejectedProblem(
                exception);
        }
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

    private static IResult ToReversalRejectedProblem(
        ReversalCommandRejectedException exception)
    {
        return exception.EligibilityCode switch
        {
            ReversalEligibilityCode.NotPosted =>
                Results.Problem(
                    statusCode:
                        StatusCodes.Status409Conflict,

                    title:
                        "Transaction is not posted",

                    detail:
                        exception.Message,

                    extensions:
                        new Dictionary<string, object?>
                        {
                            ["code"] =
                                "TRANSACTION_NOT_POSTED"
                        }),

            ReversalEligibilityCode.AlreadyReversed =>
                Results.Problem(
                    statusCode:
                        StatusCodes.Status409Conflict,

                    title:
                        "Transaction already reversed",

                    detail:
                        exception.Message,

                    extensions:
                        new Dictionary<string, object?>
                        {
                            ["code"] =
                                "TRANSACTION_ALREADY_REVERSED",

                            ["existingReversalTransactionId"] =
                                exception
                                    .ExistingReversalTransactionId
                        }),

            ReversalEligibilityCode.BlockedByDependencies =>
                Results.Problem(
                    statusCode:
                        StatusCodes.Status409Conflict,

                    title:
                        "Reversal blocked by dependencies",

                    detail:
                        exception.Message,

                    extensions:
                        new Dictionary<string, object?>
                        {
                            ["code"] =
                                "REVERSAL_DEPENDENCY_CONFLICT",

                            ["blockingTransactionIds"] =
                                exception
                                    .BlockingTransactionIds
                        }),

            ReversalEligibilityCode.TargetIsReversal =>
                Results.Problem(
                    statusCode:
                        StatusCodes.Status422UnprocessableEntity,

                    title:
                        "Reversal target is itself a reversal",

                    detail:
                        exception.Message,

                    extensions:
                        new Dictionary<string, object?>
                        {
                            ["code"] =
                                "REVERSAL_TARGET_IS_REVERSAL"
                        }),

            ReversalEligibilityCode.UnsupportedPersistedShape =>
                Results.Problem(
                    statusCode:
                        StatusCodes.Status422UnprocessableEntity,

                    title:
                        "Reversal source is unsupported",

                    detail:
                        exception.Message,

                    extensions:
                        new Dictionary<string, object?>
                        {
                            ["code"] =
                                "REVERSAL_SOURCE_UNSUPPORTED"
                        }),

            ReversalEligibilityCode.Eligible =>
                throw new InvalidOperationException(
                    "An eligible reversal cannot be mapped to an API rejection."),

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(exception),
                    exception.EligibilityCode,
                    "Unsupported reversal eligibility code.")
        };
    }
}
