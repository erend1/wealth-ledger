namespace WealthLedger.Application.Setup;

public sealed class CoreLedgerAlreadyInitializedException
    : InvalidOperationException
{
    public CoreLedgerAlreadyInitializedException()
        : base("Core ledger setup has already been completed.")
    {
    }
}
