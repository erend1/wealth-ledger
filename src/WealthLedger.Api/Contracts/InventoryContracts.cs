namespace WealthLedger.Api.Contracts;

/// <summary>
/// Derived positions, one per exact scope. Quantities are signed raw E8 sums
/// of effective Posted entries; nothing here is stored state.
/// </summary>
public sealed record PositionInventoryResponse(
    Guid HouseholdId,
    DateOnly? AsOf,
    IReadOnlyList<PositionInventoryItemResponse> Items);

public sealed record PositionInventoryItemResponse(
    Guid PortfolioId,
    string PortfolioCode,
    string PortfolioName,
    string PortfolioStatusCode,
    Guid AccountId,
    string AccountCode,
    string AccountName,
    string AccountTypeCode,
    bool AccountIsActive,
    Guid? InstitutionId,
    string? InstitutionCode,
    string? InstitutionName,
    string? InstitutionTypeCode,
    bool? InstitutionIsActive,
    Guid AssetId,
    string AssetCode,
    string AssetName,
    string AssetTypeCode,
    string AssetBaseUnitCode,
    string? AssetBaseCurrencyCode,
    string AssetLotTrackingModeCode,
    bool AssetIsActive,
    long QuantityRawE8,
    int SourceEntryCount,
    int SourceTransactionCount);

/// <summary>
/// One page of derived acquisition lots with their custody and allocations.
/// </summary>
public sealed record LotInventoryResponse(
    Guid HouseholdId,
    DateOnly? AsOf,
    IReadOnlyList<LotInventoryItemResponse> Items,
    string? NextCursor);

public sealed record LotInventoryItemResponse(
    Guid AssetLotId,
    Guid CreatingTransactionId,
    Guid OpeningTransactionEntryId,
    Guid AssetId,
    string AssetCode,
    string AssetName,
    string AssetTypeCode,
    string AssetBaseUnitCode,
    string? AssetBaseCurrencyCode,
    string AssetLotTrackingModeCode,
    bool AssetIsActive,
    DateOnly? AcquiredOn,
    string CostStatusCode,
    long? CostMinorUnits,
    string? CostCurrencyCode,
    long OriginalQuantityRawE8,
    long GlobalQuantityRawE8,
    IReadOnlyList<LotCustodyInventoryResponse> Custody,
    IReadOnlyList<LotAllocationInventoryResponse> Allocations,
    PhysicalGoldLotInventoryResponse? PhysicalGold);

public sealed record LotCustodyInventoryResponse(
    Guid PortfolioId,
    string PortfolioCode,
    string PortfolioName,
    string PortfolioStatusCode,
    Guid AccountId,
    string AccountCode,
    string AccountName,
    string AccountTypeCode,
    bool AccountIsActive,
    Guid? InstitutionId,
    string? InstitutionCode,
    string? InstitutionName,
    string? InstitutionTypeCode,
    bool? InstitutionIsActive,
    long QuantityRawE8,
    int? PieceCount,
    decimal? FineWeightGrams);

public sealed record LotAllocationInventoryResponse(
    Guid AllocationId,
    Guid TransactionEntryId,
    Guid TransactionId,
    string TransactionTypeCode,
    DateOnly ExecutionDate,
    Guid PortfolioId,
    Guid AccountId,
    long QuantityDeltaRawE8,
    int? PieceDelta);

public sealed record PhysicalGoldLotInventoryResponse(
    int FinenessPpm,
    int OriginalPieceCount,
    int GlobalPieceCount,
    decimal GlobalFineWeightGrams,
    string? Hallmark,
    string? CertificateReference,
    string? Note);
