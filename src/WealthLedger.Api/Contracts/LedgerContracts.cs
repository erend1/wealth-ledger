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
    string? Note = null);

public sealed record RecordFundPurchaseResponse(
    Guid TransactionId,
    Guid AssetLotId);
