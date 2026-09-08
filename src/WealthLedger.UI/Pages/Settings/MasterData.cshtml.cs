using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Navigation;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Settings;

[LocalStartupPage(
    supportsPost: false,
    LocalStartupMode.Ready)]
public sealed class MasterDataModel : PageModel
{
    private const int PageSize = 100;
    private readonly ReadyHouseholdResolver _householdResolver;
    private readonly ListHouseholdMembersUseCase _listMembers;
    private readonly ListInstitutionsUseCase _listInstitutions;
    private readonly ListPortfoliosUseCase _listPortfolios;
    private readonly ListAccountsUseCase _listAccounts;
    private readonly ListCurrenciesUseCase _listCurrencies;
    private readonly ListAssetsUseCase _listAssets;
    private readonly ValuePresenter _values;

    public MasterDataModel(
        ReadyHouseholdResolver householdResolver,
        ListHouseholdMembersUseCase listMembers,
        ListInstitutionsUseCase listInstitutions,
        ListPortfoliosUseCase listPortfolios,
        ListAccountsUseCase listAccounts,
        ListCurrenciesUseCase listCurrencies,
        ListAssetsUseCase listAssets,
        ValuePresenter values)
    {
        _householdResolver = householdResolver
            ?? throw new ArgumentNullException(nameof(householdResolver));
        _listMembers = listMembers
            ?? throw new ArgumentNullException(nameof(listMembers));
        _listInstitutions = listInstitutions
            ?? throw new ArgumentNullException(nameof(listInstitutions));
        _listPortfolios = listPortfolios
            ?? throw new ArgumentNullException(nameof(listPortfolios));
        _listAccounts = listAccounts
            ?? throw new ArgumentNullException(nameof(listAccounts));
        _listCurrencies = listCurrencies
            ?? throw new ArgumentNullException(nameof(listCurrencies));
        _listAssets = listAssets
            ?? throw new ArgumentNullException(nameof(listAssets));
        _values = values ?? throw new ArgumentNullException(nameof(values));
    }

    public string HouseholdName { get; private set; } = string.Empty;

    public string HouseholdStateResourceKey { get; private set; } = string.Empty;

    public bool HasHousehold => HouseholdStateResourceKey.Length == 0;

    public bool LoadUnavailable { get; private set; }

    public bool HasMoreData { get; private set; }

    public IReadOnlyList<MemberDisplay> Members { get; private set; } = [];

    public IReadOnlyList<InstitutionDisplay> Institutions { get; private set; } = [];

    public IReadOnlyList<PortfolioDisplay> Portfolios { get; private set; } = [];

    public IReadOnlyList<AccountDisplay> Accounts { get; private set; } = [];

    public IReadOnlyList<CurrencyDisplay> Currencies { get; private set; } = [];

    public IReadOnlyList<AssetDisplay> Assets { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        var resolution = await _householdResolver.ResolveAsync(
            cancellationToken);

        if (resolution.State != ReadyHouseholdResolutionState.Single)
        {
            HouseholdStateResourceKey = resolution.State
                == ReadyHouseholdResolutionState.None
                    ? "Ready_Household_None"
                    : "Ready_Household_Multiple";
            return Page();
        }

        var household = resolution.Household!;
        HouseholdName = household.Name;

        try
        {
            var members = await _listMembers.ExecuteAsync(
                new ListHouseholdMembersQuery(
                    household.HouseholdId,
                    PageSize,
                    IncludeInactive: true),
                cancellationToken);
            var institutions = await _listInstitutions.ExecuteAsync(
                new ListInstitutionsQuery(
                    PageSize,
                    IncludeInactive: true),
                cancellationToken);
            var portfolios = await _listPortfolios.ExecuteAsync(
                new ListPortfoliosQuery(
                    household.HouseholdId,
                    PageSize,
                    IncludeInactive: true),
                cancellationToken);
            var accounts = await _listAccounts.ExecuteAsync(
                new ListAccountsQuery(
                    household.HouseholdId,
                    PageSize,
                    IncludeInactive: true),
                cancellationToken);
            var currencies = await _listCurrencies.ExecuteAsync(
                new ListCurrenciesQuery(PageSize),
                cancellationToken);
            var assets = await _listAssets.ExecuteAsync(
                new ListAssetsQuery(
                    PageSize,
                    IncludeInactive: true),
                cancellationToken);

            HasMoreData = new[]
            {
                members.NextCursor,
                institutions.NextCursor,
                portfolios.NextCursor,
                accounts.NextCursor,
                currencies.NextCursor,
                assets.NextCursor
            }.Any(cursor => cursor is not null);
            Members = members.Items.Select(Present).ToArray();
            Institutions = institutions.Items.Select(Present).ToArray();
            Portfolios = portfolios.Items.Select(Present).ToArray();
            Accounts = accounts.Items.Select(Present).ToArray();
            Currencies = currencies.Items.Select(Present).ToArray();
            Assets = assets.Items.Select(Present).ToArray();
        }
        catch (Exception exception)
            when (exception is NavigationRequestException
                  or HouseholdNotFoundException
                  or NavigationPersistenceException)
        {
            LoadUnavailable = true;
            Response.StatusCode = StatusCodes.Status409Conflict;
        }

        return Page();
    }

    private MemberDisplay Present(HouseholdMemberNavigationItem item)
        => new(
            item.DisplayName,
            item.IsActive,
            _values.UtcTimestamp(item.CreatedAtUtc));

    private InstitutionDisplay Present(InstitutionNavigationItem item)
        => new(
            item.Code,
            item.Name,
            _values.StableCode(item.Type),
            item.IsActive);

    private PortfolioDisplay Present(PortfolioNavigationItem item)
        => new(
            item.Code,
            item.Name,
            _values.StableCode(item.Status),
            _values.UtcTimestamp(item.CreatedAtUtc),
            item.ClosedAtUtc is null
                ? _values.NotApplicable()
                : _values.UtcTimestamp(item.ClosedAtUtc.Value));

    private AccountDisplay Present(AccountNavigationItem item)
        => new(
            item.Code,
            item.Name,
            _values.StableCode(item.Type),
            item.IsActive,
            item.Institution?.Code,
            item.Institution?.Name,
            item.Institution is null
                ? null
                : _values.StableCode(item.Institution.Type),
            item.Institution?.IsActive,
            item.OpenedOn is null
                ? _values.Unknown()
                : _values.BusinessDate(item.OpenedOn.Value),
            item.ClosedOn is null
                ? _values.NotApplicable()
                : _values.BusinessDate(item.ClosedOn.Value));

    private static CurrencyDisplay Present(CurrencyNavigationItem item)
        => new(item.Code, item.Name, item.MinorUnitDigits);

    private AssetDisplay Present(AssetNavigationItem item)
        => new(
            item.Code,
            item.Name,
            _values.StableCode(item.Type),
            _values.StableCode(item.BaseUnit),
            item.BaseCurrencyCode,
            _values.StableCode(item.LotTrackingMode),
            item.IsActive,
            _values.UtcTimestamp(item.CreatedAtUtc));

    public sealed record MemberDisplay(
        string DisplayName,
        bool IsActive,
        DisplayValue CreatedAt);

    public sealed record InstitutionDisplay(
        string Code,
        string Name,
        DisplayValue Type,
        bool IsActive);

    public sealed record PortfolioDisplay(
        string Code,
        string Name,
        DisplayValue Status,
        DisplayValue CreatedAt,
        DisplayValue ClosedAt);

    public sealed record AccountDisplay(
        string Code,
        string Name,
        DisplayValue Type,
        bool IsActive,
        string? InstitutionCode,
        string? InstitutionName,
        DisplayValue? InstitutionType,
        bool? InstitutionIsActive,
        DisplayValue OpenedOn,
        DisplayValue ClosedOn);

    public sealed record CurrencyDisplay(
        string Code,
        string Name,
        int MinorUnitDigits);

    public sealed record AssetDisplay(
        string Code,
        string Name,
        DisplayValue Type,
        DisplayValue BaseUnit,
        string? BaseCurrencyCode,
        DisplayValue LotTrackingMode,
        bool IsActive,
        DisplayValue CreatedAt);
}
