using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;

namespace WealthLedger.Application.CoreLedger
{
    internal static class ReversalAllocationApplier
    {
        internal static void Apply(
            LedgerTransaction original,
            LedgerTransaction reversal,
            IReadOnlyCollection<AssetLot> affectedLots)
        {
            ArgumentNullException.ThrowIfNull(original);
            ArgumentNullException.ThrowIfNull(reversal);
            ArgumentNullException.ThrowIfNull(affectedLots);

            var originalEntries =
                original.Entries
                    .OrderBy(x => x.Sequence)
                    .ToArray();

            var reversalEntries =
                reversal.Entries
                    .OrderBy(x => x.Sequence)
                    .ToArray();

            if (originalEntries.Length
                != reversalEntries.Length)
            {
                throw new InvalidOperationException(
                    "The reversal does not contain the expected number of entries.");
            }

            var originalById =
                originalEntries.ToDictionary(
                    x => x.Id);

            var reversalBySequence =
                reversalEntries.ToDictionary(
                    x => x.Sequence);

            // Snapshot BEFORE mutation because AssetLot.Allocate()
            // appends to the allocation collection.
            var sourceAllocations =
                affectedLots
                    .SelectMany(
                        lot =>
                            lot.Allocations
                                .Where(allocation =>
                                    originalById.ContainsKey(
                                        allocation
                                            .TransactionEntryId))
                                .Select(allocation =>
                                    new
                                    {
                                        Lot = lot,
                                        Allocation = allocation,
                                        OriginalEntry =
                                            originalById[
                                                allocation
                                                    .TransactionEntryId]
                                    }))
                    .OrderBy(x =>
                        x.OriginalEntry.Sequence)
                    .ThenBy(x =>
                        x.Lot.Id)
                    .ToArray();

            foreach (var source in sourceAllocations)
            {
                var reversalEntry =
                    reversalBySequence[
                        source.OriginalEntry.Sequence];

                source.Lot.Allocate(
                    reversalEntry,
                    source.Allocation
                        .QuantityDelta
                        .Negate());
            }
        }
    }
}
