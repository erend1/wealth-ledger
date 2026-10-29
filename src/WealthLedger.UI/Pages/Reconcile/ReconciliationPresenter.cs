using System.Globalization;
using WealthLedger.Application.Evidence;
using WealthLedger.Application.Reconciliation;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Reconciliation;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Reconcile;

/// <summary>
/// One visible state: its text, its stable code, and the badge modifier
/// that keeps it distinct without relying on colour.
/// </summary>
public sealed record StatusBadge(string Text, string Code, string Modifier);

public sealed record ObservationDisplay(
    Guid ObservationId,
    string Scope,
    string AssetName,
    DisplayValue Observed,
    DisplayValue Tolerance,
    string? Pieces,
    DisplayValue RecordedAt);

public sealed record RunSummaryDisplay(
    Guid RunId,
    Guid EvidenceId,
    DisplayValue EvidenceDate,
    DisplayValue RunAt,
    int ItemCount,
    int MatchedCount,
    int OutOfToleranceCount,
    int OpenCount,
    int CorrectedCount,
    int ExceptionCount);

public sealed record RunDetailDisplay(
    Guid RunId,
    Guid EvidenceId,
    DisplayValue EvidenceDate,
    DisplayValue RunAt,
    DisplayValue Method,
    int MethodVersion,
    bool IsStale,
    IReadOnlyList<RunItemDisplay> Items);

public sealed record RunItemDisplay(
    Guid ItemId,
    int Sequence,
    Guid PortfolioId,
    Guid AccountId,
    Guid AssetId,
    string Scope,
    string AssetName,
    DisplayValue Observed,
    DisplayValue Ledger,
    DisplayValue Delta,
    DisplayValue Tolerance,
    string? ObservedPieces,
    string? LedgerPieces,
    string? PieceDelta,
    StatusBadge Comparison,
    StatusBadge? Resolution,
    StatusBadge? Staleness,
    DisplayValue? CurrentLedger,
    string? CurrentLedgerPieces,
    Guid? CorrectiveTransactionId,
    string? Explanation,
    DisplayValue? ResolvedAt,
    long ObservedRawE8,
    long LedgerRawE8,
    long DeltaRawE8,
    long ToleranceRawE8)
{
    /// <summary>
    /// Only an out-of-tolerance item that is still open takes a resolution.
    /// </summary>
    public bool CanResolve
        => Comparison.Code == "OUT_OF_TOLERANCE"
           && Resolution?.Code == "OPEN";
}

/// <summary>
/// Formats observations and reconciliation runs. Every quantity, delta and
/// staleness flag was computed by the Application use cases; this class only
/// renders them and never compares numbers itself.
/// </summary>
public sealed class ReconciliationPresenter
{
    private readonly ValuePresenter _values;
    private readonly UiText _text;
    private readonly PresentationCulture _presentationCulture;

    public ReconciliationPresenter(
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

    public StatusBadge Badge(ReconciliationComparisonStatus status)
    {
        var text = _values.StableCode(status);

        return new StatusBadge(
            text.Text,
            text.TechnicalDetail!,
            status == ReconciliationComparisonStatus.Matched
                ? "matched"
                : "out-of-tolerance");
    }

    public StatusBadge Badge(ReconciliationResolutionState state)
    {
        var text = _values.StableCode(state);

        return new StatusBadge(
            text.Text,
            text.TechnicalDetail!,
            state switch
            {
                ReconciliationResolutionState.Open => "open",
                ReconciliationResolutionState.CorrectedByTransaction => "corrected",
                _ => "exception"
            });
    }

    public StatusBadge StaleBadge()
        => new(_text["Reconciliation_StaleBadge"], "STALE", "stale");

    internal ObservationDisplay PresentObservation(EvidenceObservationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new ObservationDisplay(
            item.ObservationId,
            ScopeText(item.Scope),
            item.Scope.AssetName,
            _values.Quantity(
                item.ObservedQuantityRawE8,
                item.Scope.AssetBaseUnit,
                QuantitySign.Absolute),
            _values.Quantity(
                item.ToleranceRawE8,
                item.Scope.AssetBaseUnit,
                QuantitySign.Absolute),
            item.ObservedPieceCount is int pieces ? Pieces(pieces) : null,
            _values.UtcTimestamp(item.RecordedAtUtc));
    }

    internal RunSummaryDisplay PresentSummary(ReconciliationRunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new RunSummaryDisplay(
            summary.ReconciliationRunId,
            summary.EvidenceId,
            _values.BusinessDate(summary.EvidenceEffectiveDate),
            _values.UtcTimestamp(summary.RunAtUtc),
            summary.ItemCount,
            summary.MatchedCount,
            summary.OutOfToleranceCount,
            summary.OpenCount,
            summary.CorrectedByTransactionCount,
            summary.DocumentedExceptionCount);
    }

    internal RunDetailDisplay PresentDetail(ReconciliationRunDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return new RunDetailDisplay(
            detail.ReconciliationRunId,
            detail.EvidenceId,
            _values.BusinessDate(detail.EvidenceEffectiveDate),
            _values.UtcTimestamp(detail.RunAtUtc),
            _values.StableCode(detail.Method),
            detail.MethodVersion,
            detail.IsStale,
            detail.Items
                .OrderBy(item => item.Sequence)
                .Select(PresentItem)
                .ToArray());
    }

    internal string Pieces(int count)
        => string.Format(Culture, _text["Assets_PieceCount"], count);

    internal static string ScopeText(EvidenceObservationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var parts = new List<string> { scope.PortfolioName, scope.AccountName };

        if (!string.IsNullOrWhiteSpace(scope.InstitutionName))
        {
            parts.Add(scope.InstitutionName);
        }

        return string.Join(" · ", parts);
    }

    private RunItemDisplay PresentItem(ReconciliationItemDetail item)
    {
        var unit = item.Scope.AssetBaseUnit;

        return new RunItemDisplay(
            item.ReconciliationItemId,
            item.Sequence,
            item.Scope.PortfolioId,
            item.Scope.AccountId,
            item.Scope.AssetId,
            ScopeText(item.Scope),
            item.Scope.AssetName,
            _values.Quantity(item.ObservedQuantityRawE8, unit, QuantitySign.Absolute),
            _values.Quantity(item.LedgerQuantityRawE8, unit, QuantitySign.Absolute),
            _values.Quantity(item.DeltaRawE8, unit, QuantitySign.SignedDelta),
            _values.Quantity(item.ToleranceRawE8, unit, QuantitySign.Absolute),
            item.ObservedPieceCount is int observed ? Pieces(observed) : null,
            item.LedgerPieceCount is int ledger ? Pieces(ledger) : null,
            item.PieceDelta is int delta
                ? string.Format(
                    Culture,
                    _text["Assets_PieceDelta"],
                    delta.ToString("+#;-#;0", Culture))
                : null,
            Badge(item.ComparisonStatus),
            item.ResolutionState is { } state ? Badge(state) : null,
            item.IsStale ? StaleBadge() : null,
            item.IsStale
                ? _values.Quantity(item.CurrentLedgerQuantityRawE8, unit, QuantitySign.Absolute)
                : null,
            item.IsStale && item.CurrentLedgerPieceCount is int current
                ? Pieces(current)
                : null,
            item.CorrectiveTransactionId,
            item.Explanation,
            item.ResolvedAtUtc is { } resolvedAt
                ? _values.UtcTimestamp(resolvedAt)
                : null,
            item.ObservedQuantityRawE8,
            item.LedgerQuantityRawE8,
            item.DeltaRawE8,
            item.ToleranceRawE8);
    }

    /// <summary>
    /// Whether the asset carries whole-piece evidence, so a page offers a
    /// piece count only where the use case accepts one.
    /// </summary>
    internal static bool TracksPieces(AssetType assetType)
        => assetType == AssetType.PhysicalGold;
}
