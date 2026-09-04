using WealthLedger.Application.LocalData;

namespace WealthLedger.Application.Setup
{
    public sealed class CoreLedgerSetupUnavailableException : Exception
    {
        public CoreLedgerSetupUnavailableException(
            LocalDataFailureCategory category)
            : base("Core ledger setup is currently unavailable.")
        {
            Category = category;
        }

        public LocalDataFailureCategory Category { get; }
    }
}
