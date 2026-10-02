namespace WealthLedger.Application.Inventory;

public interface IInventoryReadStore
{
    Task<bool> HouseholdExistsAsync(
        Guid householdId,
        CancellationToken cancellationToken = default);

    Task<bool> FilterScopeExistsAsync(
        Guid householdId,
        Guid? portfolioId,
        Guid? accountId,
        Guid? institutionId,
        Guid? assetId,
        CancellationToken cancellationToken = default);

    Task<InventoryScopeContext?> FindExactPositionScopeAsync(
        Guid householdId,
        Guid portfolioId,
        Guid accountId,
        Guid assetId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryEntryFact>> ListPositionEntryFactsAsync(
        Guid householdId,
        Guid? portfolioId,
        Guid? accountId,
        Guid? institutionId,
        Guid? assetId,
        DateOnly? asOf,
        CancellationToken cancellationToken = default);

    Task<InventoryLotFactSet> ListLotFactsAsync(
        Guid householdId,
        Guid? assetId,
        DateOnly? asOf,
        CancellationToken cancellationToken = default);
}
