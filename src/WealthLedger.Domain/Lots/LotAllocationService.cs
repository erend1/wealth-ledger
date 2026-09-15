using WealthLedger.Domain.Common;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Lots
{
    public sealed class LotAllocationService
    {
        public IReadOnlyList<LotAllocationPlanItem> PlanFifo(
            Guid assetId,
            Quantity requestedQuantity,
            IReadOnlyCollection<AssetLot> candidateLots)
        {
            if (assetId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Asset ID cannot be empty.",
                    nameof(assetId));
            }

            ArgumentNullException.ThrowIfNull(candidateLots);

            if (requestedQuantity.RawE8 == 0)
            {
                throw new DomainRuleViolationException(
                    "Requested allocation quantity must be greater than zero.");
            }

            if (candidateLots.Any(x =>
                    x.AssetId != assetId))
            {
                throw new DomainRuleViolationException(
                    "FIFO candidate lots must all belong to the requested asset.");
            }

            var openLots = candidateLots
                .Where(x =>
                    x.CurrentQuantity.RawE8 > 0)
                .OrderBy(x =>
                    x.AcquiredOn ?? DateOnly.MinValue)
                .ThenBy(x =>
                    x.CreatedAtUtc)
                .ThenBy(x =>
                    x.Id)
                .ToList();

            var totalAvailable = SumAvailable(openLots);

            if (totalAvailable < requestedQuantity.RawE8)
            {
                throw new DomainRuleViolationException(
                    "Insufficient lot quantity for the requested allocation.");
            }

            var remaining =
                requestedQuantity.RawE8;

            var result =
                new List<LotAllocationPlanItem>();

            foreach (var lot in openLots)
            {
                if (remaining == 0)
                {
                    break;
                }

                var available =
                    lot.CurrentQuantity.RawE8;

                var allocated =
                    Math.Min(
                        available,
                        remaining);

                result.Add(
                    new LotAllocationPlanItem(
                        lot.Id,
                        Quantity.FromRaw(allocated)));

                remaining -= allocated;
            }

            if (remaining != 0)
            {
                throw new InvalidOperationException(
                    "FIFO allocation failed despite sufficient available quantity.");
            }

            return result;
        }

        /// <summary>
        /// Plans a first-in-first-out allocation over lots whose availability
        /// has already been derived for one custody scope.
        /// </summary>
        /// <remarks>
        /// This overload exists because <see cref="PlanFifo"/> reads
        /// <see cref="AssetLot.CurrentQuantity"/>, which is the household-wide
        /// quantity of a lot rather than the quantity held in the selected
        /// portfolio and account. A sale must consume only what the selected
        /// scope actually holds, so the caller derives scoped availability and
        /// passes it in explicitly.
        ///
        /// Ordering keeps the existing unknown-date-first policy. An unknown
        /// acquisition date sorts before every known date, which is the
        /// conservative choice for pre-cutover holdings but is not a claim
        /// about when they were really bought; the review must say so.
        /// </remarks>
        public IReadOnlyList<LotAllocationPlanItem> PlanScopedFifo(
            Guid assetId,
            Quantity requestedQuantity,
            IReadOnlyCollection<ScopedLotCandidate> candidates)
        {
            if (assetId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Asset ID cannot be empty.",
                    nameof(assetId));
            }

            ArgumentNullException.ThrowIfNull(candidates);

            if (requestedQuantity.RawE8 <= 0)
            {
                throw new DomainRuleViolationException(
                    "Requested allocation quantity must be greater than zero.");
            }

            if (candidates.Any(x =>
                    x.AssetId != assetId))
            {
                throw new DomainRuleViolationException(
                    "FIFO candidate lots must all belong to the requested asset.");
            }

            if (candidates
                .GroupBy(x => x.AssetLotId)
                .Any(group => group.Count() > 1))
            {
                throw new DomainRuleViolationException(
                    "FIFO candidate lots cannot contain duplicate lot identities.");
            }

            var openCandidates =
                candidates
                    .Where(x =>
                        x.AvailableQuantity.RawE8 > 0)
                    .OrderBy(x =>
                        x.AcquiredOn ?? DateOnly.MinValue)
                    .ThenBy(x =>
                        x.CreatedAtUtc)
                    .ThenBy(x =>
                        x.AssetLotId)
                    .ToList();

            long totalAvailable = 0;

            foreach (var candidate in openCandidates)
            {
                totalAvailable = checked(
                    totalAvailable
                    + candidate.AvailableQuantity.RawE8);
            }

            if (totalAvailable < requestedQuantity.RawE8)
            {
                throw new DomainRuleViolationException(
                    "Insufficient lot quantity for the requested allocation.");
            }

            var remaining =
                requestedQuantity.RawE8;

            var result =
                new List<LotAllocationPlanItem>();

            foreach (var candidate in openCandidates)
            {
                if (remaining == 0)
                {
                    break;
                }

                var allocated =
                    Math.Min(
                        candidate.AvailableQuantity.RawE8,
                        remaining);

                result.Add(
                    new LotAllocationPlanItem(
                        candidate.AssetLotId,
                        Quantity.FromRaw(allocated)));

                remaining -= allocated;
            }

            if (remaining != 0)
            {
                throw new InvalidOperationException(
                    "FIFO allocation failed despite sufficient available quantity.");
            }

            return result;
        }

        private static long SumAvailable(
            IEnumerable<AssetLot> lots)
        {
            long total = 0;

            foreach (var lot in lots)
            {
                total = checked(
                    total
                    + lot.CurrentQuantity.RawE8);
            }

            return total;
        }
    }
}
