using WealthLedger.Application.Navigation;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages;

public sealed record RecentLedgerTransactionDisplay(
    Guid TransactionId,
    DisplayValue Type,
    DisplayValue Status,
    DisplayValue OrderDate,
    DisplayValue ExecutionDate,
    DisplayValue SettlementDate,
    string? ExternalReference,
    Guid? ReversalOfTransactionId,
    Guid? ReversedByTransactionId,
    DisplayValue CreatedAt,
    DisplayValue PostedAt,
    IReadOnlyList<RecentLedgerEntryEffectDisplay> EntryEffects);

public sealed record RecentLedgerEntryEffectDisplay(
    int Sequence,
    string PortfolioCode,
    string PortfolioName,
    DisplayValue PortfolioStatus,
    string AccountCode,
    string AccountName,
    DisplayValue AccountType,
    bool AccountIsActive,
    string? InstitutionCode,
    string? InstitutionName,
    DisplayValue? InstitutionType,
    bool? InstitutionIsActive,
    string AssetCode,
    string AssetName,
    DisplayValue AssetType,
    DisplayValue AssetUnit,
    bool AssetIsActive,
    DisplayValue Quantity,
    DisplayValue Role);

public sealed class RecentLedgerPresenter
{
    private readonly ValuePresenter _values;

    public RecentLedgerPresenter(ValuePresenter values)
    {
        _values = values ?? throw new ArgumentNullException(nameof(values));
    }

    internal RecentLedgerTransactionDisplay Present(
        RecentLedgerTransactionNavigationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new RecentLedgerTransactionDisplay(
            item.TransactionId,
            _values.StableCode(item.Type),
            _values.StableCode(item.Status),
            OptionalDate(item.OrderDate),
            OptionalDate(item.ExecutionDate),
            OptionalDate(item.SettlementDate),
            item.ExternalReference,
            item.ReversalOfTransactionId,
            item.ReversedByTransactionId,
            _values.UtcTimestamp(item.CreatedAtUtc),
            _values.UtcTimestamp(item.PostedAtUtc),
            item.EntryEffects
                .OrderBy(effect => effect.EntrySequence)
                .Select(PresentEffect)
                .ToArray());
    }

    private RecentLedgerEntryEffectDisplay PresentEffect(
        RecentLedgerEntryEffectNavigationItem effect)
        => new(
            effect.EntrySequence,
            effect.PortfolioCode,
            effect.PortfolioName,
            _values.StableCode(effect.PortfolioStatus),
            effect.AccountCode,
            effect.AccountName,
            _values.StableCode(effect.AccountType),
            effect.AccountIsActive,
            effect.InstitutionCode,
            effect.InstitutionName,
            effect.InstitutionType is null
                ? null
                : _values.StableCode(effect.InstitutionType.Value),
            effect.InstitutionIsActive,
            effect.AssetCode,
            effect.AssetName,
            _values.StableCode(effect.AssetType),
            _values.StableCode(effect.AssetBaseUnit),
            effect.AssetIsActive,
            _values.Quantity(
                effect.QuantityDeltaRawE8,
                effect.AssetBaseUnit,
                QuantitySign.SignedDelta),
            _values.StableCode(effect.Role));

    private DisplayValue OptionalDate(DateOnly? value)
        => value is null
            ? _values.Unknown()
            : _values.BusinessDate(value.Value);
}
