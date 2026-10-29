namespace WealthLedger.Application.Inventory;

public sealed class InventoryRequestException : Exception
{
    public const string FilterInvalidCode = "INVENTORY_FILTER_INVALID";
    public const string PageSizeInvalidCode = "INVENTORY_PAGE_SIZE_INVALID";
    public const string CursorInvalidCode = "INVENTORY_CURSOR_INVALID";
    public const string CursorScopeMismatchCode =
        "INVENTORY_CURSOR_SCOPE_MISMATCH";

    public InventoryRequestException(string errorCode, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}

public sealed class InventoryScopeNotFoundException : Exception
{
    public const string ErrorCode = "INVENTORY_SCOPE_NOT_FOUND";

    public InventoryScopeNotFoundException()
        : base("One or more requested inventory identities do not exist in the requested scope.")
    {
    }
}

/// <summary>
/// A derived quantity over persisted history does not fit 64-bit fixed
/// point; checked arithmetic refused it instead of wrapping.
/// </summary>
/// <remarks>
/// The caller's input is not at fault, so this is not a numeric-input error.
/// </remarks>
public sealed class InventoryArithmeticOverflowException : Exception
{
    public const string ErrorCode = "INVENTORY_ARITHMETIC_OVERFLOW";

    public InventoryArithmeticOverflowException(OverflowException innerException)
        : base(
            "A derived inventory quantity does not fit the fixed-point range.",
            innerException)
    {
    }
}

public sealed class InventoryPersistenceException : Exception
{
    public InventoryPersistenceException(string message)
        : base(message)
    {
    }
}
