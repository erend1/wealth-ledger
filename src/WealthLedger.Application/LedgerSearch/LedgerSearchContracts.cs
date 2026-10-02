using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Portfolios;

namespace WealthLedger.Application.LedgerSearch;

public enum LedgerSearchReversalRelationship
{
    Any,
    OriginalOnly,
    ReversalOnly,
    ReversedOriginal
}

public sealed record SearchLedgerTransactionsQuery(
    Guid HouseholdId,
    DateOnly? ExecutedFrom = null,
    DateOnly? ExecutedTo = null,
    IReadOnlyList<TransactionType>? Types = null,
    IReadOnlyList<TransactionStatus>? Statuses = null,
    Guid? AssetId = null,
    Guid? InstitutionId = null,
    Guid? AccountId = null,
    Guid? PortfolioId = null,
    string? ExternalReferenceContains = null,
    LedgerSearchReversalRelationship ReversalRelationship =
        LedgerSearchReversalRelationship.Any,
    int PageSize = 50,
    string? Cursor = null);

public sealed record LedgerSearchPage(
    IReadOnlyList<LedgerSearchTransactionItem> Items,
    string? NextCursor);

public sealed record LedgerSearchTransactionItem(
    Guid TransactionId,
    Guid HouseholdId,
    TransactionType Type,
    TransactionStatus Status,
    DateOnly? OrderDate,
    DateOnly ExecutionDate,
    DateOnly? SettlementDate,
    string? ExternalReference,
    Guid? ReversalOfTransactionId,
    Guid? ReversedByTransactionId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset PostedAtUtc,
    IReadOnlyList<LedgerSearchEntryEffect> EntryEffects);

public sealed record LedgerSearchEntryEffect(
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

public sealed record LedgerSearchNormalizedFilters(
    DateOnly? ExecutedFrom,
    DateOnly? ExecutedTo,
    IReadOnlyList<TransactionType> Types,
    IReadOnlyList<TransactionStatus> Statuses,
    Guid? AssetId,
    Guid? InstitutionId,
    Guid? AccountId,
    Guid? PortfolioId,
    string? ExternalReferenceContains,
    LedgerSearchReversalRelationship ReversalRelationship,
    string Fingerprint);

public sealed record LedgerSearchCursorKey(
    DateOnly ExecutionDate,
    DateTimeOffset PostedAtUtc,
    Guid TransactionId);
