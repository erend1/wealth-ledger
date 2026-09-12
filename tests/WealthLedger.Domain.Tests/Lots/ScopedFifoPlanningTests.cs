using WealthLedger.Domain.Common;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Tests.Lots
{
    public sealed class ScopedFifoPlanningTests
    {
        private readonly LotAllocationService _service = new();

        private static readonly Guid AssetId =
            Guid.Parse("d0000000-0000-0000-0000-000000000001");

        [Fact]
        public void PlanScopedFifo_ConsumesOldestAcquisitionFirst()
        {
            var older =
                Candidate(
                    "10000000-0000-0000-0000-000000000001",
                    available: 600m,
                    acquiredOn: new DateOnly(2026, 1, 1));

            var newer =
                Candidate(
                    "10000000-0000-0000-0000-000000000002",
                    available: 700m,
                    acquiredOn: new DateOnly(2026, 2, 1));

            var plan =
                _service.PlanScopedFifo(
                    AssetId,
                    Quantity.FromDecimal(1_000m),
                    [newer, older]);

            Assert.Equal(2, plan.Count);

            Assert.Equal(
                older.AssetLotId,
                plan[0].AssetLotId);

            Assert.Equal(
                600m,
                plan[0].Quantity.ToDecimal());

            Assert.Equal(
                newer.AssetLotId,
                plan[1].AssetLotId);

            Assert.Equal(
                400m,
                plan[1].Quantity.ToDecimal());
        }

        /*
         * An opening-balance lot may have no recorded acquisition date. It
         * sorts first, which is the conservative assumption for pre-cutover
         * holdings rather than a claim about the real purchase order.
         */
        [Fact]
        public void PlanScopedFifo_UnknownAcquisitionDateSortsFirst()
        {
            var dated =
                Candidate(
                    "10000000-0000-0000-0000-000000000002",
                    available: 100m,
                    acquiredOn: new DateOnly(2020, 1, 1));

            var undated =
                Candidate(
                    "10000000-0000-0000-0000-000000000003",
                    available: 100m,
                    acquiredOn: null);

            var plan =
                _service.PlanScopedFifo(
                    AssetId,
                    Quantity.FromDecimal(150m),
                    [dated, undated]);

            Assert.Equal(
                undated.AssetLotId,
                plan[0].AssetLotId);

            Assert.Equal(
                100m,
                plan[0].Quantity.ToDecimal());

            Assert.Equal(
                dated.AssetLotId,
                plan[1].AssetLotId);
        }

        [Fact]
        public void PlanScopedFifo_EqualDates_BreaksTiesDeterministically()
        {
            var sameDate = new DateOnly(2026, 5, 5);

            var createdEarlier =
                Candidate(
                    "10000000-0000-0000-0000-00000000000b",
                    available: 50m,
                    acquiredOn: sameDate,
                    createdAtUtc:
                        new DateTimeOffset(
                            2026, 5, 5, 8, 0, 0, TimeSpan.Zero));

            var createdLater =
                Candidate(
                    "10000000-0000-0000-0000-00000000000a",
                    available: 50m,
                    acquiredOn: sameDate,
                    createdAtUtc:
                        new DateTimeOffset(
                            2026, 5, 5, 9, 0, 0, TimeSpan.Zero));

            var plan =
                _service.PlanScopedFifo(
                    AssetId,
                    Quantity.FromDecimal(75m),
                    [createdLater, createdEarlier]);

            Assert.Equal(
                createdEarlier.AssetLotId,
                plan[0].AssetLotId);
        }

        /*
         * The point of the scoped overload: availability comes from the
         * selected portfolio and account, so a lot held elsewhere contributes
         * nothing even though its household-wide quantity is positive.
         */
        [Fact]
        public void PlanScopedFifo_IgnoresLotsWithNoAvailabilityInScope()
        {
            var heldElsewhere =
                Candidate(
                    "10000000-0000-0000-0000-000000000004",
                    available: 0m,
                    acquiredOn: new DateOnly(2020, 1, 1));

            var heldHere =
                Candidate(
                    "10000000-0000-0000-0000-000000000005",
                    available: 40m,
                    acquiredOn: new DateOnly(2026, 1, 1));

            var plan =
                _service.PlanScopedFifo(
                    AssetId,
                    Quantity.FromDecimal(40m),
                    [heldElsewhere, heldHere]);

            Assert.Equal(
                heldHere.AssetLotId,
                Assert.Single(plan).AssetLotId);
        }

        [Fact]
        public void PlanScopedFifo_InsufficientScopedQuantity_IsRejected()
        {
            var candidate =
                Candidate(
                    "10000000-0000-0000-0000-000000000006",
                    available: 10m,
                    acquiredOn: new DateOnly(2026, 1, 1));

            var exception =
                Assert.Throws<DomainRuleViolationException>(
                    () =>
                        _service.PlanScopedFifo(
                            AssetId,
                            Quantity.FromDecimal(11m),
                            [candidate]));

            Assert.Contains(
                "Insufficient",
                exception.Message,
                StringComparison.Ordinal);
        }

        [Fact]
        public void PlanScopedFifo_ZeroQuantity_IsRejected()
        {
            Assert.Throws<DomainRuleViolationException>(
                () =>
                    _service.PlanScopedFifo(
                        AssetId,
                        Quantity.Zero,
                        [
                            Candidate(
                                "10000000-0000-0000-0000-000000000007",
                                available: 10m,
                                acquiredOn: null)
                        ]));
        }

        [Fact]
        public void PlanScopedFifo_ForeignAsset_IsRejected()
        {
            var foreign =
                Candidate(
                    "10000000-0000-0000-0000-000000000008",
                    available: 10m,
                    acquiredOn: null) with
                {
                    AssetId =
                        Guid.Parse(
                            "d0000000-0000-0000-0000-000000000009")
                };

            Assert.Throws<DomainRuleViolationException>(
                () =>
                    _service.PlanScopedFifo(
                        AssetId,
                        Quantity.FromDecimal(5m),
                        [foreign]));
        }

        [Fact]
        public void PlanScopedFifo_DuplicateLotIdentity_IsRejected()
        {
            var candidate =
                Candidate(
                    "10000000-0000-0000-0000-00000000000c",
                    available: 10m,
                    acquiredOn: null);

            Assert.Throws<DomainRuleViolationException>(
                () =>
                    _service.PlanScopedFifo(
                        AssetId,
                        Quantity.FromDecimal(5m),
                        [candidate, candidate]));
        }

        [Fact]
        public void PlanScopedFifo_ExactSplitAcrossTwoLots_ReconcilesExactly()
        {
            var first =
                Candidate(
                    "10000000-0000-0000-0000-00000000000d",
                    available: 1.23456789m,
                    acquiredOn: new DateOnly(2026, 1, 1));

            var second =
                Candidate(
                    "10000000-0000-0000-0000-00000000000e",
                    available: 2.76543211m,
                    acquiredOn: new DateOnly(2026, 2, 1));

            var requested =
                Quantity.FromDecimal(4m);

            var plan =
                _service.PlanScopedFifo(
                    AssetId,
                    requested,
                    [first, second]);

            Assert.Equal(
                requested.RawE8,
                plan.Sum(x => x.Quantity.RawE8));
        }

        private static ScopedLotCandidate Candidate(
            string lotId,
            decimal available,
            DateOnly? acquiredOn,
            DateTimeOffset? createdAtUtc = null)
        {
            return new ScopedLotCandidate(
                Guid.Parse(lotId),
                AssetId,
                acquiredOn,
                createdAtUtc
                    ?? new DateTimeOffset(
                        2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Quantity.FromDecimal(available),
                CostBasis.Unknown());
        }
    }
}
