using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Playwright;
using WealthLedger.Api.Contracts;

namespace WealthLedger.UI.BrowserTests;

public sealed class CriticalJourneysTests
{
    [Fact]
    public Task CompleteFirstRunAndReadyNavigation_WorkAtDesktopViewport()
        => RunJourneyAsync(
            new JourneyOptions(
                JavaScriptEnabled: true,
                KeyboardOnly: false,
                EmulateAccessibilityPreferences: false,
                ViewportWidth: 1280,
                ViewportHeight: 800));

    [Fact]
    public Task CompleteFirstRunAndReadyNavigation_WorkWithoutJavaScriptAtNarrowViewport()
        => RunJourneyAsync(
            new JourneyOptions(
                JavaScriptEnabled: false,
                KeyboardOnly: false,
                EmulateAccessibilityPreferences: true,
                ViewportWidth: 390,
                ViewportHeight: 844));

    [Fact]
    public Task FirstRunAndLedgerNavigation_WorkByKeyboardWithValidationFocus()
        => RunJourneyAsync(
            new JourneyOptions(
                JavaScriptEnabled: true,
                KeyboardOnly: true,
                EmulateAccessibilityPreferences: false,
                ViewportWidth: 1280,
                ViewportHeight: 800));

    private static async Task RunJourneyAsync(JourneyOptions options)
    {
        var workspace = BrowserTestWorkspace.Create();
        IPlaywright? playwright = null;
        IBrowser? browser = null;
        IBrowserContext? context = null;
        var consoleErrors = new ConcurrentQueue<string>();
        var requestFailures = new ConcurrentQueue<string>();
        var browserDisconnected = false;

        try
        {
            playwright = await Playwright.CreateAsync();
            browser = await playwright.Chromium.LaunchAsync(
                new BrowserTypeLaunchOptions
                {
                    Headless = true,
                    DownloadsPath = workspace.BrowserArtifactsDirectory
                });
            context = await browser.NewContextAsync(
                new BrowserNewContextOptions
                {
                    JavaScriptEnabled = options.JavaScriptEnabled,
                    Locale = "tr-TR",
                    ForcedColors = options.EmulateAccessibilityPreferences
                        ? ForcedColors.Active
                        : null,
                    ReducedMotion = options.EmulateAccessibilityPreferences
                        ? ReducedMotion.Reduce
                        : null,
                    ServiceWorkers = ServiceWorkerPolicy.Block,
                    ViewportSize = new ViewportSize
                    {
                        Width = options.ViewportWidth,
                        Height = options.ViewportHeight
                    }
                });
            var networkGuard = new LoopbackNetworkGuard();
            await networkGuard.AttachAsync(context);
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(60_000);
            page.SetDefaultNavigationTimeout(60_000);
            page.Console += (_, message) =>
            {
                if (string.Equals(
                        message.Type,
                        "error",
                        StringComparison.OrdinalIgnoreCase))
                {
                    consoleErrors.Enqueue(message.Text);
                }
            };
            page.PageError += (_, exception) =>
                consoleErrors.Enqueue(exception);
            page.RequestFailed += (_, request) =>
                requestFailures.Enqueue(request.Url);

            await RunStorageStepAsync(
                page,
                workspace,
                options.KeyboardOnly);
            await RunWorkspaceStepAsync(
                page,
                workspace,
                options.KeyboardOnly);
            await RunBackupStepAsync(
                page,
                workspace,
                options.KeyboardOnly);
            await RunReadyNavigationAsync(
                page,
                workspace,
                options);

            Assert.Empty(networkGuard.ExternalRequests);
            Assert.Empty(consoleErrors);
            Assert.Empty(requestFailures);
            Assert.True(File.Exists(workspace.DatabasePath));
            Assert.True(Directory.Exists(workspace.BackupDirectory));
            Assert.NotEmpty(
                Directory.EnumerateFiles(
                    workspace.BackupDirectory,
                    "*.wlbackup",
                    SearchOption.TopDirectoryOnly));

            var hostOutput = string.Join(
                Environment.NewLine,
                workspace.HostOutputs);
            Assert.DoesNotContain(
                workspace.DatabasePath,
                hostOutput,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                workspace.BackupDirectory,
                hostOutput,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Browser Test Household", hostOutput);
            Assert.DoesNotContain("BROWSER-JOURNEY-REFERENCE", hostOutput);
            Assert.DoesNotContain(
                "Data Source=",
                hostOutput,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "SELECT ",
                hostOutput,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "SqliteException",
                hostOutput,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                " at WealthLedger",
                hostOutput,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (context is not null)
            {
                await context.CloseAsync();
            }

            if (browser is not null)
            {
                await browser.CloseAsync();
                browserDisconnected = !browser.IsConnected;
            }

            playwright?.Dispose();
            await workspace.DisposeAsync();
        }

        Assert.True(browserDisconnected);
        Assert.True(workspace.CleanedUp);
        Assert.False(Directory.Exists(workspace.RootPath));
    }

    private static async Task RunStorageStepAsync(
        IPage page,
        BrowserTestWorkspace workspace,
        bool keyboardOnly)
    {
        var baseAddress = await workspace.StartHostAsync();
        await GoToAsync(page, baseAddress, "/setup/storage");
        await AssertPageFrameAsync(page);
        Assert.False(File.Exists(workspace.DatabasePath));

        var confirmationLabel = await page
            .Locator("label[for=\"ConfirmInitialization\"]")
            .BoundingBoxAsync();
        var submitButton = await page
            .Locator("button[type=\"submit\"]")
            .BoundingBoxAsync();
        Assert.NotNull(confirmationLabel);
        Assert.NotNull(submitButton);
        Assert.True(confirmationLabel.Height >= 44);
        Assert.True(submitButton.Height >= 44);

        if (keyboardOnly)
        {
            await ActivateSkipLinkAsync(page);
            await TabToAsync(page, "#ConfirmInitialization");
            await page.Keyboard.PressAsync("Space");
            await TabToAsync(page, "button[type=\"submit\"]");
            await page.Keyboard.PressAsync("Enter");
        }
        else
        {
            await page.Locator("#ConfirmInitialization").CheckAsync();
            await page.Locator("button[type=\"submit\"]").ClickAsync();
        }

        await page.Locator(".panel--success").WaitForAsync();
        Assert.True(File.Exists(workspace.DatabasePath));
        await workspace.StopHostAsync();
    }

    private static async Task RunWorkspaceStepAsync(
        IPage page,
        BrowserTestWorkspace workspace,
        bool keyboardOnly)
    {
        var baseAddress = await workspace.StartHostAsync();
        await GoToAsync(page, baseAddress, "/setup/workspace");
        await AssertPageFrameAsync(page);

        if (keyboardOnly)
        {
            await ActivateSkipLinkAsync(page);
            await TabToAsync(page, "button[type=\"submit\"]");
            await page.Keyboard.PressAsync("Enter");
            await page.Locator(".validation-summary:focus").WaitForAsync();
            await page.WaitForLoadStateAsync(LoadState.Load);
            Assert.Equal(
                1,
                await page.Locator(".validation-summary:focus").CountAsync());

            const string householdValidationLink =
                ".validation-summary a[href=\"#Input_HouseholdName\"]";
            await TabToAsync(page, householdValidationLink);
            var validationLink = page.Locator(
                $"{householdValidationLink}:focus");
            Assert.Equal(1, await validationLink.CountAsync());
            var target = await validationLink.GetAttributeAsync("href");
            Assert.NotNull(target);
            await page.Keyboard.PressAsync("Enter");
            Assert.Equal(
                1,
                await page.Locator($"{target}:focus").CountAsync());

            await TypeFocusedAndAssertAsync(
                page,
                "#Input_HouseholdName",
                "Browser Test Household");
            await TabToAndTypeAsync(
                page,
                "#Input_HouseholdMemberDisplayName",
                "Browser Test Member");
            await TabToAndTypeAsync(
                page,
                "#Input_InstitutionName",
                "Browser Test Institution");
            await TabToAndTypeAsync(
                page,
                "#Input_PortfolioName",
                "Browser Test Portfolio");
            await TabToAndTypeAsync(
                page,
                "#Input_AccountName",
                "Browser Test Account");
            await TabToAndTypeAsync(
                page,
                "#Input_FundAssetName",
                "Browser Test Fund");
        }
        else
        {
            await FillWorkspaceAsync(page);
        }

        if (keyboardOnly)
        {
            Assert.Equal(
                "Browser Test Household",
                await page.Locator("#Input_HouseholdName")
                    .InputValueAsync());
            await TabToAsync(page, "button[type=\"submit\"]");
            await page.Keyboard.PressAsync("Enter");
        }
        else
        {
            await page.Locator("button[type=\"submit\"]").ClickAsync();
        }

        await page.Locator(".panel--success, .validation-summary")
            .First
            .WaitForAsync();
        var successCount = await page.Locator(".panel--success")
            .CountAsync();
        var validationDetails = string.Empty;

        if (successCount != 1)
        {
            var validationText = await page.Locator(".validation-summary")
                .AllInnerTextsAsync();
            var validationTarget = await page
                .Locator(".validation-summary a")
                .First
                .GetAttributeAsync("href");
            validationDetails =
                $"{validationTarget}: {string.Join(" | ", validationText)}";
        }

        Assert.True(
            successCount == 1,
            validationDetails);
        await workspace.StopHostAsync();
    }

    private static async Task RunBackupStepAsync(
        IPage page,
        BrowserTestWorkspace workspace,
        bool keyboardOnly)
    {
        var baseAddress = await workspace.StartHostAsync();
        await GoToAsync(page, baseAddress, "/setup/backup");
        await AssertPageFrameAsync(page);
        Assert.False(Directory.Exists(workspace.BackupDirectory));

        if (keyboardOnly)
        {
            await ActivateSkipLinkAsync(page);
            await TabToAsync(page, "#ConfirmBackupCreation");
            await page.Keyboard.PressAsync("Space");
            await TabToAsync(page, "button[type=\"submit\"]");
            await page.Keyboard.PressAsync("Enter");
        }
        else
        {
            await page.Locator("#ConfirmBackupCreation").CheckAsync();
            await page.Locator("button[type=\"submit\"]").ClickAsync();
        }

        await page.Locator("#complete-heading").WaitForAsync();
        Assert.Equal(
            "/setup/complete",
            new Uri(page.Url).AbsolutePath);
        await AssertPageFrameAsync(page);
        Assert.Contains(
            "yeniden başlat",
            await page.Locator("main").InnerTextAsync(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Single(
            Directory.EnumerateFiles(
                workspace.BackupDirectory,
                "*.wlbackup",
                SearchOption.TopDirectoryOnly));
        await workspace.StopHostAsync();
    }

    private static async Task RunReadyNavigationAsync(
        IPage page,
        BrowserTestWorkspace workspace,
        JourneyOptions options)
    {
        var baseAddress = await workspace.StartHostAsync();
        var transactionId = await SeedContributionAsync(baseAddress);

        await GoToAsync(page, baseAddress, "/");
        await AssertPageFrameAsync(page);
        await AssertResponsiveReflowAsync(page, options);

        if (options.KeyboardOnly)
        {
            await ActivateSkipLinkAsync(page);
            await GoToAsync(page, baseAddress, "/");
            await TabToAsync(page, ".primary-nav a[href=\"/ledger\"]");
            await page.Keyboard.PressAsync("Enter");
        }
        else
        {
            await page
                .Locator(".primary-nav a[href=\"/ledger\"]")
                .ClickAsync();
        }

        await page.Locator("#ledger-heading").WaitForAsync();
        Assert.Equal("/ledger", new Uri(page.Url).AbsolutePath);
        Assert.Equal(1, await page.Locator("h1").CountAsync());
        var transactionLink = page.Locator(
            ".transaction-card h2 a").First;
        var transactionHref = await transactionLink
            .GetAttributeAsync("href");
        Assert.Equal($"/ledger/{transactionId:D}", transactionHref);

        if (options.KeyboardOnly)
        {
            await TabToAsync(
                page,
                ".transaction-card h2 a");
            await page.Keyboard.PressAsync("Enter");
        }
        else
        {
            await transactionLink.ClickAsync();
        }

        await page.Locator("#detail-heading").WaitForAsync();
        Assert.Equal(
            $"/ledger/{transactionId:D}",
            new Uri(page.Url).AbsolutePath);
        Assert.Contains(
            "+123,45 para birimi",
            await page.Locator("main").InnerTextAsync(),
            StringComparison.Ordinal);

        if (options.KeyboardOnly)
        {
            await TabToAsync(
                page,
                ".primary-nav a[href=\"/settings\"]");
            await page.Keyboard.PressAsync("Enter");
        }
        else
        {
            await page
                .Locator(".primary-nav a[href=\"/settings\"]")
                .ClickAsync();
        }

        await page.Locator("#settings-heading").WaitForAsync();
        Assert.Equal("/settings", new Uri(page.Url).AbsolutePath);
        await AssertPageFrameAsync(page);
        await workspace.StopHostAsync();
    }

    private static async Task<Guid> SeedContributionAsync(Uri baseAddress)
    {
        using var client = new HttpClient
        {
            BaseAddress = baseAddress
        };
        var households = await client.GetFromJsonAsync<
            NavigationPageResponse<HouseholdNavigationResponse>>(
            "/api/households?pageSize=100");
        var household = Assert.Single(households!.Items);
        var portfolios = await client.GetFromJsonAsync<
            NavigationPageResponse<PortfolioNavigationResponse>>(
            $"/api/households/{household.HouseholdId:D}/portfolios?pageSize=100");
        var portfolio = Assert.Single(portfolios!.Items);
        var accounts = await client.GetFromJsonAsync<
            NavigationPageResponse<AccountNavigationResponse>>(
            $"/api/households/{household.HouseholdId:D}/accounts?pageSize=100");
        var account = Assert.Single(accounts!.Items);
        var assets = await client.GetFromJsonAsync<
            NavigationPageResponse<AssetNavigationResponse>>(
            "/api/assets?pageSize=100");
        var cashAsset = Assert.Single(
            assets!.Items,
            item => string.Equals(
                item.Code,
                "TRY_NAKIT",
                StringComparison.Ordinal));
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/ledger/contributions")
        {
            Content = JsonContent.Create(
                new RecordContributionRequest(
                    household.HouseholdId,
                    portfolio.PortfolioId,
                    account.AccountId,
                    cashAsset.AssetId,
                    AmountMinorUnits: 12_345,
                    CurrencyCode: "TRY",
                    CashFlowCategoryCode: "ACADEMIC_INCOME",
                    ExecutionDate: new DateOnly(2026, 9, 8),
                    ExternalReference: "BROWSER-JOURNEY-REFERENCE",
                    Note: "Synthetic browser journey note."))
        };
        request.Headers.Add(
            "Idempotency-Key",
            $"browser-{Guid.NewGuid():N}");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content
            .ReadFromJsonAsync<RecordContributionResponse>();
        Assert.NotNull(result);
        return result.TransactionId;
    }

    private static async Task FillWorkspaceAsync(IPage page)
    {
        await page.Locator("#Input_HouseholdName")
            .FillAsync("Browser Test Household");
        await page.Locator("#Input_HouseholdMemberDisplayName")
            .FillAsync("Browser Test Member");
        await page.Locator("#Input_InstitutionName")
            .FillAsync("Browser Test Institution");
        await page.Locator("#Input_PortfolioName")
            .FillAsync("Browser Test Portfolio");
        await page.Locator("#Input_AccountName")
            .FillAsync("Browser Test Account");
        await page.Locator("#Input_FundAssetName")
            .FillAsync("Browser Test Fund");
    }

    private static async Task GoToAsync(
        IPage page,
        Uri baseAddress,
        string path)
    {
        var response = await page.GotoAsync(
            new Uri(baseAddress, path).AbsoluteUri,
            new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });

        Assert.NotNull(response);
        Assert.True(response.Ok, $"Navigation to {path} returned {response.Status}.");
    }

    private static async Task AssertPageFrameAsync(IPage page)
    {
        Assert.Equal(1, await page.Locator("h1").CountAsync());
        Assert.Equal(1, await page.Locator("body > header").CountAsync());
        Assert.Equal(1, await page.Locator("body > main#main-content").CountAsync());
        Assert.Equal(1, await page.Locator("body > footer").CountAsync());
        Assert.True(await page.Locator("main#main-content").IsVisibleAsync());
    }

    private static async Task ActivateSkipLinkAsync(IPage page)
    {
        await page.Keyboard.PressAsync("Tab");
        Assert.Equal(1, await page.Locator(".skip-link:focus").CountAsync());
        await page.Keyboard.PressAsync("Enter");
        Assert.Equal(
            1,
            await page.Locator("main#main-content:focus").CountAsync());
    }

    private static async Task TabToAsync(
        IPage page,
        string selector)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            await page.Keyboard.PressAsync("Tab");

            if (await page.Locator($"{selector}:focus").CountAsync() == 1)
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"Keyboard focus did not reach {selector}.");
    }

    private static async Task TabToAndTypeAsync(
        IPage page,
        string selector,
        string value)
    {
        await TabToAsync(page, selector);
        await TypeFocusedAndAssertAsync(page, selector, value);
    }

    private static async Task TypeFocusedAndAssertAsync(
        IPage page,
        string selector,
        string value)
    {
        var field = page.Locator(selector);
        var focusedCount = await page.Locator($"{selector}:focus")
            .CountAsync();
        var activeElement = await page.EvaluateAsync<string>(
            "document.activeElement?.id || document.activeElement?.tagName || 'none'");
        Assert.True(
            focusedCount == 1,
            $"Expected {selector} to be focused; active element was {activeElement}.");
        await field.PressSequentiallyAsync(value);
        Assert.Equal(
            value,
            await field.InputValueAsync());
    }

    private static async Task AssertResponsiveReflowAsync(
        IPage page,
        JourneyOptions options)
    {
        await AssertMainWithinViewportAsync(
            page,
            options.ViewportWidth);
        var effectiveTwoHundredPercentWidth = Math.Max(
            160,
            options.ViewportWidth / 2);
        await page.SetViewportSizeAsync(
            effectiveTwoHundredPercentWidth,
            Math.Max(420, options.ViewportHeight / 2));
        await AssertMainWithinViewportAsync(
            page,
            effectiveTwoHundredPercentWidth);
        await page.SetViewportSizeAsync(
            options.ViewportWidth,
            options.ViewportHeight);
    }

    private static async Task AssertMainWithinViewportAsync(
        IPage page,
        int viewportWidth)
    {
        var bounds = await page.Locator("main#main-content")
            .BoundingBoxAsync();
        Assert.NotNull(bounds);
        Assert.True(bounds.X >= -0.5);
        Assert.True(bounds.X + bounds.Width <= viewportWidth + 0.5);
    }

    private sealed record JourneyOptions(
        bool JavaScriptEnabled,
        bool KeyboardOnly,
        bool EmulateAccessibilityPreferences,
        int ViewportWidth,
        int ViewportHeight);
}
