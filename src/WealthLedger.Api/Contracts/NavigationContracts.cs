namespace WealthLedger.Api.Contracts;

public sealed record NavigationPageResponse<T>(
    IReadOnlyList<T> Items,
    string? NextCursor);

public sealed record CurrencyNavigationResponse(
    string Code,
    string Name,
    int MinorUnitDigits);

public sealed record HouseholdNavigationResponse(
    Guid HouseholdId,
    string Name,
    CurrencyNavigationResponse BaseCurrency,
    DateTimeOffset CreatedAtUtc);

public sealed record HouseholdMemberNavigationResponse(
    Guid HouseholdMemberId,
    Guid HouseholdId,
    string DisplayName,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);

public sealed record InstitutionNavigationResponse(
    Guid InstitutionId,
    string Code,
    string Name,
    string TypeCode,
    bool IsActive);

public sealed record PortfolioNavigationResponse(
    Guid PortfolioId,
    Guid HouseholdId,
    string Code,
    string Name,
    string StatusCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ClosedAtUtc);

public sealed record AccountInstitutionNavigationResponse(
    Guid InstitutionId,
    string Code,
    string Name,
    string TypeCode,
    bool IsActive);

public sealed record AccountNavigationResponse(
    Guid AccountId,
    Guid HouseholdId,
    AccountInstitutionNavigationResponse? Institution,
    string Code,
    string Name,
    string TypeCode,
    bool IsActive,
    DateOnly? OpenedOn,
    DateOnly? ClosedOn);

public sealed record AssetNavigationResponse(
    Guid AssetId,
    string Code,
    string Name,
    string TypeCode,
    string BaseUnitCode,
    string? BaseCurrencyCode,
    string LotTrackingModeCode,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);

public sealed record RecentLedgerTransactionNavigationResponse(
    Guid TransactionId,
    Guid HouseholdId,
    string TypeCode,
    string StatusCode,
    DateOnly? OrderDate,
    DateOnly? ExecutionDate,
    DateOnly? SettlementDate,
    string? ExternalReference,
    Guid? ReversalOfTransactionId,
    Guid? ReversedByTransactionId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset PostedAtUtc,
    IReadOnlyList<RecentLedgerEntryEffectNavigationResponse> EntryEffects);

public sealed record RecentLedgerEntryEffectNavigationResponse(
    Guid EntryId,
    int EntrySequence,
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
    long QuantityDeltaRawE8,
    string RoleCode);
