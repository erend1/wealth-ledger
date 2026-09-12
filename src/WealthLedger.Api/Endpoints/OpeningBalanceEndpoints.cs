using WealthLedger.Api.Contracts;
using WealthLedger.Api.Mapping;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Api.Endpoints;

internal static class OpeningBalanceEndpoints
{
    private const string IdempotencyKeyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 256;

    internal static IEndpointRouteBuilder MapOpeningBalanceEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/ledger/opening-balances/preview",
                PreviewAsync)
            .WithName("PreviewOpeningBalance");

        endpoints.MapPost(
                "/api/ledger/opening-balances",
                RecordAsync)
            .WithName("RecordOpeningBalance");

        endpoints.MapGet(
                "/api/households/{householdId:guid}/ledger/opening-balances/{transactionId:guid}/verification",
                GetVerificationAsync)
            .WithName("GetOpeningBalanceVerification");

        endpoints.MapPut(
                "/api/opening-balance-reference-data/currencies/{currencyCode}",
                CreateCurrencyAsync)
            .WithName("CreateOpeningBalanceCurrency");

        endpoints.MapPut(
                "/api/opening-balance-reference-data/institutions/{institutionCode}",
                CreateInstitutionAsync)
            .WithName("CreateOpeningBalanceInstitution");

        endpoints.MapPut(
                "/api/households/{householdId:guid}/opening-balance-reference-data/accounts/{accountCode}",
                CreateAccountAsync)
            .WithName("CreateOpeningBalanceAccount");

        endpoints.MapPut(
                "/api/opening-balance-reference-data/assets/{assetCode}",
                CreateAssetAsync)
            .WithName("CreateOpeningBalanceAsset");

        return endpoints;
    }

    private static async Task<IResult> PreviewAsync(
        OpeningBalanceRequest request,
        PreviewOpeningBalanceUseCase useCase,
        CancellationToken cancellationToken)
    {
        var preview = await useCase.ExecuteAsync(
            request.ToCommand(),
            cancellationToken);

        return Results.Ok(preview.ToResponse());
    }

    private static async Task<IResult> RecordAsync(
        HttpRequest httpRequest,
        OpeningBalanceRequest request,
        RecordOpeningBalanceUseCase useCase,
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
            var verificationLocation =
                $"/api/households/{request.HouseholdId:D}/ledger/opening-balances/{result.TransactionId:D}/verification";

            return TypedResults.Created(
                $"/api/ledger/transactions/{result.TransactionId:D}",
                new RecordOpeningBalanceResponse(
                    result.TransactionId,
                    result.AssetLotIds,
                    result.SubmittedQuantityRawE8,
                    result.PersistedQuantityRawE8,
                    verificationLocation));
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
        GetOpeningBalanceVerificationUseCase useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            new GetOpeningBalanceVerificationQuery(
                householdId,
                transactionId),
            cancellationToken);

        return Results.Ok(result.ToResponse());
    }

    private static async Task<IResult> CreateCurrencyAsync(
        HttpRequest httpRequest,
        string currencyCode,
        CreateOpeningBalanceCurrencyRequest request,
        CreateOpeningBalanceCurrencyUseCase useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            new CreateOpeningBalanceCurrencyCommand(
                new CurrencyCode(currencyCode),
                request.Name,
                request.MinorUnitDigits),
            cancellationToken);
        var response = new OpeningBalanceCurrencyResponse(
            result.WasCreated,
            result.Reference.Code.Value,
            result.Reference.Name,
            result.Reference.MinorUnitDigits);

        return CreatedOrEquivalent(httpRequest, result.WasCreated, response);
    }

    private static async Task<IResult> CreateInstitutionAsync(
        HttpRequest httpRequest,
        string institutionCode,
        CreateOpeningBalanceInstitutionRequest request,
        CreateOpeningBalanceInstitutionUseCase useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            new CreateOpeningBalanceInstitutionCommand(
                institutionCode,
                request.Name,
                OpeningBalanceContractMapper.ParseInstitutionType(
                    request.TypeCode)),
            cancellationToken);
        var reference = result.Reference;
        var response = new OpeningBalanceInstitutionResponse(
            result.WasCreated,
            reference.InstitutionId,
            reference.Code,
            reference.Name,
            OpeningBalanceContractMapper.ToCode(reference.Type),
            reference.IsActive);

        return CreatedOrEquivalent(httpRequest, result.WasCreated, response);
    }

    private static async Task<IResult> CreateAccountAsync(
        HttpRequest httpRequest,
        Guid householdId,
        string accountCode,
        CreateOpeningBalanceAccountRequest request,
        CreateOpeningBalanceAccountUseCase useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            new CreateOpeningBalanceAccountCommand(
                householdId,
                request.InstitutionId,
                accountCode,
                request.Name,
                OpeningBalanceContractMapper.ParseAccountType(
                    request.TypeCode),
                request.OpenedOn),
            cancellationToken);
        var reference = result.Reference;
        var response = new OpeningBalanceAccountResponse(
            result.WasCreated,
            reference.AccountId,
            reference.HouseholdId,
            reference.InstitutionId,
            reference.Code,
            reference.Name,
            OpeningBalanceContractMapper.ToCode(reference.Type),
            reference.IsActive,
            reference.OpenedOn);

        return CreatedOrEquivalent(httpRequest, result.WasCreated, response);
    }

    private static async Task<IResult> CreateAssetAsync(
        HttpRequest httpRequest,
        string assetCode,
        CreateOpeningBalanceAssetRequest request,
        CreateOpeningBalanceAssetUseCase useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            new CreateOpeningBalanceAssetCommand(
                assetCode,
                request.Name,
                OpeningBalanceContractMapper.ParseAssetType(request.TypeCode),
                new CurrencyCode(request.BaseCurrencyCode),
                OpeningBalanceContractMapper.ParseLotTrackingMode(
                    request.LotTrackingModeCode)),
            cancellationToken);
        var reference = result.Reference;
        var response = new OpeningBalanceAssetResponse(
            result.WasCreated,
            reference.AssetId,
            reference.Code,
            reference.Name,
            OpeningBalanceContractMapper.ToCode(reference.Type),
            OpeningBalanceContractMapper.ToCode(reference.BaseUnit),
            reference.BaseCurrency?.Value
                ?? throw new InvalidOperationException(
                    "An opening-balance asset must have a base currency."),
            OpeningBalanceContractMapper.ToCode(reference.LotTrackingMode),
            reference.IsActive);

        return CreatedOrEquivalent(httpRequest, result.WasCreated, response);
    }

    private static IResult CreatedOrEquivalent<T>(
        HttpRequest request,
        bool wasCreated,
        T response)
        => wasCreated
            ? TypedResults.Created(request.Path.Value, response)
            : TypedResults.Ok(response);

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
                detail: "Exactly one Idempotency-Key header is required.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "IDEMPOTENCY_KEY_REQUIRED"
                });
            return false;
        }

        var value = values[0];

        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumIdempotencyKeyLength
            || value.Any(char.IsControl))
        {
            idempotencyKey = string.Empty;
            error = Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid idempotency key",
                detail:
                    $"Idempotency-Key must contain between 1 and {MaximumIdempotencyKeyLength} non-control characters.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "IDEMPOTENCY_KEY_INVALID"
                });
            return false;
        }

        idempotencyKey = value;
        error = null;
        return true;
    }
}
