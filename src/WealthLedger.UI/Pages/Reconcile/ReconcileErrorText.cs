using Microsoft.AspNetCore.Http;
using WealthLedger.Application.Evidence;
using WealthLedger.Application.Reconciliation;

namespace WealthLedger.UI.Pages.Reconcile;

/// <summary>
/// Maps stable evidence and reconciliation error codes onto sanitized
/// Turkish messages and HTTP status codes.
/// </summary>
/// <remarks>
/// The exception message is never shown: a message names only its rule, but
/// the page speaks in the household's language and never echoes input.
/// </remarks>
public static class ReconcileErrorText
{
    public static string ResourceKey(string errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        return "M010Error_" + errorCode;
    }

    public static int StatusCode(EvidenceErrorCategory category)
        => category switch
        {
            EvidenceErrorCategory.NotFound => StatusCodes.Status404NotFound,
            EvidenceErrorCategory.Conflict => StatusCodes.Status409Conflict,
            EvidenceErrorCategory.Integrity => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity
        };

    public static int StatusCode(ReconciliationErrorCategory category)
        => category switch
        {
            ReconciliationErrorCategory.NotFound => StatusCodes.Status404NotFound,
            ReconciliationErrorCategory.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity
        };
}
