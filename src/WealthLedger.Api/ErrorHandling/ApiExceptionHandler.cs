using Microsoft.AspNetCore.Diagnostics;
using WealthLedger.Application.Common;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Navigation;
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

        await Results.Problem(
                statusCode: failure.Value.StatusCode,
                title: failure.Value.Title,
                detail: failure.Value.Detail,
                extensions: extensions)
            .ExecuteAsync(httpContext);

        return true;
    }

    private static ApiFailure? Map(Exception exception)
        => exception switch
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

    private readonly record struct ApiFailure(
        int StatusCode,
        string Title,
        string Detail,
        string? ErrorCode);
}
