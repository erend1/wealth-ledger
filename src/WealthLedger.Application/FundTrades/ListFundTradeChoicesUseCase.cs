using WealthLedger.Application.Navigation;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Portfolios;

namespace WealthLedger.Application.FundTrades;

public sealed record ListFundTradeChoicesQuery(
    Guid HouseholdId,
    int PageSize = 50,
    string? PortfolioCursor = null,
    string? FundAccountCursor = null,
    string? CashAccountCursor = null,
    string? InstitutionCursor = null,
    string? CurrencyCursor = null,
    string? FundAssetCursor = null,
    string? CashAssetCursor = null);

/// <summary>
/// The current references a fund trade can actually use.
/// </summary>
/// <remarks>
/// Fund accounts and cash accounts are listed separately because they accept
/// different account types, and a user should not have to discover that by
/// having a selection rejected after filling in the rest of the form.
/// </remarks>
public sealed record FundTradeChoices(
    HouseholdNavigationItem Household,
    NavigationPage<PortfolioNavigationItem> Portfolios,
    NavigationPage<AccountNavigationItem> FundAccounts,
    NavigationPage<AccountNavigationItem> CashAccounts,
    NavigationPage<InstitutionNavigationItem> Institutions,
    NavigationPage<CurrencyNavigationItem> Currencies,
    NavigationPage<AssetNavigationItem> FundAssets,
    NavigationPage<AssetNavigationItem> CashAssets);

public sealed class ListFundTradeChoicesUseCase
{
    private readonly GetHouseholdUseCase _getHousehold;
    private readonly ListPortfoliosUseCase _listPortfolios;
    private readonly ListAccountsUseCase _listAccounts;
    private readonly ListInstitutionsUseCase _listInstitutions;
    private readonly ListCurrenciesUseCase _listCurrencies;
    private readonly ListAssetsUseCase _listAssets;

    public ListFundTradeChoicesUseCase(
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

    public async Task<FundTradeChoices> ExecuteAsync(
        ListFundTradeChoicesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var household =
            await _getHousehold.ExecuteAsync(
                new GetHouseholdQuery(query.HouseholdId),
                cancellationToken);

        var portfolios =
            await _listPortfolios.ExecuteAsync(
                new ListPortfoliosQuery(
                    query.HouseholdId,
                    query.PageSize,
                    query.PortfolioCursor),
                cancellationToken);

        var fundAccounts =
            await _listAccounts.ExecuteAsync(
                new ListAccountsQuery(
                    query.HouseholdId,
                    query.PageSize,
                    query.FundAccountCursor),
                cancellationToken);

        var cashAccounts =
            query.CashAccountCursor == query.FundAccountCursor
                ? fundAccounts
                : await _listAccounts.ExecuteAsync(
                    new ListAccountsQuery(
                        query.HouseholdId,
                        query.PageSize,
                        query.CashAccountCursor),
                    cancellationToken);

        var institutions =
            await _listInstitutions.ExecuteAsync(
                new ListInstitutionsQuery(
                    query.PageSize,
                    query.InstitutionCursor),
                cancellationToken);

        var currencies =
            await _listCurrencies.ExecuteAsync(
                new ListCurrenciesQuery(
                    query.PageSize,
                    query.CurrencyCursor),
                cancellationToken);

        var fundAssets =
            await _listAssets.ExecuteAsync(
                new ListAssetsQuery(
                    query.PageSize,
                    query.FundAssetCursor),
                cancellationToken);

        var cashAssets =
            query.CashAssetCursor == query.FundAssetCursor
                ? fundAssets
                : await _listAssets.ExecuteAsync(
                    new ListAssetsQuery(
                        query.PageSize,
                        query.CashAssetCursor),
                    cancellationToken);

        return new FundTradeChoices(
            household,
            FilterPage(portfolios, x => x.Status == PortfolioStatus.Active),
            FilterPage(fundAccounts, IsFundAccount),
            FilterPage(cashAccounts, IsCashAccount),
            institutions,
            currencies,
            FilterPage(fundAssets, IsFundAsset),
            FilterPage(cashAssets, IsCashAsset));
    }

    private static NavigationPage<T> FilterPage<T>(
        NavigationPage<T> page,
        Func<T, bool> predicate)
        => new(
            page.Items.Where(predicate).ToArray(),
            page.NextCursor);

    private static bool IsFundAccount(AccountNavigationItem account)
        => account.IsActive
           && account.ClosedOn is null
           && account.Type is AccountType.Investment
               or AccountType.Pension
           && account.Institution is { IsActive: true };

    private static bool IsCashAccount(AccountNavigationItem account)
        => account.IsActive
           && account.ClosedOn is null
           && account.Type is AccountType.Cash
               or AccountType.Investment
               or AccountType.Pension
           && (account.Type is not AccountType.Investment
                   and not AccountType.Pension
               || account.Institution is { IsActive: true });

    private static bool IsFundAsset(AssetNavigationItem asset)
        => asset.IsActive
           && asset.BaseCurrencyCode is not null
           && asset.Type == AssetType.Fund
           && asset.BaseUnit == AssetUnit.FundUnit
           && asset.LotTrackingMode is LotTrackingMode.Optional
               or LotTrackingMode.Required;

    private static bool IsCashAsset(AssetNavigationItem asset)
        => asset.IsActive
           && asset.BaseCurrencyCode is not null
           && asset.Type is AssetType.Cash or AssetType.Currency
           && asset.BaseUnit == AssetUnit.CurrencyUnit
           && asset.LotTrackingMode == LotTrackingMode.None;
}
