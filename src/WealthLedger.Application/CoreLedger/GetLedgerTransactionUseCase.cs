namespace WealthLedger.Application.CoreLedger
{
    public sealed class GetLedgerTransactionUseCase
    {
        private readonly ILedgerTransactionReadStore
            _readStore;

        public GetLedgerTransactionUseCase(
            ILedgerTransactionReadStore readStore)
        {
            _readStore = readStore
                ?? throw new ArgumentNullException(
                    nameof(readStore));
        }

        public Task<LedgerTransactionDetail?> ExecuteAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default)
        {
            if (transactionId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Transaction ID cannot be empty.",
                    nameof(transactionId));
            }

            return _readStore.FindByIdAsync(
                transactionId,
                cancellationToken);
        }
    }
}
