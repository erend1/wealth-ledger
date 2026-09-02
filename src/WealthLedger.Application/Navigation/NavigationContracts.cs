using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Portfolios;

namespace WealthLedger.Application.Navigation;

public sealed record NavigationPage<T>(
    IReadOnlyList<T> Items,
    string? NextCursor);

public sealed record ListHouseholdsQuery(
    int PageSize = 50,
    string? Cursor = null);

public sealed record GetHouseholdQuery(Guid HouseholdId);

public sealed record ListHouseholdMembersQuery(
    Guid HouseholdId,
    int PageSize = 50,
    string? Cursor = null,
    bool IncludeInactive = false);

public sealed record ListInstitutionsQuery(
    int PageSize = 50,
    string? Cursor = null,
    bool IncludeInactive = false);

public sealed record ListPortfoliosQuery(
    Guid HouseholdId,
    int PageSize = 50,
    string? Cursor = null,
    bool IncludeInactive = false);

public sealed record ListAccountsQuery(
    Guid HouseholdId,
    int PageSize = 50,
    string? Cursor = null,
    bool IncludeInactive = false);

public sealed record ListCurrenciesQuery(
    int PageSize = 50,
    string? Cursor = null);

public sealed record ListAssetsQuery(
    int PageSize = 50,
    string? Cursor = null,
    bool IncludeInactive = false);

public sealed record ListRecentLedgerTransactionsQuery(
    Guid HouseholdId,
    int PageSize = 50,
    string? Cursor = null);

public sealed record CurrencyNavigationItem(
    string Code,
    string Name,
    int MinorUnitDigits);

public sealed record HouseholdNavigationItem(
    Guid HouseholdId,
    string Name,
    CurrencyNavigationItem BaseCurrency,
    DateTimeOffset CreatedAtUtc);

public sealed record HouseholdMemberNavigationItem(
    Guid HouseholdMemberId,
    Guid HouseholdId,
    string DisplayName,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);

public sealed record InstitutionNavigationItem(
    Guid InstitutionId,
    string Code,
    string Name,
    InstitutionType Type,
    bool IsActive);

public sealed record PortfolioNavigationItem(
    Guid PortfolioId,
    Guid HouseholdId,
    string Code,
    string Name,
    PortfolioStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ClosedAtUtc);

public sealed record AccountInstitutionNavigationItem(
    Guid InstitutionId,
    string Code,
    string Name,
    InstitutionType Type,
    bool IsActive);

public sealed record AccountNavigationItem(
    Guid AccountId,
    Guid HouseholdId,
    AccountInstitutionNavigationItem? Institution,
    string Code,
    string Name,
    AccountType Type,
    bool IsActive,
    DateOnly? OpenedOn,
    DateOnly? ClosedOn);

public sealed record AssetNavigationItem(
    Guid AssetId,
    string Code,
    string Name,
    AssetType Type,
    AssetUnit BaseUnit,
    string? BaseCurrencyCode,
    LotTrackingMode LotTrackingMode,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);

public sealed record RecentLedgerTransactionNavigationItem(
    Guid TransactionId,
    Guid HouseholdId,
    TransactionType Type,
    TransactionStatus Status,
    DateOnly? OrderDate,
    DateOnly? ExecutionDate,
    DateOnly? SettlementDate,
    string? ExternalReference,
    Guid? ReversalOfTransactionId,
    Guid? ReversedByTransactionId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset PostedAtUtc,
    IReadOnlyList<RecentLedgerEntryEffectNavigationItem> EntryEffects);

public sealed record RecentLedgerEntryEffectNavigationItem(
    Guid EntryId,
    int EntrySequence,
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
    long QuantityDeltaRawE8,
    EntryRole Role);

public sealed record NavigationCreatedAtKey(
    DateTimeOffset CreatedAtUtc,
    Guid Id);

public sealed record NavigationCodeKey(
    string Code,
    Guid Id);

public sealed record NavigationCurrencyKey(string Code);

public sealed record RecentLedgerNavigationKey(
    DateTimeOffset PostedAtUtc,
    Guid TransactionId);
