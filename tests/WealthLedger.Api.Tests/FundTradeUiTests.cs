using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Ledger;

namespace WealthLedger.Api.Tests;

/// <summary>
/// Drives the M008 workflows through the real UI host.
/// </summary>
public sealed partial class FundTradeUiTests
{
    [Fact]
    public async Task Purchase_ReviewWritesNothingThenPostRedirectsToReceipt()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = CreateClient(factory);

        var page = await GetFormAsync(client, "/record/fund-purchase");
        var form = PurchaseForm(factory, page);

        using var review = await client.PostAsync(
            "/record/fund-purchase?handler=Review",
            new FormUrlEncodedContent(form));

        var reviewHtml = WebUtility.HtmlDecode(
            await review.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Contains("Ekonomik etki", reviewHtml);

        // Preview must leave the database exactly as it found it.
        await using (var previewContext = factory.CreateDbContext())
        {
            Assert.Equal(
                0,
                await previewContext.LedgerTransactions
                    .CountAsync(x => x.Type == TransactionType.Buy));

            Assert.Equal(
                0,
                await previewContext.CommandReceipts.CountAsync(x =>
                    x.OperationCode
                        == LedgerOperationCodes.RecordFundPurchase));
        }

        form["__RequestVerificationToken"] = TokenFrom(reviewHtml);

        using var post = await client.PostAsync(
            "/record/fund-purchase?handler=Post",
            new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);

        var receiptPath =
            Assert.IsType<Uri>(post.Headers.Location).OriginalString;

        Assert.Matches(
            @"^/record/fund-trade/[0-9a-f-]{36}/receipt$",
            receiptPath);

        using var receipt = await client.GetAsync(receiptPath);
        var receiptHtml = WebUtility.HtmlDecode(
            await receipt.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        Assert.Contains("Kaydedildi", receiptHtml);
        Assert.Contains("Açılan lot", receiptHtml);

        // Refreshing a receipt must never create a second transaction.
        using var refreshed = await client.GetAsync(receiptPath);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);

        await using var context = factory.CreateDbContext();

        Assert.Equal(
            1,
            await context.LedgerTransactions
                .CountAsync(x => x.Type == TransactionType.Buy));
    }

    [Fact]
    public async Task Purchase_WithoutProvenance_IsRefusedAndWritesNothing()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = CreateClient(factory);

        var page = await GetFormAsync(client, "/record/fund-purchase");
        var form = PurchaseForm(factory, page);

        form["Input.ExternalReference"] = string.Empty;
        form["Input.Note"] = string.Empty;

        using var review = await client.PostAsync(
            "/record/fund-purchase?handler=Review",
            new FormUrlEncodedContent(form));

        var html = WebUtility.HtmlDecode(
            await review.Content.ReadAsStringAsync());

        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            review.StatusCode);

        Assert.Contains("referans numarası veya açıklama", html);

        await using var context = factory.CreateDbContext();

        Assert.Equal(
            0,
            await context.LedgerTransactions
                .CountAsync(x => x.Type == TransactionType.Buy));
    }

    [Fact]
    public async Task Purchase_UnexplainedDiscrepancy_RequiresANote()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = CreateClient(factory);

        var page = await GetFormAsync(client, "/record/fund-purchase");
        var form = PurchaseForm(factory, page);

        // Ten units at ten lira is a hundred; entering five hundred is a
        // difference no rounding can explain.
        form["Input.CashConsideration"] = "500";
        form["Input.Note"] = string.Empty;
        form["Input.ExternalReference"] = "REF-DISCREPANCY";

        using var review = await client.PostAsync(
            "/record/fund-purchase?handler=Review",
            new FormUrlEncodedContent(form));

        var html = WebUtility.HtmlDecode(
            await review.Content.ReadAsStringAsync());

        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            review.StatusCode);

        Assert.Contains("farkın nedenini", html);

        // The same figures post once the difference is explained.
        form["__RequestVerificationToken"] = TokenFrom(html);
        form["Input.Note"] = "Broker ekstresinde düzeltme uygulandı.";

        using var explained = await client.PostAsync(
            "/record/fund-purchase?handler=Review",
            new FormUrlEncodedContent(form));

        var explainedHtml = WebUtility.HtmlDecode(
            await explained.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, explained.StatusCode);

        // The entered fact is preserved, not normalized towards the price.
        Assert.Contains("500,00 TRY", explainedHtml);
        Assert.Contains("100,00 TRY", explainedHtml);
    }

    [Fact]
    public async Task Sale_ReviewShowsFifoPlanAndPostConsumesIt()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = CreateClient(factory);

        await PostPurchaseAsync(factory, client, quantity: "10", note: "İlk alım.");

        var page = await GetFormAsync(client, "/record/fund-sale");
        var form = SaleForm(factory, page, quantity: "4");

        using var review = await client.PostAsync(
            "/record/fund-sale?handler=Review",
            new FormUrlEncodedContent(form));

        var reviewHtml = WebUtility.HtmlDecode(
            await review.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Contains("Kullanılacak lotlar", reviewHtml);
        Assert.Contains("Gerçekleşen alım maliyeti", reviewHtml);
        Assert.Contains("ADR009_CUMULATIVE_ROUND_HALF_TO_EVEN_V1", reviewHtml);

        await using (var previewContext = factory.CreateDbContext())
        {
            Assert.Equal(
                0,
                await previewContext.LedgerTransactions
                    .CountAsync(x => x.Type == TransactionType.Sell));
        }

        // The reviewed plan is carried forward exactly as displayed.
        var posting = CarryReviewedPlan(form, reviewHtml);

        using var post = await client.PostAsync(
            "/record/fund-sale?handler=Post",
            new FormUrlEncodedContent(posting));

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);

        var receiptPath =
            Assert.IsType<Uri>(post.Headers.Location).OriginalString;

        using var receipt = await client.GetAsync(receiptPath);
        var receiptHtml = WebUtility.HtmlDecode(
            await receipt.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        Assert.Contains("Kullanılan lotlar", receiptHtml);
        Assert.Contains("COMPLETE_KNOWN", receiptHtml);

        await using var context = factory.CreateDbContext();

        Assert.Equal(
            1,
            await context.LedgerTransactions
                .CountAsync(x => x.Type == TransactionType.Sell));

        // Six of the ten units remain.
        Assert.Equal(
            6_00000000L,
            await context.LotEntryAllocations.SumAsync(
                x => x.QuantityDeltaE8));
    }

    [Fact]
    public async Task Sale_ExceedingHeldQuantity_IsRefused()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = CreateClient(factory);

        await PostPurchaseAsync(factory, client, quantity: "3", note: "Küçük alım.");

        var page = await GetFormAsync(client, "/record/fund-sale");
        var form = SaleForm(factory, page, quantity: "9");

        using var review = await client.PostAsync(
            "/record/fund-sale?handler=Review",
            new FormUrlEncodedContent(form));

        var html = WebUtility.HtmlDecode(
            await review.Content.ReadAsStringAsync());

        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            review.StatusCode);

        Assert.Contains("yeterli adet bulunmuyor", html);
    }

    [Fact]
    public async Task Contribution_ReviewThenPostRedirectsToTransactionDetail()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = CreateClient(factory);

        var page = await GetFormAsync(client, "/record/contribution");

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = page.Token,
            ["Input.IdempotencyKey"] = page.CommandKey,
            ["Input.PortfolioId"] =
                factory.ReadySetup.PortfolioId.ToString("D"),
            ["Input.AccountId"] =
                factory.ReadySetup.AccountId.ToString("D"),
            ["Input.CashAssetId"] =
                factory.ReadySetup.CashAssetId.ToString("D"),
            ["Input.CategoryCode"] = "SALARY",
            ["Input.Amount"] = "1500",
            ["Input.ExecutionDate"] = "2026-09-08",
            ["Input.Note"] = "Aylık maaş girişi."
        };

        using var review = await client.PostAsync(
            "/record/contribution?handler=Review",
            new FormUrlEncodedContent(form));

        var reviewHtml = WebUtility.HtmlDecode(
            await review.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Contains("1.500,00 TRY", reviewHtml);
        Assert.Contains("artış", reviewHtml);

        form["__RequestVerificationToken"] = TokenFrom(reviewHtml);

        using var post = await client.PostAsync(
            "/record/contribution?handler=Post",
            new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);

        Assert.Matches(
            @"^/ledger/[0-9a-f-]{36}$",
            Assert.IsType<Uri>(post.Headers.Location).OriginalString);

        await using var context = factory.CreateDbContext();

        Assert.Equal(
            1,
            await context.LedgerTransactions
                .CountAsync(x => x.Type == TransactionType.Contribution));
    }

    [Fact]
    public async Task FundPages_AreReachableOnlyInReadyMode()
    {
        using var factory = new WealthLedgerApiFactory(
            ApiTestStartupMode.WorkspaceUninitialized);

        using var client = CreateClient(factory);

        foreach (var path in new[]
                 {
                     "/record/contribution",
                     "/record/fund-purchase",
                     "/record/fund-sale"
                 })
        {
            using var response = await client.GetAsync(path);

            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task FundPages_RequireAntiforgeryTokens()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = CreateClient(factory);

        foreach (var path in new[]
                 {
                     "/record/contribution?handler=Review",
                     "/record/fund-purchase?handler=Review",
                     "/record/fund-sale?handler=Review"
                 })
        {
            using var response = await client.PostAsync(
                path,
                new FormUrlEncodedContent([]));

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);
        }
    }

    private static async Task PostPurchaseAsync(
        WealthLedgerApiFactory factory,
        HttpClient client,
        string quantity,
        string note)
    {
        var page = await GetFormAsync(client, "/record/fund-purchase");
        var form = PurchaseForm(factory, page);

        form["Input.Quantity"] = quantity;
        form["Input.CashConsideration"] =
            (decimal.Parse(
                quantity,
                System.Globalization.CultureInfo.InvariantCulture) * 10m)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        form["Input.Note"] = note;

        using var review = await client.PostAsync(
            "/record/fund-purchase?handler=Review",
            new FormUrlEncodedContent(form));

        var html = WebUtility.HtmlDecode(
            await review.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, review.StatusCode);

        form["__RequestVerificationToken"] = TokenFrom(html);

        using var post = await client.PostAsync(
            "/record/fund-purchase?handler=Post",
            new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
    }

    private static Dictionary<string, string> PurchaseForm(
        WealthLedgerApiFactory factory,
        FormPage page)
    {
        var setup = factory.ReadySetup;

        return new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = page.Token,
            ["Input.IdempotencyKey"] = page.CommandKey,
            ["Input.PortfolioId"] = setup.PortfolioId.ToString("D"),
            ["Input.FundAccountId"] = setup.AccountId.ToString("D"),
            ["Input.CashAccountId"] = setup.AccountId.ToString("D"),
            ["Input.FundAssetId"] = setup.FundAssetId.ToString("D"),
            ["Input.CashAssetId"] = setup.CashAssetId.ToString("D"),
            ["Input.Quantity"] = "10",
            ["Input.UnitPrice"] = "10",
            ["Input.CashConsideration"] = "100",
            ["Input.ExecutionDate"] = "2026-09-08",
            ["Input.ExternalReference"] = "UI-PURCHASE",
            ["Input.Note"] = "Sentetik alım."
        };
    }

    private static Dictionary<string, string> SaleForm(
        WealthLedgerApiFactory factory,
        FormPage page,
        string quantity)
    {
        var setup = factory.ReadySetup;

        return new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = page.Token,
            ["Input.IdempotencyKey"] = page.CommandKey,
            ["Input.PortfolioId"] = setup.PortfolioId.ToString("D"),
            ["Input.FundAccountId"] = setup.AccountId.ToString("D"),
            ["Input.CashAccountId"] = setup.AccountId.ToString("D"),
            ["Input.FundAssetId"] = setup.FundAssetId.ToString("D"),
            ["Input.CashAssetId"] = setup.CashAssetId.ToString("D"),
            ["Input.Quantity"] = quantity,
            ["Input.UnitPrice"] = "12",
            ["Input.CashConsideration"] =
                (decimal.Parse(
                    quantity,
                    System.Globalization.CultureInfo.InvariantCulture) * 12m)
                .ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Input.ExecutionDate"] = "2026-09-09",
            ["Input.ExternalReference"] = "UI-SALE",
            ["Input.Note"] = "Sentetik satış."
        };
    }

    /// <summary>
    /// Copies the reviewed plan out of the rendered page into the next post.
    /// </summary>
    /// <remarks>
    /// This is what a browser does with the hidden fields, so posting through
    /// them proves the plan really does travel with the submission.
    /// </remarks>
    private static Dictionary<string, string> CarryReviewedPlan(
        Dictionary<string, string> form,
        string reviewHtml)
    {
        var posting = new Dictionary<string, string>(form)
        {
            ["__RequestVerificationToken"] = TokenFrom(reviewHtml),
            ["Input.ReviewedPlanFingerprint"] =
                HiddenValue(
                    PlanFingerprintPattern(),
                    reviewHtml,
                    "reviewed plan fingerprint")
        };

        var lotIds = PlanLotPattern().Matches(reviewHtml);
        var quantities = PlanQuantityPattern().Matches(reviewHtml);

        Assert.NotEmpty(lotIds);
        Assert.Equal(lotIds.Count, quantities.Count);

        for (var index = 0; index < lotIds.Count; index++)
        {
            posting[$"Input.ReviewedPlan[{index}].AssetLotId"] =
                lotIds[index].Groups[1].Value;

            posting[$"Input.ReviewedPlan[{index}].QuantityRawE8"] =
                quantities[index].Groups[1].Value;
        }

        return posting;
    }

    private static async Task<FormPage> GetFormAsync(
        HttpClient client,
        string path)
    {
        using var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return new FormPage(
            html,
            TokenFrom(html),
            HiddenValue(CommandKeyPattern(), html, "command key"));
    }

    private static string TokenFrom(string html)
        => WebUtility.HtmlDecode(
            HiddenValue(
                AntiforgeryTokenPattern(),
                html,
                "antiforgery token"));

    private static string HiddenValue(
        Regex pattern,
        string html,
        string description)
    {
        var match = pattern.Match(html);

        Assert.True(
            match.Success,
            $"The form did not render its {description}.");

        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static HttpClient CreateClient(WealthLedgerApiFactory factory)
        => factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing
                .WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

    private sealed record FormPage(
        string Html,
        string Token,
        string CommandKey);

    [GeneratedRegex(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex AntiforgeryTokenPattern();

    [GeneratedRegex(
        "name=\"Input.IdempotencyKey\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex CommandKeyPattern();

    [GeneratedRegex(
        "name=\"Input.ReviewedPlanFingerprint\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex PlanFingerprintPattern();

    [GeneratedRegex(
        "name=\"Input.ReviewedPlan\\[\\d+\\].AssetLotId\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex PlanLotPattern();

    [GeneratedRegex(
        "name=\"Input.ReviewedPlan\\[\\d+\\].QuantityRawE8\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex PlanQuantityPattern();
}
