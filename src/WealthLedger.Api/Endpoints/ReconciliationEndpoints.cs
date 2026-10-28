using WealthLedger.Api.Contracts;
using WealthLedger.Api.Mapping;
using WealthLedger.Application.Reconciliation;

namespace WealthLedger.Api.Endpoints;

/// <summary>
/// Explicit reconciliation commands and reads. A run compares and records;
/// it never posts, edits or deletes a ledger transaction.
/// </summary>
internal static class ReconciliationEndpoints
{
    internal const string RequestInvalidCode = "RECONCILIATION_REQUEST_INVALID";

    internal static readonly BoundedRequestProblems Problems = new(
        "Invalid reconciliation request",
        RequestInvalidCode,
        ReconciliationErrorCodes.PageSizeInvalid,
        ReconciliationErrorCodes.CursorInvalid);

    internal static IEndpointRouteBuilder MapReconciliationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/households/{householdId:guid}/reconciliations")
            .AddEndpointFilter<M010RequestLogFilter>();

        group.MapGet(string.Empty, ListAsync)
            .WithName("ListReconciliationRuns");
        group.MapPost(string.Empty, RunAsync)
            .WithName("RunReconciliation");
        group.MapGet("/{reconciliationRunId:guid}", GetAsync)
            .WithName("GetReconciliationRun");
        group.MapPost(
                "/{reconciliationRunId:guid}/items/{reconciliationItemId:guid}/resolution",
                ResolveAsync)
            .WithName("ResolveReconciliationItem");

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        HttpContext httpContext,
        Guid householdId,
        ListReconciliationRunsUseCase useCase,
        CancellationToken cancellationToken)
    {
        var query = new BoundedQueryReader(httpContext.Request, Problems);

        if (!query.TryRouteIdentity(householdId)
            || !query.TryOptionalGuid("evidenceId", out var evidenceId)
            || !query.TryPageSize(out var pageSize)
            || !query.TryCursor(out var cursor)
            || !query.TryRejectUnsupported())
        {
            return query.Error!;
        }

        var page = await useCase.ExecuteAsync(
            new ListReconciliationRunsQuery(householdId, evidenceId, pageSize, cursor),
            cancellationToken);
        M010RequestLogFilter.SetItemCount(httpContext, page.Items.Count);

        return Results.Ok(
            new NavigationPageResponse<ReconciliationRunSummaryResponse>(
                page.Items.Select(item => item.ToResponse()).ToArray(),
                page.NextCursor));
    }

    private static async Task<IResult> GetAsync(
        HttpContext httpContext,
        Guid householdId,
        Guid reconciliationRunId,
        GetReconciliationRunUseCase useCase,
        CancellationToken cancellationToken)
    {
        var query = new BoundedQueryReader(httpContext.Request, Problems);

        if (!query.TryRouteIdentity(householdId)
            || !query.TryRouteIdentity(reconciliationRunId)
            || !query.TryRejectUnsupported())
        {
            return query.Error!;
        }

        var run = await useCase.ExecuteAsync(
            new GetReconciliationRunQuery(householdId, reconciliationRunId),
            cancellationToken);
        M010RequestLogFilter.SetItemCount(httpContext, run.Items.Count);

        return Results.Ok(run.ToResponse());
    }

    private static async Task<IResult> RunAsync(
        HttpContext httpContext,
        Guid householdId,
        RunReconciliationUseCase useCase,
        CancellationToken cancellationToken)
    {
        var query = new BoundedQueryReader(httpContext.Request, Problems);

        if (!query.TryRouteIdentity(householdId)
            || !query.TryRejectUnsupported())
        {
            return query.Error!;
        }

        if (!LedgerEndpoints.TryGetIdempotencyKey(
                httpContext.Request,
                out var idempotencyKey,
                out var keyError))
        {
            return keyError!;
        }

        var body = await BoundedJsonBody.ReadAsync<RunReconciliationRequest>(
            httpContext.Request,
            Problems,
            cancellationToken);

        if (body.Error is not null)
        {
            return body.Error;
        }

        if (body.Value!.EvidenceId == Guid.Empty)
        {
            return BoundedProblems.BadRequest(
                Problems,
                RequestInvalidCode,
                "The evidence identity must not be empty.");
        }

        var result = await useCase.ExecuteAsync(
            idempotencyKey,
            new RunReconciliationCommand(householdId, body.Value.EvidenceId),
            cancellationToken);
        var location = ReconciliationContractMapper.RunLocation(
            householdId,
            result.ReconciliationRunId);
        M010RequestLogFilter.SetItemCount(httpContext, 1);

        return Results.Created(
            location,
            new RunReconciliationResponse(
                result.ReconciliationRunId,
                result.WasReplay,
                location));
    }

    // One immutable resolution per out-of-tolerance item; repeating the same
    // resolution is a no-op and a different one is a conflict, so a retry
    // needs no idempotency key.
    private static async Task<IResult> ResolveAsync(
        HttpContext httpContext,
        Guid householdId,
        Guid reconciliationRunId,
        Guid reconciliationItemId,
        ResolveReconciliationItemUseCase useCase,
        CancellationToken cancellationToken)
    {
        var query = new BoundedQueryReader(httpContext.Request, Problems);

        if (!query.TryRouteIdentity(householdId)
            || !query.TryRouteIdentity(reconciliationRunId)
            || !query.TryRouteIdentity(reconciliationItemId)
            || !query.TryRejectUnsupported())
        {
            return query.Error!;
        }

        var body = await BoundedJsonBody.ReadAsync<ResolveReconciliationItemRequest>(
            httpContext.Request,
            Problems,
            cancellationToken);

        if (body.Error is not null)
        {
            return body.Error;
        }

        if (!ReconciliationContractMapper.KindCodes.TryGetValue(
                body.Value!.KindCode,
                out var kind))
        {
            return BoundedJsonBody.Malformed(Problems);
        }

        var result = await useCase.ExecuteAsync(
            new ResolveReconciliationItemCommand(
                householdId,
                reconciliationRunId,
                reconciliationItemId,
                kind,
                body.Value.CorrectiveTransactionId,
                body.Value.Explanation),
            cancellationToken);
        M010RequestLogFilter.SetItemCount(httpContext, 1);

        return Results.Ok(
            new ResolveReconciliationItemResponse(
                result.ReconciliationItemId,
                ReconciliationContractMapper.ToCode(result.Kind),
                result.WasAlreadyResolved));
    }
}
