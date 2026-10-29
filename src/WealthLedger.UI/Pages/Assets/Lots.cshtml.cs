using Microsoft.AspNetCore.Mvc;
using WealthLedger.Application.Inventory;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Navigation;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Assets;

/// <summary>
/// Acquisition lots with derived global and custody quantities.
/// </summary>
/// <remarks>
/// A lot is acquisition lineage, not custody: its custody rows and current
/// quantities are derived from signed allocations on every read. Physical
/// gold keeps gross weight, whole pieces and immutable fineness separate.
/// </remarks>
[LocalStartupPage(
    supportsPost: false,
    LocalStartupMode.Ready)]
public sealed class LotsModel : InventoryPageModel
{
    private const int CurrencyPageSize = 100;

    private readonly ListLotInventoryUseCase _listLots;
    private readonly ListCurrenciesUseCase _listCurrencies;
    private readonly InventoryPresenter _presenter;

    public LotsModel(
        ReadyHouseholdResolver householdResolver,
        ScopeChoiceLoader choiceLoader,
        ListLotInventoryUseCase listLots,
        ListCurrenciesUseCase listCurrencies,
        InventoryPresenter presenter,
        UiText text)
        : base(householdResolver, choiceLoader, text)
    {
        _listLots = listLots
            ?? throw new ArgumentNullException(nameof(listLots));
        _listCurrencies = listCurrencies
            ?? throw new ArgumentNullException(nameof(listCurrencies));
        _presenter = presenter
            ?? throw new ArgumentNullException(nameof(presenter));
    }

    public IReadOnlyList<LotDisplay> Lots { get; private set; } = [];

    public string? NextPageUrl { get; private set; }

    public bool IncludeExhausted
        => Filters.IncludeExhausted?.Trim() == InventoryFilterMapper.CheckedValue;

    public async Task<IActionResult> OnGetAsync(
        [FromQuery] InventoryFilterForm filters,
        CancellationToken cancellationToken)
    {
        Filters = filters ?? new InventoryFilterForm();

        if (!await PrepareAsync(cancellationToken))
        {
            return Page();
        }

        if (!InventoryFilterMapper.TryParseLotPageSize(Filters.PageSize, out var pageSize))
        {
            InvalidNavigation();
            return Page();
        }

        if (!InventoryFilterMapper.TryMapLots(
                Filters,
                HouseholdId,
                pageSize,
                out var query,
                out var errors))
        {
            AddFilterErrors(errors);
            return Page();
        }

        AsOfDate = query!.AsOf;

        try
        {
            var currencies = await _listCurrencies.ExecuteAsync(
                new ListCurrenciesQuery(CurrencyPageSize),
                cancellationToken);
            var lookup = currencies.Items.ToDictionary(
                currency => currency.Code,
                StringComparer.Ordinal);
            var inventory = await _listLots.ExecuteAsync(query, cancellationToken);

            Lots = inventory.Items
                .Select(lot => _presenter.PresentLot(lot, Choices, lookup))
                .ToArray();
            NextPageUrl = inventory.NextCursor is null
                ? null
                : Url.Page("/Assets/Lots")
                  + InventoryFilterMapper.NextPageQuery(
                      Filters,
                      pageSize,
                      inventory.NextCursor);
        }
        catch (Exception exception) when (HandleInventoryFailure(exception))
        {
        }
        catch (Exception exception)
            when (exception is NavigationRequestException
                  or NavigationPersistenceException)
        {
            Unavailable();
        }

        return Page();
    }
}
