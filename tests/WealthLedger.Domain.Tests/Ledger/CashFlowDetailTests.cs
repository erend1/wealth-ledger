using WealthLedger.Domain.Ledger;

namespace WealthLedger.Domain.Tests.Ledger
{
    public sealed class CashFlowDetailTests
    {
        [Fact]
        public void Constructor_AllowsMemberAttribution()
        {
            var memberId = Guid.NewGuid();

            var detail = new CashFlowDetail(
                CashFlowCategory.Salary,
                memberId);

            Assert.Equal(
                CashFlowCategory.Salary,
                detail.Category);

            Assert.Equal(
                memberId,
                detail.HouseholdMemberId);
        }

        [Fact]
        public void Constructor_RejectsEmptyMemberId()
        {
            Assert.Throws<ArgumentException>(() =>
                new CashFlowDetail(
                    CashFlowCategory.Salary,
                    Guid.Empty));
        }
    }
}
