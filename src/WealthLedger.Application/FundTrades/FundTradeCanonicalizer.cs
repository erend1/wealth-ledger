using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.FundTrades;

/// <summary>
/// Normalizes fund-trade source text and cost components into one canonical
/// form, so equivalent submissions produce equivalent fingerprints.
/// </summary>
internal static class FundTradeCanonicalizer
{
    internal const int MaximumExternalReferenceLength = 256;

    internal const int MaximumNoteLength = 2_000;

    /// <summary>
    /// Orders costs so request order cannot change the canonical form, and
    /// rejects components that are economically indistinguishable.
    /// </summary>
    /// <remarks>
    /// Two identical charges on one trade are almost always one aggregate
    /// charge entered twice. Requiring the user to aggregate them, or to tell
    /// them apart with a meaningful note, keeps the receipt explainable.
    /// </remarks>
    internal static IReadOnlyList<FundTradeCostInput> NormalizeCosts(
        IReadOnlyList<FundTradeCostInput>? costs,
        TransactionType tradeType,
        CurrencyCode tradeCurrency)
    {
        if (costs is null || costs.Count == 0)
        {
            return [];
        }

        if (costs.Count
            > FundTradeCostRules.MaxComponentsPerTransaction)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.CostLimitExceeded,
                $"A fund trade may carry at most "
                + $"{FundTradeCostRules.MaxComponentsPerTransaction} cost components.");
        }

        var normalized =
            new List<FundTradeCostInput>(costs.Count);

        var seen =
            new HashSet<(CostType, CostTreatment, long, string?)>();

        foreach (var cost in costs)
        {
            if (cost is null)
            {
                throw FundTradeException.Invalid(
                    FundTradeErrorCodes.CostAmountInvalid,
                    "A fund-trade cost component cannot be null.");
            }

            ArgumentNullException.ThrowIfNull(cost.Amount);

            if (!FundTradeCostRules.IsSupportedCostType(cost.Type))
            {
                throw FundTradeException.Invalid(
                    FundTradeErrorCodes.CostTypeNotSupported,
                    $"Cost type '{cost.Type}' is not supported for a fund trade.");
            }

            if (!FundTradeCostRules.IsSupportedTreatment(
                    tradeType,
                    cost.Treatment))
            {
                throw FundTradeException.Invalid(
                    FundTradeErrorCodes.CostTreatmentNotSupported,
                    $"Cost treatment '{cost.Treatment}' is not supported for this trade direction.");
            }

            /*
             * A zero component explains nothing and would still occupy a
             * receipt line, so it is rejected rather than persisted as a
             * placeholder.
             */
            if (cost.Amount.MinorUnits <= 0)
            {
                throw FundTradeException.Invalid(
                    FundTradeErrorCodes.CostAmountInvalid,
                    "A fund-trade cost amount must be greater than zero.");
            }

            if (cost.Amount.Currency != tradeCurrency)
            {
                throw FundTradeException.Invalid(
                    FundTradeErrorCodes.CurrencyMismatch,
                    "Every fund-trade cost must use the trade currency.");
            }

            var note =
                NormalizeOptionalText(
                    cost.Note,
                    FundTradeCostRules.MaxComponentNoteLength);

            var candidate =
                cost with { Note = note };

            if (!seen.Add(
                    (candidate.Type,
                     candidate.Treatment,
                     candidate.Amount.MinorUnits,
                     candidate.Note)))
            {
                throw FundTradeException.Invalid(
                    FundTradeErrorCodes.CostDuplicate,
                    "Indistinguishable cost components must be aggregated or distinguished by a note.");
            }

            normalized.Add(candidate);
        }

        return Order(normalized);
    }

    /// <summary>
    /// Produces the deterministic canonical order used for fingerprints,
    /// review and receipts.
    /// </summary>
    internal static IReadOnlyList<FundTradeCostInput> Order(
        IReadOnlyList<FundTradeCostInput> costs)
        => costs
            .OrderBy(x => ToCostTypeCode(x.Type), StringComparer.Ordinal)
            .ThenBy(x => ToTreatmentCode(x.Treatment), StringComparer.Ordinal)
            .ThenBy(x => x.Amount.MinorUnits)
            .ThenBy(x => x.Note ?? string.Empty, StringComparer.Ordinal)
            .ToArray();

    internal static string ToCostTypeCode(CostType type)
        => type switch
        {
            CostType.Commission => "COMMISSION",
            CostType.Brokerage => "BROKERAGE",
            CostType.WithholdingTax => "WITHHOLDING_TAX",
            CostType.OtherTax => "OTHER_TAX",
            CostType.Other => "OTHER",
            CostType.MakingCharge => "MAKING_CHARGE",
            CostType.TitleDeed => "TITLE_DEED",
            CostType.Expertise => "EXPERTISE",
            CostType.Notary => "NOTARY",
            CostType.Insurance => "INSURANCE",
            _ => throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "Unsupported cost type.")
        };

    internal static string ToTreatmentCode(CostTreatment treatment)
        => treatment switch
        {
            CostTreatment.AdditionalCashOutflow =>
                "ADDITIONAL_CASH_OUTFLOW",
            CostTreatment.WithheldFromProceeds =>
                "WITHHELD_FROM_PROCEEDS",
            CostTreatment.IncludedInConsideration =>
                "INCLUDED_IN_CONSIDERATION",
            CostTreatment.InformationalOnly =>
                "INFORMATIONAL_ONLY",
            _ => throw new ArgumentOutOfRangeException(
                nameof(treatment),
                treatment,
                "Unsupported cost treatment.")
        };

    /// <summary>
    /// Applies the minimum-provenance rule.
    /// </summary>
    /// <remarks>
    /// Decision 14 takes precedence over version-1 transport compatibility:
    /// a new submission that names neither a reference nor a note cannot be
    /// explained later, so it is rejected even in the legacy request shape.
    /// Receipts written before this rule existed still replay.
    /// </remarks>
    internal static void EnsureProvenance(
        string? externalReference,
        string? note)
    {
        if (string.IsNullOrEmpty(externalReference)
            && string.IsNullOrEmpty(note))
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.ProvenanceRequired,
                "A fund trade requires an external reference or a note.");
        }
    }

    internal static string? NormalizeExternalReference(string? value)
        => NormalizeOptionalText(
            value,
            MaximumExternalReferenceLength);

    internal static string? NormalizeNote(string? value)
        => NormalizeOptionalText(
            value,
            MaximumNoteLength);

    internal static string? NormalizeOptionalText(
        string? value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();

        if (normalized.Length > maximumLength
            || normalized.Any(char.IsControl))
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.SourceTextInvalid,
                "Fund-trade source text is invalid.");
        }

        return normalized;
    }
}
