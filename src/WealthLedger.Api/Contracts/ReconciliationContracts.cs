namespace WealthLedger.Api.Contracts;

/// <summary>
/// Compares every observation of one evidence record with the ledger as of
/// the record's effective date.
/// </summary>
public sealed record RunReconciliationRequest(Guid EvidenceId);

public sealed record RunReconciliationResponse(
    Guid ReconciliationRunId,
    bool WasReplay,
    string ReconciliationLocation);

/// <summary>
/// Resolves one out-of-tolerance item by a Posted corrective transaction or a
/// documented exception with an explanation. It never changes the item's
/// comparison.
/// </summary>
public sealed record ResolveReconciliationItemRequest(
    string KindCode,
    Guid? CorrectiveTransactionId = null,
    string? Explanation = null)
{
    // The explanation is household text; never print it.
    public override string ToString() => nameof(ResolveReconciliationItemRequest);
}

public sealed record ResolveReconciliationItemResponse(
    Guid ReconciliationItemId,
    string KindCode,
    bool WasAlreadyResolved);

/// <summary>
/// Stored run facts and counts. Staleness is derived on the run detail only.
/// </summary>
public sealed record ReconciliationRunSummaryResponse(
    Guid ReconciliationRunId,
    Guid HouseholdId,
    Guid EvidenceId,
    DateOnly EvidenceEffectiveDate,
    DateTimeOffset RunAtUtc,
    string MethodCode,
    int MethodVersion,
    int ItemCount,
    int MatchedCount,
    int OutOfToleranceCount,
    int OpenCount,
    int CorrectedByTransactionCount,
    int DocumentedExceptionCount);

/// <summary>
/// A run's immutable item snapshots with a fresh re-derivation of the ledger
/// side. A stale run is shown as stale; it is never rewritten.
/// </summary>
public sealed record ReconciliationRunDetailResponse(
    Guid ReconciliationRunId,
    Guid HouseholdId,
    Guid EvidenceId,
    DateOnly EvidenceEffectiveDate,
    DateTimeOffset RunAtUtc,
    string MethodCode,
    int MethodVersion,
    bool IsStale,
    IReadOnlyList<ReconciliationItemResponse> Items)
{
    public override string ToString() => nameof(ReconciliationRunDetailResponse);
}

public sealed record ReconciliationItemResponse(
    Guid ReconciliationItemId,
    int Sequence,
    Guid ObservationId,
    EvidenceObservationScopeResponse Scope,
    long ObservedQuantityRawE8,
    long LedgerQuantityRawE8,
    long DeltaRawE8,
    long ToleranceRawE8,
    int? ObservedPieceCount,
    int? LedgerPieceCount,
    int? PieceDelta,
    string ComparisonStatusCode,
    string? ResolutionStateCode,
    Guid? CorrectiveTransactionId,
    string? Explanation,
    DateTimeOffset? ResolvedAtUtc,
    long CurrentLedgerQuantityRawE8,
    int? CurrentLedgerPieceCount,
    bool IsStale)
{
    public override string ToString() => nameof(ReconciliationItemResponse);
}
