using WealthLedger.Api.Contracts;
using WealthLedger.Api.Mapping;
using WealthLedger.Application.LedgerSearch;

namespace WealthLedger.Api.Endpoints;

internal static class LedgerSearchEndpoints
{
    internal static readonly BoundedRequestProblems Problems = new(
        "Invalid ledger search request",
        LedgerSearchRequestException.FilterInvalidCode,
        LedgerSearchRequestException.PageSizeInvalidCode,
        LedgerSearchRequestException.CursorInvalidCode);

    internal static IEndpointRouteBuilder MapLedgerSearchEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/households/{householdId:guid}/ledger/search",
                SearchAsync)
            .WithName("SearchLedgerTransactions")
            .AddEndpointFilter<M010RequestLogFilter>();

        return endpoints;
    }

    // Filters combine with AND across categories and OR within a repeated
    // type or status. Each identity must exist in the household; entry-level
    // filters match any entry, so an account and a different institution
    // still select a transfer between them.
    private static async Task<IResult> SearchAsync(
        HttpContext httpContext,
        Guid householdId,
        SearchLedgerTransactionsUseCase useCase,
        CancellationToken cancellationToken)
    {
        var query = new BoundedQueryReader(httpContext.Request, Problems);

        if (!query.TryRouteIdentity(householdId)
            || !query.TryOptionalDate("executedFrom", out var executedFrom)
            || !query.TryOptionalDate("executedTo", out var executedTo)
            || !query.TryCodeList(
                "type",
                LedgerSearchContractMapper.TypeCodes,
                out var types)
            || !query.TryCodeList(
                "status",
                LedgerSearchContractMapper.StatusCodes,
                out var statuses)
            || !query.TryOptionalGuid("assetId", out var assetId)
            || !query.TryOptionalGuid("institutionId", out var institutionId)
            || !query.TryOptionalGuid("accountId", out var accountId)
            || !query.TryOptionalGuid("portfolioId", out var portfolioId)
            || !query.TryOptionalText("externalReference", out var externalReference)
            || !query.TryOptionalCode(
                "reversal",
                LedgerSearchContractMapper.ReversalCodes,
                out var reversal)
            || !query.TryPageSize(out var pageSize)
            || !query.TryCursor(out var cursor)
            || !query.TryRejectUnsupported())
        {
            return query.Error!;
        }

        var page = await useCase.ExecuteAsync(
            new SearchLedgerTransactionsQuery(
                householdId,
                executedFrom,
                executedTo,
                types,
                statuses,
                assetId,
                institutionId,
                accountId,
                portfolioId,
                externalReference,
                reversal ?? LedgerSearchReversalRelationship.Any,
                pageSize,
                cursor),
            cancellationToken);
        M010RequestLogFilter.SetItemCount(httpContext, page.Items.Count);

        return Results.Ok(
            new NavigationPageResponse<LedgerSearchTransactionResponse>(
                page.Items.Select(item => item.ToResponse()).ToArray(),
                page.NextCursor));
    }
}
