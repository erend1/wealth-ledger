using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.Inventory;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Navigation;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Assets;

/// <summary>
/// The shared Ready-household, scope-choice and failure handling of the two
/// Assets pages.
/// </summary>
public abstract class InventoryPageModel : PageModel
{
    private readonly ReadyHouseholdResolver _householdResolver;
    private readonly ScopeChoiceLoader _choiceLoader;

    protected InventoryPageModel(
        ReadyHouseholdResolver householdResolver,
        ScopeChoiceLoader choiceLoader,
        UiText text)
    {
        _householdResolver = householdResolver
            ?? throw new ArgumentNullException(nameof(householdResolver));
        _choiceLoader = choiceLoader
            ?? throw new ArgumentNullException(nameof(choiceLoader));
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public UiText Text { get; }

    public InventoryFilterForm Filters { get; protected set; } = new();

    public ScopeChoices? Choices { get; private set; }

    public Guid HouseholdId { get; private set; }

    public string HouseholdName { get; private set; } = string.Empty;

    public string HouseholdStateResourceKey { get; private set; } = string.Empty;

    public bool HasHousehold => HouseholdStateResourceKey.Length == 0;

    public bool NavigationError { get; private set; }

    public bool ScopeNotFound { get; private set; }

    public bool LoadUnavailable { get; private set; }

    public bool HasFilters => InventoryFilterMapper.HasFilters(Filters);

    public DateOnly? AsOfDate { get; protected set; }

    public ScopeSelect Select(string name)
    {
        var choices = Choices ?? new ScopeChoices([], [], [], []);

        return name switch
        {
            "portfolioId" => CreateSelect(
                name,
                "LedgerSearch_PortfolioLabel",
                ScopeChoiceLoader.PortfolioOptions(choices),
                Filters.PortfolioId),
            "accountId" => CreateSelect(
                name,
                "LedgerSearch_AccountLabel",
                ScopeChoiceLoader.AccountOptions(choices),
                Filters.AccountId),
            "institutionId" => CreateSelect(
                name,
                "LedgerSearch_InstitutionLabel",
                ScopeChoiceLoader.InstitutionOptions(choices),
                Filters.InstitutionId),
            "assetId" => CreateSelect(
                name,
                "LedgerSearch_AssetLabel",
                ScopeChoiceLoader.AssetOptions(choices),
                Filters.AssetId),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
    }

    /// <summary>
    /// Resolves the Ready household and loads scope choices. Returns false
    /// when the page must render a state instead of a result.
    /// </summary>
    protected async Task<bool> PrepareAsync(CancellationToken cancellationToken)
    {
        var resolution = await _householdResolver.ResolveAsync(cancellationToken);

        if (resolution.State != ReadyHouseholdResolutionState.Single)
        {
            HouseholdStateResourceKey =
                resolution.State == ReadyHouseholdResolutionState.None
                    ? "Ready_Household_None"
                    : "Ready_Household_Multiple";
            return false;
        }

        HouseholdId = resolution.Household!.HouseholdId;
        HouseholdName = resolution.Household.Name;

        try
        {
            Choices = await _choiceLoader.LoadAsync(HouseholdId, cancellationToken);
            return true;
        }
        catch (Exception exception)
            when (exception is NavigationRequestException
                  or HouseholdNotFoundException
                  or NavigationPersistenceException)
        {
            Unavailable();
            return false;
        }
    }

    protected void AddFilterErrors(IReadOnlyList<InventoryFilterError> errors)
    {
        // Binding records entries under property names; clear them so each
        // message is keyed by the id of the control it names.
        ModelState.Clear();

        foreach (var error in errors)
        {
            ModelState.AddModelError(FieldId(error.Field), Text[error.ResourceKey]);
        }

        Response.StatusCode = StatusCodes.Status400BadRequest;
    }

    /// <summary>
    /// Maps an inventory failure onto a sanitized page state. Returns false
    /// when the exception is not an inventory failure.
    /// </summary>
    protected bool HandleInventoryFailure(Exception exception)
    {
        switch (exception)
        {
            case InventoryRequestException request
                when request.ErrorCode is InventoryRequestException.CursorInvalidCode
                    or InventoryRequestException.CursorScopeMismatchCode
                    or InventoryRequestException.PageSizeInvalidCode:
                InvalidNavigation();
                return true;
            case InventoryRequestException:
                ModelState.AddModelError(string.Empty, Text["Assets_Error_FilterInvalid"]);
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return true;
            case InventoryScopeNotFoundException:
                ScopeNotFound = true;
                Response.StatusCode = StatusCodes.Status404NotFound;
                return true;
            case InventoryArithmeticOverflowException:
            case InventoryPersistenceException:
                Unavailable();
                return true;
            default:
                return false;
        }
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

    private ScopeSelect CreateSelect(
        string name,
        string labelKey,
        IReadOnlyList<ScopeOption> options,
        string? selected)
        => new(
            name,
            Text[labelKey],
            Text["LedgerSearch_AnyOption"],
            Text["Scope_InactiveMarker"],
            options,
            selected?.Trim());

    private static string FieldId(string field)
        => field switch
        {
            nameof(InventoryFilterForm.AsOf) => "asOf",
            nameof(InventoryFilterForm.IncludeZero) => "includeZero",
            nameof(InventoryFilterForm.IncludeExhausted) => "includeExhausted",
            nameof(InventoryFilterForm.PortfolioId) => "portfolioId",
            nameof(InventoryFilterForm.AccountId) => "accountId",
            nameof(InventoryFilterForm.InstitutionId) => "institutionId",
            nameof(InventoryFilterForm.AssetId) => "assetId",
            _ => string.Empty
        };
}
