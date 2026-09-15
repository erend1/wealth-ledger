using WealthLedger.Domain.Ledger;

namespace WealthLedger.Application.FundTrades;

/// <summary>
/// The stable text codes for fund-trade vocabulary.
/// </summary>
/// <remarks>
/// One source of truth for canonical ordering, fingerprints, transport and
/// persistence. Codes are explicit strings rather than CLR enum names, so
/// renaming a member cannot silently change a stored or transmitted value.
/// </remarks>
public static class FundTradeCodes
{
    public static string ToCostTypeCode(CostType type)
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
                "Unsupported cost treatment.")
        };

    /// <summary>
    /// Parses a cost-type code, refusing anything outside the fund subset.
    /// </summary>
    /// <remarks>
    /// Parsing rejects the physical-gold and property vocabulary here rather
    /// than deeper in validation, so an unsupported code produces a precise
    /// message instead of a generic one.
    /// </remarks>
    public static CostType ParseCostType(string code)
        => code switch
        {
            "COMMISSION" => CostType.Commission,
            "BROKERAGE" => CostType.Brokerage,
            "WITHHOLDING_TAX" => CostType.WithholdingTax,
            "OTHER_TAX" => CostType.OtherTax,
            "OTHER" => CostType.Other,
            _ => throw FundTradeException.Invalid(
                FundTradeErrorCodes.CostTypeNotSupported,
                "The cost type is not supported for a fund trade.")
        };

    public static CostTreatment ParseTreatment(string code)
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
            _ => throw FundTradeException.Invalid(
                FundTradeErrorCodes.CostTreatmentNotSupported,
                "The cost treatment is not recognized.")
        };
}
