using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using WealthLedger.Api.Contracts;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Api.Tests;

/// <summary>
/// Synthetic M010 history in the one Ready household: two January cash
/// contributions, a reversed February contribution into a direct account, a
/// Fund purchase, and a physical-gold purchase with a partial transfer.
/// </summary>
/// <remarks>
/// Unlike <see cref="M010ApiTestData.SeedAsync"/>, this seeds no second
/// household, because the Ready UI refuses an ambiguous household context.
/// </remarks>
internal sealed record M010UiHistory(
    InitializeCoreLedgerResponse Setup,
    Guid DirectAccountId,
    Guid FirstJanuaryId,
    Guid SecondJanuaryId,
    Guid DirectFebruaryId,
    Guid FundPurchaseId,
    Guid FundLotId,
    Guid ReversalId,
    M010GoldHistory Gold);

internal static partial class M010UiTestData
{
    internal static readonly Guid DirectAccountId =
        Guid.Parse("51000000-0000-0000-0000-0000000000b1");

    internal static async Task<M010UiHistory> SeedAsync(
        WealthLedgerApiFactory factory,
        HttpClient client)
    {
        var setup = factory.ReadySetup;

        await using (var context = factory.CreateDbContext())
        {
            context.Accounts.Add(new AccountRow
            {
                Id = DirectAccountId,
                HouseholdId = setup.HouseholdId,
                InstitutionId = null,
                Code = "UI_DIRECT_CASH",
                Name = "Synthetic Direct Account",
                Type = AccountType.Cash,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            });
            await context.SaveChangesAsync();
        }

        var firstJanuary = await M010ApiTestData.ContributeAsync(
            client,
            setup.HouseholdId,
            setup.PortfolioId,
            setup.AccountId,
            setup.CashAssetId,
            100_000,
            M010ApiTestData.January,
            "SYNTH-JAN-001");
        var secondJanuary = await M010ApiTestData.ContributeAsync(
            client,
            setup.HouseholdId,
            setup.PortfolioId,
            setup.AccountId,
            setup.CashAssetId,
            50_000,
            M010ApiTestData.January,
            "SYNTH-JAN-002");
        var directFebruary = await M010ApiTestData.ContributeAsync(
            client,
            setup.HouseholdId,
            setup.PortfolioId,
            DirectAccountId,
            setup.CashAssetId,
            7_000,
            M010ApiTestData.February,
            externalReference: null);
        var fundPurchase = await M010ApiTestData.PostAsync<RecordFundPurchaseResponse>(
            client,
            "/api/ledger/fund-purchases",
            new RecordFundPurchaseRequest(
                setup.HouseholdId,
                setup.PortfolioId,
                setup.AccountId,
                setup.FundAssetId,
                setup.CashAssetId,
                FundQuantityRawE8: 125_000_000,
                ExecutedUnitPriceRawE8: 20_000_000_000,
                PriceCurrencyCode: "TRY",
                CashConsiderationMinorUnits: 25_000,
                CashConsiderationCurrencyCode: "TRY",
                M010ApiTestData.FundDate,
                ExternalReference: "SYNTH-FUND-001"));
        var reversal = await M010ApiTestData.PostAsync<ReversePostedTransactionResponse>(
            client,
            $"/api/ledger/transactions/{directFebruary}/reversals",
            new ReversePostedTransactionRequest("Synthetic reversal."));
        var gold = await M010ApiTestData.SeedGoldAsync(factory, client);

        return new M010UiHistory(
            setup,
            DirectAccountId,
            firstJanuary,
            secondJanuary,
            directFebruary,
            fundPurchase.TransactionId,
            fundPurchase.AssetLotId,
            reversal.ReversalTransactionId,
            gold);
    }

    internal static HttpClient CreateClient(WealthLedgerApiFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

    /// <summary>
    /// Reads a page and returns its status with HTML-decoded text, so
    /// assertions can name Turkish text and query strings directly.
    /// </summary>
    internal static async Task<(HttpStatusCode Status, string Html)> GetPageAsync(
        HttpClient client,
        string path)
    {
        using var response = await client.GetAsync(path);
        return (
            response.StatusCode,
            WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    internal static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client,
        string path,
        IEnumerable<KeyValuePair<string, string>> fields)
        => await client.PostAsync(path, new FormUrlEncodedContent(fields));

    internal static string TokenFrom(string html)
        => HiddenValue(AntiforgeryTokenPattern(), html, "antiforgery token");

    internal static string HiddenValue(
        Regex pattern,
        string html,
        string description)
    {
        var match = pattern.Match(html);
        Assert.True(match.Success, $"The page did not render its {description}.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    /// <summary>
    /// Asserts that rendered HTML exposes no storage or exception internals.
    /// </summary>
    /// <remarks>
    /// SQL is matched case-sensitively: M010 pages render HTML
    /// <c>&lt;select&gt;</c> controls, and EF Core emits upper-case SQL.
    /// </remarks>
    internal static void AssertNoInternals(string html)
    {
        Assert.DoesNotContain("Data Source=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT ", html, StringComparison.Ordinal);
        Assert.DoesNotContain(" FROM \"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("SqliteException", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at WealthLedger", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The distinct transaction identities linked from a rendered result list,
    /// in page order.
    /// </summary>
    internal static IReadOnlyList<Guid> LinkedTransactionIds(string html)
        => TransactionLinkPattern()
            .Matches(html)
            .Select(match => Guid.Parse(match.Groups[1].Value))
            .Distinct()
            .ToArray();

    [GeneratedRegex(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex AntiforgeryTokenPattern();

    [GeneratedRegex(
        "<h2>\\s*<a href=\"/ledger/([0-9a-f-]{36})\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex TransactionLinkPattern();
}
