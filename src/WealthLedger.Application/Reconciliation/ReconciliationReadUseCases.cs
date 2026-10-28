using WealthLedger.Application.Inventory;
using WealthLedger.Domain.Reconciliation;

namespace WealthLedger.Application.Reconciliation;

/// <summary>
/// Lists a household's reconciliation runs, newest first, in bounded pages,
/// optionally for one evidence record.
/// </summary>
public sealed class ListReconciliationRunsUseCase
{
    private readonly IReconciliationReadStore _readStore;

    public ListReconciliationRunsUseCase(IReconciliationReadStore readStore)
    {
        _readStore = readStore
            ?? throw new ArgumentNullException(nameof(readStore));
    }

    public async Task<ReconciliationRunPage> ExecuteAsync(
        ListReconciliationRunsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (query.HouseholdId == Guid.Empty || query.EvidenceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Household and evidence filter identities cannot be empty.",
                nameof(query));
        }

        if (query.PageSize is < 1 or > 100)
        {
            throw ReconciliationException.Invalid(
                ReconciliationErrorCodes.PageSizeInvalid,
                "Page size must be an integer between 1 and 100.");
        }

        var after = ReconciliationCursorCodec.Decode(
            query.Cursor,
            query.HouseholdId,
            query.EvidenceId);

        if (!await _readStore.HouseholdExistsAsync(
                query.HouseholdId,
                cancellationToken))
        {
            throw ReconciliationException.Missing(
                ReconciliationErrorCodes.HouseholdNotFound,
                "The reconciliation household does not exist.");
        }

        if (query.EvidenceId is Guid evidenceId
            && !await _readStore.EvidenceExistsAsync(
                query.HouseholdId,
                evidenceId,
                cancellationToken))
        {
            throw ReconciliationException.Missing(
                ReconciliationErrorCodes.EvidenceNotFound,
                "The evidence record does not exist in this household.");
        }

        var rows = await _readStore.ListRunsAsync(
            query.HouseholdId,
            query.EvidenceId,
            checked(query.PageSize + 1),
            after,
            cancellationToken);

        if (rows.Count <= query.PageSize)
        {
            return new ReconciliationRunPage(rows, NextCursor: null);
        }

        var page = rows.Take(query.PageSize).ToArray();
        var last = page[^1];
        var nextCursor = ReconciliationCursorCodec.Encode(
            query.HouseholdId,
            query.EvidenceId,
            new ReconciliationRunCursorKey(
                last.RunAtUtc,
                last.ReconciliationRunId));

        return new ReconciliationRunPage(page, nextCursor);
    }
}

/// <summary>
/// Reads one run with its immutable item snapshots, re-derives each item's
/// ledger side as of the run's evidence date, and reports staleness.
/// </summary>
/// <remarks>
/// A later effective-history change, such as a reversal dated on or before
/// the evidence date, makes the run stale. The stored snapshot is returned
/// unchanged beside the current derivation; a new run is required to compare
/// again.
/// </remarks>
public sealed class GetReconciliationRunUseCase
{
    private readonly IReconciliationReadStore _readStore;
    private readonly ReconciliationLedgerDerivation _ledger;

    public GetReconciliationRunUseCase(
        IReconciliationReadStore readStore,
        IInventoryReadStore inventoryReadStore)
    {
        _readStore = readStore
            ?? throw new ArgumentNullException(nameof(readStore));
        _ledger = new ReconciliationLedgerDerivation(
            inventoryReadStore
            ?? throw new ArgumentNullException(nameof(inventoryReadStore)));
    }

    public async Task<ReconciliationRunDetail> ExecuteAsync(
        GetReconciliationRunQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.HouseholdId == Guid.Empty || query.ReconciliationRunId == Guid.Empty)
        {
            throw new ArgumentException(
                "Household and run identities cannot be empty.",
                nameof(query));
        }

        var stored = await _readStore.GetRunAsync(
            query.HouseholdId,
            query.ReconciliationRunId,
            cancellationToken);

        if (stored is null
            || stored.ReconciliationRunId != query.ReconciliationRunId
            || stored.HouseholdId != query.HouseholdId)
        {
            throw ReconciliationException.Missing(
                ReconciliationErrorCodes.RunNotFound,
                "The reconciliation run does not exist in this household.");
        }

        if (stored.Items.Count == 0)
        {
            throw new ReconciliationPersistenceException(
                "A persisted reconciliation run has no items.");
        }

        var current = await _ledger.DeriveAsync(
            stored.HouseholdId,
            stored.EvidenceEffectiveDate,
            stored.Items
                .Select(item => (ScopeOf(item), item.ObservedPieceCount is not null))
                .ToArray(),
            cancellationToken);

        var items = stored.Items
            .OrderBy(item => item.Sequence)
            .Select(item => ToDetail(item, current[ScopeOf(item)]))
            .ToArray();

        return new ReconciliationRunDetail(
            stored.ReconciliationRunId,
            stored.HouseholdId,
            stored.EvidenceId,
            stored.EvidenceEffectiveDate,
            stored.RunAtUtc,
            stored.Method,
            stored.MethodVersion,
            items.Any(item => item.IsStale),
            items);
    }

    private static ReconciliationScope ScopeOf(StoredReconciliationItem item)
        => new(item.Scope.PortfolioId, item.Scope.AccountId, item.Scope.AssetId);

    private static ReconciliationItemDetail ToDetail(
        StoredReconciliationItem item,
        ReconciliationLedgerSide current)
    {
        var isStale = current.QuantityRawE8 != item.LedgerQuantityRawE8
                      || current.PieceCount != item.LedgerPieceCount;
        ReconciliationResolutionState? state = item.ComparisonStatus switch
        {
            ReconciliationComparisonStatus.Matched when item.ResolutionKind is null => null,
            ReconciliationComparisonStatus.OutOfTolerance => item.ResolutionKind switch
            {
                null => ReconciliationResolutionState.Open,
                ReconciliationResolutionKind.CorrectedByTransaction =>
                    ReconciliationResolutionState.CorrectedByTransaction,
                ReconciliationResolutionKind.DocumentedException =>
                    ReconciliationResolutionState.DocumentedException,
                _ => throw UnsupportedShape()
            },
            _ => throw UnsupportedShape()
        };

        return new ReconciliationItemDetail(
            item.ReconciliationItemId,
            item.Sequence,
            item.ObservationId,
            item.Scope,
            item.ObservedQuantityRawE8,
            item.LedgerQuantityRawE8,
            item.DeltaRawE8,
            item.ToleranceRawE8,
            item.ObservedPieceCount,
            item.LedgerPieceCount,
            item.PieceDelta,
            item.ComparisonStatus,
            state,
            item.CorrectiveTransactionId,
            item.Explanation,
            item.ResolvedAtUtc,
            current.QuantityRawE8,
            current.PieceCount,
            isStale);
    }

    private static ReconciliationPersistenceException UnsupportedShape()
        => new("A persisted reconciliation item has an unsupported resolution shape.");
}
