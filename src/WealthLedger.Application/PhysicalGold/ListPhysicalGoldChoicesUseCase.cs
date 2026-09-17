using WealthLedger.Application.Navigation;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Portfolios;

namespace WealthLedger.Application.PhysicalGold;

public sealed record ListPhysicalGoldChoicesQuery(
    Guid HouseholdId,
    int PageSize = 50,
    string? PortfolioCursor = null,
    string? GoldAccountCursor = null,
    string? CashAccountCursor = null,
    string? InstitutionCursor = null,
    string? CurrencyCursor = null,
    string? GoldAssetCursor = null,
    string? CashAssetCursor = null);

public sealed record PhysicalGoldChoices(
    HouseholdNavigationItem Household,
    NavigationPage<PortfolioNavigationItem> Portfolios,
    NavigationPage<AccountNavigationItem> GoldAccounts,
    NavigationPage<AccountNavigationItem> CashAccounts,
    NavigationPage<InstitutionNavigationItem> Counterparties,
    NavigationPage<CurrencyNavigationItem> Currencies,
    NavigationPage<AssetNavigationItem> GoldAssets,
    NavigationPage<AssetNavigationItem> CashAssets);

public sealed class ListPhysicalGoldChoicesUseCase
{
    private readonly GetHouseholdUseCase _getHousehold;
    private readonly ListPortfoliosUseCase _listPortfolios;
    private readonly ListAccountsUseCase _listAccounts;
    private readonly ListInstitutionsUseCase _listInstitutions;
    private readonly ListCurrenciesUseCase _listCurrencies;
    private readonly ListAssetsUseCase _listAssets;

    public ListPhysicalGoldChoicesUseCase(
        GetHouseholdUseCase getHousehold,
        ListPortfoliosUseCase listPortfolios,
        ListAccountsUseCase listAccounts,
        ListInstitutionsUseCase listInstitutions,
        ListCurrenciesUseCase listCurrencies,
        ListAssetsUseCase listAssets)
    {
        _getHousehold = getHousehold;
        _listPortfolios = listPortfolios;
        _listAccounts = listAccounts;
        _listInstitutions = listInstitutions;
        _listCurrencies = listCurrencies;
        _listAssets = listAssets;
    }

    public async Task<PhysicalGoldChoices> ExecuteAsync(
        ListPhysicalGoldChoicesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var household = await _getHousehold.ExecuteAsync(
            new GetHouseholdQuery(query.HouseholdId), cancellationToken);
        var portfolios = await _listPortfolios.ExecuteAsync(
            new ListPortfoliosQuery(
                query.HouseholdId, query.PageSize, query.PortfolioCursor),
            cancellationToken);
        var goldAccounts = await _listAccounts.ExecuteAsync(
            new ListAccountsQuery(
                query.HouseholdId, query.PageSize, query.GoldAccountCursor),
            cancellationToken);
        var cashAccounts = query.CashAccountCursor == query.GoldAccountCursor
            ? goldAccounts
            : await _listAccounts.ExecuteAsync(
                new ListAccountsQuery(
                    query.HouseholdId,
                    query.PageSize,
                    query.CashAccountCursor),
                cancellationToken);
        var institutions = await _listInstitutions.ExecuteAsync(
            new ListInstitutionsQuery(
                query.PageSize, query.InstitutionCursor),
            cancellationToken);
        var currencies = await _listCurrencies.ExecuteAsync(
            new ListCurrenciesQuery(
                query.PageSize, query.CurrencyCursor),
            cancellationToken);
        var goldAssets = await _listAssets.ExecuteAsync(
            new ListAssetsQuery(query.PageSize, query.GoldAssetCursor),
            cancellationToken);
        var cashAssets = query.CashAssetCursor == query.GoldAssetCursor
            ? goldAssets
            : await _listAssets.ExecuteAsync(
                new ListAssetsQuery(query.PageSize, query.CashAssetCursor),
                cancellationToken);

        return new PhysicalGoldChoices(
            household,
            Filter(portfolios, x => x.Status == PortfolioStatus.Active),
            Filter(goldAccounts, IsGoldAccount),
            Filter(cashAccounts, IsCashAccount),
            Filter(institutions, IsCounterparty),
            currencies,
            Filter(goldAssets, IsGoldAsset),
            Filter(cashAssets, IsCashAsset));
    }

    private static NavigationPage<T> Filter<T>(
        NavigationPage<T> page,
        Func<T, bool> predicate)
        => new(page.Items.Where(predicate).ToArray(), page.NextCursor);

    private static bool IsGoldAccount(AccountNavigationItem account)
        => account.IsActive
           && account.ClosedOn is null
           && account.Type == AccountType.PhysicalVault
           && (account.Institution is null
               || account.Institution.IsActive);

    private static bool IsCashAccount(AccountNavigationItem account)
        => account.IsActive
           && account.ClosedOn is null
           && account.Type is AccountType.Cash
               or AccountType.Investment
               or AccountType.Pension
           && (account.Type is not AccountType.Investment
                   and not AccountType.Pension
               || account.Institution is { IsActive: true });

    private static bool IsCounterparty(InstitutionNavigationItem institution)
        => institution.IsActive
           && institution.Type is InstitutionType.Jeweler
               or InstitutionType.Bank
               or InstitutionType.Other;

    private static bool IsGoldAsset(AssetNavigationItem asset)
        => asset.IsActive
           && asset.BaseCurrencyCode is not null
           && asset.Type == AssetType.PhysicalGold
           && asset.BaseUnit == AssetUnit.GrossGram
           && asset.LotTrackingMode == LotTrackingMode.Required;

    private static bool IsCashAsset(AssetNavigationItem asset)
        => asset.IsActive
           && asset.BaseCurrencyCode is not null
           && asset.Type is AssetType.Cash or AssetType.Currency
           && asset.BaseUnit == AssetUnit.CurrencyUnit
           && asset.LotTrackingMode == LotTrackingMode.None;
}
