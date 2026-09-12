namespace WealthLedger.Api.Contracts;

public sealed record OpeningBalanceRequest(
    Guid HouseholdId,
    Guid PortfolioId,
    Guid AccountId,
    Guid AssetId,
    DateOnly AsOfDate,
    long QuantityRawE8,
    string? ExternalReference,
    string Note,
    IReadOnlyList<OpeningBalanceLotRequest>? Lots);

public sealed record OpeningBalanceLotRequest(
    long QuantityRawE8,
    DateOnly? AcquiredOn,
    string CostBasisStatusCode,
    long? OriginalCostBasisMinorUnits,
    string? CostBasisCurrencyCode,
    OpeningBalancePhysicalGoldDetailRequest? PhysicalGoldDetail);

public sealed record OpeningBalancePhysicalGoldDetailRequest(
    int FinenessPartsPerMillion,
    int PieceCount,
    string? Hallmark,
    string? CertificateReference,
    string? Note);

public sealed record OpeningBalancePreviewResponse(
    Guid HouseholdId,
    Guid PortfolioId,
    string PortfolioCode,
    string PortfolioName,
    Guid AccountId,
    string AccountCode,
    string AccountName,
    string AccountTypeCode,
    Guid AssetId,
    string AssetCode,
    string AssetName,
    string AssetTypeCode,
    string AssetUnitCode,
    string AssetBaseCurrencyCode,
    DateOnly AsOfDate,
    long QuantityRawE8,
    long AllocationTotalRawE8,
    bool AllocationsReconcile,
    string? UnlottedCostBasisStatusCode,
    string? TotalFineWeightGramsExact,
    string? ExternalReference,
    string Note,
    IReadOnlyList<OpeningBalancePreviewLotResponse> Lots,
    IReadOnlyList<string> WarningCodes,
    bool IsSemanticallyEligible);

public sealed record OpeningBalancePreviewLotResponse(
    int Sequence,
    long QuantityRawE8,
    DateOnly? AcquiredOn,
    string CostBasisStatusCode,
    long? OriginalCostBasisMinorUnits,
    string? CostBasisCurrencyCode,
    int? FinenessPartsPerMillion,
    int? PieceCount,
    string? Hallmark,
    string? CertificateReference,
    string? Note,
    string? FineWeightGramsExact);

public sealed record RecordOpeningBalanceResponse(
    Guid TransactionId,
    IReadOnlyList<Guid> AssetLotIds,
    long SubmittedQuantityRawE8,
    long PersistedQuantityRawE8,
    string VerificationLocation);

public sealed record OpeningBalanceVerificationResponse(
    LedgerTransactionResponse Transaction,
    long PersistedQuantityRawE8,
    long AllocationTotalRawE8,
    bool AllocationsReconcile,
    long CurrentPositionRawE8,
    int PositionSourceEntryCount,
    bool CurrentPositionEqualsOpeningQuantity,
    bool HasAdditionalEffectiveHistory,
    bool IsIndependentlyReconciled,
    IReadOnlyList<string> WarningCodes);

public sealed record CreateOpeningBalanceCurrencyRequest(
    string Name,
    int MinorUnitDigits);

public sealed record OpeningBalanceCurrencyResponse(
    bool WasCreated,
    string Code,
    string Name,
    int MinorUnitDigits);

public sealed record CreateOpeningBalanceInstitutionRequest(
    string Name,
    string TypeCode);

public sealed record OpeningBalanceInstitutionResponse(
    bool WasCreated,
    Guid InstitutionId,
    string Code,
    string Name,
    string TypeCode,
    bool IsActive);

public sealed record CreateOpeningBalanceAccountRequest(
    Guid? InstitutionId,
    string Name,
    string TypeCode,
    DateOnly? OpenedOn);

public sealed record OpeningBalanceAccountResponse(
    bool WasCreated,
    Guid AccountId,
    Guid HouseholdId,
    Guid? InstitutionId,
    string Code,
    string Name,
    string TypeCode,
    bool IsActive,
    DateOnly? OpenedOn);

public sealed record CreateOpeningBalanceAssetRequest(
    string Name,
    string TypeCode,
    string BaseCurrencyCode,
    string LotTrackingModeCode);

public sealed record OpeningBalanceAssetResponse(
    bool WasCreated,
    Guid AssetId,
    string Code,
    string Name,
    string TypeCode,
    string BaseUnitCode,
    string BaseCurrencyCode,
    string LotTrackingModeCode,
    bool IsActive);
