using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.FundTrades;

/// <summary>
/// One explanatory cost on a fund trade.
/// </summary>
/// <remarks>
/// A component always explains a cost. Whether it also moves cash a second
/// time is decided by <paramref name="Treatment"/>, never by the amount.
/// </remarks>
public sealed record FundTradeCostInput(
    CostType Type,
    CostTreatment Treatment,
    Money Amount,
    string? Note = null);

/// <summary>
/// The source facts of a completed fund purchase.
/// </summary>
/// <remarks>
/// Quantity, unit price, cash consideration and every cost stay independent.
/// The review compares them but never reconciles one into another.
/// </remarks>
public sealed record FundPurchaseCommand(
    Guid HouseholdId,
    Guid PortfolioId,
    Guid FundAccountId,
    Guid CashAccountId,
    Guid FundAssetId,
    Guid CashAssetId,
    Quantity FundQuantity,
    UnitPrice ExecutedUnitPrice,
    Money CashConsideration,
    DateOnly ExecutionDate,
    DateOnly? OrderDate = null,
    DateOnly? SettlementDate = null,
    IReadOnlyList<FundTradeCostInput>? Costs = null,
    string? ExternalReference = null,
    string? Note = null);

/// <summary>
/// The source facts of a completed fund sale.
/// </summary>
public sealed record FundSaleCommand(
    Guid HouseholdId,
    Guid PortfolioId,
    Guid FundAccountId,
    Guid CashAccountId,
    Guid FundAssetId,
    Guid CashAssetId,
    Quantity FundQuantity,
    UnitPrice ExecutedUnitPrice,
    Money CashConsideration,
    DateOnly ExecutionDate,
    DateOnly? OrderDate = null,
    DateOnly? SettlementDate = null,
    IReadOnlyList<FundTradeCostInput>? Costs = null,
    string? ExternalReference = null,
    string? Note = null,
    IReadOnlyList<ReviewedLotAllocation>? ReviewedPlan = null,
    string? ReviewedPlanFingerprint = null);

/// <summary>
/// One lot the user actually reviewed before posting a sale.
/// </summary>
/// <remarks>
/// This is not lot selection. The user does not choose lots in M008; the plan
/// is recomputed deterministically at post time and compared with what was
/// shown, so a reviewed plan can never be silently substituted.
/// </remarks>
public sealed record ReviewedLotAllocation(
    Guid AssetLotId,
    Quantity Quantity);
