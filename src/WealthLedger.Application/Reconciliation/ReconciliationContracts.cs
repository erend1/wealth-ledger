using WealthLedger.Application.Evidence;
using WealthLedger.Domain.Reconciliation;

namespace WealthLedger.Application.Reconciliation;

/// <summary>
/// Compares all observations of one evidence record with the ledger as of the
/// record's effective date.
/// </summary>
public sealed record RunReconciliationCommand(
    Guid HouseholdId,
    Guid EvidenceId);

public sealed record RunReconciliationResult(
    Guid ReconciliationRunId,
    bool WasReplay);

/// <summary>
/// Resolves one out-of-tolerance item, either by a Posted corrective
/// transaction or by a documented exception with an explanation.
/// </summary>
public sealed record ResolveReconciliationItemCommand(
    Guid HouseholdId,
    Guid ReconciliationRunId,
    Guid ReconciliationItemId,
    ReconciliationResolutionKind Kind,
    Guid? CorrectiveTransactionId = null,
    string? Explanation = null)
{
    // The explanation is human text; never print it.
    public override string ToString() => nameof(ResolveReconciliationItemCommand);
}

public sealed record ResolveReconciliationItemResult(
    Guid ReconciliationItemId,
    ReconciliationResolutionKind Kind,
    bool WasAlreadyResolved);

public sealed record ListReconciliationRunsQuery(
    Guid HouseholdId,
    Guid? EvidenceId = null,
    int PageSize = 50,
    string? Cursor = null);

public sealed record ReconciliationRunPage(
    IReadOnlyList<ReconciliationRunSummary> Items,
    string? NextCursor);

/// <summary>
/// Stored run facts and item counts. Staleness needs re-derivation of the
/// ledger side, so it is reported by the run detail only.
/// </summary>
public sealed record ReconciliationRunSummary(
    Guid ReconciliationRunId,
    Guid HouseholdId,
    Guid EvidenceId,
    DateOnly EvidenceEffectiveDate,
    DateTimeOffset RunAtUtc,
    ReconciliationMethod Method,
    int MethodVersion,
    int ItemCount,
    int MatchedCount,
    int OutOfToleranceCount,
    int OpenCount,
    int CorrectedByTransactionCount,
    int DocumentedExceptionCount);

public sealed record GetReconciliationRunQuery(
    Guid HouseholdId,
    Guid ReconciliationRunId);

/// <summary>
/// The resolution state of an out-of-tolerance item. A matched item has none.
/// </summary>
public enum ReconciliationResolutionState
{
    Open,
    CorrectedByTransaction,
    DocumentedException
}

/// <summary>
/// One run with its immutable item snapshots and a fresh re-derivation of the
/// ledger side. A run is stale when any item's ledger side no longer
/// reproduces its snapshot; the run itself is never rewritten.
/// </summary>
public sealed record ReconciliationRunDetail(
    Guid ReconciliationRunId,
    Guid HouseholdId,
    Guid EvidenceId,
    DateOnly EvidenceEffectiveDate,
    DateTimeOffset RunAtUtc,
    ReconciliationMethod Method,
    int MethodVersion,
    bool IsStale,
    IReadOnlyList<ReconciliationItemDetail> Items)
{
    public override string ToString() => nameof(ReconciliationRunDetail);
}

public sealed record ReconciliationItemDetail(
    Guid ReconciliationItemId,
    int Sequence,
    Guid ObservationId,
    EvidenceObservationScope Scope,
    long ObservedQuantityRawE8,
    long LedgerQuantityRawE8,
    long DeltaRawE8,
    long ToleranceRawE8,
    int? ObservedPieceCount,
    int? LedgerPieceCount,
    int? PieceDelta,
    ReconciliationComparisonStatus ComparisonStatus,
    ReconciliationResolutionState? ResolutionState,
    Guid? CorrectiveTransactionId,
    string? Explanation,
    DateTimeOffset? ResolvedAtUtc,
    long CurrentLedgerQuantityRawE8,
    int? CurrentLedgerPieceCount,
    bool IsStale)
{
    // Quantities and explanations are household facts; never print them.
    public override string ToString() => nameof(ReconciliationItemDetail);
}
