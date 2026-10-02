namespace WealthLedger.Application.Inventory;

public sealed class InventoryScopeNotFoundException : Exception
{
    public const string ErrorCode = "INVENTORY_SCOPE_NOT_FOUND";

    public InventoryScopeNotFoundException()
        : base("One or more requested inventory identities do not exist in the requested scope.")
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
