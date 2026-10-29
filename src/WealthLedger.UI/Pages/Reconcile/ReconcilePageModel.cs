using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.Evidence;
using WealthLedger.Application.Reconciliation;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Reconcile;

/// <summary>
/// The Ready-household and sanitized failure handling shared by the
/// Reconcile pages.
/// </summary>
public abstract class ReconcilePageModel : PageModel
{
    private readonly ReadyHouseholdResolver _householdResolver;

    protected ReconcilePageModel(
        ReadyHouseholdResolver householdResolver,
        UiText text)
    {
        _householdResolver = householdResolver
            ?? throw new ArgumentNullException(nameof(householdResolver));
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public UiText Text { get; }

    public Guid HouseholdId { get; private set; }

    public string HouseholdName { get; private set; } = string.Empty;

    public string HouseholdStateResourceKey { get; private set; } = string.Empty;

    public bool HasHousehold => HouseholdStateResourceKey.Length == 0;

    public bool NavigationError { get; protected set; }

    public bool LoadUnavailable { get; protected set; }

    /// <summary>
    /// The resource key of a sanitized "not found" state, or null.
    /// </summary>
    public string? NotFoundResourceKey { get; protected set; }

    public bool HasPageState
        => NavigationError
           || LoadUnavailable
           || NotFoundResourceKey is not null
           || !HasHousehold;

    protected async Task<bool> ResolveHouseholdAsync(
        CancellationToken cancellationToken)
    {
        var resolution = await _householdResolver.ResolveAsync(cancellationToken);

        if (resolution.State != ReadyHouseholdResolutionState.Single)
        {
            HouseholdStateResourceKey =
                resolution.State == ReadyHouseholdResolutionState.None
                    ? "Ready_Household_None"
                    : "Ready_Household_Multiple";
            Response.StatusCode = StatusCodes.Status409Conflict;
            return false;
        }

        HouseholdId = resolution.Household!.HouseholdId;
        HouseholdName = resolution.Household.Name;
        return true;
    }

    /// <summary>
    /// Records a failure as one sanitized form message with its status.
    /// </summary>
    protected void AddFormError(string resourceKey, int statusCode)
    {
        ModelState.AddModelError(string.Empty, Text[resourceKey]);
        Response.StatusCode = statusCode;
    }

    protected void AddFormError(EvidenceException exception)
        => AddFormError(
            ReconcileErrorText.ResourceKey(exception.ErrorCode),
            ReconcileErrorText.StatusCode(exception.Category));

    protected void AddFormError(ReconciliationException exception)
        => AddFormError(
            ReconcileErrorText.ResourceKey(exception.ErrorCode),
            ReconcileErrorText.StatusCode(exception.Category));

    protected void MarkNotFound(string resourceKey)
    {
        NotFoundResourceKey = resourceKey;
        Response.StatusCode = StatusCodes.Status404NotFound;
    }

    protected void InvalidNavigation()
    {
        ModelState.Clear();
        NavigationError = true;
        Response.StatusCode = StatusCodes.Status400BadRequest;
    }

    protected void Unavailable()
    {
        LoadUnavailable = true;
        Response.StatusCode = StatusCodes.Status409Conflict;
    }

    protected static bool IsNavigationCode(string errorCode)
        => errorCode is EvidenceErrorCodes.PageSizeInvalid
            or EvidenceErrorCodes.CursorInvalid
            or EvidenceErrorCodes.CursorScopeMismatch
            or ReconciliationErrorCodes.PageSizeInvalid
            or ReconciliationErrorCodes.CursorInvalid
            or ReconciliationErrorCodes.CursorScopeMismatch;
}
