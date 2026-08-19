using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Tests.Lots
{
    public sealed class CostBasisTests
    {
        [Fact]
        public void KnownCostBasis_RequiresAmount()
        {
            var amount =
                Money.FromMinorUnits(
                    3_000_000,
                    CurrencyCode.TRY);

            var basis =
                CostBasis.Known(amount);

            Assert.Equal(
                CostBasisStatus.Known,
                basis.Status);

            Assert.Equal(
                amount,
                basis.Amount);
        }

        [Fact]
        public void UnknownCostBasis_HasNoAmount()
        {
            var basis =
                CostBasis.Unknown();

            Assert.Equal(
                CostBasisStatus.Unknown,
                basis.Status);

            Assert.Null(
                basis.Amount);
        }

        [Fact]
        public void KnownCostBasis_CannotBeNegative()
        {
            var negative =
                Money.FromMinorUnits(
                    -1,
                    CurrencyCode.TRY);

            Assert.Throws<ArgumentException>(
                () => CostBasis.Known(negative));
        }
    }
}
