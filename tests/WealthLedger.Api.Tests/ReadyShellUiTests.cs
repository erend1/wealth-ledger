using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using WealthLedger.Api.Contracts;

namespace WealthLedger.Api.Tests;

public sealed partial class ReadyShellUiTests
{
    [Fact]
    public async Task ReadyShell_ExposesOnlyImplementedReadOnlyDestinations()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = CreateClient(factory);

        foreach (var path in new[]
                 {
                     "/",
                     "/ledger",
                     "/settings",
                     "/settings/master-data",
                     "/settings/data-safety"
                 })
        {
            using var get = await client.GetAsync(path);
            using var post = await client.PostAsync(
                path,
                new FormUrlEncodedContent([]));
            var html = await get.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
            Assert.DoesNotContain("Data Source=", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SELECT ", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SqliteException", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(" at WealthLedger", html, StringComparison.OrdinalIgnoreCase);
        }

        using var shellResponse = await client.GetAsync("/");
        var shell = await shellResponse.Content.ReadAsStringAsync();
        var cookies = shellResponse.Headers.TryGetValues(
            "Set-Cookie",
            out var values)
            ? values.ToArray()
            : [];

        Assert.Contains("href=\"/\"", shell, StringComparison.Ordinal);
        Assert.Contains("href=\"/ledger\"", shell, StringComparison.Ordinal);
        Assert.Contains("href=\"/settings\"", shell, StringComparison.Ordinal);
        Assert.DoesNotContain(">Record<", shell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">Assets<", shell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">Plan<", shell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<form", shell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localStorage", shell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", shell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://", shell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", shell, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(cookies);

        using var style = await client.GetAsync(
            "/_content/WealthLedger.UI/css/shell.css");
        var styleText = await style.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, style.StatusCode);
        Assert.Equal("text/css", style.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "nosniff",
            style.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.DoesNotContain("url(http", styleText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Today_UsesAnHonestEmptyStateWithoutInventingTotals()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/");
        var html = WebUtility.HtmlDecode(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Henüz kaydedilmiş işlem yok", html);
        Assert.Contains("tahmin edilmez", html);
        Assert.DoesNotContain("<dt>Toplam varlık", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<dt>Piyasa değeri", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<dt>Nakit mevcudu", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<dt>Getiri", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0,00 TRY", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ledger_PreservesOpaqueCursorAndSanitizesInvalidNavigation()
    {
        using var factory = new WealthLedgerApiFactory();
        var fixture = await factory.SeedReadyUiLedgerAsync();
        using var client = CreateClient(factory);

        using var firstResponse = await client.GetAsync("/ledger?pageSize=1");
        var firstHtml = WebUtility.HtmlDecode(
            await firstResponse.Content.ReadAsStringAsync());
        var nextLink = CursorLinkPattern().Match(firstHtml);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Contains(
            fixture.ContributionTransactionId.ToString("D"),
            firstHtml,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            fixture.PurchaseTransactionId.ToString("D"),
            firstHtml,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(nextLink.Success, "The first page did not expose its server cursor.");

        using var secondResponse = await client.GetAsync(nextLink.Groups[1].Value);
        var secondHtml = WebUtility.HtmlDecode(
            await secondResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Contains(
            fixture.PurchaseTransactionId.ToString("D"),
            secondHtml,
            StringComparison.OrdinalIgnoreCase);

        const string privateCursor = "PRIVATE-RAW-CURSOR-PAYLOAD";
        using var invalidResponse = await client.GetAsync(
            "/ledger?pageSize=1&cursor=" + privateCursor);
        var invalidHtml = WebUtility.HtmlDecode(
            await invalidResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.Contains("İlk sayfaya dön", invalidHtml);
        Assert.DoesNotContain(privateCursor, invalidHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            factory.Logs.Messages,
            message => message.Contains(privateCursor, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TransactionExplanation_RendersEveryRecordedFactAndBothReversalDirections()
    {
        using var factory = new WealthLedgerApiFactory();
        var fixture = await factory.SeedReadyUiLedgerAsync();
        using var client = CreateClient(factory);
        var reversal = await ReverseAsync(
            client,
            fixture.PurchaseTransactionId);

        using var purchaseResponse = await client.GetAsync(
            $"/ledger/{fixture.PurchaseTransactionId:D}");
        var purchaseHtml = WebUtility.HtmlDecode(
            await purchaseResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, purchaseResponse.StatusCode);
        Assert.Contains("SHELL-PURCHASE-REFERENCE", purchaseHtml);
        Assert.Contains("Synthetic shell purchase note.", purchaseHtml);
        Assert.Contains("05.09.2026", purchaseHtml);
        Assert.Contains("06.09.2026", purchaseHtml);
        Assert.Contains("07.09.2026", purchaseHtml);
        Assert.Contains("1,25 fon birimi — artış", purchaseHtml);
        Assert.Contains("-123,45 para birimi — azalış", purchaseHtml);
        Assert.Contains("98,7654321 TRY", purchaseHtml);
        Assert.Contains("2,50 TRY", purchaseHtml);
        Assert.Contains("123,45 TRY", purchaseHtml);
        Assert.Contains("COMMISSION", purchaseHtml);
        Assert.Contains("ADDITIONAL_CASH_OUTFLOW", purchaseHtml);
        Assert.Contains("KNOWN", purchaseHtml);
        Assert.Contains(
            fixture.CostId.ToString("D"),
            purchaseHtml,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            fixture.AssetLotId.ToString("D"),
            purchaseHtml,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            fixture.OpeningAllocationId.ToString("D"),
            purchaseHtml,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            reversal.ReversalTransactionId.ToString("D"),
            purchaseHtml,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(
            purchaseHtml.IndexOf(
                fixture.PrincipalEntryId.ToString("D"),
                StringComparison.OrdinalIgnoreCase)
            < purchaseHtml.IndexOf(
                fixture.ConsiderationEntryId.ToString("D"),
                StringComparison.OrdinalIgnoreCase));

        using var reversalResponse = await client.GetAsync(
            $"/ledger/{reversal.ReversalTransactionId:D}");
        var reversalHtml = WebUtility.HtmlDecode(
            await reversalResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, reversalResponse.StatusCode);
        Assert.Contains("REVERSAL", reversalHtml);
        Assert.Contains("-1,25 fon birimi — azalış", reversalHtml);
        Assert.Contains("+123,45 para birimi — artış", reversalHtml);
        Assert.Contains(
            fixture.PurchaseTransactionId.ToString("D"),
            reversalHtml,
            StringComparison.OrdinalIgnoreCase);

        using var contributionResponse = await client.GetAsync(
            $"/ledger/{fixture.ContributionTransactionId:D}");
        var contributionHtml = WebUtility.HtmlDecode(
            await contributionResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, contributionResponse.StatusCode);
        Assert.Contains("ACADEMIC_INCOME", contributionHtml);
        Assert.Contains("Synthetic Member", contributionHtml);
        Assert.Contains("+500 para birimi — artış", contributionHtml);
        Assert.Contains(
            factory.ReadySetup.HouseholdMemberId!.Value.ToString("D"),
            contributionHtml,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TransactionExplanation_UsesCurrentInactiveContextWithoutHidingHistory()
    {
        using var factory = new WealthLedgerApiFactory();
        var fixture = await factory.SeedReadyUiLedgerAsync();
        await factory.ArchiveReadyUiMastersAsync();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(
            $"/ledger/{fixture.PurchaseTransactionId:D}");
        var html = WebUtility.HtmlDecode(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Synthetic Fund", html);
        Assert.Contains("Core Portfolio", html);
        Assert.Contains("Primary Account", html);
        Assert.Contains("Synthetic Institution", html);
        Assert.Contains("ARCHIVED", html);
        Assert.Contains("artık etkin değil veya arşivlenmiş", html);
        Assert.Contains("bugünkü bağlam", html);
    }

    [Theory]
    [InlineData("not-a-guid", HttpStatusCode.BadRequest)]
    [InlineData("00000000-0000-0000-0000-000000000000", HttpStatusCode.BadRequest)]
    [InlineData("10000000-0000-0000-0000-000000000001", HttpStatusCode.NotFound)]
    public async Task TransactionExplanation_InvalidOrUnknownIdentityIsNonDisclosing(
        string requestValue,
        HttpStatusCode expectedStatus)
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/ledger/" + requestValue);
        var html = WebUtility.HtmlDecode(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.DoesNotContain(requestValue, html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            factory.Logs.Messages,
            message => message.Contains(requestValue, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("SELECT ", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Data Source=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at WealthLedger", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Settings_SeparateCurrentMasterDataFromDataSafetyAndRemainReadOnly()
    {
        using var factory = new WealthLedgerApiFactory();
        await factory.ArchiveReadyUiMastersAsync();
        using var client = CreateClient(factory);

        using var masterResponse = await client.GetAsync(
            "/settings/master-data");
        var masterHtml = WebUtility.HtmlDecode(
            await masterResponse.Content.ReadAsStringAsync());
        using var safetyResponse = await client.GetAsync(
            "/settings/data-safety");
        var safetyHtml = WebUtility.HtmlDecode(
            await safetyResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, masterResponse.StatusCode);
        Assert.Contains("Synthetic Household", masterHtml);
        Assert.Contains("Synthetic Member", masterHtml);
        Assert.Contains("Synthetic Institution", masterHtml);
        Assert.Contains("Core Portfolio", masterHtml);
        Assert.Contains("Primary Account", masterHtml);
        Assert.Contains("SYNTHETIC_CASH", masterHtml);
        Assert.Contains("SYNTHETIC_FUND", masterHtml);
        Assert.Contains("ARCHIVED", masterHtml);
        Assert.Contains("Etkin değil", masterHtml);
        Assert.DoesNotContain("Save", masterHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Delete", masterHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<form", masterHtml, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.OK, safetyResponse.StatusCode);
        Assert.Contains(factory.DatabasePath, safetyHtml);
        Assert.Contains(factory.BackupDirectory, safetyHtml);
        Assert.Contains("COMPATIBLE", safetyHtml);
        Assert.Contains("PASSED", safetyHtml);
        Assert.Contains("MATCHED", safetyHtml);
        Assert.Contains("005_WorkspaceIdentity", safetyHtml);
        Assert.Contains("Operatör beyanları", safetyHtml);
        Assert.DoesNotContain("<form", safetyHtml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadyPageLogs_OmitNotesReferencesSqlAndInfrastructureDiagnostics()
    {
        using var factory = new WealthLedgerApiFactory();
        var fixture = await factory.SeedReadyUiLedgerAsync();
        using var client = CreateClient(factory);

        foreach (var path in new[]
                 {
                     "/",
                     "/ledger",
                     $"/ledger/{fixture.PurchaseTransactionId:D}",
                     "/settings",
                     "/settings/master-data",
                     "/settings/data-safety"
                 })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        const string privateCursor = "PRIVATE-READY-CURSOR-PAYLOAD";
        const string privateRequestValue = "PRIVATE-READY-REQUEST-VALUE";
        using var invalidCursor = await client.GetAsync(
            "/ledger?pageSize=1&cursor=" + privateCursor);
        using var invalidIdentity = await client.GetAsync(
            "/ledger/" + privateRequestValue);

        Assert.Equal(HttpStatusCode.BadRequest, invalidCursor.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidIdentity.StatusCode);

        var logs = string.Join(Environment.NewLine, factory.Logs.Messages);
        Assert.DoesNotContain("SHELL-PURCHASE-REFERENCE", logs);
        Assert.DoesNotContain("Synthetic shell purchase note.", logs);
        Assert.DoesNotContain("Synthetic shell cost note.", logs);
        Assert.DoesNotContain("Synthetic Household", logs);
        Assert.DoesNotContain("Synthetic Member", logs);
        Assert.DoesNotContain("Synthetic Institution", logs);
        Assert.DoesNotContain("Core Portfolio", logs);
        Assert.DoesNotContain("Primary Account", logs);
        Assert.DoesNotContain("Synthetic Fund", logs);
        Assert.DoesNotContain("98,7654321", logs);
        Assert.DoesNotContain(privateCursor, logs);
        Assert.DoesNotContain(privateRequestValue, logs);
        Assert.DoesNotContain(factory.DatabasePath, logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(factory.BackupDirectory, logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Data Source=", logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT ", logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqliteException", logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at WealthLedger", logs, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ReversePostedTransactionResponse> ReverseAsync(
        HttpClient client,
        Guid transactionId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/ledger/transactions/{transactionId:D}/reversals")
        {
            Content = JsonContent.Create(
                new ReversePostedTransactionRequest(
                    "Synthetic shell reversal reason."))
        };
        request.Headers.Add(
            "Idempotency-Key",
            Guid.NewGuid().ToString("D"));

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content
            .ReadFromJsonAsync<ReversePostedTransactionResponse>();
        return Assert.IsType<ReversePostedTransactionResponse>(result);
    }

    private static HttpClient CreateClient(WealthLedgerApiFactory factory)
        => factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing
                .WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

    [GeneratedRegex("href=\"([^\"]*\\?pageSize=1&cursor=[^\"]+)\"")]
    private static partial Regex CursorLinkPattern();
}
