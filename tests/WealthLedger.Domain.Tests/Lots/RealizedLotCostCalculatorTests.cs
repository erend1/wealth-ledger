using WealthLedger.Domain.Common;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Tests.Lots
{
    public sealed class RealizedLotCostCalculatorTests
    {
        private static readonly Guid LotId =
            Guid.Parse("a0000000-0000-0000-0000-000000000001");

        private static readonly Guid SecondLotId =
            Guid.Parse("a0000000-0000-0000-0000-000000000002");

        private static readonly Guid SaleOne =
            Guid.Parse("b0000000-0000-0000-0000-000000000001");

        private static readonly Guid SaleTwo =
            Guid.Parse("b0000000-0000-0000-0000-000000000002");

        private static readonly Guid SaleThree =
            Guid.Parse("b0000000-0000-0000-0000-000000000003");

        [Fact]
        public void Compute_FullDisposal_AssignsExactlyTheOriginalCost()
        {
            var history =
                KnownLot(
                    originalQuantity: 100m,
                    costMinorUnits: 1_000_00,
                    (SaleOne, 100m));

            var result =
                RealizedLotCostCalculator.Compute(
                    SaleOne,
                    [history]);

            Assert.Equal(
                RealizedCostCompleteness.CompleteKnown,
                result.Completeness);

            Assert.Equal(
                1_000_00,
                Assert.Single(result.KnownAmountsByCurrency)
                    .MinorUnits);
        }

        /*
         * The defining property of ADR-009: partial sales that individually
         * need rounding must still add up to exactly the original cost once
         * the lot closes. Three equal thirds of one hundred minor units is
         * the smallest case where independent rounding would lose or invent a
         * minor unit.
         */
        [Fact]
        public void Compute_ThreeEqualPartialSales_ConservesTotalCost()
        {
            var disposals =
                new[]
                {
                    (SaleOne, 1m),
                    (SaleTwo, 1m),
                    (SaleThree, 1m)
                };

            var assigned =
                new List<long>();

            foreach (var (saleId, _) in disposals)
            {
                var result =
                    RealizedLotCostCalculator.Compute(
                        saleId,
                        [
                            KnownLot(
                                originalQuantity: 3m,
                                costMinorUnits: 100,
                                disposals)
                        ]);

                assigned.Add(
                    Assert.Single(
                            result.KnownAmountsByCurrency)
                        .MinorUnits);
            }

            // R(i) = round_even(100 * D(i) / 3) over the running total gives
            // 33, 67, 100, so the per-sale increments are 33, 34, 33.
            Assert.Equal(
                [33, 34, 33],
                assigned);

            Assert.Equal(
                100,
                assigned.Sum());
        }

        [Fact]
        public void Compute_ExactMidpoint_RoundsToEven()
        {
            // Half of one lot, cost 5 minor units: the running total is
            // exactly 2.5 and must resolve to the even 2, not 3.
            var result =
                RealizedLotCostCalculator.Compute(
                    SaleOne,
                    [
                        KnownLot(
                            originalQuantity: 2m,
                            costMinorUnits: 5,
                            (SaleOne, 1m),
                            (SaleTwo, 1m))
                    ]);

            Assert.Equal(
                2,
                Assert.Single(result.KnownAmountsByCurrency)
                    .MinorUnits);
        }

        [Fact]
        public void Compute_SecondHalfAfterMidpoint_TakesTheRemainder()
        {
            var result =
                RealizedLotCostCalculator.Compute(
                    SaleTwo,
                    [
                        KnownLot(
                            originalQuantity: 2m,
                            costMinorUnits: 5,
                            (SaleOne, 1m),
                            (SaleTwo, 1m))
                    ]);

            Assert.Equal(
                3,
                Assert.Single(result.KnownAmountsByCurrency)
                    .MinorUnits);
        }

        [Fact]
        public void Compute_KnownZeroCost_StaysKnown()
        {
            var result =
                RealizedLotCostCalculator.Compute(
                    SaleOne,
                    [
                        KnownLot(
                            originalQuantity: 10m,
                            costMinorUnits: 0,
                            (SaleOne, 4m))
                    ]);

            Assert.Equal(
                RealizedCostCompleteness.CompleteKnown,
                result.Completeness);

            Assert.Equal(
                0,
                Assert.Single(result.KnownAmountsByCurrency)
                    .MinorUnits);
        }

        [Fact]
        public void Compute_UnknownCostLot_NeverBecomesZero()
        {
            var result =
                RealizedLotCostCalculator.Compute(
                    SaleOne,
                    [
                        UnknownLot(
                            LotId,
                            originalQuantity: 10m,
                            (SaleOne, 4m))
                    ]);

            Assert.Equal(
                RealizedCostCompleteness.Unknown,
                result.Completeness);

            Assert.Empty(
                result.KnownAmountsByCurrency);

            Assert.Equal(
                4m,
                result.UnknownQuantity.ToDecimal());

            Assert.Null(
                Assert.Single(result.Lines).KnownCost);
        }

        [Fact]
        public void Compute_MixedKnownAndUnknownLots_ReportsPartiallyKnown()
        {
            var result =
                RealizedLotCostCalculator.Compute(
                    SaleOne,
                    [
                        KnownLot(
                            originalQuantity: 10m,
                            costMinorUnits: 500,
                            (SaleOne, 10m)),

                        UnknownLot(
                            SecondLotId,
                            originalQuantity: 10m,
                            (SaleOne, 5m))
                    ]);

            Assert.Equal(
                RealizedCostCompleteness.PartiallyKnown,
                result.Completeness);

            Assert.Equal(
                10m,
                result.KnownQuantity.ToDecimal());

            Assert.Equal(
                5m,
                result.UnknownQuantity.ToDecimal());

            Assert.Equal(
                500,
                Assert.Single(result.KnownAmountsByCurrency)
                    .MinorUnits);
        }

        [Fact]
        public void Compute_DifferentCostCurrencies_StayInSeparateBuckets()
        {
            var result =
                RealizedLotCostCalculator.Compute(
                    SaleOne,
                    [
                        KnownLot(
                            originalQuantity: 10m,
                            costMinorUnits: 300,
                            (SaleOne, 10m)),

                        KnownLot(
                            SecondLotId,
                            originalQuantity: 10m,
                            costMinorUnits: 700,
                            currency: new CurrencyCode("USD"),
                            (SaleOne, 10m))
                    ]);

            Assert.Equal(
                2,
                result.KnownAmountsByCurrency.Count);

            Assert.Equal(
                "TRY",
                result.KnownAmountsByCurrency[0]
                    .Currency.Value);

            Assert.Equal(
                "USD",
                result.KnownAmountsByCurrency[1]
                    .Currency.Value);

            Assert.Equal(
                RealizedCostCompleteness.CompleteKnown,
                result.Completeness);
        }

        /*
         * A reversed sale leaves the effective sequence entirely, so the
         * remaining effective sales are re-apportioned. The accepted cost of
         * that choice is that a later correction can move a minor unit of
         * derived cost onto a sale that did not itself change.
         */
        [Fact]
        public void Compute_AfterEarlierSaleIsReversed_RedistributesResidue()
        {
            var beforeReversal =
                RealizedLotCostCalculator.Compute(
                    SaleTwo,
                    [
                        KnownLot(
                            originalQuantity: 3m,
                            costMinorUnits: 100,
                            (SaleOne, 1m),
                            (SaleTwo, 1m),
                            (SaleThree, 1m))
                    ]);

            var afterReversal =
                RealizedLotCostCalculator.Compute(
                    SaleTwo,
                    [
                        KnownLot(
                            originalQuantity: 3m,
                            costMinorUnits: 100,
                            (SaleTwo, 1m),
                            (SaleThree, 1m))
                    ]);

            // Before the reversal this sale is second in the sequence and
            // carries the rounding residue.
            Assert.Equal(
                34,
                Assert.Single(
                        beforeReversal.KnownAmountsByCurrency)
                    .MinorUnits);

            // Once the earlier sale leaves the effective sequence this sale
            // becomes first, and the residue moves off it even though the
            // sale itself did not change. This is the accepted cost of exact
            // conservation, which is why every result is labelled derived and
            // carries its effective as-of context.
            Assert.Equal(
                33,
                Assert.Single(
                        afterReversal.KnownAmountsByCurrency)
                    .MinorUnits);

            var laterSaleAfterReversal =
                RealizedLotCostCalculator.Compute(
                    SaleThree,
                    [
                        KnownLot(
                            originalQuantity: 3m,
                            costMinorUnits: 100,
                            (SaleTwo, 1m),
                            (SaleThree, 1m))
                    ]);

            // The reversed sale's share is not silently kept: the still
            // effective sales account for the correctly rounded cost of the
            // quantity that is still effectively disposed.
            Assert.Equal(
                34,
                Assert.Single(
                        laterSaleAfterReversal
                            .KnownAmountsByCurrency)
                    .MinorUnits);
        }

        [Fact]
        public void Compute_VeryLargeValues_DoesNotOverflow()
        {
            var history =
                new RealizedCostLotHistory(
                    LotId,
                    Quantity.FromRaw(long.MaxValue),
                    CostBasis.Known(
                        Money.FromMinorUnits(
                            long.MaxValue,
                            new CurrencyCode("TRY"))),
                    [
                        Disposal(
                            SaleOne,
                            Quantity.FromRaw(
                                long.MaxValue / 2),
                            0)
                    ]);

            var result =
                RealizedLotCostCalculator.Compute(
                    SaleOne,
                    [history]);

            Assert.Equal(
                long.MaxValue / 2,
                Assert.Single(result.KnownAmountsByCurrency)
                    .MinorUnits);
        }

        [Fact]
        public void Compute_OverDisposedLot_FailsClosed()
        {
            var history =
                KnownLot(
                    originalQuantity: 5m,
                    costMinorUnits: 100,
                    (SaleOne, 4m),
                    (SaleTwo, 2m));

            var exception =
                Assert.Throws<DomainRuleViolationException>(
                    () =>
                        RealizedLotCostCalculator.Compute(
                            SaleOne,
                            [history]));

            Assert.Contains(
                "exceed",
                exception.Message,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Compute_NotApplicableCostBasis_FailsClosed()
        {
            var history =
                new RealizedCostLotHistory(
                    LotId,
                    Quantity.FromDecimal(10m),
                    CostBasis.NotApplicable(),
                    [
                        Disposal(
                            SaleOne,
                            Quantity.FromDecimal(1m),
                            0)
                    ]);

            Assert.Throws<DomainRuleViolationException>(
                () =>
                    RealizedLotCostCalculator.Compute(
                        SaleOne,
                        [history]));
        }

        [Fact]
        public void Compute_SaleAbsentFromEveryLot_FailsClosed()
        {
            var history =
                KnownLot(
                    originalQuantity: 10m,
                    costMinorUnits: 100,
                    (SaleOne, 1m));

            Assert.Throws<DomainRuleViolationException>(
                () =>
                    RealizedLotCostCalculator.Compute(
                        SaleThree,
                        [history]));
        }

        [Fact]
        public void Compute_SameEffectiveHistory_IsDeterministic()
        {
            RealizedCostLotHistory Build()
                => KnownLot(
                    originalQuantity: 7m,
                    costMinorUnits: 1_234,
                    (SaleOne, 2m),
                    (SaleTwo, 3m));

            var first =
                RealizedLotCostCalculator.Compute(
                    SaleTwo,
                    [Build()]);

            var second =
                RealizedLotCostCalculator.Compute(
                    SaleTwo,
                    [Build()]);

            Assert.Equal(
                first.KnownAmountsByCurrency[0].MinorUnits,
                second.KnownAmountsByCurrency[0].MinorUnits);

            Assert.Equal(
                "ADR009_CUMULATIVE_ROUND_HALF_TO_EVEN_V1",
                first.MethodCode);
        }

        private static RealizedCostLotHistory KnownLot(
            decimal originalQuantity,
            long costMinorUnits,
            params (Guid SaleId, decimal Quantity)[] disposals)
            => KnownLot(
                LotId,
                originalQuantity,
                costMinorUnits,
                new CurrencyCode("TRY"),
                disposals);

        private static RealizedCostLotHistory KnownLot(
            Guid lotId,
            decimal originalQuantity,
            long costMinorUnits,
            CurrencyCode currency,
            params (Guid SaleId, decimal Quantity)[] disposals)
        {
            return new RealizedCostLotHistory(
                lotId,
                Quantity.FromDecimal(originalQuantity),
                CostBasis.Known(
                    Money.FromMinorUnits(
                        costMinorUnits,
                        currency)),
                BuildDisposals(disposals));
        }

        private static RealizedCostLotHistory UnknownLot(
            Guid lotId,
            decimal originalQuantity,
            params (Guid SaleId, decimal Quantity)[] disposals)
        {
            return new RealizedCostLotHistory(
                lotId,
                Quantity.FromDecimal(originalQuantity),
                CostBasis.Unknown(),
                BuildDisposals(disposals));
        }

        private static IReadOnlyList<EffectiveLotDisposal> BuildDisposals(
            (Guid SaleId, decimal Quantity)[] disposals)
        {
            var result =
                new List<EffectiveLotDisposal>();

            for (var index = 0; index < disposals.Length; index++)
            {
                result.Add(
                    Disposal(
                        disposals[index].SaleId,
                        Quantity.FromDecimal(
                            disposals[index].Quantity),
                        index));
            }

            return result;
        }

        private static EffectiveLotDisposal Disposal(
            Guid saleId,
            Quantity quantity,
            int index)
        {
            return new EffectiveLotDisposal(
                saleId,
                new DateTimeOffset(
                    2026,
                    3,
                    1,
                    12,
                    0,
                    0,
                    TimeSpan.Zero)
                    .AddDays(index),
                EntrySequence: 0,
                AllocationId:
                    Guid.Parse(
                        $"c0000000-0000-0000-0000-{index:D12}"),
                quantity);
        }
    }
}
