using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Tests.ValueObjects
{
    public sealed class QuantityTests
    {
        [Fact]
        public void FromDecimal_PreservesEightDecimalPrecision()
        {
            var quantity =
                Quantity.FromDecimal(6412.34918000m);

            Assert.Equal(
                6412.34918000m,
                quantity.ToDecimal());
        }

        [Fact]
        public void FromDecimal_RejectsNegativeQuantity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => Quantity.FromDecimal(-1m));
        }

        [Fact]
        public void FromDecimal_RejectsMoreThanEightDecimals()
        {
            Assert.Throws<ArgumentException>(
                () => Quantity.FromDecimal(
                    1.123456789m));
        }

        [Fact]
        public void Subtract_CannotProduceNegativeQuantity()
        {
            var current =
                Quantity.FromDecimal(500m);

            var requested =
                Quantity.FromDecimal(700m);

            Assert.Throws<InvalidOperationException>(
                () => current.Subtract(requested));
        }

        [Fact]
        public void Add_WhenRawQuantityOverflows_Throws()
        {
            var maximum = Quantity.FromRaw(long.MaxValue);
            var one = Quantity.FromRaw(1);

            Assert.Throws<OverflowException>(
                () => maximum.Add(one));
        }
    }
}
