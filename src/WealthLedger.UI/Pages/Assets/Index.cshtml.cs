using Microsoft.AspNetCore.Mvc;
using WealthLedger.Application.Inventory;
using WealthLedger.Application.LocalData;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Assets;

/// <summary>
/// Derived position inventory, current or as of a financial date.
/// </summary>
/// <remarks>
/// Every quantity is summed by the Application use case from effective
/// Posted entries. The page shows no market value, price or performance,
/// and links each position to the transactions and lots that explain it.
/// </remarks>
[LocalStartupPage(
    supportsPost: false,
    LocalStartupMode.Ready)]
public sealed class IndexModel : InventoryPageModel
{
    private readonly ListPositionInventoryUseCase _listPositions;
    private readonly InventoryPresenter _presenter;

    public IndexModel(
        ReadyHouseholdResolver householdResolver,
        ScopeChoiceLoader choiceLoader,
        ListPositionInventoryUseCase listPositions,
        InventoryPresenter presenter,
        UiText text)
        : base(householdResolver, choiceLoader, text)
    {
        _listPositions = listPositions
            ?? throw new ArgumentNullException(nameof(listPositions));
        _presenter = presenter
            ?? throw new ArgumentNullException(nameof(presenter));
    }

    public IReadOnlyList<PositionGroupDisplay> Groups { get; private set; } = [];

    public bool IncludeZero => Filters.IncludeZero?.Trim() == InventoryFilterMapper.CheckedValue;

    /// <summary>
    /// The Ledger search over one position's exact scope, bounded by the
    /// as-of date when one is set, so the listed transactions are exactly
    /// the history the quantity was derived from.
    /// </summary>
    public string? TransactionsUrl(PositionGroupDisplay group, PositionDisplay position)
        => Url.Page(
            "/Ledger/Index",
            new
            {
                portfolioId = group.PortfolioId.ToString("D"),
                accountId = group.AccountId.ToString("D"),
                assetId = position.AssetId.ToString("D"),
                executedTo = AsOfDate?.ToString("yyyy-MM-dd")
            });

    public string? LotsUrl(PositionGroupDisplay group, PositionDisplay position)
        => Url.Page(
            "/Assets/Lots",
            new
            {
                portfolioId = group.PortfolioId.ToString("D"),
                accountId = group.AccountId.ToString("D"),
                assetId = position.AssetId.ToString("D"),
                asOf = AsOfDate?.ToString("yyyy-MM-dd")
            });

    public async Task<IActionResult> OnGetAsync(
        [FromQuery] InventoryFilterForm filters,
        CancellationToken cancellationToken)
    {
        Filters = filters ?? new InventoryFilterForm();

        if (!await PrepareAsync(cancellationToken))
        {
            return Page();
        }

        if (!InventoryFilterMapper.TryMapPositions(
                Filters,
                HouseholdId,
                out var query,
                out var errors))
        {
            AddFilterErrors(errors);
            return Page();
        }

        AsOfDate = query!.AsOf;

        try
        {
            var inventory = await _listPositions.ExecuteAsync(query, cancellationToken);
            Groups = _presenter.PresentPositions(inventory);
        }
        catch (Exception exception) when (HandleInventoryFailure(exception))
        {
        }

        return Page();
    }
}
