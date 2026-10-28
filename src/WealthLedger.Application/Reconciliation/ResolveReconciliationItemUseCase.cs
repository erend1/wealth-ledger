using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Reconciliation;

namespace WealthLedger.Application.Reconciliation;

/// <summary>
/// Records the one resolution of an out-of-tolerance reconciliation item.
/// </summary>
/// <remarks>
/// The resolution is a separate relationship record: the item keeps its
/// delta and its OutOfTolerance comparison, and a corrected difference
/// becomes Matched only in a new run. A corrective transaction must be
/// Posted in the run's household; reconciliation never posts one itself.
/// Repeating the same resolution is a no-op, while a different one is a
/// conflict. A stale run's items can still be resolved.
/// </remarks>
public sealed class ResolveReconciliationItemUseCase
{
    private readonly IReconciliationStore _store;
    private readonly TimeProvider _timeProvider;

    public ResolveReconciliationItemUseCase(
        IReconciliationStore store,
        TimeProvider timeProvider)
    {
        _store = store
            ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ResolveReconciliationItemResult> ExecuteAsync(
        ResolveReconciliationItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.HouseholdId == Guid.Empty
            || command.ReconciliationRunId == Guid.Empty
            || command.ReconciliationItemId == Guid.Empty)
        {
            throw new ArgumentException(
                "Household, run and item identities cannot be empty.",
                nameof(command));
        }

        var resolution = CreateResolution(command, _timeProvider.GetUtcNow());

        var item = await _store.FindItemAsync(
            command.ReconciliationItemId,
            cancellationToken);

        if (item is null
            || item.ReconciliationItemId != command.ReconciliationItemId
            || item.ReconciliationRunId != command.ReconciliationRunId
            || item.HouseholdId != command.HouseholdId)
        {
            throw ReconciliationException.Missing(
                ReconciliationErrorCodes.ItemNotFound,
                "The reconciliation item does not exist in this run and household.");
        }

        if (item.ComparisonStatus == ReconciliationComparisonStatus.Matched)
        {
            throw ReconciliationException.Invalid(
                ReconciliationErrorCodes.ItemMatched,
                "A matched reconciliation item needs no resolution.");
        }

        if (item.Resolution is not null)
        {
            return Existing(resolution, item.Resolution);
        }

        if (resolution.CorrectiveTransactionId is Guid transactionId)
        {
            var transaction = await _store.FindTransactionAsync(
                transactionId,
                cancellationToken);

            if (transaction is null
                || transaction.TransactionId != transactionId
                || transaction.HouseholdId != command.HouseholdId
                || transaction.Status != TransactionStatus.Posted)
            {
                throw ReconciliationException.Invalid(
                    ReconciliationErrorCodes.CorrectionInvalid,
                    "A corrective transaction must be a Posted transaction of the run's household.");
            }
        }

        var commit = await _store.TryAddResolutionAsync(
            resolution,
            cancellationToken);

        return commit.WasCommitted
            ? new ResolveReconciliationItemResult(
                resolution.ItemId,
                resolution.Kind,
                WasAlreadyResolved: false)
            : Existing(resolution, commit.Resolution);
    }

    private static ReconciliationItemResolution CreateResolution(
        ResolveReconciliationItemCommand command,
        DateTimeOffset resolvedAtUtc)
    {
        switch (command.Kind)
        {
            case ReconciliationResolutionKind.CorrectedByTransaction
                when command.CorrectiveTransactionId is Guid transactionId
                     && transactionId != Guid.Empty
                     && command.Explanation is null:
                return ReconciliationItemResolution.CorrectedByTransaction(
                    command.ReconciliationItemId,
                    transactionId,
                    resolvedAtUtc);

            case ReconciliationResolutionKind.DocumentedException
                when command.CorrectiveTransactionId is null:
                try
                {
                    return ReconciliationItemResolution.DocumentedException(
                        command.ReconciliationItemId,
                        command.Explanation!,
                        resolvedAtUtc);
                }
                catch (ArgumentException exception)
                {
                    throw ReconciliationException.Invalid(
                        ReconciliationErrorCodes.ResolutionInvalid,
                        "A documented exception requires an explanation of up to 2000 characters without control characters.",
                        exception);
                }

            default:
                throw ReconciliationException.Invalid(
                    ReconciliationErrorCodes.ResolutionInvalid,
                    "A correction names only a corrective transaction; a documented exception names only an explanation.");
        }
    }

    private static ResolveReconciliationItemResult Existing(
        ReconciliationItemResolution requested,
        ReconciliationItemResolution stored)
    {
        if (!stored.HasSameFacts(requested))
        {
            throw ReconciliationException.Conflict(
                ReconciliationErrorCodes.ItemAlreadyResolved,
                "The reconciliation item is already resolved differently; rerun the reconciliation instead.");
        }

        return new ResolveReconciliationItemResult(
            stored.ItemId,
            stored.Kind,
            WasAlreadyResolved: true);
    }
}
