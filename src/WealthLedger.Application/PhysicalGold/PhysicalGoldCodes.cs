using WealthLedger.Domain.Ledger;

namespace WealthLedger.Application.PhysicalGold;

/// <summary>
/// Stable external codes for the bounded physical-gold vocabulary.
/// </summary>
public static class PhysicalGoldCodes
{
    public static string ToCostTypeCode(CostType type)
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

    public static CostType ParseCostType(string? code)
        => code switch
        {
            "MAKING_CHARGE" => CostType.MakingCharge,
            "COMMISSION" => CostType.Commission,
            "OTHER_TAX" => CostType.OtherTax,
            "INSURANCE" => CostType.Insurance,
            "OTHER" => CostType.Other,
            _ => throw new PhysicalGoldException(
                PhysicalGoldErrorCategory.Validation,
                PhysicalGoldErrorCodes.CostTypeNotSupported,
                "The cost type is not supported for physical gold.")
        };

    public static string ToTreatmentCode(CostTreatment treatment)
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
                "Unsupported physical-gold cost treatment.")
        };

    public static CostTreatment ParseTreatment(string? code)
        => code switch
        {
            "ADDITIONAL_CASH_OUTFLOW" =>
                CostTreatment.AdditionalCashOutflow,
            "WITHHELD_FROM_PROCEEDS" =>
                CostTreatment.WithheldFromProceeds,
            "INCLUDED_IN_CONSIDERATION" =>
                CostTreatment.IncludedInConsideration,
            "INFORMATIONAL_ONLY" =>
                CostTreatment.InformationalOnly,
            _ => throw new PhysicalGoldException(
                PhysicalGoldErrorCategory.Validation,
                PhysicalGoldErrorCodes.CostTreatmentNotSupported,
                "The cost treatment is not recognized for physical gold.")
        };
}
