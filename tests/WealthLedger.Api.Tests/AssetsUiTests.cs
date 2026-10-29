using System.Net;
using System.Text.RegularExpressions;

namespace WealthLedger.Api.Tests;

/// <summary>
/// Drives the M010 Assets position and lot inventory through the Ready UI.
/// </summary>
public sealed partial class AssetsUiTests
{
    [Fact]
    public async Task Assets_ShowDerivedPositionsByScopeWithTraceLinksAndNoValuation()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = M010UiTestData.CreateClient(factory);
        var history = await M010UiTestData.SeedAsync(factory, client);
        var setup = history.Setup;

        var (status, html) = await M010UiTestData.GetPageAsync(client, "/assets");

        Assert.Equal(HttpStatusCode.OK, status);
        M010UiTestData.AssertNoInternals(html);
        var positions = M010UiTestData.After(html, "positions-heading");

        Assert.Contains("Güncel pozisyonlar", positions);
        Assert.Contains("1,25 fon birimi", positions);
        Assert.Contains("Synthetic Vault A", positions);
        Assert.Contains("15 gram", positions);
        Assert.Contains("Synthetic Vault B", positions);
        Assert.Contains("5 gram", positions);

        // The reversed direct contribution nets to an exact zero, which is
        // hidden by default and never fabricated for an unused scope.
        Assert.DoesNotContain(
            "Synthetic Direct Account",
            M010UiTestData.After(html, "positions-heading"));
        Assert.DoesNotContain("<dt>Piyasa", html);
        Assert.DoesNotContain("<dt>Getiri", html);
        Assert.Contains(
            $"href=\"/ledger?portfolioId={setup.PortfolioId:D}&accountId={setup.AccountId:D}&assetId={setup.FundAssetId:D}\"",
            html);
        Assert.Contains(
            $"href=\"/assets/lots?portfolioId={setup.PortfolioId:D}&accountId={setup.AccountId:D}&assetId={setup.FundAssetId:D}\"",
            html);
        Assert.DoesNotContain(
            $"href=\"/assets/lots?portfolioId={setup.PortfolioId:D}&accountId={setup.AccountId:D}&assetId={setup.CashAssetId:D}\"",
            html);

        var (traceStatus, traceHtml) = await M010UiTestData.GetPageAsync(
            client,
            $"/ledger?portfolioId={setup.PortfolioId:D}&accountId={setup.AccountId:D}&assetId={setup.FundAssetId:D}");

        Assert.Equal(HttpStatusCode.OK, traceStatus);
        Assert.Equal(
            [history.FundPurchaseId],
            M010UiTestData.LinkedTransactionIds(traceHtml));

        var (zeroStatus, zeroHtml) = await M010UiTestData.GetPageAsync(
            client,
            "/assets?includeZero=true");

        Assert.Equal(HttpStatusCode.OK, zeroStatus);
        Assert.Contains("Synthetic Direct Account", zeroHtml);
        Assert.Contains("Geçerli kapsam: etkin geçmişin toplamı tam olarak sıfır.", zeroHtml);

        using var post = await client.PostAsync(
            "/assets",
            new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
    }

    [Fact]
    public async Task Assets_AsOfExcludesLaterHistoryAndBoundsTheTraceLinks()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = M010UiTestData.CreateClient(factory);
        var history = await M010UiTestData.SeedAsync(factory, client);
        var setup = history.Setup;

        var (status, html) = await M010UiTestData.GetPageAsync(
            client,
            "/assets?asOf=2026-01-31");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Şu tarih itibarıyla pozisyonlar: 31.01.2026", html);
        Assert.DoesNotContain("fon birimi", M010UiTestData.After(html, "positions-heading"));
        Assert.DoesNotContain("Synthetic Vault A", M010UiTestData.After(html, "positions-heading"));

        var cashTrace =
            $"/ledger?portfolioId={setup.PortfolioId:D}&accountId={setup.AccountId:D}&assetId={setup.CashAssetId:D}&executedTo=2026-01-31";
        Assert.Contains($"href=\"{cashTrace}\"", html);

        var (_, traceHtml) = await M010UiTestData.GetPageAsync(client, cashTrace);

        Assert.Equal(
            [history.SecondJanuaryId, history.FirstJanuaryId],
            M010UiTestData.LinkedTransactionIds(traceHtml));

        // A reversal keeps the original's effective date, so from 5 February
        // the reversed direct contribution nets to an exact zero: hidden by
        // default, shown as a valid zero only when asked.
        var (februaryStatus, februaryHtml) = await M010UiTestData.GetPageAsync(
            client,
            "/assets?asOf=2026-02-10");
        var (zeroStatus, zeroHtml) = await M010UiTestData.GetPageAsync(
            client,
            "/assets?asOf=2026-02-10&includeZero=true");

        Assert.Equal(HttpStatusCode.OK, februaryStatus);
        Assert.DoesNotContain(
            "Synthetic Direct Account",
            M010UiTestData.After(februaryHtml, "positions-heading"));
        Assert.Equal(HttpStatusCode.OK, zeroStatus);
        Assert.Contains(
            "Synthetic Direct Account",
            M010UiTestData.After(zeroHtml, "positions-heading"));
        Assert.Contains("Geçerli kapsam: etkin geçmişin toplamı tam olarak sıfır.", zeroHtml);
    }

    [Fact]
    public async Task Assets_UnknownScopeIsNotZeroAndMalformedFiltersAreFieldMessages()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = M010UiTestData.CreateClient(factory);
        var history = await M010UiTestData.SeedAsync(factory, client);

        foreach (var path in new[]
                 {
                     $"/assets?assetId={Guid.NewGuid():D}",
                     $"/assets?accountId={history.DirectAccountId:D}&institutionId={history.Setup.InstitutionId:D}",
                     $"/assets/lots?portfolioId={Guid.NewGuid():D}"
                 })
        {
            var (status, html) = await M010UiTestData.GetPageAsync(client, path);

            Assert.Equal(HttpStatusCode.NotFound, status);
            Assert.Contains("Seçilen kapsam bulunamadı", html);
            Assert.DoesNotContain("Gösterilecek pozisyon yok", html);
            Assert.DoesNotContain("Gösterilecek lot yok", html);
        }

        foreach (var (path, message, field) in new[]
                 {
                     ("/assets?asOf=31.01.2026", "Tarih YYYY-AA-GG", "#asOf"),
                     ("/assets?includeZero=yes", "Seçim geçersiz.", "#includeZero"),
                     ("/assets/lots?accountId=not-a-guid", "Seçim geçersiz.", "#accountId"),
                     ("/assets/lots?includeExhausted=1", "Seçim geçersiz.", "#includeExhausted")
                 })
        {
            var (status, html) = await M010UiTestData.GetPageAsync(client, path);

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Contains(message, html);
            Assert.Contains($"href=\"{field}\"", html);
            M010UiTestData.AssertNoInternals(html);
        }

        var (pageSizeStatus, pageSizeHtml) = await M010UiTestData.GetPageAsync(
            client,
            "/assets/lots?pageSize=101");

        Assert.Equal(HttpStatusCode.BadRequest, pageSizeStatus);
        Assert.Contains("Bu envanter sayfası açılamadı", pageSizeHtml);

        var logs = string.Join(Environment.NewLine, factory.Logs.Messages);
        Assert.DoesNotContain("Synthetic Vault A", logs);
        Assert.DoesNotContain("SYNTH-CERT-001", logs);
    }

    [Fact]
    public async Task Lots_ShowGoldCustodyPiecesFineWeightCostAndTraceToTransactions()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = M010UiTestData.CreateClient(factory);
        var history = await M010UiTestData.SeedAsync(factory, client);

        var (status, html) = await M010UiTestData.GetPageAsync(
            client,
            $"/assets/lots?assetId={history.Gold.GoldAssetId:D}");

        Assert.Equal(HttpStatusCode.OK, status);
        M010UiTestData.AssertNoInternals(html);
        Assert.Contains("Synthetic Physical Gold", html);
        Assert.Contains("916 ‰ (916000 ppm)", html);
        Assert.Contains("20 gram", html);
        Assert.Contains("18,32 gram saf altın", html);
        Assert.Contains("15 gram", html);
        Assert.Contains("13,74 gram saf altın", html);
        Assert.Contains("5 gram", html);
        Assert.Contains("4,58 gram saf altın", html);
        Assert.Contains("2 adet", html);
        Assert.Contains("1 adet", html);
        Assert.Contains("Synthetic Vault A", M010UiTestData.After(html, "lot-results-heading"));
        Assert.Contains("Synthetic Vault B", M010UiTestData.After(html, "lot-results-heading"));
        Assert.Contains("SYNTH-CERT-001", html);
        Assert.Contains($"href=\"/ledger/{history.Gold.PurchaseId:D}\"", html);
        Assert.Contains($"href=\"/ledger/{history.Gold.TransferId:D}\"", html);
        Assert.DoesNotContain("Tükenmiş</span>", html);

        var (fundStatus, fundHtml) = await M010UiTestData.GetPageAsync(
            client,
            $"/assets/lots?assetId={history.Setup.FundAssetId:D}");

        Assert.Equal(HttpStatusCode.OK, fundStatus);
        Assert.Contains("1,25 fon birimi", fundHtml);
        Assert.Contains("250,00 TRY", fundHtml);
        Assert.Contains("Biliniyor", fundHtml);
        Assert.Contains($"href=\"/ledger/{history.FundPurchaseId:D}\"", fundHtml);
        Assert.DoesNotContain("‰", fundHtml);
    }

    [Fact]
    public async Task Lots_PaginateWithinTheSameScopeAndRefuseAnAlteredCursor()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = M010UiTestData.CreateClient(factory);
        var history = await M010UiTestData.SeedAsync(factory, client);

        var (firstStatus, firstHtml) = await M010UiTestData.GetPageAsync(
            client,
            "/assets/lots?pageSize=1");
        var next = NextLinkPattern().Match(firstHtml);

        Assert.Equal(HttpStatusCode.OK, firstStatus);
        Assert.True(next.Success, "The first lot page did not expose its server cursor.");

        var (secondStatus, secondHtml) = await M010UiTestData.GetPageAsync(
            client,
            next.Groups[1].Value);
        var lotIds = new[] { history.FundLotId, history.Gold.LotId };

        Assert.Equal(HttpStatusCode.OK, secondStatus);
        Assert.Single(lotIds, id => firstHtml.Contains(id.ToString("D"), StringComparison.Ordinal));
        Assert.Single(lotIds, id => secondHtml.Contains(id.ToString("D"), StringComparison.Ordinal));
        Assert.DoesNotMatch(NextLinkPattern(), secondHtml);

        var (alteredStatus, alteredHtml) = await M010UiTestData.GetPageAsync(
            client,
            next.Groups[1].Value + "&includeExhausted=true");

        Assert.Equal(HttpStatusCode.BadRequest, alteredStatus);
        Assert.Contains("Bu envanter sayfası açılamadı", alteredHtml);

        using var post = await client.PostAsync(
            "/assets/lots",
            new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
    }

    [GeneratedRegex(
        "href=\"(/assets/lots\\?pageSize=1[^\"]*&cursor=[^\"]+)\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex NextLinkPattern();
}
