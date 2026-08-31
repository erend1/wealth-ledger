using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;

namespace WealthLedger.Application.CoreLedger
{
    public sealed record ReversePostedTransactionCommand(
        Guid OriginalTransactionId,
        string Reason);

    public sealed record ReversePostedTransactionResult(
        Guid ReversalTransactionId,
        Guid ReversalOfTransactionId);

    public sealed class ReversePostedTransactionUseCase
    {
        private readonly ILedgerReversalStore
            _reversalStore;

        private readonly TimeProvider
            _timeProvider;

        public ReversePostedTransactionUseCase(
            ILedgerReversalStore reversalStore,
            TimeProvider timeProvider)
        {
            _reversalStore =
                reversalStore
                ?? throw new ArgumentNullException(
                    nameof(reversalStore));

            _timeProvider =
                timeProvider
                ?? throw new ArgumentNullException(
                    nameof(timeProvider));
        }

        public async Task<ReversePostedTransactionResult?>
            ExecuteAsync(
                string idempotencyKey,
                ReversePostedTransactionCommand command,
                CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(
                idempotencyKey);

            var normalizedCommand =
                ReversePostedTransactionCommandCanonicalizer
                    .Normalize(command);

            // Phase 1:
            // Load only immutable identity needed for
            // household-scoped receipt lookup.
            var identity =
                await _reversalStore.FindTargetIdentityAsync(
                    normalizedCommand.OriginalTransactionId,
                    cancellationToken);

            if (identity is null)
            {
                return null;
            }

            if (identity.TransactionId
                != normalizedCommand.OriginalTransactionId
                || identity.HouseholdId == Guid.Empty)
            {
                throw new InvalidOperationException(
                    "The reversal store returned inconsistent target identity.");
            }

            var scope =
                new LedgerSubmissionScope(
                    identity.HouseholdId,
                    LedgerOperationCodes
                        .ReversePostedTransaction,
                    idempotencyKey);

            // CRITICAL:
            // Receipt replay comes before current reversal
            // eligibility. Otherwise a successful retry would
            // become ALREADY_REVERSED.
            var existingReceipt =
                await _reversalStore.FindReceiptAsync(
                    scope,
                    cancellationToken);

            if (existingReceipt is not null)
            {
                return ResolveReceipt(
                    scope,
                    normalizedCommand,
                    existingReceipt);
            }

            // Only a genuinely new logical command reaches
            // current eligibility evaluation.
            var candidate =
                await _reversalStore.LoadCandidateAsync(
                    normalizedCommand.OriginalTransactionId,
                    cancellationToken);

            if (candidate is null)
            {
                throw new InvalidOperationException(
                    "The reversal target disappeared after identity lookup.");
            }

            if (candidate.TransactionId
                != identity.TransactionId
                || candidate.HouseholdId
                != identity.HouseholdId)
            {
                throw new InvalidOperationException(
                    "The reversal store returned a candidate for a different target.");
            }

            var evaluation =
                ReversalEligibilityEvaluator.Evaluate(
                    candidate);

            if (!evaluation.CanReverse)
            {
                throw new ReversalCommandRejectedException(
                    evaluation);
            }

            var original =
                candidate.Original
                ?? throw new InvalidOperationException(
                    "An eligible reversal candidate must contain reconstructed Domain state.");

            var recordedAtUtc =
                _timeProvider.GetUtcNow();

            LedgerTransaction reversal;

            try
            {
                reversal =
                    LedgerTransaction.CreateReversal(
                        Guid.NewGuid(),
                        original,
                        recordedAtUtc,
                        normalizedCommand.Reason);

                ReversalAllocationApplier.Apply(
                    original,
                    reversal,
                    candidate.AffectedLots);

                reversal.Post(
                    recordedAtUtc);
            }
            catch (OverflowException)
            {
                throw new ReversalCommandRejectedException(
                    new ReversalEvaluation(
                        ReversalEligibilityCode
                            .UnsupportedPersistedShape,
                        null,
                        [],
                        [],
                        []));
            }

            var fingerprint =
                ReversePostedTransactionCommandFingerprint
                    .ComputeCurrent(
                        normalizedCommand);

            var receipt =
                new LedgerSubmissionReceipt(
                    scope,
                    fingerprint,
                    reversal.Id,
                    AssetLotId: null,
                    CreatedAtUtc: recordedAtUtc);

            var commitResult =
                await _reversalStore.TryCommitAsync(
                    receipt,
                    reversal,
                    candidate.AffectedLots,
                    cancellationToken);

            return commitResult switch
            {
                ReversalCommitResult.Committed committed =>
                    ResolveCommitted(
                        normalizedCommand,
                        receipt,
                        committed),

                ReversalCommitResult.ReceiptWinner winner =>
                    ResolveReceipt(
                        scope,
                        normalizedCommand,
                        winner.Receipt),

                ReversalCommitResult.AlreadyReversed already =>
                    throw new ReversalCommandRejectedException(
                        new ReversalEvaluation(
                            ReversalEligibilityCode
                                .AlreadyReversed,
                            EnsureNonEmpty(
                                already
                                    .ExistingReversalTransactionId,
                                "Existing reversal transaction ID"),
                            [],
                            [],
                            [])),

                ReversalCommitResult.DependencyConflict dependency =>
                    throw new ReversalCommandRejectedException(
                        new ReversalEvaluation(
                            ReversalEligibilityCode
                                .BlockedByDependencies,
                            null,
                            NormalizeBlockingIds(
                                dependency
                                    .BlockingTransactionIds),
                            [],
                            [])),

                _ =>
                    throw new InvalidOperationException(
                        "The reversal store returned an unknown commit result.")
            };
        }

        private static ReversePostedTransactionResult
            ResolveCommitted(
                ReversePostedTransactionCommand command,
                LedgerSubmissionReceipt attemptedReceipt,
                ReversalCommitResult.Committed result)
        {
            if (result.Receipt != attemptedReceipt)
            {
                throw new InvalidOperationException(
                    "The reversal store returned an inconsistent committed receipt.");
            }

            return new ReversePostedTransactionResult(
                result.Receipt.TransactionId,
                command.OriginalTransactionId);
        }

        private static ReversePostedTransactionResult
            ResolveReceipt(
                LedgerSubmissionScope expectedScope,
                ReversePostedTransactionCommand normalizedCommand,
                LedgerSubmissionReceipt receipt)
        {
            if (receipt.Scope != expectedScope)
            {
                throw new InvalidOperationException(
                    "The reversal store returned a receipt for a different scope.");
            }

            if (receipt.TransactionId == Guid.Empty
                || receipt.TransactionId
                == normalizedCommand.OriginalTransactionId)
            {
                throw new InvalidOperationException(
                    "The reversal receipt contains an invalid result transaction ID.");
            }

            if (receipt.AssetLotId is not null)
            {
                throw new InvalidOperationException(
                    "A reversal receipt cannot contain an asset-lot result.");
            }

            var replayFingerprint =
                ReversePostedTransactionCommandFingerprint
                    .Compute(
                        normalizedCommand,
                        receipt.Fingerprint.AlgorithmCode,
                        receipt.Fingerprint.Version);

            if (replayFingerprint
                != receipt.Fingerprint)
            {
                throw new IdempotencyConflictException();
            }

            return new ReversePostedTransactionResult(
                receipt.TransactionId,
                normalizedCommand.OriginalTransactionId);
        }

        private static Guid EnsureNonEmpty(
            Guid value,
            string fieldName)
        {
            if (value == Guid.Empty)
            {
                throw new InvalidOperationException(
                    $"{fieldName} cannot be empty.");
            }

            return value;
        }

        private static IReadOnlyList<Guid>
            NormalizeBlockingIds(
                IReadOnlyList<Guid> blockingTransactionIds)
        {
            ArgumentNullException.ThrowIfNull(
                blockingTransactionIds);

            if (blockingTransactionIds.Any(
                    x => x == Guid.Empty))
            {
                throw new InvalidOperationException(
                    "A blocking transaction ID cannot be empty.");
            }

            return blockingTransactionIds
                .Distinct()
                .OrderBy(x => x)
                .ToArray();
        }
    }
}