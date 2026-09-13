namespace WealthLedger.Api.Contracts;

/*
 * Fund-trade transport.
 *
 * Quantities and prices travel as raw E8 integers, money as integer minor
 * units, dates as ISO strings and vocabulary as stable explicit codes. The API
 * never accepts a formatted display string as authority, and never returns a
 * value it has rounded for presentation.
 */

public sealed record FundTradeCostRequest(
    string TypeCode,
    string TreatmentCode,
    long AmountMinorUnits,
    string CurrencyCode,
    string? Note = null);

public sealed record FundPurchaseRequest(
    Guid HouseholdId,
    Guid PortfolioId,
    Guid FundAccountId,
    Guid CashAccountId,
    Guid FundAssetId,
    Guid CashAssetId,
    long FundQuantityRawE8,
    long ExecutedUnitPriceRawE8,
    string PriceCurrencyCode,
    long CashConsiderationMinorUnits,
    string CashConsiderationCurrencyCode,
    DateOnly ExecutionDate,
    DateOnly? OrderDate = null,
    DateOnly? SettlementDate = null,
    IReadOnlyList<FundTradeCostRequest>? Costs = null,
    string? ExternalReference = null,
    string? Note = null);

public sealed record FundSaleRequest(
    Guid HouseholdId,
    Guid PortfolioId,
    Guid FundAccountId,
    Guid CashAccountId,
    Guid FundAssetId,
    Guid CashAssetId,
    long FundQuantityRawE8,
    long ExecutedUnitPriceRawE8,
    string PriceCurrencyCode,
    long CashConsiderationMinorUnits,
    string CashConsiderationCurrencyCode,
    DateOnly ExecutionDate,
    DateOnly? OrderDate = null,
    DateOnly? SettlementDate = null,
    IReadOnlyList<FundTradeCostRequest>? Costs = null,
    string? ExternalReference = null,
    string? Note = null,
    IReadOnlyList<ReviewedLotAllocationRequest>? ReviewedPlan = null,
    string? ReviewedPlanFingerprint = null);

public sealed record ReviewedLotAllocationRequest(
    Guid AssetLotId,
    long QuantityRawE8);

public sealed record FundTradeEconomicsResponse(
    long CashConsiderationMinorUnits,
    long AdditionalCashOutflowMinorUnits,
    long IncludedInConsiderationMinorUnits,
    long WithheldFromProceedsMinorUnits,
    long InformationalOnlyMinorUnits,
    long AdditionalFeeMinorUnits,
    long AdditionalTaxMinorUnits,
    long NetCashEffectMinorUnits,
    long PriceImpliedGrossMinorUnits,
    bool PriceImpliedGrossIsRounded,
    long ExpectedConsiderationMinorUnits,
    long DiscrepancyMinorUnits,
    string DiscrepancyCode,
    long? AcquisitionLotCostMinorUnits,
    string CurrencyCode);

public sealed record FundTradeCostResponse(
    string TypeCode,
    string TreatmentCode,
    long AmountMinorUnits,
    string CurrencyCode,
    string? Note);

public sealed record FundPurchasePreviewResponse(
    Guid HouseholdId,
    Guid PortfolioId,
    string PortfolioName,
    Guid FundAccountId,
    string FundAccountName,
    Guid CashAccountId,
    string CashAccountName,
    Guid FundAssetId,
    string FundAssetCode,
    string FundAssetName,
    Guid CashAssetId,
    string CurrencyCode,
    int MinorUnitDigits,
    DateOnly ExecutionDate,
    DateOnly? OrderDate,
    DateOnly? SettlementDate,
    long FundQuantityRawE8,
    long ExecutedUnitPriceRawE8,
    FundTradeEconomicsResponse Economics,
    IReadOnlyList<FundTradeCostResponse> Costs,
    string? ExternalReference,
    string? Note,
    IReadOnlyList<string> WarningCodes);

public sealed record FundSalePlanLineResponse(
    Guid AssetLotId,
    DateOnly? AcquiredOn,
    bool HasUnknownAcquisitionDate,
    long AvailableQuantityRawE8,
    long ConsumedQuantityRawE8,
    string CostStatusCode,
    long? LotCostMinorUnits,
    string? LotCostCurrencyCode);

public sealed record RealizedCostAmountResponse(
    string CurrencyCode,
    long MinorUnits);

public sealed record RealizedCostResponse(
    string CompletenessCode,
    long KnownQuantityRawE8,
    long UnknownQuantityRawE8,
    IReadOnlyList<RealizedCostAmountResponse> KnownAmounts,
    string MethodCode,
    DateTimeOffset? DerivedAtUtc = null,
    bool? SourceSaleIsEffective = null);

public sealed record FundSalePreviewResponse(
    Guid HouseholdId,
    Guid PortfolioId,
    string PortfolioName,
    Guid FundAccountId,
    string FundAccountName,
    Guid CashAccountId,
    string CashAccountName,
    Guid FundAssetId,
    string FundAssetCode,
    string FundAssetName,
    Guid CashAssetId,
    string CurrencyCode,
    int MinorUnitDigits,
    DateOnly ExecutionDate,
    DateOnly? OrderDate,
    DateOnly? SettlementDate,
    long FundQuantityRawE8,
    long ExecutedUnitPriceRawE8,
    long AvailableQuantityRawE8,
    FundTradeEconomicsResponse Economics,
    IReadOnlyList<FundTradeCostResponse> Costs,
    IReadOnlyList<FundSalePlanLineResponse> Plan,
    string PlanFingerprint,
    RealizedCostResponse RealizedCost,
    string? ExternalReference,
    string? Note,
    IReadOnlyList<string> WarningCodes);

public sealed record RecordFundSaleResponse(
    Guid TransactionId,
    string VerificationLocation);

public sealed record FundTradeAllocationResponse(
    Guid AssetLotId,
    Guid AllocationId,
    DateOnly? AcquiredOn,
    string CostStatusCode,
    long? LotCostMinorUnits,
    string? LotCostCurrencyCode,
    long QuantityRawE8);

public sealed record FundTradeVerificationResponse(
    Guid TransactionId,
    Guid HouseholdId,
    string TransactionTypeCode,
    string StatusCode,
    DateOnly? OrderDate,
    DateOnly ExecutionDate,
    DateOnly? SettlementDate,
    DateTimeOffset PostedAtUtc,
    string? ExternalReference,
    string? Note,
    Guid? ReversedByTransactionId,
    Guid PortfolioId,
    Guid FundAccountId,
    Guid CashAccountId,
    Guid FundAssetId,
    Guid CashAssetId,
    long FundQuantityRawE8,
    long ExecutedUnitPriceRawE8,
    FundTradeEconomicsResponse Economics,
    IReadOnlyList<FundTradeCostResponse> Costs,
    IReadOnlyList<FundTradeAllocationResponse> Allocations,
    long CurrentFundPositionRawE8,
    RealizedCostResponse? RealizedCost,
    IReadOnlyList<string> WarningCodes);
