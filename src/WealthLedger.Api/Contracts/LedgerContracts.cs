namespace WealthLedger.Api.Contracts;

public sealed record RecordContributionRequest(
    Guid HouseholdId,
    Guid PortfolioId,
    Guid AccountId,
    Guid CashAssetId,
    long AmountMinorUnits,
    string CurrencyCode,
    string CashFlowCategoryCode,
    DateOnly ExecutionDate,
    Guid? HouseholdMemberId = null,
    string? ExternalReference = null,
    string? Note = null);

public sealed record RecordContributionResponse(Guid TransactionId);

/// <summary>
/// The fund-purchase request, extended additively.
/// </summary>
/// <remarks>
/// Every field an existing caller sends keeps its meaning and position, and
/// every new field is optional with a legacy default, so a version-1 body
/// still deserializes and still means what it meant.
///
/// Transport compatibility is not validation compatibility: a new submission
/// must still satisfy the accepted provenance, date, currency and cost rules.
/// </remarks>
public sealed record RecordFundPurchaseRequest(
    Guid HouseholdId,
    Guid PortfolioId,
    Guid AccountId,
    Guid FundAssetId,
    Guid CashAssetId,
    long FundQuantityRawE8,
    long ExecutedUnitPriceRawE8,
    string PriceCurrencyCode,
    long CashConsiderationMinorUnits,
    string CashConsiderationCurrencyCode,
    DateOnly ExecutionDate,
    string? ExternalReference = null,
    string? Note = null,
    Guid? CashAccountId = null,
    DateOnly? OrderDate = null,
    DateOnly? SettlementDate = null,
    IReadOnlyList<FundTradeCostRequest>? Costs = null);

/// <summary>
/// The fund-purchase result.
/// </summary>
/// <remarks>
/// The two original fields keep their names, types and positions. The
/// verification location is appended with a default, so an existing client
/// that ignores it is unaffected.
/// </remarks>
public sealed record RecordFundPurchaseResponse(
    Guid TransactionId,
    Guid AssetLotId,
    string? VerificationLocation = null);
