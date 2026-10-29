using System.Net;
using System.Text.RegularExpressions;

namespace WealthLedger.Api.Tests;

/// <summary>
/// Drives the M010 Ledger search through the server-rendered Ready UI.
/// </summary>
public sealed partial class LedgerSearchUiTests
{
    [Fact]
    public async Task Ledger_ListsPostedHistoryByFinancialDateAndOpensExplanations()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = M010UiTestData.CreateClient(factory);
        var history = await M010UiTestData.SeedAsync(factory, client);

        var (status, html) = await M010UiTestData.GetPageAsync(client, "/ledger");
        var ids = M010UiTestData.LinkedTransactionIds(html);

        Assert.Equal(HttpStatusCode.OK, status);
        M010UiTestData.AssertNoInternals(html);
        Assert.Contains("Yalnızca kalıcı olarak kaydedilmiş etkin geçmiş aranır", html);
        Assert.Contains("role=\"search\"", html);
        Assert.Equal(7, ids.Count);
        Assert.True(
            ids.ToList().IndexOf(history.Gold.TransferId)
            < ids.ToList().IndexOf(history.FundPurchaseId));
        Assert.True(
            ids.ToList().IndexOf(history.FundPurchaseId)
            < ids.ToList().IndexOf(history.FirstJanuaryId));
        Assert.Contains("Ters kayıt · asıl işlemi aç", html);
        Assert.Contains("Ters çevrildi · ters kaydı aç", html);
        Assert.DoesNotContain(">Düzenle<", html);
        Assert.DoesNotContain(">Sil<", html);

        var (detailStatus, detailHtml) = await M010UiTestData.GetPageAsync(
            client,
            $"/ledger/{history.FundPurchaseId:D}");

        Assert.Equal(HttpStatusCode.OK, detailStatus);
        Assert.Contains("SYNTH-FUND-001", detailHtml);
    }

    [Fact]
    public async Task Ledger_EachFilterAndCombinationSelectsEachTransactionOnce()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = M010UiTestData.CreateClient(factory);
        var history = await M010UiTestData.SeedAsync(factory, client);
        var setup = history.Setup;

        await AssertResultsAsync(
            client,
            "/ledger?executedFrom=2026-01-01&executedTo=2026-01-31",
            history.SecondJanuaryId,
            history.FirstJanuaryId);
        await AssertResultsAsync(
            client,
            $"/ledger?accountId={history.DirectAccountId:D}&reversal=REVERSED_ORIGINAL",
            history.DirectFebruaryId);
        await AssertResultsAsync(
            client,
            "/ledger?reversal=REVERSAL_ONLY",
            history.ReversalId);
        await AssertResultsAsync(
            client,
            $"/ledger?assetId={setup.FundAssetId:D}",
            history.FundPurchaseId);
        await AssertResultsAsync(
            client,
            "/ledger?reference=synth-jan",
            history.SecondJanuaryId,
            history.FirstJanuaryId);
        await AssertResultsAsync(
            client,
            $"/ledger?portfolioId={setup.PortfolioId:D}&type=TRANSFER&assetId={history.Gold.GoldAssetId:D}",
            history.Gold.TransferId);

        // A Fund purchase and a gold purchase each have two entries in the
        // cash account; entry-level filters must still list them once.
        var (_, cashHtml) = await M010UiTestData.GetPageAsync(
            client,
            $"/ledger?accountId={setup.AccountId:D}&assetId={setup.CashAssetId:D}");
        var cashIds = M010UiTestData.LinkedTransactionIds(cashHtml);
        var cashLinks = TransactionHeadingPattern().Matches(cashHtml).Count;

        Assert.Equal(cashIds.Count, cashLinks);
        Assert.Contains(history.FundPurchaseId, cashIds);
        Assert.Contains(history.Gold.PurchaseId, cashIds);
        Assert.DoesNotContain(history.DirectFebruaryId, cashIds);

        var (_, institutionHtml) = await M010UiTestData.GetPageAsync(
            client,
            $"/ledger?institutionId={setup.InstitutionId:D}");
        var institutionIds = M010UiTestData.LinkedTransactionIds(institutionHtml);

        Assert.Contains(history.FirstJanuaryId, institutionIds);
        Assert.DoesNotContain(history.DirectFebruaryId, institutionIds);
        Assert.DoesNotContain(history.ReversalId, institutionIds);

        var (_, typeHtml) = await M010UiTestData.GetPageAsync(
            client,
            "/ledger?type=CONTRIBUTION&executedTo=2026-01-31&type=CONTRIBUTION");

        Assert.Equal(
            [history.SecondJanuaryId, history.FirstJanuaryId],
            M010UiTestData.LinkedTransactionIds(typeHtml));
        Assert.Matches(
            "value=\"CONTRIBUTION\"\\s+checked=\"checked\"",
            typeHtml);
        Assert.Contains("<details class=\"flow\" open=\"open\">", typeHtml);
    }

    [Fact]
    public async Task Ledger_PaginationKeepsTheSearchAndRefusesAnAlteredCursor()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = M010UiTestData.CreateClient(factory);
        var history = await M010UiTestData.SeedAsync(factory, client);
        var path = "/ledger?pageSize=1&executedFrom=2026-01-01&executedTo=2026-02-28";
        var seen = new List<Guid>();
        string? firstNext = null;

        for (var page = 0; page < 10; page++)
        {
            var (status, html) = await M010UiTestData.GetPageAsync(client, path);
            Assert.Equal(HttpStatusCode.OK, status);
            seen.AddRange(M010UiTestData.LinkedTransactionIds(html));

            var next = NextLinkPattern().Match(html);

            if (!next.Success)
            {
                break;
            }

            path = next.Groups[1].Value;
            firstNext ??= path;
            Assert.Contains("executedFrom=2026-01-01", path);
            Assert.Contains("executedTo=2026-02-28", path);
        }

        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Equal(
            new[]
            {
                history.FundPurchaseId,
                history.DirectFebruaryId,
                history.SecondJanuaryId,
                history.FirstJanuaryId
            }.Order(),
            seen.Where(id => id != history.ReversalId).Order());

        Assert.NotNull(firstNext);
        var altered = firstNext!.Replace(
            "executedTo=2026-02-28",
            "executedTo=2026-01-31",
            StringComparison.Ordinal);
        var cursor = CursorValuePattern().Match(firstNext).Groups[1].Value;
        var (alteredStatus, alteredHtml) = await M010UiTestData.GetPageAsync(
            client,
            altered);

        Assert.Equal(HttpStatusCode.BadRequest, alteredStatus);
        Assert.Contains("Bu defter sayfası açılamadı", alteredHtml);
        Assert.DoesNotContain(Uri.UnescapeDataString(cursor), alteredHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            factory.Logs.Messages,
            message => message.Contains(Uri.UnescapeDataString(cursor), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ledger_InvalidFiltersAreFieldMessagesAndUnknownScopesAreNotEmptyResults()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = M010UiTestData.CreateClient(factory);
        await M010UiTestData.SeedAsync(factory, client);
        const string privateReference = "PRIVATE-LEDGER-SEARCH-TEXT";

        foreach (var (path, message, field) in new[]
                 {
                     ("/ledger?executedFrom=2026-13-40", "Tarih YYYY-AA-GG", "#executedFrom"),
                     ("/ledger?executedFrom=2026-02-01&executedTo=2026-01-01", "Bitiş tarihi başlangıç", "#executedTo"),
                     ("/ledger?type=NOT_A_TYPE", "Seçilen işlem türü desteklenmiyor", "#type-legend"),
                     ("/ledger?assetId=not-a-guid", "Seçim geçersiz", "#assetId"),
                     ("/ledger?reversal=SIDEWAYS", "Ters kayıt ilişkisi seçimi geçersiz", "#reversal-legend"),
                     ($"/ledger?reference={privateReference}{new string('x', 257)}", "en fazla 256 karakter", "#reference")
                 })
        {
            var (status, html) = await M010UiTestData.GetPageAsync(client, path);

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Contains(message, html);
            Assert.Contains($"href=\"{field}\"", html);
            Assert.DoesNotContain("Kaydedilmiş işlem bulunamadı", html);
            M010UiTestData.AssertNoInternals(html);
        }

        var (unknownStatus, unknownHtml) = await M010UiTestData.GetPageAsync(
            client,
            $"/ledger?assetId={Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.NotFound, unknownStatus);
        Assert.Contains("Seçilen kapsam bulunamadı", unknownHtml);
        Assert.DoesNotContain("Kaydedilmiş işlem bulunamadı", unknownHtml);

        var (pageSizeStatus, pageSizeHtml) = await M010UiTestData.GetPageAsync(
            client,
            "/ledger?pageSize=0");

        Assert.Equal(HttpStatusCode.BadRequest, pageSizeStatus);
        Assert.Contains("İlk sayfaya dön", pageSizeHtml);

        var (emptyStatus, emptyHtml) = await M010UiTestData.GetPageAsync(
            client,
            "/ledger?reference=NO-SUCH-SYNTHETIC-REFERENCE");

        Assert.Equal(HttpStatusCode.OK, emptyStatus);
        Assert.Contains("Bu filtrelerle eşleşen kaydedilmiş işlem yok", emptyHtml);

        var logs = string.Join(Environment.NewLine, factory.Logs.Messages);
        Assert.DoesNotContain(privateReference, logs);
        Assert.DoesNotContain("SYNTH-JAN-001", logs);
        Assert.DoesNotContain("Synthetic Direct Account", logs);
    }

    private static async Task AssertResultsAsync(
        HttpClient client,
        string path,
        params Guid[] expected)
    {
        var (status, html) = await M010UiTestData.GetPageAsync(client, path);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(expected, M010UiTestData.LinkedTransactionIds(html));
    }

    [GeneratedRegex(
        "<h2>\\s*<a href=\"/ledger/[0-9a-f-]{36}\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex TransactionHeadingPattern();

    [GeneratedRegex(
        "href=\"(/ledger\\?pageSize=[^\"]*&cursor=[^\"]+)\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex NextLinkPattern();

    [GeneratedRegex("cursor=([^&\"]+)", RegexOptions.CultureInvariant)]
    private static partial Regex CursorValuePattern();
}
