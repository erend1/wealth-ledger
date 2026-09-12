using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Tests.ValueObjects
{
    public sealed class FinenessTests
    {
        [Fact]
        public void FromPerMille_MapsAcceptedPresetsExactly()
        {
            var cases = new[]
            {
                (PerMille: 999.9m, ExpectedPpm: 999_900),
                (PerMille: 916m, ExpectedPpm: 916_000),
                (PerMille: 750m, ExpectedPpm: 750_000),
                (PerMille: 585m, ExpectedPpm: 585_000),
                (PerMille: 333m, ExpectedPpm: 333_000)
            };

            foreach (var testCase in cases)
            {
                var fineness =
                    Fineness.FromPerMille(
                        testCase.PerMille);

                Assert.Equal(
                    testCase.ExpectedPpm,
                    fineness.Ppm);

                Assert.Equal(
                    testCase.PerMille,
                    fineness.ToPerMille());
            }
        }

        [Fact]
        public void FromPerMille_MapsPreciseMinimumAndPureGoldExactly()
        {
            Assert.Equal(
                1,
                Fineness.FromPerMille(0.001m).Ppm);

            Assert.Equal(
                Fineness.MaximumPpm,
                Fineness.FromPerMille(1_000m).Ppm);
        }

        [Fact]
        public void FromPerMille_RejectsExtraPrecisionInsteadOfRounding()
        {
            Assert.Throws<ArgumentException>(
                () => Fineness.FromPerMille(916.0001m));
        }

        [Fact]
        public void FromPerMille_RejectsValuesOutsideAcceptedRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => Fineness.FromPerMille(0m));

            Assert.Throws<ArgumentOutOfRangeException>(
                () => Fineness.FromPerMille(1_000.001m));
        }
    }
}
