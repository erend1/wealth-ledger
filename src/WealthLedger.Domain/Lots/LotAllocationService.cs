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
