namespace WealthLedger.Api.Contracts;

public sealed record PositionResponse(
    Guid HouseholdId,
    Guid PortfolioId,
    Guid AccountId,
    Guid AssetId,
    long QuantityRawE8,
    int SourceEntryCount);
