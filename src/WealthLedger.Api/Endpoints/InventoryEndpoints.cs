using WealthLedger.Api.Mapping;
using WealthLedger.Application.Inventory;

namespace WealthLedger.Api.Endpoints;

internal static class InventoryEndpoints
{
    internal static readonly BoundedRequestProblems Problems = new(
        "Invalid inventory request",
        InventoryRequestException.FilterInvalidCode,
        InventoryRequestException.PageSizeInvalidCode,
        InventoryRequestException.CursorInvalidCode);

    internal static IEndpointRouteBuilder MapInventoryEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/households/{householdId:guid}/inventory/positions",
                ListPositionsAsync)
            .WithName("ListPositionInventory")
            .AddEndpointFilter<M010RequestLogFilter>();
        endpoints.MapGet(
                "/api/households/{householdId:guid}/inventory/lots",
                ListLotsAsync)
            .WithName("ListLotInventory")
            .AddEndpointFilter<M010RequestLogFilter>();

        return endpoints;
    }

    // Inventory names exact scopes, so a filter combination that names no
    // existing scope, such as an account outside the given institution, is
    // INVENTORY_SCOPE_NOT_FOUND rather than an empty result.
    private static async Task<IResult> ListPositionsAsync(
        HttpContext httpContext,
        Guid householdId,
        ListPositionInventoryUseCase useCase,
        CancellationToken cancellationToken)
    {
        var query = new BoundedQueryReader(httpContext.Request, Problems);

        if (!query.TryRouteIdentity(householdId)
            || !TryReadScope(query, out var scope)
            || !query.TryOptionalBoolean("includeZero", out var includeZero)
            || !query.TryRejectUnsupported())
        {
            return query.Error!;
        }

        var inventory = await useCase.ExecuteAsync(
            new ListPositionInventoryQuery(
                householdId,
                scope.PortfolioId,
                scope.AccountId,
                scope.InstitutionId,
                scope.AssetId,
                scope.AsOf,
                includeZero),
            cancellationToken);
        M010RequestLogFilter.SetItemCount(httpContext, inventory.Items.Count);

        return Results.Ok(inventory.ToResponse());
    }

    private static async Task<IResult> ListLotsAsync(
        HttpContext httpContext,
        Guid householdId,
        ListLotInventoryUseCase useCase,
        CancellationToken cancellationToken)
    {
        var query = new BoundedQueryReader(httpContext.Request, Problems);

        if (!query.TryRouteIdentity(householdId)
            || !TryReadScope(query, out var scope)
            || !query.TryOptionalBoolean("includeExhausted", out var includeExhausted)
            || !query.TryPageSize(out var pageSize)
            || !query.TryCursor(out var cursor)
            || !query.TryRejectUnsupported())
        {
            return query.Error!;
        }

        var inventory = await useCase.ExecuteAsync(
            new ListLotInventoryQuery(
                householdId,
                scope.PortfolioId,
                scope.AccountId,
                scope.InstitutionId,
                scope.AssetId,
                scope.AsOf,
                includeExhausted,
                pageSize,
                cursor),
            cancellationToken);
        M010RequestLogFilter.SetItemCount(httpContext, inventory.Items.Count);

        return Results.Ok(inventory.ToResponse());
    }

    private static bool TryReadScope(
        BoundedQueryReader query,
        out InventoryScopeFilter scope)
    {
        scope = default;

        if (!query.TryOptionalGuid("portfolioId", out var portfolioId)
            || !query.TryOptionalGuid("accountId", out var accountId)
            || !query.TryOptionalGuid("institutionId", out var institutionId)
            || !query.TryOptionalGuid("assetId", out var assetId)
            || !query.TryOptionalDate("asOf", out var asOf))
        {
            return false;
        }

        scope = new InventoryScopeFilter(
            portfolioId,
            accountId,
            institutionId,
            assetId,
            asOf);
        return true;
    }

    private readonly record struct InventoryScopeFilter(
        Guid? PortfolioId,
        Guid? AccountId,
        Guid? InstitutionId,
        Guid? AssetId,
        DateOnly? AsOf);
}
