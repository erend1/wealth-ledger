using System.Diagnostics;
using WealthLedger.Api.Contracts;
using WealthLedger.Api.Mapping;
using WealthLedger.Application.Navigation;

namespace WealthLedger.Api.Endpoints;

internal static class NavigationEndpoints
{
    internal static IEndpointRouteBuilder MapNavigationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/households",
                ListHouseholdsAsync)
            .WithName("ListHouseholds");
        endpoints.MapGet(
                "/api/households/{householdId:guid}",
                GetHouseholdAsync)
            .WithName("GetHousehold");
        endpoints.MapGet(
                "/api/households/{householdId:guid}/members",
                ListHouseholdMembersAsync)
            .WithName("ListHouseholdMembers");
        endpoints.MapGet(
                "/api/households/{householdId:guid}/portfolios",
                ListPortfoliosAsync)
            .WithName("ListPortfolios");
        endpoints.MapGet(
                "/api/households/{householdId:guid}/accounts",
                ListAccountsAsync)
            .WithName("ListAccounts");
        endpoints.MapGet(
                "/api/institutions",
                ListInstitutionsAsync)
            .WithName("ListInstitutions");
        endpoints.MapGet(
                "/api/currencies",
                ListCurrenciesAsync)
            .WithName("ListCurrencies");
        endpoints.MapGet(
                "/api/assets",
                ListAssetsAsync)
            .WithName("ListAssets");
        endpoints.MapGet(
                "/api/households/{householdId:guid}/ledger/transactions",
                ListRecentLedgerTransactionsAsync)
            .WithName("ListRecentLedgerTransactions");

        return endpoints;
    }

    private static async Task<IResult> ListHouseholdsAsync(
        HttpRequest request,
        ListHouseholdsUseCase useCase,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!NavigationHttpQueryParser.TryParse(
                request,
                allowIncludeInactive: false,
                out var query,
                out var error))
        {
            return error!;
        }

        var startedAt = Stopwatch.GetTimestamp();
        var result = await useCase.ExecuteAsync(
            new ListHouseholdsQuery(query.PageSize, query.Cursor),
            cancellationToken);
        LogCompleted(
            loggerFactory,
            "/api/households",
            result.Items.Count,
            startedAt);

        return Results.Ok(
            new NavigationPageResponse<HouseholdNavigationResponse>(
                result.Items.Select(item => item.ToResponse()).ToArray(),
                result.NextCursor));
    }

    private static async Task<IResult> GetHouseholdAsync(
        Guid householdId,
        GetHouseholdUseCase useCase,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var result = await useCase.ExecuteAsync(
            new GetHouseholdQuery(householdId),
            cancellationToken);
        LogCompleted(
            loggerFactory,
            "/api/households/{householdId}",
            itemCount: 1,
            startedAt);

        return Results.Ok(result.ToResponse());
    }

    private static async Task<IResult> ListHouseholdMembersAsync(
        HttpRequest request,
        Guid householdId,
        ListHouseholdMembersUseCase useCase,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!NavigationHttpQueryParser.TryParse(
                request,
                allowIncludeInactive: true,
                out var query,
                out var error))
        {
            return error!;
        }

        var startedAt = Stopwatch.GetTimestamp();
        var result = await useCase.ExecuteAsync(
            new ListHouseholdMembersQuery(
                householdId,
                query.PageSize,
                query.Cursor,
                query.IncludeInactive),
            cancellationToken);
        LogCompleted(
            loggerFactory,
            "/api/households/{householdId}/members",
            result.Items.Count,
            startedAt);

        return Results.Ok(
            new NavigationPageResponse<HouseholdMemberNavigationResponse>(
                result.Items.Select(item => item.ToResponse()).ToArray(),
                result.NextCursor));
    }

    private static async Task<IResult> ListInstitutionsAsync(
        HttpRequest request,
        ListInstitutionsUseCase useCase,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!NavigationHttpQueryParser.TryParse(
                request,
                allowIncludeInactive: true,
                out var query,
                out var error))
        {
            return error!;
        }

        var startedAt = Stopwatch.GetTimestamp();
        var result = await useCase.ExecuteAsync(
            new ListInstitutionsQuery(
                query.PageSize,
                query.Cursor,
                query.IncludeInactive),
            cancellationToken);
        LogCompleted(
            loggerFactory,
            "/api/institutions",
            result.Items.Count,
            startedAt);

        return Results.Ok(
            new NavigationPageResponse<InstitutionNavigationResponse>(
                result.Items.Select(item => item.ToResponse()).ToArray(),
                result.NextCursor));
    }

    private static async Task<IResult> ListPortfoliosAsync(
        HttpRequest request,
        Guid householdId,
        ListPortfoliosUseCase useCase,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!NavigationHttpQueryParser.TryParse(
                request,
                allowIncludeInactive: true,
                out var query,
                out var error))
        {
            return error!;
        }

        var startedAt = Stopwatch.GetTimestamp();
        var result = await useCase.ExecuteAsync(
            new ListPortfoliosQuery(
                householdId,
                query.PageSize,
                query.Cursor,
                query.IncludeInactive),
            cancellationToken);
        LogCompleted(
            loggerFactory,
            "/api/households/{householdId}/portfolios",
            result.Items.Count,
            startedAt);

        return Results.Ok(
            new NavigationPageResponse<PortfolioNavigationResponse>(
                result.Items.Select(item => item.ToResponse()).ToArray(),
                result.NextCursor));
    }

    private static async Task<IResult> ListAccountsAsync(
        HttpRequest request,
        Guid householdId,
        ListAccountsUseCase useCase,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!NavigationHttpQueryParser.TryParse(
                request,
                allowIncludeInactive: true,
                out var query,
                out var error))
        {
            return error!;
        }

        var startedAt = Stopwatch.GetTimestamp();
        var result = await useCase.ExecuteAsync(
            new ListAccountsQuery(
                householdId,
                query.PageSize,
                query.Cursor,
                query.IncludeInactive),
            cancellationToken);
        LogCompleted(
            loggerFactory,
            "/api/households/{householdId}/accounts",
            result.Items.Count,
            startedAt);

        return Results.Ok(
            new NavigationPageResponse<AccountNavigationResponse>(
                result.Items.Select(item => item.ToResponse()).ToArray(),
                result.NextCursor));
    }

    private static async Task<IResult> ListCurrenciesAsync(
        HttpRequest request,
        ListCurrenciesUseCase useCase,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!NavigationHttpQueryParser.TryParse(
                request,
                allowIncludeInactive: false,
                out var query,
                out var error))
        {
            return error!;
        }

        var startedAt = Stopwatch.GetTimestamp();
        var result = await useCase.ExecuteAsync(
            new ListCurrenciesQuery(query.PageSize, query.Cursor),
            cancellationToken);
        LogCompleted(
            loggerFactory,
            "/api/currencies",
            result.Items.Count,
            startedAt);

        return Results.Ok(
            new NavigationPageResponse<CurrencyNavigationResponse>(
                result.Items.Select(item => item.ToResponse()).ToArray(),
                result.NextCursor));
    }

    private static async Task<IResult> ListAssetsAsync(
        HttpRequest request,
        ListAssetsUseCase useCase,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!NavigationHttpQueryParser.TryParse(
                request,
                allowIncludeInactive: true,
                out var query,
                out var error))
        {
            return error!;
        }

        var startedAt = Stopwatch.GetTimestamp();
        var result = await useCase.ExecuteAsync(
            new ListAssetsQuery(
                query.PageSize,
                query.Cursor,
                query.IncludeInactive),
            cancellationToken);
        LogCompleted(
            loggerFactory,
            "/api/assets",
            result.Items.Count,
            startedAt);

        return Results.Ok(
            new NavigationPageResponse<AssetNavigationResponse>(
                result.Items.Select(item => item.ToResponse()).ToArray(),
                result.NextCursor));
    }

    private static async Task<IResult> ListRecentLedgerTransactionsAsync(
        HttpRequest request,
        Guid householdId,
        ListRecentLedgerTransactionsUseCase useCase,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!NavigationHttpQueryParser.TryParse(
                request,
                allowIncludeInactive: false,
                out var query,
                out var error))
        {
            return error!;
        }

        var startedAt = Stopwatch.GetTimestamp();
        var result = await useCase.ExecuteAsync(
            new ListRecentLedgerTransactionsQuery(
                householdId,
                query.PageSize,
                query.Cursor),
            cancellationToken);
        LogCompleted(
            loggerFactory,
            "/api/households/{householdId}/ledger/transactions",
            result.Items.Count,
            startedAt);

        return Results.Ok(
            new NavigationPageResponse<
                RecentLedgerTransactionNavigationResponse>(
                result.Items.Select(item => item.ToResponse()).ToArray(),
                result.NextCursor));
    }

    private static void LogCompleted(
        ILoggerFactory loggerFactory,
        string route,
        int itemCount,
        long startedAt)
    {
        var durationMilliseconds = checked(
            (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        loggerFactory
            .CreateLogger("WealthLedger.Api.Navigation")
            .LogInformation(
                "Navigation request completed for route {Route} with outcome {Outcome}, item count {ItemCount}, and duration {DurationMilliseconds} ms.",
                route,
                "SUCCESS",
                itemCount,
                durationMilliseconds);
    }
}
