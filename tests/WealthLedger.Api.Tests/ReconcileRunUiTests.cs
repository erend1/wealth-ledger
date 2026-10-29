using System.Net;
using System.Text.RegularExpressions;
using WealthLedger.Api.Contracts;

namespace WealthLedger.Api.Tests;

/// <summary>
/// Drives M010 observations, reconciliation runs, staleness and resolution
/// through the Ready UI, against a synthetic statement dated 31 January.
/// </summary>
/// <remarks>
/// On 31 January the cash account holds the two January contributions,
/// 1 500 TRY, and no Fund units: the Fund purchase is dated 20 February.
/// </remarks>
public sealed partial class ReconcileRunUiTests
{
    private const string ExplanationText = "Synthetic broker timing difference.";

    [Fact]
    public async Task Observe_ReviewsThenRecordsAnExactObservationOnce()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = M010UiTestData.CreateClient(factory);
        var history = await M010UiTestData.SeedAsync(factory, client);
        var evidence = await ReconcileEvidenceUiTests.RecordStatementAsync(client, history);
        var observe = evidence + "/observe";

        var (formStatus, form) = await M010UiTestData.GetPageAsync(client, observe);

        Assert.Equal(HttpStatusCode.OK, formStatus);
        Assert.Contains($"value=\"{history.Setup.AccountId:D}\" selected=\"selected\"", form);

        var fields = ObservationFields(history, history.Setup.CashAssetId, "1500", form);
        using var review = await M010UiTestData.PostFormAsync(client, observe + "?handler=Review", fields);
        var reviewHtml = WebUtility.HtmlDecode(await review.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Contains("Kaydetmeden önce inceleyin", reviewHtml);
        Assert.Contains("150000000000", reviewHtml);
        Assert.Equal(0, (await M010ApiTestData.ReadAuditCountsAsync(factory))[4]);

        using var record = await M010UiTestData.PostFormAsync(client, observe + "?handler=Record", fields);

        Assert.Equal(HttpStatusCode.Redirect, record.StatusCode);
        Assert.Equal(
            $"{evidence}?observed=1&alreadyObserved=0",
            record.Headers.Location?.OriginalString);

        using var replay = await M010UiTestData.PostFormAsync(client, observe + "?handler=Record", fields);

        Assert.Equal(
            $"{evidence}?observed=0&alreadyObserved=1",
            replay.Headers.Location?.OriginalString);
        Assert.Equal(1, (await M010ApiTestData.ReadAuditCountsAsync(factory))[4]);

        var conflicting = ObservationFields(history, history.Setup.CashAssetId, "1499", form);
        using var conflict = await M010UiTestData.PostFormAsync(client, observe + "?handler=Record", conflicting);
        var conflictHtml = WebUtility.HtmlDecode(await conflict.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Contains("farklı bir gözlem zaten var", conflictHtml);

        foreach (var (quantity, tolerance, pieces, message, field) in new[]
                 {
                     ("1,123456789", "0", "", "en fazla 8 ondalık", "#Input_ObservedQuantity"),
                     ("1.234,5", "0", "", "en fazla 8 ondalık", "#Input_ObservedQuantity"),
                     ("1500", "-1", "", "Tolerans sıfır veya pozitif", "#Input_Tolerance"),
                     ("1500", "0", "2", "yalnızca fiziki altın", "#Input_ObservedPieceCount")
                 })
        {
            var invalid = ObservationFields(history, history.Setup.FundAssetId, quantity, form);
            invalid.RemoveAll(pair => pair.Key is "Input.Tolerance" or "Input.ObservedPieceCount");
            invalid.Add(new("Input.Tolerance", tolerance));
            invalid.Add(new("Input.ObservedPieceCount", pieces));

            using var refused = await M010UiTestData.PostFormAsync(client, observe + "?handler=Review", invalid);
            var refusedHtml = WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            Assert.Contains(message, refusedHtml);
            Assert.Contains($"href=\"{field}\"", refusedHtml);
        }

        var (_, detail) = await M010UiTestData.GetPageAsync(client, evidence);
        Assert.Contains("Gözlemler", detail);
        Assert.Contains("Defterle karşılaştır", detail);
    }

    [Fact]
    public async Task Run_ShowsMatchedAndOutOfToleranceAndKeepsADocumentedExceptionVisible()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = M010UiTestData.CreateClient(factory);
        var history = await M010UiTestData.SeedAsync(factory, client);
        var evidence = await ReconcileEvidenceUiTests.RecordStatementAsync(client, history);
        await ObserveAsync(client, history, evidence, history.Setup.CashAssetId, "1500");
        await ObserveAsync(client, history, evidence, history.Setup.FundAssetId, "1,25");
        var ledgerBefore = await M010ApiTestData.ReadLedgerCountsAsync(factory);

        var (run, replayLocation) = await RunTwiceAsync(client, evidence);

        Assert.Equal(run, replayLocation);

        var (status, html) = await M010UiTestData.GetPageAsync(client, run);
        var items = Items(html);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, items.Count);
        Assert.Contains("data-state=\"MATCHED\"", items[0].Html);
        Assert.DoesNotContain("data-state=\"OPEN\"", items[0].Html);
        Assert.DoesNotContain("handler=Exception", items[0].Html);
        Assert.Contains("data-state=\"OUT_OF_TOLERANCE\"", items[1].Html);
        Assert.Contains("data-state=\"OPEN\"", items[1].Html);
        Assert.Contains("1,25 fon birimi", items[1].Html);
        Assert.Contains("+1,25 fon birimi — artış", items[1].Html);
        Assert.Contains("0 fon birimi", items[1].Html);
        Assert.DoesNotContain("Bu karşılaştırma artık güncel değil", html);
        Assert.DoesNotContain("<dt>Piyasa", html);

        using (var empty = await PostRunFormAsync(client, run, "Exception", html,
                   new("itemId", items[1].Id.ToString("D")),
                   new("explanation", "   ")))
        {
            var emptyHtml = WebUtility.HtmlDecode(await empty.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.UnprocessableEntity, empty.StatusCode);
            Assert.Contains("açıklama zorunludur", emptyHtml);
            Assert.Contains($"href=\"#explanation-{items[1].Id:N}\"", emptyHtml);
        }

        using (var matched = await PostRunFormAsync(client, run, "Exception", html,
                   new("itemId", items[0].Id.ToString("D")),
                   new("explanation", ExplanationText)))
        {
            var matchedHtml = WebUtility.HtmlDecode(await matched.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.UnprocessableEntity, matched.StatusCode);
            Assert.Contains("Eşleşen bir kalem çözümlenmez.", matchedHtml);
        }

        using (var exception = await PostRunFormAsync(client, run, "Exception", html,
                   new("itemId", items[1].Id.ToString("D")),
                   new("explanation", ExplanationText)))
        {
            Assert.Equal(HttpStatusCode.Redirect, exception.StatusCode);
            Assert.Equal($"{run}?resolved=True", exception.Headers.Location?.OriginalString);
        }

        var (_, resolved) = await M010UiTestData.GetPageAsync(client, run);
        var resolvedItems = Items(resolved);

        Assert.Contains("data-state=\"OUT_OF_TOLERANCE\"", resolvedItems[1].Html);
        Assert.Contains("data-state=\"DOCUMENTED_EXCEPTION\"", resolvedItems[1].Html);
        Assert.DoesNotContain("data-state=\"MATCHED\"", resolvedItems[1].Html);
        Assert.Contains(ExplanationText, resolvedItems[1].Html);
        Assert.DoesNotContain("handler=Exception", resolved);
        Assert.Equal(ledgerBefore, await M010ApiTestData.ReadLedgerCountsAsync(factory));

        var (_, index) = await M010UiTestData.GetPageAsync(client, "/reconcile");
        Assert.Contains($"href=\"{run}\"", index);
        Assert.Contains("Eşleşti: 1", index);
        Assert.Contains("Belgelenmiş istisna: 1", index);

        var logs = string.Join(Environment.NewLine, factory.Logs.Messages);
        Assert.DoesNotContain(ExplanationText, logs);
        Assert.DoesNotContain("150000000000", logs);
    }

    [Fact]
    public async Task Run_TurnsStaleAfterAReversalAndARerunWithACorrectionMatches()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = M010UiTestData.CreateClient(factory);
        var history = await M010UiTestData.SeedAsync(factory, client);
        var setup = history.Setup;
        var evidence = await ReconcileEvidenceUiTests.RecordStatementAsync(client, history);
        await ObserveAsync(client, history, evidence, setup.CashAssetId, "1500");
        var (first, _) = await RunTwiceAsync(client, evidence);

        // A later reversal of a January contribution changes the ledger as of
        // the statement date: the stored run becomes stale, not rewritten.
        await M010ApiTestData.PostAsync<ReversePostedTransactionResponse>(
            client,
            $"/api/ledger/transactions/{history.FirstJanuaryId}/reversals",
            new ReversePostedTransactionRequest("Synthetic reversal."));

        var (_, stale) = await M010UiTestData.GetPageAsync(client, first);
        var staleItem = Items(stale).Single();

        Assert.Contains("Bu karşılaştırma artık güncel değil", stale);
        Assert.Contains("data-state=\"STALE\"", staleItem.Html);
        Assert.Contains("data-state=\"MATCHED\"", staleItem.Html);
        Assert.Contains("Bugün yeniden türetilen defter miktarı", staleItem.Html);
        Assert.Contains("150000000000", staleItem.Html);

        using var rerun = await PostRunFormAsync(client, first, "Rerun", stale,
            KeyValuePair.Create("rerunKey", M010UiTestData.HiddenValue(RerunKeyPattern(), stale, "rerun key")));
        var second = Assert.IsType<Uri>(rerun.Headers.Location).OriginalString;

        Assert.Equal(HttpStatusCode.Redirect, rerun.StatusCode);
        Assert.NotEqual(first, second);

        var (_, secondHtml) = await M010UiTestData.GetPageAsync(client, second);
        var openItem = Items(secondHtml).Single();

        Assert.DoesNotContain("Bu karşılaştırma artık güncel değil", secondHtml);
        Assert.Contains("data-state=\"OUT_OF_TOLERANCE\"", openItem.Html);
        Assert.Contains("data-state=\"OPEN\"", openItem.Html);

        // The correction is an ordinary contribution recorded through the
        // existing workflow, dated inside the statement period.
        var correction = await M010ApiTestData.ContributeAsync(
            client,
            setup.HouseholdId,
            setup.PortfolioId,
            setup.AccountId,
            setup.CashAssetId,
            100_000,
            new DateOnly(2026, 1, 20),
            "SYNTH-JAN-CORRECTION");
        var (choiceStatus, choices) = await M010UiTestData.GetPageAsync(
            client,
            $"{second}?correct={openItem.Id:D}");

        Assert.Equal(HttpStatusCode.OK, choiceStatus);
        Assert.Contains($"value=\"{correction:D}\"", choices);

        using (var foreign = await PostRunFormAsync(client, second, "Correct", choices,
                   new("itemId", openItem.Id.ToString("D")),
                   new("correctiveTransactionId", Guid.NewGuid().ToString("D"))))
        {
            var foreignHtml = WebUtility.HtmlDecode(await foreign.Content.ReadAsStringAsync());

            Assert.False(foreign.IsSuccessStatusCode);
            Assert.Contains("kalıcı olarak kaydedilmiş bir işlem olmalıdır", foreignHtml);
        }

        using (var correct = await PostRunFormAsync(client, second, "Correct", choices,
                   new("itemId", openItem.Id.ToString("D")),
                   new("correctiveTransactionId", correction.ToString("D"))))
        {
            Assert.Equal(HttpStatusCode.Redirect, correct.StatusCode);
        }

        var (_, corrected) = await M010UiTestData.GetPageAsync(client, second);
        var correctedItem = Items(corrected).Single();

        Assert.Contains("data-state=\"OUT_OF_TOLERANCE\"", correctedItem.Html);
        Assert.Contains("data-state=\"CORRECTED_BY_TRANSACTION\"", correctedItem.Html);
        Assert.Contains($"href=\"/ledger/{correction:D}\"", correctedItem.Html);

        using var third = await PostRunFormAsync(client, second, "Rerun", corrected,
            KeyValuePair.Create("rerunKey", M010UiTestData.HiddenValue(RerunKeyPattern(), corrected, "rerun key")));
        var (_, thirdHtml) = await M010UiTestData.GetPageAsync(
            client,
            Assert.IsType<Uri>(third.Headers.Location).OriginalString);

        Assert.Contains("data-state=\"MATCHED\"", Items(thirdHtml).Single().Html);
    }

    [Fact]
    public async Task Run_RefusesWithoutObservationsAndForSupersededEvidenceAndSanitizesUnknownRuns()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = M010UiTestData.CreateClient(factory);
        var history = await M010UiTestData.SeedAsync(factory, client);
        var evidence = await ReconcileEvidenceUiTests.RecordStatementAsync(client, history);

        var (_, empty) = await M010UiTestData.GetPageAsync(client, evidence);

        Assert.DoesNotContain("handler=Run", empty);

        using (var refused = await M010UiTestData.PostFormAsync(
                   client,
                   evidence + "?handler=Run",
                   [
                       new("__RequestVerificationToken", M010UiTestData.TokenFrom(empty)),
                       new("runKey", Guid.NewGuid().ToString("D"))
                   ]))
        {
            var refusedHtml = WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            Assert.Contains("en az bir gözlem girin", refusedHtml);
        }

        await ObserveAsync(client, history, evidence, history.Setup.CashAssetId, "1500");
        var (run, _) = await RunTwiceAsync(client, evidence);
        var evidenceId = evidence[(evidence.LastIndexOf('/') + 1)..];
        var form = await ReconcileEvidenceUiTests.GetEvidenceFormAsync(client, $"?supersedes={evidenceId}");
        var replacement = ReconcileEvidenceUiTests.Statement(history);
        replacement["Input.SupersedesEvidenceId"] = evidenceId;
        using (var post = await ReconcileEvidenceUiTests.PostEvidenceAsync(client, form, replacement))
        {
            Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        }

        var (runStatus, runHtml) = await M010UiTestData.GetPageAsync(client, run);

        Assert.Equal(HttpStatusCode.OK, runStatus);
        Assert.Contains("kanıtı yenisiyle değiştirildi", runHtml);
        Assert.DoesNotContain("handler=Rerun", runHtml);

        var (observeStatus, observeHtml) = await M010UiTestData.GetPageAsync(client, evidence + "/observe");

        Assert.Equal(HttpStatusCode.Conflict, observeStatus);
        Assert.DoesNotContain("handler=Review", observeHtml);

        var (unknownStatus, unknown) = await M010UiTestData.GetPageAsync(
            client,
            $"/reconcile/runs/{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.NotFound, unknownStatus);
        Assert.Contains("Karşılaştırma bu hanede bulunamadı.", unknown);
        M010UiTestData.AssertNoInternals(unknown);

        var (cursorStatus, cursorHtml) = await M010UiTestData.GetPageAsync(
            client,
            "/reconcile?runCursor=PRIVATE-RUN-CURSOR");

        Assert.Equal(HttpStatusCode.BadRequest, cursorStatus);
        Assert.DoesNotContain("PRIVATE-RUN-CURSOR", cursorHtml);
    }

    [Fact]
    public async Task EveryReconcilePostHandler_RefusesAMissingAntiforgeryTokenWithoutMutation()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = M010UiTestData.CreateClient(factory);
        var history = await M010UiTestData.SeedAsync(factory, client);
        var evidence = await ReconcileEvidenceUiTests.RecordStatementAsync(client, history);
        await ObserveAsync(client, history, evidence, history.Setup.FundAssetId, "1,25");
        var (run, _) = await RunTwiceAsync(client, evidence);
        var (_, runHtml) = await M010UiTestData.GetPageAsync(client, run);
        var item = Items(runHtml).Single();
        var auditBefore = await M010ApiTestData.ReadAuditCountsAsync(factory);
        var ledgerBefore = await M010ApiTestData.ReadLedgerCountsAsync(factory);
        var observation = ObservationFields(history, history.Setup.CashAssetId, "1500", runHtml)
            .Where(pair => pair.Key != "__RequestVerificationToken")
            .ToList();

        foreach (var (path, fields) in new (string Path, List<KeyValuePair<string, string>> Fields)[]
                 {
                     (evidence + "/observe?handler=Review", observation),
                     (evidence + "/observe?handler=Record", observation),
                     (evidence + "?handler=Run", [new("runKey", Guid.NewGuid().ToString("D"))]),
                     (evidence + "?handler=Link", [new("transactionIds", history.FirstJanuaryId.ToString("D"))]),
                     (run + "?handler=Rerun", [new("rerunKey", Guid.NewGuid().ToString("D"))]),
                     (run + "?handler=Exception", [new("itemId", item.Id.ToString("D")), new("explanation", ExplanationText)]),
                     (run + "?handler=Correct", [new("itemId", item.Id.ToString("D")), new("correctiveTransactionId", history.FundPurchaseId.ToString("D"))])
                 })
        {
            using var response = await M010UiTestData.PostFormAsync(client, path, fields);

            Assert.True(
                response.StatusCode == HttpStatusCode.BadRequest,
                $"{path} answered {(int)response.StatusCode} without an antiforgery token.");
        }

        Assert.Equal(auditBefore, await M010ApiTestData.ReadAuditCountsAsync(factory));
        Assert.Equal(ledgerBefore, await M010ApiTestData.ReadLedgerCountsAsync(factory));
    }

    private static List<KeyValuePair<string, string>> ObservationFields(
        M010UiHistory history,
        Guid assetId,
        string quantity,
        string html)
        =>
        [
            new("__RequestVerificationToken", M010UiTestData.TokenFrom(html)),
            new("Input.PortfolioId", history.Setup.PortfolioId.ToString("D")),
            new("Input.AccountId", history.Setup.AccountId.ToString("D")),
            new("Input.AssetId", assetId.ToString("D")),
            new("Input.ObservedQuantity", quantity),
            new("Input.Tolerance", "0"),
            new("Input.ObservedPieceCount", string.Empty)
        ];

    internal static async Task ObserveAsync(
        HttpClient client,
        M010UiHistory history,
        string evidence,
        Guid assetId,
        string quantity)
    {
        var (_, form) = await M010UiTestData.GetPageAsync(client, evidence + "/observe");
        using var record = await M010UiTestData.PostFormAsync(
            client,
            evidence + "/observe?handler=Record",
            ObservationFields(history, assetId, quantity, form));

        Assert.Equal(HttpStatusCode.Redirect, record.StatusCode);
    }

    /// <summary>
    /// Runs the comparison from the evidence page, then submits the same
    /// page again, which must return the same run.
    /// </summary>
    internal static async Task<(string Run, string Replay)> RunTwiceAsync(
        HttpClient client,
        string evidence)
    {
        var (_, html) = await M010UiTestData.GetPageAsync(client, evidence);
        var fields = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", M010UiTestData.TokenFrom(html)),
            new("runKey", M010UiTestData.HiddenValue(RunKeyPattern(), html, "run key"))
        };

        using var first = await M010UiTestData.PostFormAsync(client, evidence + "?handler=Run", fields);
        using var replay = await M010UiTestData.PostFormAsync(client, evidence + "?handler=Run", fields);

        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Matches("^/reconcile/runs/[0-9a-f-]{36}$", first.Headers.Location!.OriginalString);
        return (first.Headers.Location.OriginalString, replay.Headers.Location!.OriginalString);
    }

    private static async Task<HttpResponseMessage> PostRunFormAsync(
        HttpClient client,
        string run,
        string handler,
        string html,
        params KeyValuePair<string, string>[] fields)
        => await M010UiTestData.PostFormAsync(
            client,
            $"{run}?handler={handler}",
            fields.Prepend(new("__RequestVerificationToken", M010UiTestData.TokenFrom(html))));

    /// <summary>
    /// Each rendered item, in page order, with its own slice of the page.
    /// </summary>
    private static IReadOnlyList<(Guid Id, string Html)> Items(string html)
    {
        var matches = ItemPattern().Matches(html);

        return matches
            .Select((match, index) =>
            {
                var end = index + 1 < matches.Count ? matches[index + 1].Index : html.Length;
                return (Guid.ParseExact(match.Groups[1].Value, "N"), html[match.Index..end]);
            })
            .ToArray();
    }

    [GeneratedRegex("aria-labelledby=\"item-([0-9a-f]{32})\"", RegexOptions.CultureInvariant)]
    private static partial Regex ItemPattern();

    [GeneratedRegex("name=\"runKey\" value=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex RunKeyPattern();

    [GeneratedRegex("name=\"rerunKey\" value=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex RerunKeyPattern();
}
