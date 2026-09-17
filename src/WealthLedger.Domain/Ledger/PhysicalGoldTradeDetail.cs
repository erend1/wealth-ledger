namespace WealthLedger.Domain.Ledger
{
    /// <summary>
    /// Optional counterparty evidence for one physical-gold purchase or sale.
    /// </summary>
    /// <remarks>
    /// The counterparty is independent from the institution attached to a
    /// custody or cash account. Institution activity and supported type are
    /// current-reference checks owned by the application and persistence
    /// boundary.
    /// </remarks>
    public sealed class PhysicalGoldTradeDetail
    {
        public Guid LedgerTransactionId { get; }

        public Guid? CounterpartyInstitutionId { get; }

        internal PhysicalGoldTradeDetail(
            Guid ledgerTransactionId,
            Guid? counterpartyInstitutionId)
        {
            if (ledgerTransactionId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Ledger transaction ID cannot be empty.",
                    nameof(ledgerTransactionId));
            }

            if (counterpartyInstitutionId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Counterparty institution ID cannot be empty.",
                    nameof(counterpartyInstitutionId));
            }

            LedgerTransactionId = ledgerTransactionId;
            CounterpartyInstitutionId = counterpartyInstitutionId;
        }
    }
}
