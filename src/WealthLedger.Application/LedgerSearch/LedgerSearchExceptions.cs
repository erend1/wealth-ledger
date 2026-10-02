namespace WealthLedger.Application.LedgerSearch;

public sealed class LedgerSearchRequestException : Exception
{
    public const string PageSizeInvalidCode = "LEDGER_SEARCH_PAGE_SIZE_INVALID";
    public const string FilterInvalidCode = "LEDGER_SEARCH_FILTER_INVALID";
    public const string CursorInvalidCode = "LEDGER_SEARCH_CURSOR_INVALID";
    public const string CursorScopeMismatchCode =
        "LEDGER_SEARCH_CURSOR_SCOPE_MISMATCH";

    public LedgerSearchRequestException(string errorCode, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}

public sealed class LedgerSearchHouseholdNotFoundException : Exception
{
    public const string ErrorCode = "LEDGER_SEARCH_HOUSEHOLD_NOT_FOUND";

    public LedgerSearchHouseholdNotFoundException()
        : base("The requested household does not exist.")
    {
    }
}

public sealed class LedgerSearchFilterScopeNotFoundException : Exception
{
    public const string ErrorCode = "LEDGER_SEARCH_FILTER_SCOPE_NOT_FOUND";

    public LedgerSearchFilterScopeNotFoundException()
        : base("One or more requested ledger-search filter identities do not exist in the requested scope.")
    {
    }
}

public sealed class LedgerSearchPersistenceException : Exception
{
    public LedgerSearchPersistenceException(string message)
        : base(message)
    {
    }
}
