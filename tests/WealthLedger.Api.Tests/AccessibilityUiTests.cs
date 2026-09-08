using System.Net;
using System.Text.RegularExpressions;

namespace WealthLedger.Api.Tests;

public sealed partial class AccessibilityUiTests
{
    [Fact]
    public async Task RenderedPages_HaveLandmarksOneHeadingAndSkipLinkFirst()
    {
        using (var factory = new WealthLedgerApiFactory())
        {
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
                await AssertAccessibleDocumentAsync(client, path);
            }
        }

        await AssertModePageAsync(
            ApiTestStartupMode.Blocked,
            "/blocked");
        await AssertModePageAsync(
            ApiTestStartupMode.StorageUninitialized,
            "/setup/storage");
        await AssertModePageAsync(
            ApiTestStartupMode.WorkspaceUninitialized,
            "/setup/workspace");

        using (var factory = new WealthLedgerApiFactory(
                   ApiTestStartupMode.InitialBackupRequired))
        {
            using var client = CreateClient(factory);
            await AssertAccessibleDocumentAsync(client, "/setup/backup");

            var token = await GetAntiforgeryTokenAsync(
                client,
                "/setup/backup");
            using var post = await client.PostAsync(
                "/setup/backup",
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["ConfirmBackupCreation"] = "true",
                        ["__RequestVerificationToken"] = token
                    }));

            Assert.Equal(HttpStatusCode.Found, post.StatusCode);
            Assert.Equal(
                "/setup/complete",
                post.Headers.Location?.OriginalString);
            await AssertAccessibleDocumentAsync(client, "/setup/complete");
        }
    }

    [Fact]
    public async Task SetupControls_HaveLabelsAndAssociatedHelpOrErrors()
    {
        foreach (var (mode, path) in new[]
                 {
                     (ApiTestStartupMode.StorageUninitialized, "/setup/storage"),
                     (ApiTestStartupMode.WorkspaceUninitialized, "/setup/workspace"),
                     (ApiTestStartupMode.InitialBackupRequired, "/setup/backup")
                 })
        {
            using var factory = new WealthLedgerApiFactory(mode);
            using var client = CreateClient(factory);
            using var response = await client.GetAsync(path);
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            foreach (Match control in FormControlPattern().Matches(html))
            {
                var attributes = control.Groups["attributes"].Value;

                if (string.Equals(
                        Attribute(attributes, "type"),
                        "hidden",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var id = Attribute(attributes, "id");
                Assert.False(
                    string.IsNullOrWhiteSpace(id),
                    $"An input on {path} has no id: {control.Value}");
                Assert.Matches(
                    $"<label\\b[^>]*\\bfor=\"{Regex.Escape(id!)}\"[^>]*>",
                    html);

                var describedBy = Attribute(
                    attributes,
                    "aria-describedby");

                if (describedBy is null)
                {
                    continue;
                }

                foreach (var descriptionId in describedBy.Split(
                             ' ',
                             StringSplitOptions.RemoveEmptyEntries))
                {
                    Assert.Contains(
                        $"id=\"{descriptionId}\"",
                        html,
                        StringComparison.Ordinal);
                }
            }
        }
    }

    [Fact]
    public async Task ValidationSummary_IsFocusableAndLinksToInvalidControl()
    {
        using var factory = new WealthLedgerApiFactory(
            ApiTestStartupMode.WorkspaceUninitialized);
        using var client = CreateClient(factory);
        var token = await GetAntiforgeryTokenAsync(
            client,
            "/setup/workspace");

        using var response = await client.PostAsync(
            "/setup/workspace",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["Input.HouseholdName"] = string.Empty,
                    ["Input.InstitutionName"] = string.Empty,
                    ["Input.PortfolioName"] = string.Empty,
                    ["Input.AccountName"] = string.Empty,
                    ["Input.FundAssetName"] = string.Empty,
                    ["__RequestVerificationToken"] = token
                }));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches(
            FocusableValidationSummaryPattern(),
            html);
        Assert.Contains(
            "href=\"#Input_HouseholdName\"",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "aria-describedby=\"Input_HouseholdName-error\"",
            html,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalStyles_PreserveFocusReducedMotionAndTargetSizing()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = CreateClient(factory);
        var styles = new List<string>();

        foreach (var path in new[]
                 {
                     "/_content/WealthLedger.UI/css/setup.css",
                     "/_content/WealthLedger.UI/css/shell.css"
                 })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            styles.Add(await response.Content.ReadAsStringAsync());
        }

        var combined = string.Join(Environment.NewLine, styles);
        Assert.DoesNotMatch(DisabledOutlinePattern(), combined);
        Assert.Contains(":focus-visible", combined, StringComparison.Ordinal);
        Assert.Contains(
            "prefers-reduced-motion: no-preference",
            combined,
            StringComparison.Ordinal);
        Assert.Contains(
            "@media (forced-colors: active)",
            combined,
            StringComparison.Ordinal);
        Assert.Contains("min-height: 2.75rem", combined, StringComparison.Ordinal);
    }

    private static async Task AssertModePageAsync(
        ApiTestStartupMode mode,
        string path)
    {
        using var factory = new WealthLedgerApiFactory(mode);
        using var client = CreateClient(factory);
        await AssertAccessibleDocumentAsync(client, path);
    }

    private static async Task AssertAccessibleDocumentAsync(
        HttpClient client,
        string path)
    {
        using var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        var bodyMatch = BodyPattern().Match(html);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(bodyMatch.Success, $"{path} did not render a body.");
        Assert.Single(HeadingPattern().Matches(html).Cast<Match>());
        Assert.Contains("<header", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<main", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<footer", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"main-content\"", html, StringComparison.Ordinal);

        var firstFocusable = FocusablePattern().Match(
            bodyMatch.Groups["body"].Value);
        Assert.True(
            firstFocusable.Success,
            $"{path} did not render a focusable element.");
        Assert.Contains(
            "class=\"skip-link\"",
            firstFocusable.Value,
            StringComparison.Ordinal);
        Assert.Contains(
            "href=\"#main-content\"",
            firstFocusable.Value,
            StringComparison.Ordinal);
    }

    private static HttpClient CreateClient(WealthLedgerApiFactory factory)
        => factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing
                .WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

    private static async Task<string> GetAntiforgeryTokenAsync(
        HttpClient client,
        string path)
    {
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var match = AntiforgeryTokenPattern().Match(html);

        Assert.True(match.Success, "The form did not render an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static string? Attribute(string attributes, string name)
    {
        var match = Regex.Match(
            attributes,
            $"\\b{Regex.Escape(name)}=\"([^\"]*)\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(
        "<body\\b[^>]*>(?<body>.*)</body>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline
        | RegexOptions.CultureInvariant)]
    private static partial Regex BodyPattern();

    [GeneratedRegex(
        "<h1\\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(
        "<(?:a|button|input|select|textarea|summary)\\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FocusablePattern();

    [GeneratedRegex(
        "<(?:input|select|textarea)\\b(?<attributes>[^>]*)>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FormControlPattern();

    [GeneratedRegex(
        "<div\\b(?=[^>]*class=\"validation-summary\")(?=[^>]*tabindex=\"-1\")(?=[^>]*autofocus)[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FocusableValidationSummaryPattern();

    [GeneratedRegex(
        "outline\\s*:\\s*(?:none|0(?:[;\\s}]|$))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DisabledOutlinePattern();

    [GeneratedRegex(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex AntiforgeryTokenPattern();
}
