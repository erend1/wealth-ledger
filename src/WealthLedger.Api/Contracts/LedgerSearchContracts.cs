namespace WealthLedger.Api.Contracts;

/// <summary>
/// One effective Posted transaction matching a ledger search, with the same
/// entry-effect shape as the recent-ledger feed.
/// </summary>
public sealed record LedgerSearchTransactionResponse(
    Guid TransactionId,
    Guid HouseholdId,
    string TypeCode,
    string StatusCode,
    DateOnly? OrderDate,
    DateOnly ExecutionDate,
    DateOnly? SettlementDate,
    string? ExternalReference,
    Guid? ReversalOfTransactionId,
    Guid? ReversedByTransactionId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset PostedAtUtc,
    IReadOnlyList<RecentLedgerEntryEffectNavigationResponse> EntryEffects);
