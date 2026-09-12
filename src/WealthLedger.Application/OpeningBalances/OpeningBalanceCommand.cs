using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.OpeningBalances;

public sealed record OpeningBalanceLotCommand(
    Quantity Quantity,
    DateOnly? AcquiredOn,
    CostBasis CostBasis,
    PhysicalGoldLotDetail? PhysicalGoldDetail = null);

public sealed record RecordOpeningBalanceCommand(
    Guid HouseholdId,
    Guid PortfolioId,
    Guid AccountId,
    Guid AssetId,
    DateOnly AsOfDate,
    Quantity Quantity,
    string? ExternalReference,
    string Note,
    IReadOnlyList<OpeningBalanceLotCommand> Lots);

public sealed record OpeningBalancePreviewLot(
    int Sequence,
    long QuantityRawE8,
    DateOnly? AcquiredOn,
    CostBasisStatus CostBasisStatus,
    long? OriginalCostBasisMinorUnits,
    string? CostBasisCurrencyCode,
    int? FinenessPartsPerMillion,
    int? PieceCount,
    string? Hallmark,
    string? CertificateReference,
    string? Note,
    decimal? FineWeightGrams);

public sealed record OpeningBalancePreview(
    Guid HouseholdId,
    Guid PortfolioId,
    string PortfolioCode,
    string PortfolioName,
    Guid AccountId,
    string AccountCode,
    string AccountName,
    AccountType AccountType,
    Guid AssetId,
    string AssetCode,
    string AssetName,
    AssetType AssetType,
    AssetUnit AssetUnit,
    string AssetBaseCurrencyCode,
    DateOnly AsOfDate,
    long QuantityRawE8,
    long AllocationTotalRawE8,
    bool AllocationsReconcile,
    CostBasisStatus? UnlottedCostBasisStatus,
    decimal? TotalFineWeightGrams,
    string? ExternalReference,
    string Note,
    IReadOnlyList<OpeningBalancePreviewLot> Lots,
    IReadOnlyList<string> WarningCodes,
    bool IsSemanticallyEligible);

public sealed record RecordOpeningBalanceResult(
    Guid TransactionId,
    IReadOnlyList<Guid> AssetLotIds,
    long SubmittedQuantityRawE8,
    long PersistedQuantityRawE8);
