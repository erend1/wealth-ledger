using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Tests.Ledger
{
    public sealed class TransactionCostComponentTests
    {
        [Fact]
        public void Constructor_AllowsIncludedMakingCharge()
        {
            var cost = new TransactionCostComponent(
                Guid.NewGuid(),
                CostType.MakingCharge,
                CostTreatment.IncludedInConsideration,
                Money.FromMinorUnits(
                    480_000,
                    CurrencyCode.TRY),
                "Jeweler making charge");

            Assert.Equal(
                CostType.MakingCharge,
                cost.Type);

            Assert.Equal(
                CostTreatment.IncludedInConsideration,
                cost.Treatment);

            Assert.Equal(
                480_000,
                cost.Amount.MinorUnits);
        }

        [Fact]
        public void Constructor_RejectsNegativeCost()
        {
            var negative = Money.FromMinorUnits(
                -100,
                CurrencyCode.TRY);

            Assert.Throws<ArgumentException>(() =>
                new TransactionCostComponent(
                    Guid.NewGuid(),
                    CostType.Commission,
                    CostTreatment.AdditionalCashOutflow,
                    negative,
                    null));
        }
    }
}
