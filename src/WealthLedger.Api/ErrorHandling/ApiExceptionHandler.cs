using Microsoft.AspNetCore.Diagnostics;
using WealthLedger.Application.Common;
using WealthLedger.Application.CoreLedger;
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

        await Results.Problem(
                statusCode: failure.Value.StatusCode,
                title: failure.Value.Title,
                detail: failure.Value.Detail)
            .ExecuteAsync(httpContext);

        return true;
    }

    private static ApiFailure? Map(Exception exception)
        => exception switch
        {
            CoreLedgerAlreadyInitializedException => new ApiFailure(
                StatusCodes.Status409Conflict,
                "Core ledger already initialized",
                exception.Message),
            ApplicationRuleViolationException => new ApiFailure(
                StatusCodes.Status422UnprocessableEntity,
                "Ledger rule violation",
                exception.Message),
            DomainRuleViolationException => new ApiFailure(
                StatusCodes.Status422UnprocessableEntity,
                "Ledger rule violation",
                exception.Message),
            CoreLedgerPersistenceException => new ApiFailure(
                StatusCodes.Status409Conflict,
                "Ledger persistence conflict",
                "The ledger write conflicted with persisted history."),
            OverflowException => new ApiFailure(
                StatusCodes.Status400BadRequest,
                "Invalid numeric value",
                "A numeric value exceeds the supported range."),
            ArgumentException => new ApiFailure(
                StatusCodes.Status400BadRequest,
                "Invalid request",
                exception.Message),
            _ => null
        };

    private readonly record struct ApiFailure(
        int StatusCode,
        string Title,
        string Detail);
}
