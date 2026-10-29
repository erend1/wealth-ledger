using System.Net;
using System.Net.Http.Json;
using System.Text;
using WealthLedger.Api.Contracts;

namespace WealthLedger.Api.Tests;

public sealed class ReconciliationApiTests
{
    private const long Unit = M010ApiTestData.Unit;
    private static readonly DateOnly EvidenceDate = new(2026, 3, 31);

    [Fact]
    public async Task Run_RecordsMatchedAndOutOfToleranceItemsAndReplaysWithoutLedgerWrites()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var history = await M010ApiTestData.SeedAsync(factory, client);
        var evidenceId = await ObservedStatementAsync(client, history, cashObserved: 1_250 * Unit);
        var route = $"{history.HouseholdRoute}/reconciliations";
        var ledgerBefore = await M010ApiTestData.ReadLedgerCountsAsync(factory);

        using var created = await M010ApiTestData.SendJsonAsync(
            client,
            route,
            new RunReconciliationRequest(evidenceId),
            "reconcile-key-1");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var run = (await created.Content.ReadFromJsonAsync<RunReconciliationResponse>())!;
        Assert.False(run.WasReplay);
        Assert.Equal($"{route}/{run.ReconciliationRunId}", created.Headers.Location?.OriginalString);
        Assert.Equal(created.Headers.Location?.OriginalString, run.ReconciliationLocation);

        using var replay = await M010ApiTestData.SendJsonAsync(
            client,
            route,
            new RunReconciliationRequest(evidenceId),
            "reconcile-key-1");
        var replayed = (await replay.Content.ReadFromJsonAsync<RunReconciliationResponse>())!;
        Assert.Equal((run.ReconciliationRunId, true), (replayed.ReconciliationRunId, replayed.WasReplay));

        var otherEvidenceId = await ObservedStatementAsync(client, history, cashObserved: 1);
        using var conflict = await M010ApiTestData.SendJsonAsync(
            client,
            route,
            new RunReconciliationRequest(otherEvidenceId),
            "reconcile-key-1");
        await M010ApiTestData.AssertProblemAsync(conflict, HttpStatusCode.Conflict, "IDEMPOTENCY_KEY_CONFLICT");

        var detail = await M010ApiTestData.GetAsync<ReconciliationRunDetailResponse>(client, run.ReconciliationLocation);
        Assert.Equal((evidenceId, EvidenceDate, "EXACT_SCOPE_QUANTITY_AS_OF", 1, false),
            (detail.EvidenceId, detail.EvidenceEffectiveDate, detail.MethodCode, detail.MethodVersion, detail.IsStale));
        Assert.Equal(2, detail.Items.Count);

        var cash = Assert.Single(detail.Items, item => item.Scope.AssetId == history.Setup.CashAssetId);
        Assert.Equal((1_250 * Unit, 1_250 * Unit, 0L, 0L, "MATCHED", (string?)null),
            (cash.ObservedQuantityRawE8, cash.LedgerQuantityRawE8, cash.DeltaRawE8, cash.ToleranceRawE8,
             cash.ComparisonStatusCode, cash.ResolutionStateCode));
        var fund = Assert.Single(detail.Items, item => item.Scope.AssetId == history.Setup.FundAssetId);
        Assert.Equal((130_000_000L, 125_000_000L, 5_000_000L, 1_000L, "OUT_OF_TOLERANCE", "OPEN"),
            (fund.ObservedQuantityRawE8, fund.LedgerQuantityRawE8, fund.DeltaRawE8, fund.ToleranceRawE8,
             fund.ComparisonStatusCode, fund.ResolutionStateCode));
        Assert.Equal((125_000_000L, false), (fund.CurrentLedgerQuantityRawE8, fund.IsStale));
        Assert.Equal("SYNTHETIC_FUND", fund.Scope.AssetCode);

        // A second run of the same evidence, then pages of one.
        await M010ApiTestData.PostAsync<RunReconciliationResponse>(
            client,
            route,
            new RunReconciliationRequest(evidenceId),
            "reconcile-key-2");
        var first = await M010ApiTestData.GetAsync<NavigationPageResponse<ReconciliationRunSummaryResponse>>(
            client,
            $"{route}?evidenceId={evidenceId}&pageSize=1");
        var summary = Assert.Single(first.Items);
        Assert.Equal((2, 1, 1, 1, 0, 0),
            (summary.ItemCount, summary.MatchedCount, summary.OutOfToleranceCount, summary.OpenCount,
             summary.CorrectedByTransactionCount, summary.DocumentedExceptionCount));
        var cursor = Uri.EscapeDataString(Assert.IsType<string>(first.NextCursor));
        var second = await M010ApiTestData.GetAsync<NavigationPageResponse<ReconciliationRunSummaryResponse>>(
            client,
            $"{route}?evidenceId={evidenceId}&pageSize=1&cursor={cursor}");
        Assert.Single(second.Items);
        Assert.Null(second.NextCursor);
        Assert.NotEqual(summary.ReconciliationRunId, second.Items[0].ReconciliationRunId);

        await M010ApiTestData.AssertGetProblemAsync(
            client,
            $"{route}?evidenceId={otherEvidenceId}&pageSize=1&cursor={cursor}",
            HttpStatusCode.BadRequest,
            "RECONCILIATION_CURSOR_SCOPE_MISMATCH");

        // Comparison writes audit history only, never a ledger row.
        Assert.Equal(ledgerBefore, await M010ApiTestData.ReadLedgerCountsAsync(factory));
    }

    [Fact]
    public async Task Resolve_DocumentsExceptionsAndLinksOnlyPostedSameHouseholdCorrections()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var history = await M010ApiTestData.SeedAsync(factory, client);

        // 50 more cash than the ledger holds, and the fund out of tolerance.
        var evidenceId = await ObservedStatementAsync(client, history, cashObserved: 1_300 * Unit);
        var run = await M010ApiTestData.PostAsync<RunReconciliationResponse>(
            client,
            $"{history.HouseholdRoute}/reconciliations",
            new RunReconciliationRequest(evidenceId));
        var detail = await M010ApiTestData.GetAsync<ReconciliationRunDetailResponse>(client, run.ReconciliationLocation);
        var cash = Assert.Single(detail.Items, item => item.Scope.AssetId == history.Setup.CashAssetId);
        var fund = Assert.Single(detail.Items, item => item.Scope.AssetId == history.Setup.FundAssetId);
        string ResolutionRoute(Guid itemId) => $"{run.ReconciliationLocation}/items/{itemId}/resolution";

        var before = await M010ApiTestData.ReadAuditCountsAsync(factory);

        (Guid ItemId, ResolveReconciliationItemRequest Body, HttpStatusCode Status, string Code)[] refusals =
        [
            (fund.ReconciliationItemId, new("DOCUMENTED_EXCEPTION"), HttpStatusCode.UnprocessableEntity, "RECONCILIATION_RESOLUTION_INVALID"),
            (fund.ReconciliationItemId, new("DOCUMENTED_EXCEPTION", Explanation: "   "), HttpStatusCode.UnprocessableEntity, "RECONCILIATION_RESOLUTION_INVALID"),
            (fund.ReconciliationItemId, new("CORRECTED_BY_TRANSACTION"), HttpStatusCode.UnprocessableEntity, "RECONCILIATION_RESOLUTION_INVALID"),
            (cash.ReconciliationItemId, new("CORRECTED_BY_TRANSACTION", history.OtherHouseholdTransactionId), HttpStatusCode.UnprocessableEntity, "RECONCILIATION_CORRECTION_INVALID"),
            (cash.ReconciliationItemId, new("CORRECTED_BY_TRANSACTION", Guid.NewGuid()), HttpStatusCode.UnprocessableEntity, "RECONCILIATION_CORRECTION_INVALID"),
            (Guid.NewGuid(), new("DOCUMENTED_EXCEPTION", Explanation: "Synthetic private reason."), HttpStatusCode.NotFound, "RECONCILIATION_ITEM_NOT_FOUND")
        ];

        foreach (var (itemId, body, status, code) in refusals)
        {
            using var response = await M010ApiTestData.SendJsonAsync(client, ResolutionRoute(itemId), body, idempotencyKey: null);
            await M010ApiTestData.AssertProblemAsync(response, status, code, "Synthetic private reason.");
        }

        foreach (var (raw, code) in new[]
                 {
                     ("{\"kindCode\":\"MATCHED\"}", "RECONCILIATION_REQUEST_INVALID"),
                     ("{\"kindCode\":\"DOCUMENTED_EXCEPTION\",\"explanation\":\"x\",\"extra\":true}", "RECONCILIATION_REQUEST_INVALID"),
                     ("{}", "RECONCILIATION_REQUEST_INVALID")
                 })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ResolutionRoute(fund.ReconciliationItemId))
            {
                Content = new StringContent(raw, Encoding.UTF8, "application/json")
            };
            using var response = await client.SendAsync(request);
            await M010ApiTestData.AssertProblemAsync(response, HttpStatusCode.BadRequest, code);
        }

        // The run exists, but not in the other household.
        using (var foreign = await M010ApiTestData.SendJsonAsync(
                   client,
                   $"/api/households/{history.OtherHouseholdId}/reconciliations/{run.ReconciliationRunId}"
                   + $"/items/{fund.ReconciliationItemId}/resolution",
                   new ResolveReconciliationItemRequest("DOCUMENTED_EXCEPTION", Explanation: "Synthetic private reason."),
                   idempotencyKey: null))
        {
            await M010ApiTestData.AssertProblemAsync(foreign, HttpStatusCode.NotFound, "RECONCILIATION_ITEM_NOT_FOUND");
        }

        Assert.Equal(before, await M010ApiTestData.ReadAuditCountsAsync(factory));

        var documented = new ResolveReconciliationItemRequest(
            "DOCUMENTED_EXCEPTION",
            Explanation: "Synthetic broker rounding note.");
        var resolved = await M010ApiTestData.PostAsync<ResolveReconciliationItemResponse>(
            client,
            ResolutionRoute(fund.ReconciliationItemId),
            documented);
        Assert.Equal((fund.ReconciliationItemId, "DOCUMENTED_EXCEPTION", false),
            (resolved.ReconciliationItemId, resolved.KindCode, resolved.WasAlreadyResolved));
        var repeated = await M010ApiTestData.PostAsync<ResolveReconciliationItemResponse>(
            client,
            ResolutionRoute(fund.ReconciliationItemId),
            documented);
        Assert.True(repeated.WasAlreadyResolved);

        using (var different = await M010ApiTestData.SendJsonAsync(
                   client,
                   ResolutionRoute(fund.ReconciliationItemId),
                   documented with { Explanation = "Synthetic other note." },
                   idempotencyKey: null))
        {
            await M010ApiTestData.AssertProblemAsync(
                different,
                HttpStatusCode.Conflict,
                "RECONCILIATION_ITEM_ALREADY_RESOLVED",
                "Synthetic other note.");
        }

        // The missing cash is corrected by a real Posted contribution.
        var correctionId = await M010ApiTestData.ContributeAsync(
            client,
            history.Setup.HouseholdId,
            history.Setup.PortfolioId,
            history.Setup.AccountId,
            history.Setup.CashAssetId,
            5_000,
            new DateOnly(2026, 3, 15),
            "SYNTH-CORRECTION-001");
        var corrected = await M010ApiTestData.PostAsync<ResolveReconciliationItemResponse>(
            client,
            ResolutionRoute(cash.ReconciliationItemId),
            new ResolveReconciliationItemRequest("CORRECTED_BY_TRANSACTION", correctionId));
        Assert.Equal("CORRECTED_BY_TRANSACTION", corrected.KindCode);

        // Resolutions never relabel a difference as matched; the correction
        // changed effective history, so the old run is now stale.
        var afterResolution = await M010ApiTestData.GetAsync<ReconciliationRunDetailResponse>(client, run.ReconciliationLocation);
        Assert.True(afterResolution.IsStale);
        var resolvedFund = Assert.Single(afterResolution.Items, item => item.ReconciliationItemId == fund.ReconciliationItemId);
        Assert.Equal(("OUT_OF_TOLERANCE", "DOCUMENTED_EXCEPTION", "Synthetic broker rounding note."),
            (resolvedFund.ComparisonStatusCode, resolvedFund.ResolutionStateCode, resolvedFund.Explanation));
        var resolvedCash = Assert.Single(afterResolution.Items, item => item.ReconciliationItemId == cash.ReconciliationItemId);
        Assert.Equal(("OUT_OF_TOLERANCE", "CORRECTED_BY_TRANSACTION", (Guid?)correctionId, 50 * Unit),
            (resolvedCash.ComparisonStatusCode, resolvedCash.ResolutionStateCode, resolvedCash.CorrectiveTransactionId,
             resolvedCash.DeltaRawE8));
        Assert.Equal((1_300 * Unit, true), (resolvedCash.CurrentLedgerQuantityRawE8, resolvedCash.IsStale));

        // A rerun reflects the corrected history.
        var rerun = await M010ApiTestData.PostAsync<RunReconciliationResponse>(
            client,
            $"{history.HouseholdRoute}/reconciliations",
            new RunReconciliationRequest(evidenceId));
        var rerunCash = Assert.Single(
            (await M010ApiTestData.GetAsync<ReconciliationRunDetailResponse>(client, rerun.ReconciliationLocation)).Items,
            item => item.Scope.AssetId == history.Setup.CashAssetId);
        Assert.Equal(("MATCHED", 0L), (rerunCash.ComparisonStatusCode, rerunCash.DeltaRawE8));

        // A matched item needs no resolution.
        using var matched = await M010ApiTestData.SendJsonAsync(
            client,
            $"{rerun.ReconciliationLocation}/items/{rerunCash.ReconciliationItemId}/resolution",
            documented,
            idempotencyKey: null);
        await M010ApiTestData.AssertProblemAsync(matched, HttpStatusCode.UnprocessableEntity, "RECONCILIATION_ITEM_MATCHED");
    }

    [Fact]
    public async Task Get_LaterReversalMakesTheEarlierRunVisiblyStale()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var history = await M010ApiTestData.SeedAsync(factory, client);
        var evidenceId = await ObservedStatementAsync(client, history, cashObserved: 1_250 * Unit);
        var run = await M010ApiTestData.PostAsync<RunReconciliationResponse>(
            client,
            $"{history.HouseholdRoute}/reconciliations",
            new RunReconciliationRequest(evidenceId));
        Assert.False((await M010ApiTestData.GetAsync<ReconciliationRunDetailResponse>(client, run.ReconciliationLocation)).IsStale);

        // The reversal keeps the January date, inside the statement's as-of.
        await M010ApiTestData.PostAsync<ReversePostedTransactionResponse>(
            client,
            $"/api/ledger/transactions/{history.SecondJanuaryId}/reversals",
            new ReversePostedTransactionRequest("Synthetic later reversal."));

        var stale = await M010ApiTestData.GetAsync<ReconciliationRunDetailResponse>(client, run.ReconciliationLocation);
        Assert.True(stale.IsStale);
        var cash = Assert.Single(stale.Items, item => item.Scope.AssetId == history.Setup.CashAssetId);
        Assert.Equal((1_250 * Unit, 0L, "MATCHED", 750 * Unit, true),
            (cash.LedgerQuantityRawE8, cash.DeltaRawE8, cash.ComparisonStatusCode,
             cash.CurrentLedgerQuantityRawE8, cash.IsStale));
        var fund = Assert.Single(stale.Items, item => item.Scope.AssetId == history.Setup.FundAssetId);
        Assert.False(fund.IsStale);

        // The stored summary is not rewritten.
        var summary = Assert.Single(
            (await M010ApiTestData.GetAsync<NavigationPageResponse<ReconciliationRunSummaryResponse>>(
                client,
                $"{history.HouseholdRoute}/reconciliations")).Items);
        Assert.Equal((1, 1), (summary.MatchedCount, summary.OutOfToleranceCount));
    }

    [Fact]
    public async Task Reconciliation_RefusesMalformedUnknownAndCrossHouseholdRequests()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var history = await M010ApiTestData.SeedAsync(factory, client);
        var route = $"{history.HouseholdRoute}/reconciliations";
        var unobservedId = await M010ApiTestData.RecordStatementAsync(client, history);
        var observedId = await ObservedStatementAsync(client, history, cashObserved: 1_250 * Unit);
        var run = await M010ApiTestData.PostAsync<RunReconciliationResponse>(
            client,
            route,
            new RunReconciliationRequest(observedId));
        var before = await M010ApiTestData.ReadAuditCountsAsync(factory);

        (string Route, object Body, string? Key, HttpStatusCode Status, string Code)[] commands =
        [
            (route, new RunReconciliationRequest(unobservedId), "reconcile-unobserved", HttpStatusCode.UnprocessableEntity, "RECONCILIATION_NO_OBSERVATIONS"),
            (route, new RunReconciliationRequest(Guid.NewGuid()), "reconcile-unknown", HttpStatusCode.NotFound, "RECONCILIATION_EVIDENCE_NOT_FOUND"),
            (route, new RunReconciliationRequest(Guid.Empty), "reconcile-empty", HttpStatusCode.BadRequest, "RECONCILIATION_REQUEST_INVALID"),
            (route, new { evidenceId = observedId, extra = 1 }, "reconcile-extra", HttpStatusCode.BadRequest, "RECONCILIATION_REQUEST_INVALID"),
            (route, new { }, "reconcile-missing", HttpStatusCode.BadRequest, "RECONCILIATION_REQUEST_INVALID"),
            (route, new RunReconciliationRequest(observedId), null, HttpStatusCode.BadRequest, "IDEMPOTENCY_KEY_REQUIRED"),
            ($"/api/households/{history.OtherHouseholdId}/reconciliations", new RunReconciliationRequest(observedId), "reconcile-foreign", HttpStatusCode.NotFound, "RECONCILIATION_EVIDENCE_NOT_FOUND"),
            // A run resolves its evidence inside the household first.
            ($"/api/households/{Guid.NewGuid()}/reconciliations", new RunReconciliationRequest(observedId), "reconcile-no-household", HttpStatusCode.NotFound, "RECONCILIATION_EVIDENCE_NOT_FOUND")
        ];

        foreach (var (commandRoute, body, key, status, code) in commands)
        {
            using var response = await M010ApiTestData.SendJsonAsync(client, commandRoute, body, key);
            await M010ApiTestData.AssertProblemAsync(response, status, code);
        }

        Assert.Equal(before, await M010ApiTestData.ReadAuditCountsAsync(factory));

        foreach (var (query, status, code) in new (string, HttpStatusCode, string)[]
                 {
                     ("?pageSize=0", HttpStatusCode.BadRequest, "RECONCILIATION_PAGE_SIZE_INVALID"),
                     ("?pageSize=many", HttpStatusCode.BadRequest, "RECONCILIATION_PAGE_SIZE_INVALID"),
                     ("?cursor=PRIVATE%2BCURSOR", HttpStatusCode.BadRequest, "RECONCILIATION_CURSOR_INVALID"),
                     ("?evidenceId=not-a-guid", HttpStatusCode.BadRequest, "RECONCILIATION_REQUEST_INVALID"),
                     ("?status=OPEN", HttpStatusCode.BadRequest, "RECONCILIATION_REQUEST_INVALID"),
                     ($"/{Guid.NewGuid()}", HttpStatusCode.NotFound, "RECONCILIATION_RUN_NOT_FOUND"),
                     ($"/{Guid.Empty}", HttpStatusCode.BadRequest, "RECONCILIATION_REQUEST_INVALID")
                 })
        {
            await M010ApiTestData.AssertGetProblemAsync(client, route + query, status, code, "PRIVATE+CURSOR");
        }

        await M010ApiTestData.AssertGetProblemAsync(
            client,
            $"/api/households/{history.OtherHouseholdId}/reconciliations/{run.ReconciliationRunId}",
            HttpStatusCode.NotFound,
            "RECONCILIATION_RUN_NOT_FOUND");
        Assert.Empty(
            (await M010ApiTestData.GetAsync<NavigationPageResponse<ReconciliationRunSummaryResponse>>(
                client,
                $"/api/households/{history.OtherHouseholdId}/reconciliations")).Items);
        await M010ApiTestData.AssertGetProblemAsync(
            client,
            $"/api/households/{Guid.NewGuid()}/reconciliations",
            HttpStatusCode.NotFound,
            "RECONCILIATION_HOUSEHOLD_NOT_FOUND");

        // No generic adjustment, edit or delete surface.
        using (var delete = await client.DeleteAsync(run.ReconciliationLocation))
        {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, delete.StatusCode);
        }

        using (var adjust = await M010ApiTestData.SendJsonAsync(
                   client,
                   $"{run.ReconciliationLocation}/adjustments",
                   new { },
                   idempotencyKey: null))
        {
            Assert.Equal(HttpStatusCode.NotFound, adjust.StatusCode);
        }
    }

    // A household-wide statement on the March 31 effective date observing
    // the primary account's cash and 1.30 fund units with a small tolerance.
    private static async Task<Guid> ObservedStatementAsync(
        HttpClient client,
        M010ApiHistory history,
        long cashObserved)
    {
        var setup = history.Setup;
        var evidenceId = await M010ApiTestData.RecordStatementAsync(
            client,
            history,
            M010ApiTestData.Statement(history) with
            {
                EffectiveDate = EvidenceDate,
                InstitutionId = null,
                AccountId = null
            });
        await M010ApiTestData.PostAsync<RecordEvidenceObservationsResponse>(
            client,
            $"{history.HouseholdRoute}/evidence/{evidenceId}/observations",
            new RecordEvidenceObservationsRequest(
            [
                new EvidenceObservationRequest(setup.PortfolioId, setup.AccountId, setup.CashAssetId, cashObserved),
                new EvidenceObservationRequest(setup.PortfolioId, setup.AccountId, setup.FundAssetId, 130_000_000, ToleranceRawE8: 1_000)
            ]));

        return evidenceId;
    }
}
