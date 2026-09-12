using WealthLedger.Application.Navigation;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Portfolios;

namespace WealthLedger.Application.OpeningBalances;

public sealed record ListOpeningBalanceChoicesQuery(
    Guid HouseholdId,
    int PageSize = 50,
    string? PortfolioCursor = null,
    string? AccountCursor = null,
    string? InstitutionCursor = null,
    string? CurrencyCursor = null,
    string? AssetCursor = null);

public sealed record OpeningBalanceChoices(
    HouseholdNavigationItem Household,
    NavigationPage<PortfolioNavigationItem> Portfolios,
    NavigationPage<AccountNavigationItem> Accounts,
    NavigationPage<InstitutionNavigationItem> Institutions,
    NavigationPage<CurrencyNavigationItem> Currencies,
    NavigationPage<AssetNavigationItem> Assets);

public sealed class ListOpeningBalanceChoicesUseCase
{
    private readonly GetHouseholdUseCase _getHousehold;
    private readonly ListPortfoliosUseCase _listPortfolios;
    private readonly ListAccountsUseCase _listAccounts;
    private readonly ListInstitutionsUseCase _listInstitutions;
    private readonly ListCurrenciesUseCase _listCurrencies;
    private readonly ListAssetsUseCase _listAssets;

    public ListOpeningBalanceChoicesUseCase(
        GetHouseholdUseCase getHousehold,
        ListPortfoliosUseCase listPortfolios,
        ListAccountsUseCase listAccounts,
        ListInstitutionsUseCase listInstitutions,
        ListCurrenciesUseCase listCurrencies,
        ListAssetsUseCase listAssets)
    {
        _getHousehold = getHousehold
            ?? throw new ArgumentNullException(nameof(getHousehold));
        _listPortfolios = listPortfolios
            ?? throw new ArgumentNullException(nameof(listPortfolios));
        _listAccounts = listAccounts
            ?? throw new ArgumentNullException(nameof(listAccounts));
        _listInstitutions = listInstitutions
            ?? throw new ArgumentNullException(nameof(listInstitutions));
        _listCurrencies = listCurrencies
            ?? throw new ArgumentNullException(nameof(listCurrencies));
        _listAssets = listAssets
            ?? throw new ArgumentNullException(nameof(listAssets));
    }

    public async Task<OpeningBalanceChoices> ExecuteAsync(
        ListOpeningBalanceChoicesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var household = await _getHousehold.ExecuteAsync(
            new GetHouseholdQuery(query.HouseholdId),
            cancellationToken);

        var portfolios = await _listPortfolios.ExecuteAsync(
            new ListPortfoliosQuery(
                query.HouseholdId,
                query.PageSize,
                query.PortfolioCursor),
            cancellationToken);

        var accounts = await _listAccounts.ExecuteAsync(
            new ListAccountsQuery(
                query.HouseholdId,
                query.PageSize,
                query.AccountCursor),
            cancellationToken);

        var institutions = await _listInstitutions.ExecuteAsync(
            new ListInstitutionsQuery(
                query.PageSize,
                query.InstitutionCursor),
            cancellationToken);

        var currencies = await _listCurrencies.ExecuteAsync(
            new ListCurrenciesQuery(
                query.PageSize,
                query.CurrencyCursor),
            cancellationToken);

        var assets = await _listAssets.ExecuteAsync(
            new ListAssetsQuery(
                query.PageSize,
                query.AssetCursor),
            cancellationToken);

        return new OpeningBalanceChoices(
            household,
            portfolios,
            FilterPage(accounts, IsCompatibleAccount),
            institutions,
            currencies,
            FilterPage(assets, IsCompatibleAsset));
    }

    private static NavigationPage<T> FilterPage<T>(
        NavigationPage<T> page,
        Func<T, bool> predicate)
        => new(
            page.Items.Where(predicate).ToArray(),
            page.NextCursor);

    private static bool IsCompatibleAccount(AccountNavigationItem account)
        => account.IsActive
           && account.ClosedOn is null
           && account.Type is AccountType.Cash
               or AccountType.Investment
               or AccountType.PhysicalVault
               or AccountType.Pension
           && (account.Type is not AccountType.Investment
                   and not AccountType.Pension
               || account.Institution is { IsActive: true });

    private static bool IsCompatibleAsset(AssetNavigationItem asset)
        => asset.IsActive
           && asset.BaseCurrencyCode is not null
           && asset.Type switch
           {
               AssetType.Cash or AssetType.Currency =>
                   asset.BaseUnit == AssetUnit.CurrencyUnit
                   && asset.LotTrackingMode == LotTrackingMode.None,

               AssetType.Fund =>
                   asset.BaseUnit == AssetUnit.FundUnit
                   && asset.LotTrackingMode is LotTrackingMode.Optional
                       or LotTrackingMode.Required,

               AssetType.Equity =>
                   asset.BaseUnit == AssetUnit.Share
                   && asset.LotTrackingMode is LotTrackingMode.Optional
                       or LotTrackingMode.Required,

               AssetType.PhysicalGold =>
                   asset.BaseUnit == AssetUnit.GrossGram
                   && asset.LotTrackingMode == LotTrackingMode.Required,

               _ => false
           };
}
