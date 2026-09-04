using WealthLedger.Application.LocalData;

namespace WealthLedger.Application.Setup
{
    public interface ICoreLedgerSetupSessionFactory
    {
        Task<LocalDataOperationResult<ICoreLedgerSetupSession>> OpenAsync(
            CancellationToken cancellationToken = default);
    }

    public interface ICoreLedgerSetupSession : IAsyncDisposable
    {
        Task<bool> TryInitializeAsync(
            CoreLedgerSetup setup,
            CancellationToken cancellationToken = default);
    }
}
