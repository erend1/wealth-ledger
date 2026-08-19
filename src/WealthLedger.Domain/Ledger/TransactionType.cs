namespace WealthLedger.Domain.Ledger
{
    public enum TransactionType
    {
        Contribution,
        Withdrawal,

        Buy,
        Sell,

        Transfer,

        Dividend,
        Income,
        Expense,

        Fee,
        Tax,

        CorporateAction,

        OpeningBalance,
        Adjustment,
        Reversal
    }
}
