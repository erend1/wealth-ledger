using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Navigation;
using WealthLedger.UI.Hosting;

namespace WealthLedger.UI.Pages.Ledger;

[LocalStartupPage(
    supportsPost: false,
    LocalStartupMode.Ready)]
public sealed class IndexModel : PageModel
{
    private const int DefaultPageSize = 20;
    private readonly ReadyHouseholdResolver _householdResolver;
    private readonly ListRecentLedgerTransactionsUseCase _listRecent;
    private readonly RecentLedgerPresenter _presenter;

    public IndexModel(
        ReadyHouseholdResolver householdResolver,
        ListRecentLedgerTransactionsUseCase listRecent,
        RecentLedgerPresenter presenter)
    {
        _householdResolver = householdResolver
            ?? throw new ArgumentNullException(nameof(householdResolver));
        _listRecent = listRecent
            ?? throw new ArgumentNullException(nameof(listRecent));
        _presenter = presenter
            ?? throw new ArgumentNullException(nameof(presenter));
    }

    public string HouseholdName { get; private set; } = string.Empty;

    public string HouseholdStateResourceKey { get; private set; } = string.Empty;

    public bool HasHousehold => HouseholdStateResourceKey.Length == 0;

    public bool NavigationError { get; private set; }

    public bool LoadUnavailable { get; private set; }

    public int PageSize { get; private set; } = DefaultPageSize;

    public string? NextCursor { get; private set; }

    public IReadOnlyList<RecentLedgerTransactionDisplay> Transactions
    {
        get;
        private set;
    } = [];

    public async Task<IActionResult> OnGetAsync(
        int? pageSize,
        string? cursor,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return InvalidNavigation();
        }

        PageSize = pageSize ?? DefaultPageSize;
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
            var page = await _listRecent.ExecuteAsync(
                new ListRecentLedgerTransactionsQuery(
                    household.HouseholdId,
                    PageSize,
                    cursor),
                cancellationToken);
            Transactions = page.Items
                .Select(_presenter.Present)
                .ToArray();
            NextCursor = page.NextCursor;
        }
        catch (NavigationRequestException)
        {
            return InvalidNavigation();
        }
        catch (Exception exception)
            when (exception is HouseholdNotFoundException
                  or NavigationPersistenceException)
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
        NextCursor = null;
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return Page();
    }
}
