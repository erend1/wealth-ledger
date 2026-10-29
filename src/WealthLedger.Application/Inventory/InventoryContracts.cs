using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;

namespace WealthLedger.Application.Inventory;

public sealed record ListPositionInventoryQuery(
    Guid HouseholdId,
    Guid? PortfolioId = null,
    Guid? AccountId = null,
    Guid? InstitutionId = null,
    Guid? AssetId = null,
    DateOnly? AsOf = null,
    bool IncludeZero = false);

public sealed record PositionInventory(
    Guid HouseholdId,
    DateOnly? AsOf,
    IReadOnlyList<PositionInventoryItem> Items);

public sealed record PositionInventoryItem(
    Guid PortfolioId,
    string PortfolioCode,
    string PortfolioName,
    PortfolioStatus PortfolioStatus,
    Guid AccountId,
    string AccountCode,
    string AccountName,
    AccountType AccountType,
    bool AccountIsActive,
    Guid? InstitutionId,
    string? InstitutionCode,
    string? InstitutionName,
    InstitutionType? InstitutionType,
    bool? InstitutionIsActive,
    Guid AssetId,
    string AssetCode,
    string AssetName,
    AssetType AssetType,
    AssetUnit AssetBaseUnit,
    string? AssetBaseCurrencyCode,
    LotTrackingMode AssetLotTrackingMode,
    bool AssetIsActive,
    long QuantityRawE8,
    int SourceEntryCount,
    int SourceTransactionCount);

public sealed record ListLotInventoryQuery(
    Guid HouseholdId,
    Guid? PortfolioId = null,
    Guid? AccountId = null,
    Guid? InstitutionId = null,
    Guid? AssetId = null,
    DateOnly? AsOf = null,
    bool IncludeExhausted = false,
    int PageSize = 50,
    string? Cursor = null);

public sealed record LotInventory(
    Guid HouseholdId,
    DateOnly? AsOf,
    IReadOnlyList<LotInventoryItem> Items,
    string? NextCursor);

public sealed record LotInventoryCursorKey(
    string AssetCode,
    DateOnly? AcquiredOn,
    Guid AssetLotId);

public sealed record LotInventoryItem(
    Guid AssetLotId,
    Guid CreatingTransactionId,
    Guid OpeningTransactionEntryId,
    Guid AssetId,
    string AssetCode,
    string AssetName,
    AssetType AssetType,
    AssetUnit AssetBaseUnit,
    string? AssetBaseCurrencyCode,
    LotTrackingMode AssetLotTrackingMode,
    bool AssetIsActive,
    DateOnly? AcquiredOn,
    CostBasisStatus CostStatus,
    long? CostMinorUnits,
    string? CostCurrencyCode,
    long OriginalQuantityRawE8,
    long GlobalQuantityRawE8,
    IReadOnlyList<LotCustodyInventoryItem> Custody,
    IReadOnlyList<LotAllocationInventoryItem> Allocations,
    PhysicalGoldLotInventoryDetail? PhysicalGold);

public sealed record LotCustodyInventoryItem(
    Guid PortfolioId,
    string PortfolioCode,
    string PortfolioName,
    PortfolioStatus PortfolioStatus,
    Guid AccountId,
    string AccountCode,
    string AccountName,
    AccountType AccountType,
    bool AccountIsActive,
    Guid? InstitutionId,
    string? InstitutionCode,
    string? InstitutionName,
    InstitutionType? InstitutionType,
    bool? InstitutionIsActive,
    long QuantityRawE8,
    int? PieceCount,
    decimal? FineWeightGrams);

public sealed record LotAllocationInventoryItem(
    Guid AllocationId,
    Guid TransactionEntryId,
    Guid TransactionId,
    TransactionType TransactionType,
    DateOnly ExecutionDate,
    Guid PortfolioId,
    Guid AccountId,
    long QuantityDeltaRawE8,
    int? PieceDelta);

public sealed record PhysicalGoldLotInventoryDetail(
    int FinenessPpm,
    int OriginalPieceCount,
    int GlobalPieceCount,
    decimal GlobalFineWeightGrams,
    string? Hallmark,
    string? CertificateReference,
    string? Note);

public sealed record InventoryScopeContext(
    Guid PortfolioId,
    string PortfolioCode,
    string PortfolioName,
    PortfolioStatus PortfolioStatus,
    Guid AccountId,
    string AccountCode,
    string AccountName,
    AccountType AccountType,
    bool AccountIsActive,
    Guid? InstitutionId,
    string? InstitutionCode,
    string? InstitutionName,
    InstitutionType? InstitutionType,
    bool? InstitutionIsActive,
    Guid AssetId,
    string AssetCode,
    string AssetName,
    AssetType AssetType,
    AssetUnit AssetBaseUnit,
    string? AssetBaseCurrencyCode,
    LotTrackingMode AssetLotTrackingMode,
    bool AssetIsActive);

public sealed record InventoryEntryFact(
    Guid EntryId,
    Guid TransactionId,
    DateOnly ExecutionDate,
    DateTimeOffset TransactionCreatedAtUtc,
    int EntrySequence,
    InventoryScopeContext Scope,
    long QuantityDeltaRawE8);

public sealed record InventoryLotDescriptor(
    Guid AssetLotId,
    Guid CreatingTransactionId,
    Guid OpeningTransactionEntryId,
    Guid AssetId,
    string AssetCode,
    string AssetName,
    AssetType AssetType,
    AssetUnit AssetBaseUnit,
    string? AssetBaseCurrencyCode,
    LotTrackingMode AssetLotTrackingMode,
    bool AssetIsActive,
    DateOnly? AcquiredOn,
    CostBasisStatus CostStatus,
    long? CostMinorUnits,
    string? CostCurrencyCode,
    long? OriginalQuantityRawE8,
    int? FinenessPpm,
    int? OriginalPieceCount,
    string? Hallmark,
    string? CertificateReference,
    string? Note);

public sealed record InventoryLotAllocationFact(
    Guid AssetLotId,
    Guid AllocationId,
    Guid TransactionEntryId,
    Guid TransactionId,
    TransactionType TransactionType,
    DateOnly ExecutionDate,
    DateTimeOffset TransactionCreatedAtUtc,
    int EntrySequence,
    InventoryScopeContext Scope,
    long QuantityDeltaRawE8,
    int? PieceDelta);

public sealed record InventoryLotFactSet(
    IReadOnlyList<InventoryLotDescriptor> Lots,
    IReadOnlyList<InventoryLotAllocationFact> Allocations);
