using System.Net;
using System.Net.Http.Json;
using System.Text;
using WealthLedger.Api.Contracts;

namespace WealthLedger.Api.Tests;

public sealed class EvidenceApiTests
{
    [Fact]
    public async Task Evidence_RecordsReplaysAndReadsMetadataWithoutTouchingTheLedger()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var history = await M010ApiTestData.SeedAsync(factory, client);
        var route = $"{history.HouseholdRoute}/evidence";
        var ledgerBefore = await M010ApiTestData.ReadLedgerCountsAsync(factory);
        var request = M010ApiTestData.Statement(
            history,
            transactionIds: [history.FirstJanuaryId]);

        using var created = await M010ApiTestData.SendJsonAsync(client, route, request, "evidence-key-1");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var result = Assert.IsType<RecordEvidenceResponse>(
            await created.Content.ReadFromJsonAsync<RecordEvidenceResponse>());
        Assert.Equal($"{route}/{result.EvidenceId}", created.Headers.Location?.OriginalString);
        Assert.Equal(created.Headers.Location?.OriginalString, result.EvidenceLocation);

        using var replay = await M010ApiTestData.SendJsonAsync(client, route, request, "evidence-key-1");
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(
            result.EvidenceId,
            (await replay.Content.ReadFromJsonAsync<RecordEvidenceResponse>())!.EvidenceId);

        using var conflict = await M010ApiTestData.SendJsonAsync(
            client,
            route,
            request with { Label = "Synthetic different label" },
            "evidence-key-1");
        await M010ApiTestData.AssertProblemAsync(
            conflict,
            HttpStatusCode.Conflict,
            "IDEMPOTENCY_KEY_CONFLICT",
            "Synthetic different label");

        using var missingKey = await M010ApiTestData.SendJsonAsync(client, route, request, idempotencyKey: null);
        await M010ApiTestData.AssertProblemAsync(
            missingKey,
            HttpStatusCode.BadRequest,
            "IDEMPOTENCY_KEY_REQUIRED");

        var detail = await M010ApiTestData.GetAsync<EvidenceDetailResponse>(
            client,
            $"{route}/{result.EvidenceId}");
        Assert.Equal(history.Setup.HouseholdId, detail.HouseholdId);
        Assert.Equal("STATEMENT", detail.SourceKindCode);
        Assert.Equal(new DateOnly(2026, 1, 31), detail.EffectiveDate);
        Assert.Equal("Synthetic January statement", detail.Label);
        Assert.Equal("SYNTH-STATEMENT-REF", detail.Reference);
        Assert.Equal("Synthetic private statement note.", detail.Note);
        Assert.Equal(
            (history.Setup.InstitutionId, "SYNTHETIC_INSTITUTION", history.Setup.AccountId, "PRIMARY"),
            (detail.Context.InstitutionId, detail.Context.InstitutionCode, detail.Context.AccountId, detail.Context.AccountCode));
        Assert.Null(detail.Attachment);
        Assert.Null(detail.SupersededByEvidenceId);
        var link = Assert.Single(detail.TransactionLinks);
        Assert.Equal(
            (history.FirstJanuaryId, "CONTRIBUTION", (DateOnly?)M010ApiTestData.January),
            (link.TransactionId, link.TransactionTypeCode, link.ExecutionDate));

        var page = await M010ApiTestData.GetAsync<NavigationPageResponse<EvidenceSummaryResponse>>(client, route);
        var summary = Assert.Single(page.Items);
        Assert.Equal((result.EvidenceId, 1), (summary.EvidenceId, summary.TransactionLinkCount));
        Assert.Null(page.NextCursor);

        Assert.Equal(ledgerBefore, await M010ApiTestData.ReadLedgerCountsAsync(factory));
    }

    [Fact]
    public async Task Evidence_LinksOnlySameHouseholdPostedTransactionsAndSupersedes()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var history = await M010ApiTestData.SeedAsync(factory, client);
        var route = $"{history.HouseholdRoute}/evidence";
        var evidenceId = await M010ApiTestData.RecordStatementAsync(
            client,
            history,
            M010ApiTestData.Statement(history, transactionIds: [history.FirstJanuaryId]));
        var linksRoute = $"{route}/{evidenceId}/transaction-links";

        var linked = await M010ApiTestData.PostAsync<LinkEvidenceTransactionsResponse>(
            client,
            linksRoute,
            new LinkEvidenceTransactionsRequest([history.SecondJanuaryId, history.FirstJanuaryId]));
        Assert.Equal((evidenceId, 1, 1), (linked.EvidenceId, linked.AddedCount, linked.AlreadyLinkedCount));

        var before = await M010ApiTestData.ReadAuditCountsAsync(factory);

        foreach (var transactionId in new[] { history.OtherHouseholdTransactionId, Guid.NewGuid() })
        {
            using var invalid = await M010ApiTestData.SendJsonAsync(
                client,
                linksRoute,
                new LinkEvidenceTransactionsRequest([transactionId]),
                idempotencyKey: null);
            await M010ApiTestData.AssertProblemAsync(
                invalid,
                HttpStatusCode.UnprocessableEntity,
                "EVIDENCE_TRANSACTION_LINK_INVALID");
        }

        // The record exists, but not in the other household.
        using var foreign = await M010ApiTestData.SendJsonAsync(
            client,
            $"/api/households/{history.OtherHouseholdId}/evidence/{evidenceId}/transaction-links",
            new LinkEvidenceTransactionsRequest([history.OtherHouseholdTransactionId]),
            idempotencyKey: null);
        await M010ApiTestData.AssertProblemAsync(foreign, HttpStatusCode.NotFound, "EVIDENCE_NOT_FOUND");
        await M010ApiTestData.AssertGetProblemAsync(
            client,
            $"/api/households/{history.OtherHouseholdId}/evidence/{evidenceId}",
            HttpStatusCode.NotFound,
            "EVIDENCE_NOT_FOUND",
            "Synthetic");
        Assert.Equal(before, await M010ApiTestData.ReadAuditCountsAsync(factory));

        var replacementId = await M010ApiTestData.RecordStatementAsync(
            client,
            history,
            M010ApiTestData.Statement(
                history,
                "Synthetic corrected statement",
                supersedesEvidenceId: evidenceId));
        var original = await M010ApiTestData.GetAsync<EvidenceDetailResponse>(client, $"{route}/{evidenceId}");
        Assert.Equal(replacementId, original.SupersededByEvidenceId);
        Assert.Equal(2, original.TransactionLinks.Count);
        Assert.Equal(
            evidenceId,
            (await M010ApiTestData.GetAsync<EvidenceDetailResponse>(client, $"{route}/{replacementId}"))
                .SupersedesEvidenceId);

        using var superseded = await M010ApiTestData.SendJsonAsync(
            client,
            linksRoute,
            new LinkEvidenceTransactionsRequest([history.FundPurchaseId]),
            idempotencyKey: null);
        await M010ApiTestData.AssertProblemAsync(superseded, HttpStatusCode.Conflict, "EVIDENCE_SUPERSEDED");

        using var secondSupersession = await M010ApiTestData.SendJsonAsync(
            client,
            route,
            M010ApiTestData.Statement(history, "Synthetic second correction", supersedesEvidenceId: evidenceId),
            "evidence-second-supersession");
        await M010ApiTestData.AssertProblemAsync(
            secondSupersession,
            HttpStatusCode.Conflict,
            "EVIDENCE_SUPERSESSION_INVALID",
            "Synthetic second correction");
    }

    [Fact]
    public async Task Evidence_RecordsAndListsExactObservations()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var history = await M010ApiTestData.SeedAsync(factory, client);
        var setup = history.Setup;

        // A household-wide count, so any household account can be observed.
        var evidenceId = await M010ApiTestData.RecordStatementAsync(
            client,
            history,
            M010ApiTestData.Statement(history) with { InstitutionId = null, AccountId = null });
        var route = $"{history.HouseholdRoute}/evidence/{evidenceId}/observations";
        var request = new RecordEvidenceObservationsRequest(
        [
            new EvidenceObservationRequest(
                setup.PortfolioId,
                setup.AccountId,
                setup.CashAssetId,
                1_250 * M010ApiTestData.Unit),
            new EvidenceObservationRequest(
                setup.PortfolioId,
                setup.AccountId,
                setup.FundAssetId,
                125_000_000,
                ToleranceRawE8: 1_000)
        ]);

        var added = await M010ApiTestData.PostAsync<RecordEvidenceObservationsResponse>(client, route, request);
        Assert.Equal((2, 0), (added.AddedCount, added.AlreadyRecordedCount));
        var repeated = await M010ApiTestData.PostAsync<RecordEvidenceObservationsResponse>(client, route, request);
        Assert.Equal((0, 2), (repeated.AddedCount, repeated.AlreadyRecordedCount));

        var before = await M010ApiTestData.ReadAuditCountsAsync(factory);

        (RecordEvidenceObservationsRequest Body, HttpStatusCode Status, string Code)[] refusals =
        [
            (new([request.Observations[0] with { ObservedQuantityRawE8 = 1 }]),
                HttpStatusCode.Conflict, "EVIDENCE_OBSERVATION_CONFLICT"),
            (new([new EvidenceObservationRequest(history.OtherPortfolioId, history.OtherAccountId, setup.CashAssetId, 1)]),
                HttpStatusCode.NotFound, "EVIDENCE_OBSERVATION_SCOPE_NOT_FOUND"),
            (new([request.Observations[0] with { AccountId = history.DirectAccountId, ObservedPieceCount = 1 }]),
                HttpStatusCode.UnprocessableEntity, "EVIDENCE_OBSERVATION_INVALID"),
            (new([request.Observations[0] with { AccountId = history.DirectAccountId, ToleranceRawE8 = -1 }]),
                HttpStatusCode.UnprocessableEntity, "EVIDENCE_OBSERVATION_INVALID"),
            (new([]), HttpStatusCode.UnprocessableEntity, "EVIDENCE_OBSERVATION_INVALID")
        ];

        foreach (var (body, status, code) in refusals)
        {
            using var response = await M010ApiTestData.SendJsonAsync(client, route, body, idempotencyKey: null);
            Assert.Equal(code, await ReadCodeAsync(response));
            Assert.Equal(status, response.StatusCode);
        }

        // A record about one account cannot observe another.
        var accountStatementId = await M010ApiTestData.RecordStatementAsync(client, history);
        before = await M010ApiTestData.ReadAuditCountsAsync(factory);
        using (var outside = await M010ApiTestData.SendJsonAsync(
                   client,
                   $"{history.HouseholdRoute}/evidence/{accountStatementId}/observations",
                   new RecordEvidenceObservationsRequest(
                       [request.Observations[0] with { AccountId = history.DirectAccountId }]),
                   idempotencyKey: null))
        {
            await M010ApiTestData.AssertProblemAsync(
                outside,
                HttpStatusCode.UnprocessableEntity,
                "EVIDENCE_OBSERVATION_SCOPE_MISMATCH");
        }

        Assert.Equal(before, await M010ApiTestData.ReadAuditCountsAsync(factory));

        var list = await M010ApiTestData.GetAsync<EvidenceObservationListResponse>(client, route);
        Assert.Equal((evidenceId, new DateOnly(2026, 1, 31)), (list.EvidenceId, list.EffectiveDate));
        Assert.Equal(2, list.Items.Count);
        var fund = Assert.Single(list.Items, item => item.Scope.AssetId == setup.FundAssetId);
        Assert.Equal((125_000_000L, 1_000L, (int?)null), (fund.ObservedQuantityRawE8, fund.ToleranceRawE8, fund.ObservedPieceCount));
        Assert.Equal(("CORE", "PRIMARY", "SYNTHETIC_FUND", "FUND"), (fund.Scope.PortfolioCode, fund.Scope.AccountCode, fund.Scope.AssetCode, fund.Scope.AssetTypeCode));
    }

    [Fact]
    public async Task Evidence_RefusesMalformedAndCrossHouseholdRequestsBeforeMutation()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var history = await M010ApiTestData.SeedAsync(factory, client);
        var route = $"{history.HouseholdRoute}/evidence";
        var valid = M010ApiTestData.Statement(history);
        var before = await M010ApiTestData.ReadAuditCountsAsync(factory);

        async Task AssertRawAsync(string body, string contentType, HttpStatusCode status, string code)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, route)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType)
            };
            request.Headers.Add("Idempotency-Key", $"evidence-malformed-{Guid.NewGuid():N}");
            using var response = await client.SendAsync(request);
            await M010ApiTestData.AssertProblemAsync(response, status, code, "Synthetic private");
        }

        await AssertRawAsync("{\"label\":\"Synthetic private\"}", "text/plain", HttpStatusCode.UnsupportedMediaType, "EVIDENCE_REQUEST_INVALID");
        await AssertRawAsync("{not json", "application/json", HttpStatusCode.BadRequest, "EVIDENCE_REQUEST_INVALID");
        await AssertRawAsync(
            "{\"sourceKindCode\":\"STATEMENT\",\"effectiveDate\":\"2026-01-31\",\"label\":\"Synthetic private\",\"unexpected\":1}",
            "application/json",
            HttpStatusCode.BadRequest,
            "EVIDENCE_REQUEST_INVALID");
        await AssertRawAsync(
            "{\"sourceKindCode\":\"STATEMENT\",\"effectiveDate\":\"2026-01-31\"}",
            "application/json",
            HttpStatusCode.BadRequest,
            "EVIDENCE_REQUEST_INVALID");
        await AssertRawAsync(
            "{\"sourceKindCode\":\"STATEMENT\",\"effectiveDate\":\"2026-01-31\",\"label\":null}",
            "application/json",
            HttpStatusCode.BadRequest,
            "EVIDENCE_REQUEST_INVALID");
        await AssertRawAsync(
            "{\"sourceKindCode\":\"BANK_EXPORT\",\"effectiveDate\":\"2026-01-31\",\"label\":\"Synthetic private\"}",
            "application/json",
            HttpStatusCode.BadRequest,
            "EVIDENCE_REQUEST_INVALID");
        await AssertRawAsync(
            "{\"sourceKindCode\":\"STATEMENT\",\"effectiveDate\":\"31.01.2026\",\"label\":\"Synthetic private\"}",
            "application/json",
            HttpStatusCode.BadRequest,
            "EVIDENCE_REQUEST_INVALID");
        await AssertRawAsync(
            "{\"sourceKindCode\":\"STATEMENT\",\"effectiveDate\":\"2026-01-31\",\"label\":\"Synthetic private\",\"note\":\""
            + new string('N', 70 * 1024) + "\"}",
            "application/json",
            HttpStatusCode.RequestEntityTooLarge,
            "EVIDENCE_REQUEST_INVALID");

        (RecordEvidenceRequest Body, HttpStatusCode Status, string Code)[] refusals =
        [
            (valid with { Label = new string('L', 257) }, HttpStatusCode.UnprocessableEntity, "EVIDENCE_INPUT_INVALID"),
            (valid with { EffectiveDate = new DateOnly(2999, 1, 1) }, HttpStatusCode.UnprocessableEntity, "EVIDENCE_EFFECTIVE_DATE_IN_FUTURE"),
            (valid with { AccountId = history.DirectAccountId }, HttpStatusCode.UnprocessableEntity, "EVIDENCE_CONTEXT_MISMATCH"),
            (valid with { InstitutionId = null, AccountId = history.OtherAccountId }, HttpStatusCode.NotFound, "EVIDENCE_CONTEXT_NOT_FOUND"),
            (valid with { TransactionIds = [history.OtherHouseholdTransactionId] }, HttpStatusCode.UnprocessableEntity, "EVIDENCE_TRANSACTION_LINK_INVALID"),
            (valid with { SupersedesEvidenceId = Guid.NewGuid() }, HttpStatusCode.UnprocessableEntity, "EVIDENCE_SUPERSESSION_INVALID")
        ];

        foreach (var (body, status, code) in refusals)
        {
            using var response = await M010ApiTestData.SendJsonAsync(
                client,
                route,
                body,
                $"evidence-refusal-{Guid.NewGuid():N}");
            Assert.Equal(code, await ReadCodeAsync(response));
            Assert.Equal(status, response.StatusCode);
        }

        using (var unknownHousehold = await M010ApiTestData.SendJsonAsync(
                   client,
                   $"/api/households/{Guid.NewGuid()}/evidence",
                   valid,
                   "evidence-unknown-household"))
        {
            Assert.Equal("EVIDENCE_HOUSEHOLD_NOT_FOUND", await ReadCodeAsync(unknownHousehold));
        }

        using (var emptyHousehold = await M010ApiTestData.SendJsonAsync(
                   client,
                   $"/api/households/{Guid.Empty}/evidence",
                   valid,
                   "evidence-empty-household"))
        {
            await M010ApiTestData.AssertProblemAsync(emptyHousehold, HttpStatusCode.BadRequest, "EVIDENCE_REQUEST_INVALID");
        }

        Assert.Equal(before, await M010ApiTestData.ReadAuditCountsAsync(factory));

        foreach (var (query, status, code) in new (string, HttpStatusCode, string)[]
                 {
                     ("?pageSize=0", HttpStatusCode.BadRequest, "EVIDENCE_PAGE_SIZE_INVALID"),
                     ("?pageSize=101", HttpStatusCode.BadRequest, "EVIDENCE_PAGE_SIZE_INVALID"),
                     ("?cursor=PRIVATE%2BCURSOR", HttpStatusCode.BadRequest, "EVIDENCE_CURSOR_INVALID"),
                     ("?label=Synthetic", HttpStatusCode.BadRequest, "EVIDENCE_REQUEST_INVALID"),
                     ($"/{Guid.NewGuid()}", HttpStatusCode.NotFound, "EVIDENCE_NOT_FOUND"),
                     ($"/{Guid.NewGuid()}/observations", HttpStatusCode.NotFound, "EVIDENCE_NOT_FOUND"),
                     ($"/{Guid.Empty}", HttpStatusCode.BadRequest, "EVIDENCE_REQUEST_INVALID")
                 })
        {
            await M010ApiTestData.AssertGetProblemAsync(client, route + query, status, code, "PRIVATE+CURSOR");
        }

        await M010ApiTestData.AssertGetProblemAsync(
            client,
            $"/api/households/{Guid.NewGuid()}/evidence",
            HttpStatusCode.NotFound,
            "EVIDENCE_HOUSEHOLD_NOT_FOUND");

        // Evidence is corrected by supersession: no update or delete route.
        var evidenceId = await M010ApiTestData.RecordStatementAsync(client, history);

        using (var put = await client.PutAsJsonAsync($"{route}/{evidenceId}", valid))
        {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, put.StatusCode);
        }

        using (var delete = await client.DeleteAsync($"{route}/{evidenceId}"))
        {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, delete.StatusCode);
        }

        using (var deleteLinks = await client.DeleteAsync($"{route}/{evidenceId}/transaction-links"))
        {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, deleteLinks.StatusCode);
        }
    }

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        return document.RootElement.TryGetProperty("code", out var code)
            ? code.GetString()
            : null;
    }
}
