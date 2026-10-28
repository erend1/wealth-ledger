using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Evidence;
using WealthLedger.Application.Inventory;
using WealthLedger.Domain.Reconciliation;

namespace WealthLedger.Application.Reconciliation;

/// <summary>
/// Compares every observation of one evidence record with the ledger as of
/// the record's effective date and persists the run as audit history.
/// </summary>
/// <remarks>
/// Only observed scopes are compared; an unobserved scope is never treated as
/// zero. The run never posts, edits or deletes financial history. It is
/// retry-safe: replaying the same key for the same evidence returns the
/// original run, and the same key for other evidence is a conflict.
/// </remarks>
public sealed class RunReconciliationUseCase
{
    private const int MaximumIdempotencyKeyLength = 256;

    private readonly IEvidenceStore _evidenceStore;
    private readonly IReconciliationStore _store;
    private readonly ReconciliationLedgerDerivation _ledger;
    private readonly TimeProvider _timeProvider;

    public RunReconciliationUseCase(
        IEvidenceStore evidenceStore,
        IReconciliationStore store,
        IInventoryReadStore inventoryReadStore,
        TimeProvider timeProvider)
    {
        _evidenceStore = evidenceStore
            ?? throw new ArgumentNullException(nameof(evidenceStore));
        _store = store
            ?? throw new ArgumentNullException(nameof(store));
        _ledger = new ReconciliationLedgerDerivation(
            inventoryReadStore
            ?? throw new ArgumentNullException(nameof(inventoryReadStore)));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<RunReconciliationResult> ExecuteAsync(
        string idempotencyKey,
        RunReconciliationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentNullException.ThrowIfNull(command);

        if (idempotencyKey.Length > MaximumIdempotencyKeyLength)
        {
            throw new ArgumentException(
                "The idempotency key cannot exceed 256 characters.",
                nameof(idempotencyKey));
        }

        if (command.HouseholdId == Guid.Empty || command.EvidenceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Household and evidence identities cannot be empty.",
                nameof(command));
        }

        var existing = await _store.FindRunByKeyAsync(
            command.HouseholdId,
            idempotencyKey,
            cancellationToken);

        if (existing is not null)
        {
            return Replay(command, existing);
        }

        var evidence = await _evidenceStore.FindEvidenceAsync(
            command.EvidenceId,
            cancellationToken);

        if (evidence is null
            || evidence.EvidenceId != command.EvidenceId
            || evidence.HouseholdId != command.HouseholdId)
        {
            throw ReconciliationException.Missing(
                ReconciliationErrorCodes.EvidenceNotFound,
                "The evidence record does not exist in this household.");
        }

        if (evidence.SupersededByEvidenceId is not null)
        {
            throw ReconciliationException.Conflict(
                ReconciliationErrorCodes.EvidenceSuperseded,
                "A superseded evidence record cannot be reconciled again; reconcile its replacement instead.");
        }

        var observations = await _evidenceStore.ListObservationsAsync(
            command.EvidenceId,
            cancellationToken);

        if (observations.Count == 0)
        {
            throw ReconciliationException.Invalid(
                ReconciliationErrorCodes.NoObservations,
                "The evidence record has no observations to reconcile.");
        }

        var ledger = await _ledger.DeriveAsync(
            command.HouseholdId,
            evidence.EffectiveDate,
            observations
                .Select(observation => (
                    new ReconciliationScope(
                        observation.PortfolioId,
                        observation.AccountId,
                        observation.AssetId),
                    observation.ObservedPieceCount is not null))
                .ToArray(),
            cancellationToken);

        ReconciliationRun run;

        try
        {
            run = ReconciliationRun.Create(
                Guid.NewGuid(),
                command.HouseholdId,
                command.EvidenceId,
                evidence.EffectiveDate,
                _timeProvider.GetUtcNow(),
                observations
                    .Select(observation =>
                    {
                        var side = ledger[new ReconciliationScope(
                            observation.PortfolioId,
                            observation.AccountId,
                            observation.AssetId)];

                        return new ReconciliationItemInput(
                            Guid.NewGuid(),
                            observation,
                            side.QuantityRawE8,
                            side.PieceCount);
                    })
                    .ToArray());
        }
        catch (OverflowException exception)
        {
            throw ReconciliationException.Invalid(
                ReconciliationErrorCodes.ArithmeticOverflow,
                "A reconciliation difference does not fit the fixed-point range.",
                exception);
        }

        var commit = await _store.TryCommitRunAsync(
            idempotencyKey,
            run,
            cancellationToken);

        if (commit.WasCommitted)
        {
            if (commit.Run.ReconciliationRunId != run.Id)
            {
                throw new InvalidOperationException(
                    "The reconciliation store returned an inconsistent committed run.");
            }

            return new RunReconciliationResult(run.Id, WasReplay: false);
        }

        return Replay(command, commit.Run);
    }

    private static RunReconciliationResult Replay(
        RunReconciliationCommand command,
        ReconciliationRunReference stored)
    {
        if (stored.HouseholdId != command.HouseholdId
            || stored.EvidenceId != command.EvidenceId)
        {
            throw new IdempotencyConflictException();
        }

        return new RunReconciliationResult(
            stored.ReconciliationRunId,
            WasReplay: true);
    }
}
