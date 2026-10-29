using System.Globalization;
using WealthLedger.Application.Inventory;
using WealthLedger.Application.Navigation;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Lots;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Assets;

/// <summary>One portfolio and account with the positions held there.</summary>
public sealed record PositionGroupDisplay(
    Guid PortfolioId,
    string PortfolioName,
    string PortfolioCode,
    DisplayValue PortfolioStatus,
    Guid AccountId,
    string AccountName,
    string AccountCode,
    DisplayValue AccountType,
    bool AccountIsActive,
    string? InstitutionName,
    string? InstitutionCode,
    bool? InstitutionIsActive,
    IReadOnlyList<PositionDisplay> Positions);

/// <summary>One derived position: a sum, never a stored balance.</summary>
public sealed record PositionDisplay(
    Guid AssetId,
    string AssetName,
    string AssetCode,
    DisplayValue AssetType,
    bool AssetIsActive,
    bool IsLotTracked,
    DisplayValue Quantity,
    long QuantityRawE8,
    int SourceEntryCount,
    int SourceTransactionCount);

public sealed record LotDisplay(
    Guid AssetLotId,
    Guid CreatingTransactionId,
    Guid AssetId,
    string AssetName,
    string AssetCode,
    bool AssetIsActive,
    DisplayValue AcquiredOn,
    DisplayValue CostStatus,
    DisplayValue Cost,
    DisplayValue OriginalQuantity,
    DisplayValue GlobalQuantity,
    bool IsExhausted,
    IReadOnlyList<LotCustodyDisplay> Custody,
    IReadOnlyList<LotAllocationDisplayItem> Allocations,
    LotGoldDisplay? PhysicalGold);

public sealed record LotCustodyDisplay(
    string PortfolioName,
    string AccountName,
    string? InstitutionName,
    bool IsInactive,
    DisplayValue Quantity,
    string? Pieces,
    string? FineWeight);

public sealed record LotAllocationDisplayItem(
    Guid TransactionId,
    DisplayValue TransactionType,
    DisplayValue ExecutionDate,
    string Scope,
    DisplayValue QuantityDelta,
    string? PieceDelta);

public sealed record LotGoldDisplay(
    string Fineness,
    string OriginalPieces,
    string GlobalPieces,
    string GlobalFineWeight,
    string? Hallmark,
    string? CertificateReference);

/// <summary>
/// Formats M010 inventory read models. It never sums or derives a quantity;
/// every number shown here was derived by the Application use case.
/// </summary>
public sealed class InventoryPresenter
{
    private readonly ValuePresenter _values;
    private readonly UiText _text;
    private readonly PresentationCulture _presentationCulture;

    public InventoryPresenter(
        ValuePresenter values,
        UiText text,
        PresentationCulture presentationCulture)
    {
        _values = values ?? throw new ArgumentNullException(nameof(values));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _presentationCulture = presentationCulture
            ?? throw new ArgumentNullException(nameof(presentationCulture));
    }

    private CultureInfo Culture => _presentationCulture.Culture;

    /// <summary>
    /// Groups positions by portfolio and account, keeping the use case's
    /// deterministic order inside and across groups.
    /// </summary>
    internal IReadOnlyList<PositionGroupDisplay> PresentPositions(
        PositionInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        return inventory.Items
            .GroupBy(item => (item.PortfolioId, item.AccountId))
            .Select(group =>
            {
                var first = group.First();

                return new PositionGroupDisplay(
                    first.PortfolioId,
                    first.PortfolioName,
                    first.PortfolioCode,
                    _values.StableCode(first.PortfolioStatus),
                    first.AccountId,
                    first.AccountName,
                    first.AccountCode,
                    _values.StableCode(first.AccountType),
                    first.AccountIsActive,
                    first.InstitutionName,
                    first.InstitutionCode,
                    first.InstitutionIsActive,
                    group.Select(PresentPosition).ToArray());
            })
            .ToArray();
    }

    internal LotDisplay PresentLot(
        LotInventoryItem lot,
        ScopeChoices? choices,
        IReadOnlyDictionary<string, CurrencyNavigationItem> currencies)
    {
        ArgumentNullException.ThrowIfNull(lot);
        ArgumentNullException.ThrowIfNull(currencies);

        var isGold = lot.PhysicalGold is not null;

        return new LotDisplay(
            lot.AssetLotId,
            lot.CreatingTransactionId,
            lot.AssetId,
            lot.AssetName,
            lot.AssetCode,
            lot.AssetIsActive,
            lot.AcquiredOn is null
                ? _values.Unknown()
                : _values.BusinessDate(lot.AcquiredOn.Value),
            _values.StableCode(lot.CostStatus),
            PresentCost(lot, currencies),
            _values.Quantity(
                lot.OriginalQuantityRawE8,
                lot.AssetBaseUnit,
                QuantitySign.Absolute),
            _values.Quantity(
                lot.GlobalQuantityRawE8,
                lot.AssetBaseUnit,
                QuantitySign.Absolute),
            lot.GlobalQuantityRawE8 == 0,
            lot.Custody
                .Select(custody => new LotCustodyDisplay(
                    custody.PortfolioName,
                    custody.AccountName,
                    custody.InstitutionName,
                    !custody.AccountIsActive
                    || custody.InstitutionIsActive == false,
                    _values.Quantity(
                        custody.QuantityRawE8,
                        lot.AssetBaseUnit,
                        QuantitySign.Absolute),
                    isGold && custody.PieceCount is int pieces
                        ? Pieces(pieces)
                        : null,
                    isGold && custody.FineWeightGrams is decimal grams
                        ? FineWeight(grams)
                        : null))
                .ToArray(),
            lot.Allocations
                .Select(allocation => new LotAllocationDisplayItem(
                    allocation.TransactionId,
                    _values.StableCode(allocation.TransactionType),
                    _values.BusinessDate(allocation.ExecutionDate),
                    ScopeLabel(allocation.PortfolioId, allocation.AccountId, choices, lot.Custody),
                    _values.Quantity(
                        allocation.QuantityDeltaRawE8,
                        lot.AssetBaseUnit,
                        QuantitySign.SignedDelta),
                    allocation.PieceDelta is int delta
                        ? SignedPieces(delta)
                        : null))
                .ToArray(),
            lot.PhysicalGold is null
                ? null
                : new LotGoldDisplay(
                    Fineness(lot.PhysicalGold.FinenessPpm),
                    Pieces(lot.PhysicalGold.OriginalPieceCount),
                    Pieces(lot.PhysicalGold.GlobalPieceCount),
                    FineWeight(lot.PhysicalGold.GlobalFineWeightGrams),
                    lot.PhysicalGold.Hallmark,
                    lot.PhysicalGold.CertificateReference));
    }

    internal string Fineness(int partsPerMillion)
        => ExactDecimalText.Format(partsPerMillion / 1_000m, Culture)
           + " ‰ ("
           + partsPerMillion.ToString(Culture)
           + " ppm)";

    internal string FineWeight(decimal grams)
        => ExactDecimalText.Format(grams, Culture)
           + " "
           + _text["Gold_FineGramUnit"];

    internal string Pieces(int count)
        => string.Format(Culture, _text["Assets_PieceCount"], count);

    private string SignedPieces(int delta)
        => string.Format(
            Culture,
            _text["Assets_PieceDelta"],
            delta.ToString("+#;-#;0", Culture));

    private PositionDisplay PresentPosition(PositionInventoryItem item)
        => new(
            item.AssetId,
            item.AssetName,
            item.AssetCode,
            _values.StableCode(item.AssetType),
            item.AssetIsActive,
            item.AssetLotTrackingMode != LotTrackingMode.None,
            _values.Quantity(
                item.QuantityRawE8,
                item.AssetBaseUnit,
                QuantitySign.Absolute),
            item.QuantityRawE8,
            item.SourceEntryCount,
            item.SourceTransactionCount);

    private DisplayValue PresentCost(
        LotInventoryItem lot,
        IReadOnlyDictionary<string, CurrencyNavigationItem> currencies)
        => lot.CostStatus switch
        {
            CostBasisStatus.Unknown => _values.Unknown(),
            CostBasisStatus.NotApplicable => _values.NotApplicable(),
            _ when lot.CostMinorUnits is long amount =>
                _values.Money(
                    amount,
                    lot.CostCurrencyCode,
                    lot.CostCurrencyCode is not null
                    && currencies.TryGetValue(lot.CostCurrencyCode, out var currency)
                        ? currency.MinorUnitDigits
                        : null),
            _ => _values.Unavailable(
                PresentationDiagnostics.IncompleteRecordedValue)
        };

    /// <summary>
    /// Names an allocation's scope from the loaded choices, then from the
    /// lot's own custody rows. An identity is never the visible label; a
    /// scope found in neither is shown as unavailable.
    /// </summary>
    private string ScopeLabel(
        Guid portfolioId,
        Guid accountId,
        ScopeChoices? choices,
        IReadOnlyList<LotCustodyInventoryItem> custody)
    {
        var custodyRow = custody.FirstOrDefault(
            item => item.PortfolioId == portfolioId && item.AccountId == accountId);
        var portfolio = choices?.Portfolios
                            .SingleOrDefault(item => item.PortfolioId == portfolioId)?.Name
                        ?? custodyRow?.PortfolioName;
        var account = choices?.Accounts
                          .SingleOrDefault(item => item.AccountId == accountId)?.Name
                      ?? custodyRow?.AccountName;
        var unavailable = _values.Unavailable(
            PresentationDiagnostics.IncompleteRecordedValue).Text;

        return (portfolio ?? unavailable) + " · " + (account ?? unavailable);
    }
}
