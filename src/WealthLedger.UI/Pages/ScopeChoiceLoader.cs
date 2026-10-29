using WealthLedger.Application.Navigation;

namespace WealthLedger.UI.Pages;

/// <summary>
/// The current master identities a reviewer can name as an exact scope.
/// </summary>
/// <remarks>
/// Inactive and archived masters are included on purpose: search, inventory
/// and evidence describe history, and history may name a master that is no
/// longer in use. Labels are current display context, not historical names.
/// </remarks>
public sealed record ScopeChoices(
    IReadOnlyList<PortfolioNavigationItem> Portfolios,
    IReadOnlyList<AccountNavigationItem> Accounts,
    IReadOnlyList<InstitutionNavigationItem> Institutions,
    IReadOnlyList<AssetNavigationItem> Assets)
{
    public AssetNavigationItem? FindAsset(Guid assetId)
        => Assets.SingleOrDefault(asset => asset.AssetId == assetId);
}

/// <summary>
/// One option of a scope select list.
/// </summary>
public sealed record ScopeOption(string Value, string Label, bool IsActive);

/// <summary>
/// Everything the shared scope select partial needs to render one control.
/// </summary>
public sealed record ScopeSelect(
    string Name,
    string Label,
    string EmptyOptionLabel,
    string InactiveMarker,
    IReadOnlyList<ScopeOption> Options,
    string? Selected,
    string? Help = null,
    string? Id = null);

/// <summary>
/// Loads bounded scope choices through the existing M005 navigation use
/// cases. It never reads storage directly.
/// </summary>
public sealed class ScopeChoiceLoader
{
    private const int ChoicePageSize = 100;

    private readonly ListPortfoliosUseCase _listPortfolios;
    private readonly ListAccountsUseCase _listAccounts;
    private readonly ListInstitutionsUseCase _listInstitutions;
    private readonly ListAssetsUseCase _listAssets;

    public ScopeChoiceLoader(
        ListPortfoliosUseCase listPortfolios,
        ListAccountsUseCase listAccounts,
        ListInstitutionsUseCase listInstitutions,
        ListAssetsUseCase listAssets)
    {
        _listPortfolios = listPortfolios
            ?? throw new ArgumentNullException(nameof(listPortfolios));
        _listAccounts = listAccounts
            ?? throw new ArgumentNullException(nameof(listAccounts));
        _listInstitutions = listInstitutions
            ?? throw new ArgumentNullException(nameof(listInstitutions));
        _listAssets = listAssets
            ?? throw new ArgumentNullException(nameof(listAssets));
    }

    internal async Task<ScopeChoices> LoadAsync(
        Guid householdId,
        CancellationToken cancellationToken)
    {
        var portfolios = await _listPortfolios.ExecuteAsync(
            new ListPortfoliosQuery(
                householdId,
                ChoicePageSize,
                IncludeInactive: true),
            cancellationToken);
        var accounts = await _listAccounts.ExecuteAsync(
            new ListAccountsQuery(
                householdId,
                ChoicePageSize,
                IncludeInactive: true),
            cancellationToken);
        var institutions = await _listInstitutions.ExecuteAsync(
            new ListInstitutionsQuery(
                ChoicePageSize,
                IncludeInactive: true),
            cancellationToken);
        var assets = await _listAssets.ExecuteAsync(
            new ListAssetsQuery(
                ChoicePageSize,
                IncludeInactive: true),
            cancellationToken);

        return new ScopeChoices(
            portfolios.Items,
            accounts.Items,
            institutions.Items,
            assets.Items);
    }

    internal static IReadOnlyList<ScopeOption> PortfolioOptions(
        ScopeChoices choices)
        => choices.Portfolios
            .Select(
                portfolio => new ScopeOption(
                    portfolio.PortfolioId.ToString("D"),
                    $"{portfolio.Name} ({portfolio.Code})",
                    portfolio.Status
                        == Domain.Portfolios.PortfolioStatus.Active))
            .ToArray();

    internal static IReadOnlyList<ScopeOption> AccountOptions(
        ScopeChoices choices)
        => choices.Accounts
            .Select(
                account => new ScopeOption(
                    account.AccountId.ToString("D"),
                    account.Institution is null
                        ? $"{account.Name} ({account.Code})"
                        : $"{account.Name} ({account.Code}) · {account.Institution.Name}",
                    account.IsActive))
            .ToArray();

    internal static IReadOnlyList<ScopeOption> InstitutionOptions(
        ScopeChoices choices)
        => choices.Institutions
            .Select(
                institution => new ScopeOption(
                    institution.InstitutionId.ToString("D"),
                    $"{institution.Name} ({institution.Code})",
                    institution.IsActive))
            .ToArray();

    internal static IReadOnlyList<ScopeOption> AssetOptions(
        ScopeChoices choices)
        => choices.Assets
            .Select(
                asset => new ScopeOption(
                    asset.AssetId.ToString("D"),
                    $"{asset.Name} ({asset.Code})",
                    asset.IsActive))
            .ToArray();
}
