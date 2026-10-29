using Microsoft.AspNetCore.Diagnostics;
using WealthLedger.Application.Common;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Evidence;
using WealthLedger.Application.Inventory;
using WealthLedger.Application.LedgerSearch;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Navigation;
using WealthLedger.Application.FundTrades;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Application.PhysicalGold;
using WealthLedger.Application.Reconciliation;
using WealthLedger.Application.Setup;
using WealthLedger.Domain.Common;

namespace WealthLedger.Api.ErrorHandling;

internal sealed class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var failure = Map(exception);

        if (failure is null)
        {
            return false;
        }

        var extensions = failure.Value.ErrorCode is null
            ? null
            : new Dictionary<string, object?>
            {
                ["code"] = failure.Value.ErrorCode
            };

        if (extensions is not null
            && exception is OpeningBalanceException
            {
                RelatedTransactionId: Guid relatedTransactionId
            })
        {
            extensions["relatedTransactionId"] = relatedTransactionId;
        }

        if (extensions is not null
            && exception is FundTradeException
            {
                RelatedTransactionId: Guid relatedFundTradeId
            })
        {
            extensions["relatedTransactionId"] = relatedFundTradeId;
        }

        if (extensions is not null
            && exception is PhysicalGoldException
            {
                RelatedTransactionId: Guid relatedPhysicalGoldActivityId
            })
        {
            extensions["relatedTransactionId"] =
                relatedPhysicalGoldActivityId;
        }

        await Results.Problem(
                statusCode: failure.Value.StatusCode,
                title: failure.Value.Title,
                detail: failure.Value.Detail,
                extensions: extensions)
            .ExecuteAsync(httpContext);

        return true;
    }

    /// <summary>
    /// The status and stable code this handler answers an exception with, for
    /// operational logging; an unmapped exception is a 500 without a code.
    /// </summary>
    internal static (int StatusCode, string? ErrorCode) Describe(
        Exception exception)
        => Map(exception) is { } failure
            ? (failure.StatusCode, failure.ErrorCode)
            : (StatusCodes.Status500InternalServerError, null);

    private static ApiFailure? Map(Exception exception)
        => MapM010(exception) ?? exception switch
        {
            CoreLedgerAlreadyInitializedException => new ApiFailure(
                StatusCodes.Status409Conflict,
                "Core ledger already initialized",
                exception.Message,
                ErrorCode: null),
            CoreLedgerSetupUnavailableException setupUnavailable =>
                setupUnavailable.Category
                    == LocalDataFailureCategory.OwnershipBusy
                    ? new ApiFailure(
                        StatusCodes.Status409Conflict,
                        "Core ledger setup unavailable",
                        "Another local data operation is already in progress.",
                        ErrorCode: null)
                    : new ApiFailure(
                        StatusCodes.Status409Conflict,
                        "Core ledger setup unavailable",
                        "Core ledger setup is not currently available.",
                        ErrorCode: null),
            NavigationRequestException navigationException => new ApiFailure(
                StatusCodes.Status400BadRequest,
                "Invalid navigation request",
                navigationException.Message,
                navigationException.ErrorCode),
            HouseholdNotFoundException => new ApiFailure(
                StatusCodes.Status404NotFound,
                "Household not found",
                "The requested household does not exist.",
                HouseholdNotFoundException.ErrorCode),
            PositionScopeNotFoundException => new ApiFailure(
                StatusCodes.Status404NotFound,
                "Position scope not found",
                "The requested position scope does not exist.",
                PositionScopeNotFoundException.ErrorCode),
            OpeningBalanceException openingBalanceException =>
                new ApiFailure(
                    openingBalanceException.Category switch
                    {
                        OpeningBalanceErrorCategory.NotFound =>
                            StatusCodes.Status404NotFound,
                        OpeningBalanceErrorCategory.Conflict =>
                            StatusCodes.Status409Conflict,
                        OpeningBalanceErrorCategory.Validation =>
                            StatusCodes.Status422UnprocessableEntity,
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(openingBalanceException.Category))
                    },
                    openingBalanceException.Category switch
                    {
                        OpeningBalanceErrorCategory.NotFound =>
                            "Opening balance not found",
                        OpeningBalanceErrorCategory.Conflict =>
                            "Opening balance conflict",
                        OpeningBalanceErrorCategory.Validation =>
                            "Opening balance validation failed",
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(openingBalanceException.Category))
                    },
                    openingBalanceException.Message,
                    openingBalanceException.ErrorCode),
            FundTradeException fundTradeException =>
                new ApiFailure(
                    fundTradeException.Category switch
                    {
                        FundTradeErrorCategory.NotFound =>
                            StatusCodes.Status404NotFound,
                        FundTradeErrorCategory.Conflict =>
                            StatusCodes.Status409Conflict,
                        FundTradeErrorCategory.Validation =>
                            StatusCodes.Status422UnprocessableEntity,
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(fundTradeException.Category))
                    },
                    fundTradeException.Category switch
                    {
                        FundTradeErrorCategory.NotFound =>
                            "Fund trade not found",
                        FundTradeErrorCategory.Conflict =>
                            "Fund trade conflict",
                        FundTradeErrorCategory.Validation =>
                            "Fund trade validation failed",
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(fundTradeException.Category))
                    },
                    fundTradeException.Message,
                    fundTradeException.ErrorCode),
            PhysicalGoldException physicalGoldException =>
                new ApiFailure(
                    physicalGoldException.Category switch
                    {
                        PhysicalGoldErrorCategory.NotFound =>
                            StatusCodes.Status404NotFound,
                        PhysicalGoldErrorCategory.Conflict =>
                            StatusCodes.Status409Conflict,
                        PhysicalGoldErrorCategory.Validation =>
                            StatusCodes.Status422UnprocessableEntity,
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(physicalGoldException.Category))
                    },
                    physicalGoldException.Category switch
                    {
                        PhysicalGoldErrorCategory.NotFound =>
                            "Physical-gold activity not found",
                        PhysicalGoldErrorCategory.Conflict =>
                            "Physical-gold activity conflict",
                        PhysicalGoldErrorCategory.Validation =>
                            "Physical-gold validation failed",
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(physicalGoldException.Category))
                    },
                    physicalGoldException.Message,
                    physicalGoldException.ErrorCode),
            NavigationPersistenceException => new ApiFailure(
                StatusCodes.Status409Conflict,
                "Navigation data unavailable",
                "The requested navigation data could not be read safely.",
                ErrorCode: null),
            ApplicationRuleViolationException => new ApiFailure(
                StatusCodes.Status422UnprocessableEntity,
                "Ledger rule violation",
                exception.Message,
                ErrorCode: null),
            DomainRuleViolationException => new ApiFailure(
                StatusCodes.Status422UnprocessableEntity,
                "Ledger rule violation",
                exception.Message,
                ErrorCode: null),
            CoreLedgerPersistenceException => new ApiFailure(
                StatusCodes.Status409Conflict,
                "Ledger persistence conflict",
                "The ledger write conflicted with persisted history.",
                ErrorCode: null),
            OverflowException => new ApiFailure(
                StatusCodes.Status400BadRequest,
                "Invalid numeric value",
                "A numeric value exceeds the supported range.",
                ErrorCode: null),
            ArgumentException => new ApiFailure(
                StatusCodes.Status400BadRequest,
                "Invalid request",
                exception.Message,
                ErrorCode: null),
            _ => null
        };

    // M010 search, inventory, evidence and reconciliation failures. Their
    // messages name the rule only, so they are safe as problem details.
    private static ApiFailure? MapM010(Exception exception)
        => exception switch
        {
            LedgerSearchRequestException searchRequest => new ApiFailure(
                StatusCodes.Status400BadRequest,
                "Invalid ledger search request",
                searchRequest.Message,
                searchRequest.ErrorCode),
            LedgerSearchHouseholdNotFoundException => new ApiFailure(
                StatusCodes.Status404NotFound,
                "Household not found",
                exception.Message,
                LedgerSearchHouseholdNotFoundException.ErrorCode),
            LedgerSearchFilterScopeNotFoundException => new ApiFailure(
                StatusCodes.Status404NotFound,
                "Ledger search scope not found",
                exception.Message,
                LedgerSearchFilterScopeNotFoundException.ErrorCode),
            LedgerSearchPersistenceException => new ApiFailure(
                StatusCodes.Status409Conflict,
                "Ledger search data unavailable",
                "The requested ledger history could not be read safely.",
                "LEDGER_SEARCH_DATA_UNAVAILABLE"),
            InventoryRequestException inventoryRequest => new ApiFailure(
                StatusCodes.Status400BadRequest,
                "Invalid inventory request",
                inventoryRequest.Message,
                inventoryRequest.ErrorCode),
            InventoryScopeNotFoundException => new ApiFailure(
                StatusCodes.Status404NotFound,
                "Inventory scope not found",
                exception.Message,
                InventoryScopeNotFoundException.ErrorCode),
            InventoryArithmeticOverflowException => new ApiFailure(
                StatusCodes.Status422UnprocessableEntity,
                "Inventory arithmetic overflow",
                exception.Message,
                InventoryArithmeticOverflowException.ErrorCode),
            InventoryPersistenceException => new ApiFailure(
                StatusCodes.Status409Conflict,
                "Inventory data unavailable",
                "The requested inventory could not be derived safely.",
                "INVENTORY_DATA_UNAVAILABLE"),
            EvidenceException evidence => new ApiFailure(
                evidence.ErrorCode is EvidenceErrorCodes.PageSizeInvalid
                    or EvidenceErrorCodes.CursorInvalid
                    or EvidenceErrorCodes.CursorScopeMismatch
                    ? StatusCodes.Status400BadRequest
                    : evidence.Category switch
                    {
                        EvidenceErrorCategory.NotFound =>
                            StatusCodes.Status404NotFound,
                        EvidenceErrorCategory.Conflict =>
                            StatusCodes.Status409Conflict,
                        EvidenceErrorCategory.Validation =>
                            StatusCodes.Status422UnprocessableEntity,
                        EvidenceErrorCategory.Integrity =>
                            StatusCodes.Status409Conflict,
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(evidence.Category))
                    },
                evidence.Category switch
                {
                    EvidenceErrorCategory.NotFound => "Evidence not found",
                    EvidenceErrorCategory.Conflict => "Evidence conflict",
                    EvidenceErrorCategory.Validation =>
                        "Evidence validation failed",
                    EvidenceErrorCategory.Integrity =>
                        "Evidence integrity failure",
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(evidence.Category))
                },
                evidence.Message,
                evidence.ErrorCode),
            EvidencePersistenceException => new ApiFailure(
                StatusCodes.Status409Conflict,
                "Evidence data unavailable",
                "The evidence write conflicted with persisted history.",
                "EVIDENCE_DATA_UNAVAILABLE"),
            ReconciliationException reconciliation => new ApiFailure(
                reconciliation.ErrorCode is ReconciliationErrorCodes.PageSizeInvalid
                    or ReconciliationErrorCodes.CursorInvalid
                    or ReconciliationErrorCodes.CursorScopeMismatch
                    ? StatusCodes.Status400BadRequest
                    : reconciliation.Category switch
                    {
                        ReconciliationErrorCategory.NotFound =>
                            StatusCodes.Status404NotFound,
                        ReconciliationErrorCategory.Conflict =>
                            StatusCodes.Status409Conflict,
                        ReconciliationErrorCategory.Validation =>
                            StatusCodes.Status422UnprocessableEntity,
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(reconciliation.Category))
                    },
                reconciliation.Category switch
                {
                    ReconciliationErrorCategory.NotFound =>
                        "Reconciliation not found",
                    ReconciliationErrorCategory.Conflict =>
                        "Reconciliation conflict",
                    ReconciliationErrorCategory.Validation =>
                        "Reconciliation validation failed",
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(reconciliation.Category))
                },
                reconciliation.Message,
                reconciliation.ErrorCode),
            ReconciliationPersistenceException => new ApiFailure(
                StatusCodes.Status409Conflict,
                "Reconciliation data unavailable",
                "The reconciliation could not be read or written safely.",
                "RECONCILIATION_DATA_UNAVAILABLE"),
            IdempotencyConflictException => new ApiFailure(
                StatusCodes.Status409Conflict,
                "Idempotency key conflict",
                exception.Message,
                IdempotencyConflictException.ErrorCode),
            _ => null
        };

    private readonly record struct ApiFailure(
        int StatusCode,
        string Title,
        string Detail,
        string? ErrorCode);
}
