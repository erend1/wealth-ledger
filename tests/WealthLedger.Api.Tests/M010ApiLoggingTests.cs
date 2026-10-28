using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using WealthLedger.Api.Contracts;

namespace WealthLedger.Api.Tests;

public sealed class M010ApiLoggingTests
{
    [Fact]
    public async Task M010Routes_LogOnlyRouteTemplatesOutcomesCountsSizeClassesAndDurations()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var history = await M010ApiTestData.SeedAsync(factory, client);
        var gold = await M010ApiTestData.SeedGoldAsync(factory, client);
        var setup = history.Setup;
        var households = history.HouseholdRoute;
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n%synthetic logged statement\n%%EOF\n");
        var sha256 = Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant();
        var statement = M010ApiTestData.Statement(history) with
        {
            EffectiveDate = new DateOnly(2026, 3, 31),
            InstitutionId = null,
            AccountId = null
        };

        // Successful reads and commands across every M010 route.
        var searchPage = await M010ApiTestData.GetAsync<
            NavigationPageResponse<LedgerSearchTransactionResponse>>(
            client,
            $"{households}/ledger/search?externalReference=SYNTH-JAN&pageSize=1");
        var searchCursor = Assert.IsType<string>(searchPage.NextCursor);
        await M010ApiTestData.GetAsync<PositionInventoryResponse>(client, $"{households}/inventory/positions");
        var lotPage = await M010ApiTestData.GetAsync<LotInventoryResponse>(client, $"{households}/inventory/lots?pageSize=1");
        var lotCursor = Assert.IsType<string>(lotPage.NextCursor);

        using var uploaded = await EvidenceAttachmentApiTests.UploadAsync(
            client,
            history,
            EvidenceAttachmentApiTests.Upload(statement, "synthetic-logged.pdf", "application/pdf", pdf),
            "m010-log-upload-key");
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var evidenceId = (await uploaded.Content.ReadFromJsonAsync<RecordEvidenceResponse>())!.EvidenceId;
        var evidence = $"{households}/evidence/{evidenceId}";
        await M010ApiTestData.PostAsync<LinkEvidenceTransactionsResponse>(
            client,
            $"{evidence}/transaction-links",
            new LinkEvidenceTransactionsRequest([history.FirstJanuaryId]));
        await M010ApiTestData.PostAsync<RecordEvidenceObservationsResponse>(
            client,
            $"{evidence}/observations",
            new RecordEvidenceObservationsRequest(
            [
                new EvidenceObservationRequest(setup.PortfolioId, setup.AccountId, setup.FundAssetId, 130_000_000, ToleranceRawE8: 1_000),
                new EvidenceObservationRequest(setup.PortfolioId, gold.SourceVaultId, gold.GoldAssetId, 15 * M010ApiTestData.Unit, ObservedPieceCount: 1)
            ]));
        await M010ApiTestData.GetAsync<NavigationPageResponse<EvidenceSummaryResponse>>(client, $"{households}/evidence");
        await M010ApiTestData.GetAsync<EvidenceDetailResponse>(client, evidence);
        await M010ApiTestData.GetAsync<EvidenceObservationListResponse>(client, $"{evidence}/observations");

        using (var download = await client.GetAsync($"{evidence}/attachment"))
        {
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        }

        var run = await M010ApiTestData.PostAsync<RunReconciliationResponse>(
            client,
            $"{households}/reconciliations",
            new RunReconciliationRequest(evidenceId),
            "m010-log-run-key");
        var detail = await M010ApiTestData.GetAsync<ReconciliationRunDetailResponse>(client, run.ReconciliationLocation);
        var outOfTolerance = Assert.Single(detail.Items, item => item.Scope.AssetId == setup.FundAssetId);
        Assert.Equal("OUT_OF_TOLERANCE", outOfTolerance.ComparisonStatusCode);
        await M010ApiTestData.PostAsync<ResolveReconciliationItemResponse>(
            client,
            $"{run.ReconciliationLocation}/items/{outOfTolerance.ReconciliationItemId}/resolution",
            new ResolveReconciliationItemRequest("DOCUMENTED_EXCEPTION", Explanation: "Synthetic logged explanation."));
        await M010ApiTestData.GetAsync<NavigationPageResponse<ReconciliationRunSummaryResponse>>(client, $"{households}/reconciliations");

        // Refusals: handler-level, Application-level and size refusals.
        var unknownEvidenceId = Guid.NewGuid();
        using (await client.GetAsync($"{households}/ledger/search?cursor=PRIVATE-CURSOR-PAYLOAD"))
        {
        }

        using (await client.GetAsync($"{households}/ledger/search?type=CONTRIBUTION&cursor={Uri.EscapeDataString(searchCursor)}"))
        {
        }

        using (await client.GetAsync($"{households}/inventory/lots?includeExhausted=true&cursor={Uri.EscapeDataString(lotCursor)}"))
        {
        }

        using (await client.GetAsync($"{households}/inventory/positions?accountId={history.OtherAccountId}"))
        {
        }

        using (await client.GetAsync($"{households}/evidence/{unknownEvidenceId}"))
        {
        }

        using (await M010ApiTestData.SendJsonAsync(
                   client,
                   $"{households}/evidence",
                   statement with { Label = "Synthetic conflicting label" },
                   "m010-log-upload-key"))
        {
        }

        using (await EvidenceAttachmentApiTests.UploadAsync(
                   client,
                   history,
                   EvidenceAttachmentApiTests.Upload(statement, "synthetic-logged.svg", "image/svg+xml", Encoding.UTF8.GetBytes("<svg/>")),
                   "m010-log-refused-upload"))
        {
        }

        using (await M010ApiTestData.SendJsonAsync(
                   client,
                   $"{run.ReconciliationLocation}/items/{outOfTolerance.ReconciliationItemId}/resolution",
                   new ResolveReconciliationItemRequest("DOCUMENTED_EXCEPTION", Explanation: "Synthetic conflicting explanation."),
                   idempotencyKey: null))
        {
        }

        var logs = string.Join(Environment.NewLine, factory.Logs.Messages);

        Assert.Contains(
            "route /api/households/{householdId:guid}/ledger/search with outcome SUCCESS, status 200, code NONE, item count 1, size class NONE",
            logs,
            StringComparison.Ordinal);
        Assert.Contains(
            "route /api/households/{householdId:guid}/ledger/search with outcome REJECTED, status 400, code LEDGER_SEARCH_CURSOR_INVALID",
            logs,
            StringComparison.Ordinal);
        Assert.Contains(
            "route /api/households/{householdId:guid}/ledger/search with outcome REJECTED, status 400, code LEDGER_SEARCH_CURSOR_SCOPE_MISMATCH",
            logs,
            StringComparison.Ordinal);
        Assert.Contains(
            "route /api/households/{householdId:guid}/inventory/lots with outcome REJECTED, status 400, code INVENTORY_CURSOR_SCOPE_MISMATCH",
            logs,
            StringComparison.Ordinal);
        Assert.Contains(
            "route /api/households/{householdId:guid}/inventory/positions with outcome REJECTED, status 404, code INVENTORY_SCOPE_NOT_FOUND",
            logs,
            StringComparison.Ordinal);
        Assert.Contains(
            "route /api/households/{householdId:guid}/evidence/uploads with outcome SUCCESS, status 201, code NONE, item count 1, size class UP_TO_64_KIB",
            logs,
            StringComparison.Ordinal);
        Assert.Contains(
            "route /api/households/{householdId:guid}/evidence/uploads with outcome REJECTED, status 415, code EVIDENCE_ATTACHMENT_TYPE_UNSUPPORTED",
            logs,
            StringComparison.Ordinal);
        Assert.Contains(
            "route /api/households/{householdId:guid}/evidence with outcome REJECTED, status 409, code IDEMPOTENCY_KEY_CONFLICT",
            logs,
            StringComparison.Ordinal);
        Assert.Contains(
            "route /api/households/{householdId:guid}/evidence/{evidenceId:guid} with outcome REJECTED, status 404, code EVIDENCE_NOT_FOUND",
            logs,
            StringComparison.Ordinal);
        Assert.Contains(
            "route /api/households/{householdId:guid}/evidence/{evidenceId:guid}/attachment with outcome SUCCESS, status 200, code NONE, item count 1, size class UP_TO_64_KIB",
            logs,
            StringComparison.Ordinal);
        Assert.Contains(
            "route /api/households/{householdId:guid}/evidence/{evidenceId:guid}/observations with outcome SUCCESS, status 200, code NONE, item count 2",
            logs,
            StringComparison.Ordinal);
        Assert.Contains(
            "route /api/households/{householdId:guid}/reconciliations with outcome SUCCESS, status 201",
            logs,
            StringComparison.Ordinal);
        Assert.Contains(
            "route /api/households/{householdId:guid}/reconciliations/{reconciliationRunId:guid} with outcome SUCCESS, status 200, code NONE, item count 2",
            logs,
            StringComparison.Ordinal);
        Assert.Contains(
            "/resolution with outcome REJECTED, status 409, code RECONCILIATION_ITEM_ALREADY_RESOLVED",
            logs,
            StringComparison.Ordinal);

        foreach (var marker in new[]
                 {
                     // Identities and request secrets.
                     setup.HouseholdId.ToString("D"),
                     evidenceId.ToString("D"),
                     unknownEvidenceId.ToString("D"),
                     run.ReconciliationRunId.ToString("D"),
                     outOfTolerance.ReconciliationItemId.ToString("D"),
                     "m010-log-upload-key",
                     "m010-log-run-key",
                     "PRIVATE-CURSOR-PAYLOAD",
                     searchCursor,
                     lotCursor,

                     // Evidence and reconciliation content.
                     "Synthetic January statement",
                     "Synthetic conflicting label",
                     "Synthetic private statement note",
                     "SYNTH-STATEMENT-REF",
                     "synthetic-logged",
                     sha256,
                     "%PDF",
                     "Synthetic logged explanation",
                     "Synthetic conflicting explanation",

                     // Ledger references, master names and financial values.
                     "SYNTH-JAN",
                     "SYNTH-CERT-001",
                     "Synthetic Fund",
                     "Synthetic Physical Gold",
                     "Synthetic Vault",
                     "Core Portfolio",
                     "Primary Account",
                     "SYNTHETIC_FUND",
                     "130000000",
                     "125000000",
                     "1500000000",

                     // Storage internals.
                     factory.DatabasePath,
                     "SELECT ",
                     "INSERT ",
                     "SqliteException",
                     "EntityFrameworkCore",
                     " at WealthLedger"
                 })
        {
            Assert.DoesNotContain(marker, logs, StringComparison.OrdinalIgnoreCase);
        }
    }
}
