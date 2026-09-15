using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.PhysicalGold;

public sealed record PhysicalGoldTradeScope(
    Guid HouseholdId,
    Guid PortfolioId,
    Guid GoldAccountId,
    Guid CashAccountId,
    Guid GoldAssetId,
    Guid CashAssetId);

public sealed record PhysicalGoldCustodyScope(
    Guid HouseholdId,
    Guid PortfolioId,
    Guid GoldAccountId,
    Guid GoldAssetId);

public sealed record PhysicalGoldTransferScope(
    Guid HouseholdId,
    Guid SourcePortfolioId,
    Guid SourceGoldAccountId,
    Guid DestinationPortfolioId,
    Guid DestinationGoldAccountId,
    Guid GoldAssetId,
    Guid? CashPortfolioId,
    Guid? CashAccountId,
    Guid? CashAssetId);

public sealed record PhysicalGoldCostInput(
    CostType Type,
    CostTreatment Treatment,
    Money Amount,
    string? Note = null);

public sealed record PhysicalGoldSelectedLot(
    Guid AssetLotId,
    Quantity GrossWeight,
    int PieceCount);

public sealed record PhysicalGoldPurchaseCommand(
    Guid HouseholdId,
    Guid PortfolioId,
    Guid GoldAccountId,
    Guid CashAccountId,
    Guid GoldAssetId,
    Guid CashAssetId,
    Quantity GrossWeight,
    Fineness Fineness,
    int PieceCount,
    Money CashConsideration,
    DateOnly ExecutionDate,
    UnitPrice? ExecutedUnitPrice = null,
    Guid? CounterpartyInstitutionId = null,
    DateOnly? OrderDate = null,
    DateOnly? SettlementDate = null,
    IReadOnlyList<PhysicalGoldCostInput>? Costs = null,
    string? Hallmark = null,
    string? CertificateReference = null,
    string? LotNote = null,
    string? ExternalReference = null,
    string? Note = null);

public sealed record PhysicalGoldSaleCommand(
    Guid HouseholdId,
    Guid PortfolioId,
    Guid GoldAccountId,
    Guid CashAccountId,
    Guid GoldAssetId,
    Guid CashAssetId,
    Quantity GrossWeight,
    int PieceCount,
    IReadOnlyList<PhysicalGoldSelectedLot> SelectedLots,
    Money CashConsideration,
    DateOnly ExecutionDate,
    UnitPrice? ExecutedUnitPrice = null,
    Guid? CounterpartyInstitutionId = null,
    DateOnly? OrderDate = null,
    DateOnly? SettlementDate = null,
    IReadOnlyList<PhysicalGoldCostInput>? Costs = null,
    string? ExternalReference = null,
    string? Note = null,
    string? ReviewedPlanFingerprint = null);

public sealed record PhysicalGoldTransferCommand(
    Guid HouseholdId,
    Guid SourcePortfolioId,
    Guid SourceGoldAccountId,
    Guid DestinationPortfolioId,
    Guid DestinationGoldAccountId,
    Guid GoldAssetId,
    Quantity GrossWeight,
    int PieceCount,
    IReadOnlyList<PhysicalGoldSelectedLot> SelectedLots,
    DateOnly ExecutionDate,
    Guid? CashPortfolioId = null,
    Guid? CashAccountId = null,
    Guid? CashAssetId = null,
    IReadOnlyList<PhysicalGoldCostInput>? Costs = null,
    string? ExternalReference = null,
    string? Note = null,
    string? ReviewedPlanFingerprint = null);

public enum PhysicalGoldDiscrepancyClassification
{
    NotAvailable,
    Exact,
    RoundingConsistent,
    Material
}

public sealed record PhysicalGoldEconomics(
    Money? CashConsideration,
    Money AdditionalCashOutflowTotal,
    Money IncludedInConsiderationTotal,
    Money WithheldFromProceedsTotal,
    Money InformationalOnlyTotal,
    Money AdditionalFeeTotal,
    Money AdditionalTaxTotal,
    Money NetCashEffect,
    Money? PriceImpliedGross,
    bool PriceImpliedGrossIsRounded,
    Money? ExpectedConsideration,
    long? DiscrepancyMinorUnits,
    PhysicalGoldDiscrepancyClassification Discrepancy,
    Money? AcquisitionLotCost);

public sealed record PhysicalGoldCustodyLot(
    Guid AssetLotId,
    Guid GoldAssetId,
    DateOnly? AcquiredOn,
    DateTimeOffset CreatedAtUtc,
    CostBasis CostBasis,
    Fineness Fineness,
    int OriginalPieceCount,
    string? Hallmark,
    string? CertificateReference,
    string? Note,
    Quantity ScopedGrossWeight,
    int ScopedPieceCount,
    Quantity GlobalGrossWeight,
    int GlobalPieceCount);

public sealed record PhysicalGoldPlanLine(
    Guid AssetLotId,
    DateOnly? AcquiredOn,
    long AvailableGrossWeightRawE8,
    int AvailablePieceCount,
    long MovedGrossWeightRawE8,
    int MovedPieceCount,
    long RemainingGrossWeightRawE8,
    int RemainingPieceCount,
    int FinenessPpm,
    decimal MovedFineWeightGrams,
    CostBasisStatus CostStatus,
    long? LotCostMinorUnits,
    string? LotCostCurrencyCode,
    string? Hallmark,
    string? CertificateReference);

public sealed record PhysicalGoldRealizedCostAmount(
    string CurrencyCode,
    long MinorUnits);

public sealed record PhysicalGoldRealizedCostProjection(
    RealizedCostCompleteness Completeness,
    long KnownQuantityRawE8,
    long UnknownQuantityRawE8,
    IReadOnlyList<PhysicalGoldRealizedCostAmount> KnownAmounts,
    string MethodCode);

public sealed record PhysicalGoldRealizedCostResult(
    RealizedCostCompleteness Completeness,
    long KnownQuantityRawE8,
    long UnknownQuantityRawE8,
    IReadOnlyList<PhysicalGoldRealizedCostAmount> KnownAmounts,
    IReadOnlyList<PhysicalGoldRealizedCostLotLine> Lines,
    string MethodCode,
    DateTimeOffset DerivedAtUtc,
    bool SourceSaleIsEffective);

public sealed record PhysicalGoldRealizedCostLotLine(
    Guid AssetLotId,
    long QuantityRawE8,
    CostBasisStatus CostStatus,
    long? KnownCostMinorUnits,
    string? KnownCostCurrencyCode);

public sealed record PhysicalGoldPurchasePreview(
    PhysicalGoldTradeScope Scope,
    string PortfolioName,
    string GoldAccountName,
    string CashAccountName,
    string GoldAssetCode,
    string GoldAssetName,
    string CurrencyCode,
    int MinorUnitDigits,
    DateOnly ExecutionDate,
    DateOnly? OrderDate,
    DateOnly? SettlementDate,
    long GrossWeightRawE8,
    int FinenessPpm,
    decimal FineWeightGrams,
    int PieceCount,
    Guid? CounterpartyInstitutionId,
    string? CounterpartyName,
    PhysicalGoldEconomics Economics,
    IReadOnlyList<PhysicalGoldCostInput> Costs,
    string? Hallmark,
    string? CertificateReference,
    string? LotNote,
    string? ExternalReference,
    string? Note,
    IReadOnlyList<string> WarningCodes);

public sealed record PhysicalGoldSalePreview(
    PhysicalGoldTradeScope Scope,
    string PortfolioName,
    string GoldAccountName,
    string CashAccountName,
    string GoldAssetCode,
    string GoldAssetName,
    string CurrencyCode,
    int MinorUnitDigits,
    DateOnly ExecutionDate,
    DateOnly? OrderDate,
    DateOnly? SettlementDate,
    long GrossWeightRawE8,
    int PieceCount,
    Guid? CounterpartyInstitutionId,
    string? CounterpartyName,
    PhysicalGoldEconomics Economics,
    IReadOnlyList<PhysicalGoldCostInput> Costs,
    IReadOnlyList<PhysicalGoldPlanLine> Plan,
    string PlanFingerprint,
    PhysicalGoldRealizedCostProjection RealizedCost,
    string? ExternalReference,
    string? Note,
    IReadOnlyList<string> WarningCodes);

public sealed record PhysicalGoldTransferPreview(
    PhysicalGoldTransferScope Scope,
    string SourcePortfolioName,
    string SourceAccountName,
    string DestinationPortfolioName,
    string DestinationAccountName,
    string GoldAssetCode,
    string GoldAssetName,
    string CurrencyCode,
    int MinorUnitDigits,
    DateOnly ExecutionDate,
    long GrossWeightRawE8,
    int PieceCount,
    decimal FineWeightGrams,
    PhysicalGoldEconomics Economics,
    IReadOnlyList<PhysicalGoldCostInput> Costs,
    IReadOnlyList<PhysicalGoldPlanLine> Plan,
    string PlanFingerprint,
    string? ExternalReference,
    string? Note,
    IReadOnlyList<string> WarningCodes);

public sealed record RecordPhysicalGoldPurchaseResult(
    Guid TransactionId,
    Guid AssetLotId);

public sealed record RecordPhysicalGoldActivityResult(Guid TransactionId);

public sealed record PhysicalGoldAllocationFact(
    Guid AssetLotId,
    Guid AllocationId,
    Guid PortfolioId,
    Guid AccountId,
    long GrossWeightDeltaRawE8,
    int PieceDelta,
    DateOnly? AcquiredOn,
    CostBasisStatus CostStatus,
    Money? LotCostBasis,
    int FinenessPpm,
    int OriginalPieceCount,
    string? Hallmark,
    string? CertificateReference,
    string? LotNote,
    decimal FineWeightDeltaGrams);

public sealed record PhysicalGoldActivityPersistedFacts(
    Guid TransactionId,
    Guid HouseholdId,
    TransactionType Type,
    TransactionStatus Status,
    DateOnly? OrderDate,
    DateOnly ExecutionDate,
    DateOnly? SettlementDate,
    DateTimeOffset PostedAtUtc,
    string? ExternalReference,
    string? Note,
    Guid? ReversedByTransactionId,
    Guid GoldAssetId,
    Guid? CashAssetId,
    string CurrencyCode,
    Guid? CounterpartyInstitutionId,
    string? CounterpartyName,
    Quantity GrossWeight,
    int PieceCount,
    UnitPrice? ExecutedUnitPrice,
    Money? CashConsideration,
    IReadOnlyList<PhysicalGoldCostInput> Costs,
    IReadOnlyList<PhysicalGoldAllocationFact> Allocations,
    Guid? CreatedAssetLotId);

public sealed record PhysicalGoldCustodyPosition(
    Guid PortfolioId,
    Guid AccountId,
    Guid GoldAssetId,
    Guid AssetLotId,
    long GrossWeightRawE8,
    int PieceCount,
    int FinenessPpm,
    decimal FineWeightGrams,
    DateOnly? AcquiredOn,
    CostBasisStatus CostStatus,
    long? CostMinorUnits,
    string? CostCurrencyCode,
    string? Hallmark,
    string? CertificateReference);

public sealed record PhysicalGoldActivityVerification(
    PhysicalGoldActivityPersistedFacts Facts,
    PhysicalGoldEconomics Economics,
    int MinorUnitDigits,
    IReadOnlyList<PhysicalGoldCustodyPosition> CurrentCustody,
    PhysicalGoldRealizedCostResult? RealizedCost,
    IReadOnlyList<string> WarningCodes);

public sealed record PhysicalGoldCustodyInventory(
    Guid HouseholdId,
    IReadOnlyList<PhysicalGoldCustodyPosition> Items);

public enum PhysicalGoldCommitStatus
{
    Committed,
    AlreadyRecorded,
    StaleReviewedPlan,
    InsufficientGrossWeight,
    InsufficientPieces,
    NegativeCashNoteRequired,
    PersistenceConflict
}

public sealed record PhysicalGoldCommitResult(
    PhysicalGoldCommitStatus Status,
    LedgerSubmissionReceipt? Receipt);
