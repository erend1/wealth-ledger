using System.Globalization;
using WealthLedger.Application.Evidence;
using WealthLedger.Application.Navigation;

namespace WealthLedger.UI.Pages.Reconcile;

public sealed class ObservationForm
{
    public string? PortfolioId { get; set; }

    public string? AccountId { get; set; }

    public string? AssetId { get; set; }

    public string? ObservedQuantity { get; set; }

    public string? Tolerance { get; set; }

    public string? ObservedPieceCount { get; set; }
}

public sealed record ObservationFormError(string Field, string ResourceKey);

/// <summary>
/// Maps one observed quantity from a statement or count onto the exact
/// raw-E8 input the reconciliation use case compares.
/// </summary>
/// <remarks>
/// Quantities are typed in the asset's own unit with at most eight decimal
/// places, using a comma or a point as the decimal separator and no
/// thousands separator, like the other M007-M009 forms. A review step shows
/// the exact parsed value before anything is recorded. The observed
/// quantity is signed; the tolerance never is.
/// </remarks>
public static class ObservationFormMapper
{
    private const decimal RawE8Scale = 100_000_000m;

    public static bool TryMap(
        ObservationForm form,
        ScopeChoices choices,
        out EvidenceObservationInput? input,
        out AssetNavigationItem? asset,
        out IReadOnlyList<ObservationFormError> errors)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(choices);

        var found = new List<ObservationFormError>();
        var portfolioId = ParseSelection(
            form.PortfolioId,
            nameof(form.PortfolioId),
            choices.Portfolios.Select(item => item.PortfolioId),
            found);
        var accountId = ParseSelection(
            form.AccountId,
            nameof(form.AccountId),
            choices.Accounts.Select(item => item.AccountId),
            found);
        var assetId = ParseSelection(
            form.AssetId,
            nameof(form.AssetId),
            choices.Assets.Select(item => item.AssetId),
            found);
        asset = assetId is null ? null : choices.FindAsset(assetId.Value);

        long? observed = null;

        if (!TryParseRawE8(form.ObservedQuantity, allowNegative: true, out var observedRaw))
        {
            found.Add(new ObservationFormError(
                nameof(form.ObservedQuantity),
                "Observation_Validation_Quantity"));
        }
        else
        {
            observed = observedRaw;
        }

        long tolerance = 0;

        if (!string.IsNullOrWhiteSpace(form.Tolerance)
            && !TryParseRawE8(form.Tolerance, allowNegative: false, out tolerance))
        {
            found.Add(new ObservationFormError(
                nameof(form.Tolerance),
                "Observation_Validation_Tolerance"));
        }

        int? pieces = null;

        if (!string.IsNullOrWhiteSpace(form.ObservedPieceCount))
        {
            if (!int.TryParse(
                    form.ObservedPieceCount.Trim(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var parsedPieces))
            {
                found.Add(new ObservationFormError(
                    nameof(form.ObservedPieceCount),
                    "Observation_Validation_Pieces"));
            }
            else if (asset is not null
                     && !ReconciliationPresenter.TracksPieces(asset.Type))
            {
                found.Add(new ObservationFormError(
                    nameof(form.ObservedPieceCount),
                    "Observation_Validation_PiecesGoldOnly"));
            }
            else
            {
                pieces = parsedPieces;
            }
        }

        errors = found;

        if (found.Count > 0)
        {
            input = null;
            return false;
        }

        input = new EvidenceObservationInput(
            portfolioId!.Value,
            accountId!.Value,
            assetId!.Value,
            observed!.Value,
            tolerance,
            pieces);
        return true;
    }

    /// <summary>
    /// Parses an exact decimal in the asset's unit into signed raw E8 with
    /// checked arithmetic. More than eight decimal places is refused, never
    /// rounded.
    /// </summary>
    public static bool TryParseRawE8(
        string? value,
        bool allowNegative,
        out long rawE8)
    {
        rawE8 = 0;
        var normalized = value?.Trim();

        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Contains('e', StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(' ')
            || (normalized.Contains('.') && normalized.Contains(',')))
        {
            return false;
        }

        if (!decimal.TryParse(
                normalized.Replace(',', '.'),
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var parsed)
            || (!allowNegative && parsed < 0))
        {
            return false;
        }

        try
        {
            var scaled = checked(parsed * RawE8Scale);

            if (decimal.Truncate(scaled) != scaled)
            {
                return false;
            }

            rawE8 = checked((long)scaled);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static Guid? ParseSelection(
        string? value,
        string field,
        IEnumerable<Guid> allowed,
        List<ObservationFormError> errors)
    {
        if (Guid.TryParseExact(value?.Trim(), "D", out var parsed)
            && allowed.Contains(parsed))
        {
            return parsed;
        }

        errors.Add(new ObservationFormError(field, "Observation_Validation_Selection"));
        return null;
    }
}
