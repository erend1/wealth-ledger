using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;

namespace WealthLedger.Application.CoreLedger
{
    internal sealed record ReversalEvaluation(
        ReversalEligibilityCode Code,
        Guid? ExistingReversalTransactionId,
        IReadOnlyList<Guid> BlockingTransactionIds,
        IReadOnlyList<ReversalPreviewEntry> InverseEntries,
        IReadOnlyList<ReversalPreviewLotAllocation>
            InverseLotAllocations)
    {
        internal bool CanReverse =>
            Code == ReversalEligibilityCode.Eligible;
    }

    internal static class ReversalEligibilityEvaluator
    {
        internal static ReversalEvaluation Evaluate(
            ReversalCandidate candidate)
        {
            ArgumentNullException.ThrowIfNull(candidate);

            EnsureCandidateConsistency(candidate);

            if (candidate.Status != TransactionStatus.Posted)
            {
                return Empty(
                    ReversalEligibilityCode.NotPosted);
            }

            if (candidate.Type == TransactionType.Reversal)
            {
                return Empty(
                    ReversalEligibilityCode.TargetIsReversal);
            }

            if (candidate.ExistingReversalTransactionId
                is Guid reversalId)
            {
                if (reversalId == Guid.Empty)
                {
                    throw new InvalidOperationException(
                        "The reversal store returned an empty reversal ID.");
                }

                return new ReversalEvaluation(
                    ReversalEligibilityCode.AlreadyReversed,
                    reversalId,
                    [],
                    [],
                    []);
            }

            if (candidate.Original is null)
            {
                return Empty(
                    ReversalEligibilityCode
                        .UnsupportedPersistedShape);
            }

            IReadOnlyList<ReversalPreviewEntry>
                inverseEntries;

            IReadOnlyList<ReversalPreviewLotAllocation>
                inverseAllocations;

            try
            {
                (inverseEntries, inverseAllocations) =
                    BuildInverseEffects(
                        candidate.Original,
                        candidate.AffectedLots);
            }
            catch (OverflowException)
            {
                return Empty(
                    ReversalEligibilityCode
                        .UnsupportedPersistedShape);
            }

            var blockers =
                candidate.BlockingTransactionIds
                    .Where(x => x != Guid.Empty)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToArray();

            if (blockers.Length != 0)
            {
                return new ReversalEvaluation(
                    ReversalEligibilityCode
                        .BlockedByDependencies,
                    null,
                    blockers,
                    [],
                    []);
            }

            return new ReversalEvaluation(
                ReversalEligibilityCode.Eligible,
                null,
                [],
                inverseEntries,
                inverseAllocations);
        }

        private static (
            IReadOnlyList<ReversalPreviewEntry> Entries,
            IReadOnlyList<ReversalPreviewLotAllocation>
                Allocations)
            BuildInverseEffects(
                LedgerTransaction original,
                IReadOnlyCollection<AssetLot> affectedLots)
        {
            var originalEntries =
                original.Entries
                    .OrderBy(x => x.Sequence)
                    .ToArray();

            var entryById =
                originalEntries.ToDictionary(
                    x => x.Id);

            var inverseEntries =
                originalEntries
                    .Select(x =>
                        new ReversalPreviewEntry(
                            x.Sequence,
                            x.PortfolioId,
                            x.AccountId,
                            x.AssetId,
                            x.QuantityDelta.Negate(),
                            x.Role,
                            x.UnitPrice))
                    .ToArray();

            var inverseAllocations =
                affectedLots
                    .SelectMany(
                        lot =>
                            lot.Allocations
                                .Where(allocation =>
                                    entryById.ContainsKey(
                                        allocation
                                            .TransactionEntryId))
                                .Select(allocation =>
                                {
                                    var entry =
                                        entryById[
                                            allocation
                                                .TransactionEntryId];

                                    return new
                                        ReversalPreviewLotAllocation(
                                            lot.Id,
                                            entry.Id,
                                            entry.Sequence,
                                            allocation
                                                .QuantityDelta
                                                .Negate());
                                }))
                    .OrderBy(x => x.EntrySequence)
                    .ThenBy(x => x.AssetLotId)
                    .ToArray();

            return (
                inverseEntries,
                inverseAllocations);
        }

        private static void EnsureCandidateConsistency(
            ReversalCandidate candidate)
        {
            if (candidate.TransactionId == Guid.Empty
                || candidate.HouseholdId == Guid.Empty)
            {
                throw new InvalidOperationException(
                    "The reversal store returned invalid target identity.");
            }

            if (candidate.Original is not null)
            {
                if (candidate.Original.Id
                    != candidate.TransactionId
                    || candidate.Original.HouseholdId
                    != candidate.HouseholdId
                    || candidate.Original.Status
                    != candidate.Status
                    || candidate.Original.Type
                    != candidate.Type)
                {
                    throw new InvalidOperationException(
                        "The reversal store returned inconsistent reconstructed transaction state.");
                }
            }

            if (candidate.AffectedLots
                .GroupBy(x => x.Id)
                .Any(x => x.Count() > 1))
            {
                throw new InvalidOperationException(
                    "The reversal store returned duplicate affected lots.");
            }

            if (candidate.BlockingTransactionIds
                .Any(x => x == Guid.Empty))
            {
                throw new InvalidOperationException(
                    "The reversal store returned an empty blocking transaction ID.");
            }
        }

        private static ReversalEvaluation Empty(
            ReversalEligibilityCode code)
        {
            return new ReversalEvaluation(
                code,
                null,
                [],
                [],
                []);
        }
    }
}
