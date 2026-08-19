namespace WealthLedger.Domain.Ledger
{
    public sealed class CashFlowDetail
    {
        public CashFlowCategory Category { get; }

        public Guid? HouseholdMemberId { get; }

        internal CashFlowDetail(
            CashFlowCategory category,
            Guid? householdMemberId)
        {
            if (householdMemberId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Household member ID cannot be empty.",
                    nameof(householdMemberId));
            }

            Category = category;
            HouseholdMemberId = householdMemberId;
        }
    }
}
