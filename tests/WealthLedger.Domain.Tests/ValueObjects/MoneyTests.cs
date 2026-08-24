using WealthLedger.Domain.Common;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Tests.ValueObjects
{
    public sealed class MoneyTests
    {
        [Fact]
        public void Add_WithSameCurrency_ReturnsSum()
        {
            var first = Money.FromMinorUnits(
                1_000,
                CurrencyCode.TRY);

            var second = Money.FromMinorUnits(
                500,
                CurrencyCode.TRY);

            var result = first.Add(second);

            Assert.Equal(1_500, result.MinorUnits);
            Assert.Equal(CurrencyCode.TRY, result.Currency);
        }

        [Fact]
        public void Add_WithDifferentCurrencies_Throws()
        {
            var tryAmount = Money.FromMinorUnits(
                1_000,
                CurrencyCode.TRY);

            var usdAmount = Money.FromMinorUnits(
                500,
                CurrencyCode.USD);

            Assert.Throws<DomainRuleViolationException>(
                () => tryAmount.Add(usdAmount));
        }

        [Fact]
        public void ToDecimal_UsesProvidedMinorUnitDigits()
        {
            var money = Money.FromMinorUnits(
                3_000_000,
                CurrencyCode.TRY);

            var result = money.ToDecimal(2);

            Assert.Equal(30_000m, result);
        }

        [Fact]
        public void Add_WhenMinorUnitsOverflow_Throws()
        {
            var maximum = Money.FromMinorUnits(
                long.MaxValue,
                CurrencyCode.TRY);

            var one = Money.FromMinorUnits(
                1,
                CurrencyCode.TRY);

            Assert.Throws<OverflowException>(
                () => maximum.Add(one));
        }

        [Fact]
        public void Negate_MinimumMinorUnits_Throws()
        {
            var minimum = Money.FromMinorUnits(
                long.MinValue,
                CurrencyCode.TRY);

            Assert.Throws<OverflowException>(() =>
            {
                _ = minimum.Negate();
            });
        }
    }
}
