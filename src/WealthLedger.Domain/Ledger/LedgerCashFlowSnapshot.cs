namespace WealthLedger.Domain.Ledger
{
    public sealed record LedgerCashFlowSnapshot(
        CashFlowCategory Category,
        Guid? HouseholdMemberId);
}
