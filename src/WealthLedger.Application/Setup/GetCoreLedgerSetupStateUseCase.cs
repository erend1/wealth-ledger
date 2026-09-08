using WealthLedger.Application.LocalData;

namespace WealthLedger.Application.Setup
{
    public sealed class GetCoreLedgerSetupStateUseCase
    {
        private readonly ICoreLedgerSetupStateReader _reader;

        public GetCoreLedgerSetupStateUseCase(
            ICoreLedgerSetupStateReader reader)
        {
            _reader = reader
                ?? throw new ArgumentNullException(nameof(reader));
        }

        public Task<
            LocalDataOperationResult<CoreLedgerSetupStateSnapshot>> ExecuteAsync(
            CancellationToken cancellationToken = default)
            => _reader.ReadAsync(cancellationToken);
    }
}
