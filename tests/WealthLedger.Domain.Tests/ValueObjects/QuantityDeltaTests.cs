using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Tests.ValueObjects
{
    public sealed class QuantityDeltaTests
    {
        [Fact]
        public void Delta_CanBeNegative()
        {
            var delta =
                QuantityDelta.FromDecimal(-1000m);

            Assert.True(delta.IsNegative);
            Assert.Equal(-1000m, delta.ToDecimal());
        }

        [Fact]
        public void Negate_ReturnsExactInverse()
        {
            var original =
                QuantityDelta.FromDecimal(6412.34918m);

            var inverse = original.Negate();

            Assert.Equal(
                -6412.34918m,
                inverse.ToDecimal());
        }

        [Fact]
        public void Negate_MinimumRawDelta_Throws()
        {
            var minimum = QuantityDelta.FromRaw(long.MinValue);

            Assert.Throws<OverflowException>(() =>
            {
                _ = minimum.Negate();
            });
        }
    }
}
