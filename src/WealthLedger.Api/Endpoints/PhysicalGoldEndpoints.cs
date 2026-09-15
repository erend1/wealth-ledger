using WealthLedger.Api.Contracts;
using WealthLedger.Api.Mapping;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.PhysicalGold;

namespace WealthLedger.Api.Endpoints;

/// <summary>
/// Ready-only JSON adapters for reviewed physical-gold activity.
/// </summary>
internal static class PhysicalGoldEndpoints
{
    private const string IdempotencyKeyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 256;

    internal static IEndpointRouteBuilder MapPhysicalGoldEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/ledger/physical-gold-purchases/preview",
                PreviewPurchaseAsync)
            .WithName("PreviewPhysicalGoldPurchase");

        endpoints.MapPost(
                "/api/ledger/physical-gold-purchases",
                RecordPurchaseAsync)
            .WithName("RecordPhysicalGoldPurchase");

        endpoints.MapPost(
                "/api/ledger/physical-gold-sales/preview",
                PreviewSaleAsync)
            .WithName("PreviewPhysicalGoldSale");

        endpoints.MapPost(
                "/api/ledger/physical-gold-sales",
                RecordSaleAsync)
            .WithName("RecordPhysicalGoldSale");

        endpoints.MapPost(
                "/api/ledger/physical-gold-transfers/preview",
                PreviewTransferAsync)
            .WithName("PreviewPhysicalGoldTransfer");

        endpoints.MapPost(
                "/api/ledger/physical-gold-transfers",
                RecordTransferAsync)
            .WithName("RecordPhysicalGoldTransfer");

        endpoints.MapGet(
                "/api/households/{householdId:guid}/ledger/physical-gold-activities/{transactionId:guid}/verification",
                GetVerificationAsync)
            .WithName("GetPhysicalGoldActivityVerification");

        endpoints.MapGet(
                "/api/households/{householdId:guid}/physical-gold/custody",
                GetCustodyAsync)
            .WithName("GetPhysicalGoldCustody");

        return endpoints;
    }

    private static async Task<IResult> PreviewPurchaseAsync(
        PhysicalGoldPurchaseRequest request,
        PreviewPhysicalGoldPurchaseUseCase useCase,
        CancellationToken cancellationToken)
        => Results.Ok((await useCase.ExecuteAsync(
            request.ToCommand(),
            cancellationToken)).ToResponse());

    private static async Task<IResult> PreviewSaleAsync(
        PhysicalGoldSaleRequest request,
        PreviewPhysicalGoldSaleUseCase useCase,
        CancellationToken cancellationToken)
        => Results.Ok((await useCase.ExecuteAsync(
            request.ToCommand(),
            cancellationToken)).ToResponse());

    private static async Task<IResult> PreviewTransferAsync(
        PhysicalGoldTransferRequest request,
        PreviewPhysicalGoldTransferUseCase useCase,
        CancellationToken cancellationToken)
        => Results.Ok((await useCase.ExecuteAsync(
            request.ToCommand(),
            cancellationToken)).ToResponse());

    private static async Task<IResult> RecordPurchaseAsync(
        HttpRequest httpRequest,
        PhysicalGoldPurchaseRequest request,
        RecordPhysicalGoldPurchaseUseCase useCase,
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
            var result = await useCase.ExecuteAsync(
                idempotencyKey,
                request.ToCommand(),
                cancellationToken);

            return Created(
                request.HouseholdId,
                result.TransactionId,
                result.AssetLotId);
        }
        catch (IdempotencyConflictException exception)
        {
            return IdempotencyConflict(exception);
        }
    }

    private static async Task<IResult> RecordSaleAsync(
        HttpRequest httpRequest,
        PhysicalGoldSaleRequest request,
        RecordPhysicalGoldSaleUseCase useCase,
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
            var result = await useCase.ExecuteAsync(
                idempotencyKey,
                request.ToCommand(),
                cancellationToken);

            return Created(
                request.HouseholdId,
                result.TransactionId,
                assetLotId: null);
        }
        catch (IdempotencyConflictException exception)
        {
            return IdempotencyConflict(exception);
        }
    }

    private static async Task<IResult> RecordTransferAsync(
        HttpRequest httpRequest,
        PhysicalGoldTransferRequest request,
        RecordPhysicalGoldTransferUseCase useCase,
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
            var result = await useCase.ExecuteAsync(
                idempotencyKey,
                request.ToCommand(),
                cancellationToken);

            return Created(
                request.HouseholdId,
                result.TransactionId,
                assetLotId: null);
        }
        catch (IdempotencyConflictException exception)
        {
            return IdempotencyConflict(exception);
        }
    }

    private static async Task<IResult> GetVerificationAsync(
        Guid householdId,
        Guid transactionId,
        GetPhysicalGoldActivityVerificationUseCase useCase,
        CancellationToken cancellationToken)
        => Results.Ok((await useCase.ExecuteAsync(
            householdId,
            transactionId,
            cancellationToken)).ToResponse());

    private static async Task<IResult> GetCustodyAsync(
        Guid householdId,
        GetPhysicalGoldCustodyInventoryUseCase useCase,
        CancellationToken cancellationToken)
        => Results.Ok((await useCase.ExecuteAsync(
            householdId,
            cancellationToken)).ToResponse());

    internal static string BuildVerificationLocation(
        Guid householdId,
        Guid transactionId)
        => $"/api/households/{householdId:D}/ledger/physical-gold-activities/{transactionId:D}/verification";

    private static IResult Created(
        Guid householdId,
        Guid transactionId,
        Guid? assetLotId)
    {
        var transactionLocation =
            $"/api/ledger/transactions/{transactionId:D}";
        var verificationLocation = BuildVerificationLocation(
            householdId,
            transactionId);

        return TypedResults.Created(
            transactionLocation,
            new RecordPhysicalGoldActivityResponse(
                transactionId,
                assetLotId,
                transactionLocation,
                verificationLocation));
    }

    private static IResult IdempotencyConflict(
        IdempotencyConflictException exception)
        => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Idempotency key conflict",
            detail: exception.Message,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = IdempotencyConflictException.ErrorCode
            });

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
            error = InvalidIdempotencyKey(
                "Exactly one Idempotency-Key header is required.");
            return false;
        }

        var candidate = values[0];
        if (string.IsNullOrWhiteSpace(candidate)
            || candidate.Length > MaximumIdempotencyKeyLength
            || candidate.Any(char.IsControl))
        {
            error = InvalidIdempotencyKey(
                "The Idempotency-Key header is not in a supported form.");
            return false;
        }

        idempotencyKey = candidate;
        return true;
    }

    private static IResult InvalidIdempotencyKey(string detail)
        => Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid idempotency key",
            detail: detail);
}
