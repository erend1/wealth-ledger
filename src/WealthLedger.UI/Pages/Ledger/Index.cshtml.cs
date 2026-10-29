using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.LedgerSearch;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Navigation;
using WealthLedger.Domain.Ledger;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Ledger;

/// <summary>
/// The M010 Ledger: bounded search over effective Posted history.
/// </summary>
/// <remarks>
/// Results are ordered by financial date, as Decision 3 requires, and every
/// result opens the existing complete transaction explanation. The M005
/// recent feed stays on Today and in its unchanged API route.
/// </remarks>
[LocalStartupPage(
    supportsPost: false,
    LocalStartupMode.Ready)]
public sealed class IndexModel : PageModel
{
    private readonly ReadyHouseholdResolver _householdResolver;
    private readonly SearchLedgerTransactionsUseCase _search;
    private readonly ScopeChoiceLoader _choiceLoader;
    private readonly RecentLedgerPresenter _presenter;
    private readonly ValuePresenter _values;
    private readonly UiText _text;

    public IndexModel(
        ReadyHouseholdResolver householdResolver,
        SearchLedgerTransactionsUseCase search,
        ScopeChoiceLoader choiceLoader,
        RecentLedgerPresenter presenter,
        ValuePresenter values,
        UiText text)
    {
        _householdResolver = householdResolver
            ?? throw new ArgumentNullException(nameof(householdResolver));
        _search = search
            ?? throw new ArgumentNullException(nameof(search));
        _choiceLoader = choiceLoader
            ?? throw new ArgumentNullException(nameof(choiceLoader));
        _presenter = presenter
            ?? throw new ArgumentNullException(nameof(presenter));
        _values = values
            ?? throw new ArgumentNullException(nameof(values));
        _text = text
            ?? throw new ArgumentNullException(nameof(text));
    }

    public LedgerSearchForm Filters { get; private set; } = new();

    public ScopeChoices? Choices { get; private set; }

    public string HouseholdName { get; private set; } = string.Empty;

    public string HouseholdStateResourceKey { get; private set; } = string.Empty;

    public bool HasHousehold => HouseholdStateResourceKey.Length == 0;

    public bool NavigationError { get; private set; }

    public bool FilterScopeNotFound { get; private set; }

    public bool LoadUnavailable { get; private set; }

    public bool HasFilters { get; private set; }

    public bool HasMoreFilters
        => (Filters.Type?.Any(type => !string.IsNullOrWhiteSpace(type)) ?? false)
           || (!string.IsNullOrWhiteSpace(Filters.Reversal)
               && Filters.Reversal != LedgerSearchFormMapper.ReversalAny);

    public int PageSize { get; private set; } =
        LedgerSearchFormMapper.DefaultPageSize;

    public string? NextPageUrl { get; private set; }

    public string FirstPageUrl
        => Url.Page("/Ledger/Index")
           + new Microsoft.AspNetCore.Http.Extensions.QueryBuilder(
                   LedgerSearchFormMapper.FilterPairs(Filters))
               .ToQueryString();

    public IReadOnlyList<RecentLedgerTransactionDisplay> Transactions
    {
        get;
        private set;
    } = [];

    public IReadOnlyList<(string Code, DisplayValue Label)> TypeChoices
        => Enum.GetValues<TransactionType>()
            .Select(type => (StableCodes.ToCode(type), _values.StableCode(type)))
            .ToArray();

    public IReadOnlyList<(string Code, string Label)> ReversalChoices
        => LedgerSearchFormMapper.ReversalCodes
            .Select(code => (code, _text["LedgerSearch_Reversal_" + code]))
            .ToArray();

    public bool IsTypeSelected(string code)
        => Filters.Type?.Contains(code, StringComparer.Ordinal) ?? false;

    public string SelectedReversal
        => string.IsNullOrWhiteSpace(Filters.Reversal)
            ? LedgerSearchFormMapper.ReversalAny
            : Filters.Reversal.Trim();

    public ScopeSelect Select(string name)
    {
        var choices = Choices
            ?? new ScopeChoices([], [], [], []);

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

    public async Task<IActionResult> OnGetAsync(
        [FromQuery] LedgerSearchForm filters,
        CancellationToken cancellationToken)
    {
        Filters = filters ?? new LedgerSearchForm();
        HasFilters = LedgerSearchFormMapper.HasFilters(Filters);

        var householdResolution = await _householdResolver.ResolveAsync(
            cancellationToken);

        if (householdResolution.State != ReadyHouseholdResolutionState.Single)
        {
            HouseholdStateResourceKey = householdResolution.State
                == ReadyHouseholdResolutionState.None
                    ? "Ready_Household_None"
                    : "Ready_Household_Multiple";
            return Page();
        }

        var household = householdResolution.Household!;
        HouseholdName = household.Name;

        try
        {
            Choices = await _choiceLoader.LoadAsync(
                household.HouseholdId,
                cancellationToken);
        }
        catch (Exception exception)
            when (exception is NavigationRequestException
                  or HouseholdNotFoundException
                  or NavigationPersistenceException)
        {
            LoadUnavailable = true;
            Response.StatusCode = StatusCodes.Status409Conflict;
            return Page();
        }

        if (!LedgerSearchFormMapper.TryParsePageSize(
                Filters.PageSize,
                out var pageSize))
        {
            return InvalidNavigation();
        }

        PageSize = pageSize;

        if (!LedgerSearchFormMapper.TryMap(
                Filters,
                household.HouseholdId,
                pageSize,
                out var query,
                out var errors))
        {
            // Binding records entries under property names; clear them so
            // each message is keyed by the id of the control it names.
            ModelState.Clear();

            foreach (var error in errors)
            {
                ModelState.AddModelError(
                    FieldId(error.Field),
                    _text[error.ResourceKey]);
            }

            Response.StatusCode = StatusCodes.Status400BadRequest;
            return Page();
        }

        try
        {
            var page = await _search.ExecuteAsync(query!, cancellationToken);
            Transactions = page.Items
                .Select(_presenter.Present)
                .ToArray();
            NextPageUrl = page.NextCursor is null
                ? null
                : Url.Page("/Ledger/Index")
                  + LedgerSearchFormMapper.NextPageQuery(
                      Filters,
                      pageSize,
                      page.NextCursor);
        }
        catch (LedgerSearchRequestException exception)
            when (exception.ErrorCode is LedgerSearchRequestException.CursorInvalidCode
                  or LedgerSearchRequestException.CursorScopeMismatchCode
                  or LedgerSearchRequestException.PageSizeInvalidCode)
        {
            return InvalidNavigation();
        }
        catch (LedgerSearchRequestException)
        {
            ModelState.AddModelError(
                string.Empty,
                _text["LedgerSearch_Error_FilterInvalid"]);
            Response.StatusCode = StatusCodes.Status400BadRequest;
        }
        catch (LedgerSearchFilterScopeNotFoundException)
        {
            FilterScopeNotFound = true;
            Response.StatusCode = StatusCodes.Status404NotFound;
        }
        catch (Exception exception)
            when (exception is LedgerSearchHouseholdNotFoundException
                  or LedgerSearchPersistenceException)
        {
            LoadUnavailable = true;
            Response.StatusCode = StatusCodes.Status409Conflict;
        }

        return Page();
    }

    private PageResult InvalidNavigation()
    {
        ModelState.Clear();
        NavigationError = true;
        Transactions = [];
        NextPageUrl = null;
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return Page();
    }

    private ScopeSelect CreateSelect(
        string name,
        string labelKey,
        IReadOnlyList<ScopeOption> options,
        string? selected)
        => new(
            name,
            _text[labelKey],
            _text["LedgerSearch_AnyOption"],
            _text["Scope_InactiveMarker"],
            options,
            selected?.Trim());

    private static string FieldId(string field)
        => field switch
        {
            nameof(LedgerSearchForm.ExecutedFrom) => "executedFrom",
            nameof(LedgerSearchForm.ExecutedTo) => "executedTo",
            nameof(LedgerSearchForm.Type) => "type-legend",
            nameof(LedgerSearchForm.PortfolioId) => "portfolioId",
            nameof(LedgerSearchForm.AccountId) => "accountId",
            nameof(LedgerSearchForm.InstitutionId) => "institutionId",
            nameof(LedgerSearchForm.AssetId) => "assetId",
            nameof(LedgerSearchForm.Reference) => "reference",
            nameof(LedgerSearchForm.Reversal) => "reversal-legend",
            _ => string.Empty
        };
}
