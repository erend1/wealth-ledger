using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.PhysicalGold;

internal static class PhysicalGoldCanonicalizer
{
    internal const int MaximumExternalReferenceLength = 256;
    internal const int MaximumNoteLength = 2_000;

    internal static string? NormalizeExternalReference(string? value)
        => NormalizeOptionalText(value, MaximumExternalReferenceLength);

    internal static string? NormalizeNote(string? value)
        => NormalizeOptionalText(value, MaximumNoteLength);

    internal static string? NormalizeHallmark(string? value)
        => NormalizeOptionalText(value, 128);

    internal static string? NormalizeCertificateReference(string? value)
        => NormalizeOptionalText(value, 256);

    internal static string? NormalizeLotNote(string? value)
        => NormalizeOptionalText(value, 1_000);

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
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.SourceTextInvalid,
                "Physical-gold source text is invalid.");
        }

        return normalized;
    }

    internal static IReadOnlyList<PhysicalGoldCostInput> NormalizeCosts(
        IReadOnlyList<PhysicalGoldCostInput>? costs,
        TransactionType activityType,
        CurrencyCode currency)
    {
        if (costs is null || costs.Count == 0)
        {
            return [];
        }

        if (costs.Count
            > PhysicalGoldActivityCostRules.MaxComponentsPerTransaction)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.CostLimitExceeded,
                $"A physical-gold activity may carry at most {PhysicalGoldActivityCostRules.MaxComponentsPerTransaction} cost components.");
        }

        var normalized = new List<PhysicalGoldCostInput>(costs.Count);
        var seen = new HashSet<(
            CostType Type,
            CostTreatment Treatment,
            long Amount,
            string? Note)>();

        foreach (var cost in costs)
        {
            if (cost is null || cost.Amount is null)
            {
                throw PhysicalGoldException.Invalid(
                    PhysicalGoldErrorCodes.CostAmountInvalid,
                    "A physical-gold cost component cannot be null.");
            }

            if (!PhysicalGoldActivityCostRules.IsSupportedCostType(
                    activityType,
                    cost.Type))
            {
                throw PhysicalGoldException.Invalid(
                    PhysicalGoldErrorCodes.CostTypeNotSupported,
                    "The cost type is not supported for this physical-gold activity.");
            }

            if (!PhysicalGoldActivityCostRules.IsSupportedTreatment(
                    activityType,
                    cost.Treatment))
            {
                throw PhysicalGoldException.Invalid(
                    PhysicalGoldErrorCodes.CostTreatmentNotSupported,
                    "The cost treatment is not supported for this physical-gold activity.");
            }

            if (cost.Amount.MinorUnits <= 0)
            {
                throw PhysicalGoldException.Invalid(
                    PhysicalGoldErrorCodes.CostAmountInvalid,
                    "A physical-gold cost amount must be greater than zero.");
            }

            if (cost.Amount.Currency != currency)
            {
                throw PhysicalGoldException.Invalid(
                    PhysicalGoldErrorCodes.CurrencyMismatch,
                    "Every physical-gold cost must use the activity currency.");
            }

            var note = NormalizeLotNote(cost.Note);

            if (cost.Type == CostType.Other && note is null)
            {
                throw PhysicalGoldException.Invalid(
                    PhysicalGoldErrorCodes.CostNoteRequired,
                    "An Other physical-gold cost requires an explanatory note.");
            }

            var item = cost with { Note = note };

            if (!seen.Add(
                    (item.Type,
                     item.Treatment,
                     item.Amount.MinorUnits,
                     item.Note)))
            {
                throw PhysicalGoldException.Invalid(
                    PhysicalGoldErrorCodes.CostDuplicate,
                    "Indistinguishable physical-gold costs must be aggregated or distinguished by a note.");
            }

            normalized.Add(item);
        }

        return OrderCosts(normalized);
    }

    internal static IReadOnlyList<PhysicalGoldCostInput> OrderCosts(
        IReadOnlyList<PhysicalGoldCostInput> costs)
        => costs
            .OrderBy(x => ToCostTypeCode(x.Type), StringComparer.Ordinal)
            .ThenBy(x => ToTreatmentCode(x.Treatment), StringComparer.Ordinal)
            .ThenBy(x => x.Amount.MinorUnits)
            .ThenBy(x => x.Note ?? string.Empty, StringComparer.Ordinal)
            .ToArray();

    internal static IReadOnlyList<PhysicalGoldSelectedLot> OrderSelections(
        IReadOnlyCollection<PhysicalGoldSelectedLot> selections)
        => selections.OrderBy(x => x.AssetLotId).ToArray();

    internal static string ToCostTypeCode(CostType type)
        => type switch
        {
            CostType.MakingCharge => "MAKING_CHARGE",
            CostType.Commission => "COMMISSION",
            CostType.OtherTax => "OTHER_TAX",
            CostType.Insurance => "INSURANCE",
            CostType.Other => "OTHER",
            _ => throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "Unsupported physical-gold cost type.")
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
            _ => throw new ArgumentOutOfRangeException(nameof(treatment))
        };
}

internal static class PhysicalGoldCashConversion
{
    internal static long ToQuantityRawE8(Money amount, int minorUnitDigits)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (minorUnitDigits is < 0 or > 8)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.PrecisionOverflow,
                "Currency minor-unit digits must be between zero and eight.");
        }

        long multiplier = 1;

        for (var digit = minorUnitDigits; digit < 8; digit++)
        {
            multiplier = checked(multiplier * 10);
        }

        try
        {
            return checked(amount.MinorUnits * multiplier);
        }
        catch (OverflowException exception)
        {
            throw new PhysicalGoldException(
                PhysicalGoldErrorCategory.Validation,
                PhysicalGoldErrorCodes.PrecisionOverflow,
                "The amount is too large to represent as a cash quantity.",
                innerException: exception);
        }
    }
}
