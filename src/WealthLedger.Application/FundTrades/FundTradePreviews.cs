using WealthLedger.Domain.Lots;

namespace WealthLedger.Application.FundTrades;

/// <summary>
/// The reviewable economic effect of a fund purchase, before anything is
/// written.
/// </summary>
public sealed record FundPurchasePreview(
    FundTradeScope Scope,
    string PortfolioName,
    string FundAccountName,
    string CashAccountName,
    string FundAssetCode,
    string FundAssetName,
    string CurrencyCode,
    int MinorUnitDigits,
    DateOnly ExecutionDate,
    DateOnly? OrderDate,
    DateOnly? SettlementDate,
    long FundQuantityRawE8,
    long ExecutedUnitPriceRawE8,
    FundTradeEconomics Economics,
    IReadOnlyList<FundTradeCostInput> Costs,
    string? ExternalReference,
    string? Note,
    IReadOnlyList<string> WarningCodes)
{
    /// <summary>
    /// The exact known cost the created acquisition lot will carry.
    /// </summary>
    public long AcquisitionLotCostMinorUnits
        => Economics.AcquisitionLotCost!.MinorUnits;
}

/// <summary>
/// One lot the deterministic plan would consume, with everything the reviewer
/// needs to judge it.
/// </summary>
public sealed record FundSalePlanLine(
    Guid AssetLotId,
    DateOnly? AcquiredOn,
    long AvailableQuantityRawE8,
    long ConsumedQuantityRawE8,
    CostBasisStatus CostStatus,
    long? LotCostMinorUnits,
    string? LotCostCurrencyCode);

/// <summary>
/// The reviewable economic effect of a fund sale, before anything is written.
/// </summary>
public sealed record FundSalePreview(
    FundTradeScope Scope,
    string PortfolioName,
    string FundAccountName,
    string CashAccountName,
    string FundAssetCode,
    string FundAssetName,
    string CurrencyCode,
    int MinorUnitDigits,
    DateOnly ExecutionDate,
    DateOnly? OrderDate,
    DateOnly? SettlementDate,
    long FundQuantityRawE8,
    long ExecutedUnitPriceRawE8,
    long AvailableQuantityRawE8,
    FundTradeEconomics Economics,
    IReadOnlyList<FundTradeCostInput> Costs,
    IReadOnlyList<FundSalePlanLine> Plan,
    string PlanFingerprint,
    RealizedCostProjection RealizedCost,
    string? ExternalReference,
    string? Note,
    IReadOnlyList<string> WarningCodes);

/// <summary>
/// The realized cost this sale would produce if it were posted now.
/// </summary>
/// <remarks>
/// A projection, not a result: it is derived from history as it stands during
/// review and is recomputed from persisted facts after posting.
/// </remarks>
public sealed record RealizedCostProjection(
    RealizedCostCompleteness Completeness,
    long KnownQuantityRawE8,
    long UnknownQuantityRawE8,
    IReadOnlyList<RealizedCostCurrencyAmount> KnownAmounts,
    string MethodCode);

public sealed record RealizedCostCurrencyAmount(
    string CurrencyCode,
    long MinorUnits);
