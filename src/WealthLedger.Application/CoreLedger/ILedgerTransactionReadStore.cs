namespace WealthLedger.Application.CoreLedger
{
    public interface ILedgerTransactionReadStore
    {
        Task<LedgerTransactionDetail?> FindByIdAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default);
    }
}
