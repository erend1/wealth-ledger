namespace WealthLedger.Application.Navigation;

public sealed class NavigationRequestException : Exception
{
    public const string PageSizeInvalidCode =
        "NAVIGATION_PAGE_SIZE_INVALID";
    public const string FilterInvalidCode =
        "NAVIGATION_FILTER_INVALID";
    public const string CursorInvalidCode =
        "NAVIGATION_CURSOR_INVALID";
    public const string CursorScopeMismatchCode =
        "NAVIGATION_CURSOR_SCOPE_MISMATCH";

    public NavigationRequestException(
        string errorCode,
        string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}

public sealed class HouseholdNotFoundException : Exception
{
    public const string ErrorCode = "HOUSEHOLD_NOT_FOUND";

    public HouseholdNotFoundException()
        : base("The requested household does not exist.")
    {
    }
}

public sealed class PositionScopeNotFoundException : Exception
{
    public const string ErrorCode = "POSITION_SCOPE_NOT_FOUND";

    public PositionScopeNotFoundException()
        : base("The requested position scope does not exist.")
    {
    }
}

public sealed class NavigationPersistenceException : Exception
{
    public NavigationPersistenceException(string message)
        : base(message)
    {
    }
}
