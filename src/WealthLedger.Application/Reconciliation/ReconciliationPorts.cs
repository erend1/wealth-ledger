using WealthLedger.Application.Evidence;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Reconciliation;

namespace WealthLedger.Application.Reconciliation;

/// <summary>
/// Write-side persistence for the reconciliation-run aggregate.
/// </summary>
/// <remarks>
/// Reconciliation writes never touch the ledger: the store inserts run, item
/// and resolution rows only. Resolutions are added through this aggregate
/// boundary rather than a resolution-specific repository.
/// </remarks>
public interface IReconciliationStore
{
    Task<ReconciliationRunReference?> FindRunByKeyAsync(
        Guid householdId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically inserts the run and its items under the retry key. When
    /// another writer already committed the key, nothing is inserted and that
    /// writer's run is returned.
    /// </summary>
    Task<ReconciliationRunCommitResult> TryCommitRunAsync(
        string idempotencyKey,
        ReconciliationRun run,
        CancellationToken cancellationToken = default);

    Task<ReconciliationItemReference?> FindItemAsync(
        Guid itemId,
        CancellationToken cancellationToken = default);

    Task<ReconciliationTransactionReference?> FindTransactionAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts the item's only resolution. When the item already has one,
    /// nothing is inserted and the stored resolution is returned.
    /// </summary>
    Task<ReconciliationResolutionCommitResult> TryAddResolutionAsync(
        ReconciliationItemResolution resolution,
        CancellationToken cancellationToken = default);
}

public interface IReconciliationReadStore
{
    Task<bool> HouseholdExistsAsync(
        Guid householdId,
        CancellationToken cancellationToken = default);

    Task<bool> EvidenceExistsAsync(
        Guid householdId,
        Guid evidenceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReconciliationRunSummary>> ListRunsAsync(
        Guid householdId,
        Guid? evidenceId,
        int take,
        ReconciliationRunCursorKey? after,
        CancellationToken cancellationToken = default);

    Task<StoredReconciliationRun?> GetRunAsync(
        Guid householdId,
        Guid reconciliationRunId,
        CancellationToken cancellationToken = default);
}

public sealed record ReconciliationRunReference(
    Guid ReconciliationRunId,
    Guid HouseholdId,
    Guid EvidenceId);

public sealed record ReconciliationRunCommitResult(
    bool WasCommitted,
    ReconciliationRunReference Run);

public sealed record ReconciliationItemReference(
    Guid ReconciliationItemId,
    Guid ReconciliationRunId,
    Guid HouseholdId,
    ReconciliationComparisonStatus ComparisonStatus,
    ReconciliationItemResolution? Resolution);

public sealed record ReconciliationTransactionReference(
    Guid TransactionId,
    Guid HouseholdId,
    TransactionStatus Status);

public sealed record ReconciliationResolutionCommitResult(
    bool WasCommitted,
    ReconciliationItemResolution Resolution);

public sealed record ReconciliationRunCursorKey(
    DateTimeOffset RunAtUtc,
    Guid ReconciliationRunId);

/// <summary>
/// A run exactly as stored, before its ledger side is re-derived.
/// </summary>
public sealed record StoredReconciliationRun(
    Guid ReconciliationRunId,
    Guid HouseholdId,
    Guid EvidenceId,
    DateOnly EvidenceEffectiveDate,
    DateTimeOffset RunAtUtc,
    ReconciliationMethod Method,
    int MethodVersion,
    IReadOnlyList<StoredReconciliationItem> Items)
{
    public override string ToString() => nameof(StoredReconciliationRun);
}

public sealed record StoredReconciliationItem(
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
    ReconciliationResolutionKind? ResolutionKind,
    Guid? CorrectiveTransactionId,
    string? Explanation,
    DateTimeOffset? ResolvedAtUtc)
{
    public override string ToString() => nameof(StoredReconciliationItem);
}
