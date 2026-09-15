using WealthLedger.Domain.Lots;

namespace WealthLedger.UI.Presentation;

/// <summary>
/// Stable codes for the fund-trade states a page must render exactly.
/// </summary>
/// <remarks>
/// Cost knowledge and realized-cost completeness are shown as codes rather
/// than prose because their states must stay distinguishable: known zero,
/// unknown, and not applicable mean different things and must never read as
/// the same thing.
/// </remarks>
public static class FundTradeDisplayCodes
{
    public static string CostStatus(CostBasisStatus status)
        => status switch
        {
            CostBasisStatus.Known => "KNOWN",
            CostBasisStatus.Unknown => "UNKNOWN",
            _ => "NOT_APPLICABLE"
        };

    public static string Completeness(
        RealizedCostCompleteness completeness)
        => completeness switch
        {
            RealizedCostCompleteness.CompleteKnown => "COMPLETE_KNOWN",
            RealizedCostCompleteness.PartiallyKnown => "PARTIALLY_KNOWN",
            _ => "UNKNOWN"
        };
}
