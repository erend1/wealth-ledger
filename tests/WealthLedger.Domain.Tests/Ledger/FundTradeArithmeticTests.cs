using WealthLedger.Domain.Common;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Tests.Ledger
{
    public sealed class ExactIntegerTests
    {
        [Theory]
        [InlineData(10, 4, 2)] // 2.5 -> even 2
        [InlineData(14, 4, 4)] // 3.5 -> even 4
        [InlineData(9, 4, 2)] // 2.25 -> 2
        [InlineData(11, 4, 3)] // 2.75 -> 3
        [InlineData(8, 4, 2)] // exact
        [InlineData(0, 7, 0)]
        public void DivideRoundHalfToEven_ResolvesMidpointsToEven(
            long numerator,
            long denominator,
            long expected)
        {
            Assert.Equal(
                expected,
                (long)ExactInteger.DivideRoundHalfToEven(
                    numerator,
                    denominator));
        }

        [Fact]
        public void DivideRoundHalfToEven_NearMaximumDenominator_DoesNotOverflow()
        {
            // The tie test must not double the remainder, because doubling a
            // remainder close to Int128.MaxValue would overflow.
            var denominator =
                Int128.MaxValue;

            var numerator =
                Int128.MaxValue - 1;

            Assert.Equal(
                1,
                ExactInteger.DivideRoundHalfToEven(
                    numerator,
                    denominator));
        }

        [Fact]
        public void DivideRoundHalfToEven_NegativeNumerator_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                    ExactInteger.DivideRoundHalfToEven(-1, 2));
        }

        [Fact]
        public void DivideRoundHalfToEven_ZeroDenominator_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                    ExactInteger.DivideRoundHalfToEven(1, 0));
        }
    }

    public sealed class PriceImpliedAmountCalculatorTests
    {
        private static readonly CurrencyCode Try =
            new("TRY");

        [Fact]
        public void Compute_ExactProduct_ReportsExact()
        {
            var result =
                PriceImpliedAmountCalculator.Compute(
                    Quantity.FromDecimal(10m),
                    UnitPrice.FromDecimal(2.5m, Try),
                    minorUnitDigits: 2);

            Assert.Equal(
                25_00,
                result.RoundedAmount.MinorUnits);

            Assert.True(result.IsExact);
            Assert.False(result.IsRounded);
        }

        [Fact]
        public void Compute_InexactProduct_ReportsRounded()
        {
            // 3 units at 3.333333 => 9.999999, which is not a whole kurus.
            var result =
                PriceImpliedAmountCalculator.Compute(
                    Quantity.FromDecimal(3m),
                    UnitPrice.FromDecimal(3.333333m, Try),
                    minorUnitDigits: 2);

            Assert.Equal(
                10_00,
                result.RoundedAmount.MinorUnits);

            Assert.True(result.IsRounded);
        }

        [Fact]
        public void Compute_Midpoint_RoundsToEven()
        {
            // 1 unit at 0.125 with two minor digits is exactly 12.5 kurus.
            var result =
                PriceImpliedAmountCalculator.Compute(
                    Quantity.FromDecimal(1m),
                    UnitPrice.FromDecimal(0.125m, Try),
                    minorUnitDigits: 2);

            Assert.Equal(
                12,
                result.RoundedAmount.MinorUnits);
        }

        [Fact]
        public void Compute_ZeroDecimalCurrency_UsesWholeUnits()
        {
            // 3 units at 1.5 is exactly 4.5 whole units, another midpoint,
            // so it resolves to the even 4 rather than away from zero to 5.
            var result =
                PriceImpliedAmountCalculator.Compute(
                    Quantity.FromDecimal(3m),
                    UnitPrice.FromDecimal(1.5m, Try),
                    minorUnitDigits: 0);

            Assert.Equal(
                4,
                result.RoundedAmount.MinorUnits);

            // A midpoint is still a rounded result: half a whole unit was
            // dropped, so review must not present this as the exact product.
            Assert.True(result.IsRounded);
        }

        /*
         * Proves the intermediate really is wider than Int64. The product here
         * is 1e27, about eight orders of magnitude past Int64.MaxValue, yet
         * the final amount is exact. Computing this in Int64 would overflow,
         * and the literal q * p * 10^d / 10^16 form would overflow even
         * Int128 at the multiplication step.
         */
        [Fact]
        public void Compute_ProductBeyondInt64_StaysExact()
        {
            var result =
                PriceImpliedAmountCalculator.Compute(
                    Quantity.FromRaw(1_000_000_000_000_000_000L),
                    UnitPrice.FromRaw(1_000_000_000L, Try),
                    minorUnitDigits: 2);

            Assert.Equal(
                10_000_000_000_000L,
                result.RoundedAmount.MinorUnits);

            Assert.True(result.IsExact);
        }

        [Fact]
        public void Compute_ResultBeyondMoneyRange_FailsClosed()
        {
            Assert.Throws<DomainRuleViolationException>(
                () =>
                    PriceImpliedAmountCalculator.Compute(
                        Quantity.FromRaw(long.MaxValue),
                        UnitPrice.FromRaw(long.MaxValue, Try),
                        minorUnitDigits: 0));
        }

        [Fact]
        public void Compute_UnsupportedPrecision_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                    PriceImpliedAmountCalculator.Compute(
                        Quantity.FromDecimal(1m),
                        UnitPrice.FromDecimal(1m, Try),
                        minorUnitDigits: 9));
        }
    }

    public sealed class FundTradeCostRulesTests
    {
        [Theory]
        [InlineData(CostType.Commission)]
        [InlineData(CostType.Brokerage)]
        [InlineData(CostType.WithholdingTax)]
        [InlineData(CostType.OtherTax)]
        [InlineData(CostType.Other)]
        public void IsSupportedCostType_AcceptsFundVocabulary(CostType type)
        {
            Assert.True(
                FundTradeCostRules.IsSupportedCostType(type));
        }

        [Theory]
        [InlineData(CostType.MakingCharge)]
        [InlineData(CostType.TitleDeed)]
        [InlineData(CostType.Expertise)]
        [InlineData(CostType.Notary)]
        [InlineData(CostType.Insurance)]
        public void IsSupportedCostType_RejectsNonFundVocabulary(
            CostType type)
        {
            Assert.False(
                FundTradeCostRules.IsSupportedCostType(type));
        }

        [Theory]
        [InlineData(CostType.Commission, EntryRole.Fee)]
        [InlineData(CostType.Brokerage, EntryRole.Fee)]
        [InlineData(CostType.Other, EntryRole.Fee)]
        [InlineData(CostType.WithholdingTax, EntryRole.Tax)]
        [InlineData(CostType.OtherTax, EntryRole.Tax)]
        public void ResolveSupportingEntryRole_MapsToFeeOrTax(
            CostType type,
            EntryRole expected)
        {
            Assert.Equal(
                expected,
                FundTradeCostRules.ResolveSupportingEntryRole(type));
        }

        [Fact]
        public void ResolveSupportingEntryRole_UnsupportedType_FailsClosed()
        {
            Assert.Throws<DomainRuleViolationException>(
                () =>
                    FundTradeCostRules.ResolveSupportingEntryRole(
                        CostType.MakingCharge));
        }

        [Fact]
        public void IsSupportedTreatment_PurchaseRejectsWithheldFromProceeds()
        {
            Assert.False(
                FundTradeCostRules.IsSupportedTreatment(
                    TransactionType.Buy,
                    CostTreatment.WithheldFromProceeds));
        }

        [Fact]
        public void IsSupportedTreatment_SaleAcceptsWithheldFromProceeds()
        {
            Assert.True(
                FundTradeCostRules.IsSupportedTreatment(
                    TransactionType.Sell,
                    CostTreatment.WithheldFromProceeds));
        }

        [Fact]
        public void IsSupportedTreatment_NonTradeType_FailsClosed()
        {
            Assert.Throws<DomainRuleViolationException>(
                () =>
                    FundTradeCostRules.IsSupportedTreatment(
                        TransactionType.Contribution,
                        CostTreatment.AdditionalCashOutflow));
        }

        [Theory]
        [InlineData(CostTreatment.AdditionalCashOutflow, true)]
        [InlineData(CostTreatment.IncludedInConsideration, false)]
        [InlineData(CostTreatment.WithheldFromProceeds, false)]
        [InlineData(CostTreatment.InformationalOnly, false)]
        public void CreatesAdditionalCashEntry_OnlyForAdditionalOutflow(
            CostTreatment treatment,
            bool expected)
        {
            Assert.Equal(
                expected,
                FundTradeCostRules.CreatesAdditionalCashEntry(
                    treatment));
        }
    }
}
