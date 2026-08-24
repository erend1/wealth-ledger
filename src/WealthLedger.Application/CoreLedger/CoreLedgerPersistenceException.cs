namespace WealthLedger.Application.CoreLedger;

public sealed class CoreLedgerPersistenceException : Exception
{
    public CoreLedgerPersistenceException(string message)
        : base(message)
    {
    }

    public CoreLedgerPersistenceException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
