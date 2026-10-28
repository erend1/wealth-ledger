using WealthLedger.Api.Contracts;
using WealthLedger.Api.Mapping;
using WealthLedger.Application.Evidence;

namespace WealthLedger.Api.Endpoints;

/// <summary>
/// Explicit evidence commands and reads. There is no update or delete route:
/// a record is corrected by a superseding record.
/// </summary>
internal static class EvidenceEndpoints
{
    internal const string RequestInvalidCode = "EVIDENCE_REQUEST_INVALID";

    internal static readonly BoundedRequestProblems Problems = new(
        "Invalid evidence request",
        RequestInvalidCode,
        EvidenceErrorCodes.PageSizeInvalid,
        EvidenceErrorCodes.CursorInvalid);

    internal static IEndpointRouteBuilder MapEvidenceEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/households/{householdId:guid}/evidence")
            .AddEndpointFilter<M010RequestLogFilter>();

        group.MapGet(string.Empty, ListAsync)
            .WithName("ListEvidence");
        group.MapPost(string.Empty, RecordAsync)
            .WithName("RecordEvidence");
        group.MapGet("/{evidenceId:guid}", GetAsync)
            .WithName("GetEvidence");
        group.MapPost("/{evidenceId:guid}/transaction-links", LinkAsync)
            .WithName("LinkEvidenceTransactions");
        group.MapGet("/{evidenceId:guid}/observations", ListObservationsAsync)
            .WithName("ListEvidenceObservations");
        group.MapPost("/{evidenceId:guid}/observations", RecordObservationsAsync)
            .WithName("RecordEvidenceObservations");
        group.MapPost("/uploads", RecordUploadAsync)
            .WithName("RecordEvidenceUpload");
        group.MapGet("/{evidenceId:guid}/attachment", DownloadAttachmentAsync)
            .WithName("DownloadEvidenceAttachment");

        return endpoints;
    }

    private static async Task<IResult> DownloadAttachmentAsync(
        HttpContext httpContext,
        Guid householdId,
        Guid evidenceId,
        GetEvidenceAttachmentContentUseCase useCase,
        CancellationToken cancellationToken)
    {
        // A retained file is never rendered here: it is always a download
        // that the browser must not sniff, run, frame or cache. The headers
        // are applied as the response starts, so a problem written after the
        // exception handler clears the response carries them too.
        httpContext.Response.OnStarting(
            static state =>
            {
                var headers = ((HttpResponse)state).Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.CacheControl = "no-store";
                headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
                return Task.CompletedTask;
            },
            httpContext.Response);

        var query = new BoundedQueryReader(httpContext.Request, Problems);

        if (!query.TryRouteIdentity(householdId)
            || !query.TryRouteIdentity(evidenceId)
            || !query.TryRejectUnsupported())
        {
            return query.Error!;
        }

        var attachment = await useCase.ExecuteAsync(
            new GetEvidenceAttachmentContentQuery(householdId, evidenceId),
            cancellationToken);
        M010RequestLogFilter.SetItemCount(httpContext, 1);
        M010RequestLogFilter.SetSizeClass(httpContext, attachment.ByteLength);

        return Results.Bytes(
            attachment.Content,
            EvidenceUploadMediaTypes.ToContentType(attachment.MediaType),
            fileDownloadName: attachment.FileName);
    }

    // One evidence record with its retained file, in one commit. The body is
    // multipart/form-data with exactly a "metadata" JSON part and one "file"
    // part. Requiring the Idempotency-Key header also means a cross-site
    // HTML form, which cannot set it, can never post a file here.
    private static async Task<IResult> RecordUploadAsync(
        HttpContext httpContext,
        Guid householdId,
        RecordEvidenceUseCase useCase,
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

        var upload = await EvidenceUploadReader.ReadAsync(
            httpContext.Request,
            cancellationToken);

        if (upload.Error is not null)
        {
            return upload.Error;
        }

        M010RequestLogFilter.SetSizeClass(httpContext, upload.Attachment!.Content.Length);

        if (!EvidenceContractMapper.SourceKindCodes.TryGetValue(
                upload.Metadata!.SourceKindCode,
                out var sourceKind))
        {
            return BoundedJsonBody.Malformed(Problems);
        }

        return await RecordCommandAsync(
            httpContext,
            useCase,
            idempotencyKey,
            upload.Metadata.ToCommand(householdId, sourceKind, upload.Attachment),
            cancellationToken);
    }

    private static async Task<IResult> ListAsync(
        HttpContext httpContext,
        Guid householdId,
        ListEvidenceUseCase useCase,
        CancellationToken cancellationToken)
    {
        var query = new BoundedQueryReader(httpContext.Request, Problems);

        if (!query.TryRouteIdentity(householdId)
            || !query.TryPageSize(out var pageSize)
            || !query.TryCursor(out var cursor)
            || !query.TryRejectUnsupported())
        {
            return query.Error!;
        }

        var page = await useCase.ExecuteAsync(
            new ListEvidenceQuery(householdId, pageSize, cursor),
            cancellationToken);
        M010RequestLogFilter.SetItemCount(httpContext, page.Items.Count);

        return Results.Ok(
            new NavigationPageResponse<EvidenceSummaryResponse>(
                page.Items.Select(item => item.ToResponse()).ToArray(),
                page.NextCursor));
    }

    private static async Task<IResult> GetAsync(
        HttpContext httpContext,
        Guid householdId,
        Guid evidenceId,
        GetEvidenceUseCase useCase,
        CancellationToken cancellationToken)
    {
        var query = new BoundedQueryReader(httpContext.Request, Problems);

        if (!query.TryRouteIdentity(householdId)
            || !query.TryRouteIdentity(evidenceId)
            || !query.TryRejectUnsupported())
        {
            return query.Error!;
        }

        var detail = await useCase.ExecuteAsync(
            new GetEvidenceQuery(householdId, evidenceId),
            cancellationToken);
        M010RequestLogFilter.SetItemCount(httpContext, 1);

        return Results.Ok(detail.ToResponse());
    }

    private static async Task<IResult> ListObservationsAsync(
        HttpContext httpContext,
        Guid householdId,
        Guid evidenceId,
        ListEvidenceObservationsUseCase useCase,
        CancellationToken cancellationToken)
    {
        var query = new BoundedQueryReader(httpContext.Request, Problems);

        if (!query.TryRouteIdentity(householdId)
            || !query.TryRouteIdentity(evidenceId)
            || !query.TryRejectUnsupported())
        {
            return query.Error!;
        }

        var list = await useCase.ExecuteAsync(
            new ListEvidenceObservationsQuery(householdId, evidenceId),
            cancellationToken);
        M010RequestLogFilter.SetItemCount(httpContext, list.Items.Count);

        return Results.Ok(list.ToResponse());
    }

    // Metadata-only evidence. A retained file goes through the upload route.
    private static async Task<IResult> RecordAsync(
        HttpContext httpContext,
        Guid householdId,
        RecordEvidenceUseCase useCase,
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

        var body = await BoundedJsonBody.ReadAsync<RecordEvidenceRequest>(
            httpContext.Request,
            Problems,
            cancellationToken);

        if (body.Error is not null)
        {
            return body.Error;
        }

        if (!EvidenceContractMapper.SourceKindCodes.TryGetValue(
                body.Value!.SourceKindCode,
                out var sourceKind))
        {
            return BoundedJsonBody.Malformed(Problems);
        }

        return await RecordCommandAsync(
            httpContext,
            useCase,
            idempotencyKey,
            body.Value.ToCommand(householdId, sourceKind),
            cancellationToken);
    }

    private static async Task<IResult> LinkAsync(
        HttpContext httpContext,
        Guid householdId,
        Guid evidenceId,
        LinkEvidenceTransactionsUseCase useCase,
        CancellationToken cancellationToken)
    {
        var query = new BoundedQueryReader(httpContext.Request, Problems);

        if (!query.TryRouteIdentity(householdId)
            || !query.TryRouteIdentity(evidenceId)
            || !query.TryRejectUnsupported())
        {
            return query.Error!;
        }

        var body = await BoundedJsonBody.ReadAsync<LinkEvidenceTransactionsRequest>(
            httpContext.Request,
            Problems,
            cancellationToken);

        if (body.Error is not null)
        {
            return body.Error;
        }

        // Linking is insert-only and an existing pair is a no-op, so a retry
        // needs no idempotency key.
        var result = await useCase.ExecuteAsync(
            new LinkEvidenceTransactionsCommand(
                householdId,
                evidenceId,
                body.Value!.TransactionIds),
            cancellationToken);
        M010RequestLogFilter.SetItemCount(
            httpContext,
            result.AddedCount + result.AlreadyLinkedCount);

        return Results.Ok(
            new LinkEvidenceTransactionsResponse(
                result.EvidenceId,
                result.AddedCount,
                result.AlreadyLinkedCount));
    }

    private static async Task<IResult> RecordObservationsAsync(
        HttpContext httpContext,
        Guid householdId,
        Guid evidenceId,
        RecordEvidenceObservationsUseCase useCase,
        CancellationToken cancellationToken)
    {
        var query = new BoundedQueryReader(httpContext.Request, Problems);

        if (!query.TryRouteIdentity(householdId)
            || !query.TryRouteIdentity(evidenceId)
            || !query.TryRejectUnsupported())
        {
            return query.Error!;
        }

        var body = await BoundedJsonBody.ReadAsync<RecordEvidenceObservationsRequest>(
            httpContext.Request,
            Problems,
            cancellationToken);

        if (body.Error is not null)
        {
            return body.Error;
        }

        // Nullable annotations are not enforced on collection elements.
        if (body.Value!.Observations.Any(observation => observation is null))
        {
            return BoundedJsonBody.Malformed(Problems);
        }

        // An identical observation is already recorded and a different one
        // for the same scope is a conflict, so a retry needs no key.
        var result = await useCase.ExecuteAsync(
            new RecordEvidenceObservationsCommand(
                householdId,
                evidenceId,
                body.Value.Observations
                    .Select(
                        observation => new EvidenceObservationInput(
                            observation.PortfolioId,
                            observation.AccountId,
                            observation.AssetId,
                            observation.ObservedQuantityRawE8,
                            observation.ToleranceRawE8,
                            observation.ObservedPieceCount))
                    .ToArray()),
            cancellationToken);
        M010RequestLogFilter.SetItemCount(
            httpContext,
            result.AddedCount + result.AlreadyRecordedCount);

        return Results.Ok(
            new RecordEvidenceObservationsResponse(
                result.EvidenceId,
                result.AddedCount,
                result.AlreadyRecordedCount));
    }

    private static async Task<IResult> RecordCommandAsync(
        HttpContext httpContext,
        RecordEvidenceUseCase useCase,
        string idempotencyKey,
        RecordEvidenceCommand command,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            idempotencyKey,
            command,
            cancellationToken);
        var location = EvidenceContractMapper.EvidenceLocation(
            command.HouseholdId,
            result.EvidenceId);
        M010RequestLogFilter.SetItemCount(httpContext, 1);

        return Results.Created(
            location,
            new RecordEvidenceResponse(result.EvidenceId, location));
    }
}
