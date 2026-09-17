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
                ExerciseOpeningCutover: false,
                ViewportWidth: 1280,
                ViewportHeight: 800));

    [Fact]
    public Task CompleteFirstRunAndReadyNavigation_WorkWithoutJavaScriptAtNarrowViewport()
        => RunJourneyAsync(
            new JourneyOptions(
                JavaScriptEnabled: false,
                KeyboardOnly: false,
                EmulateAccessibilityPreferences: true,
                ExerciseOpeningCutover: true,
                ViewportWidth: 390,
                ViewportHeight: 844,
                ExerciseFundLifecycle: true,
                ExercisePhysicalGoldLifecycle: true));

    [Fact]
    public Task FirstRunAndLedgerNavigation_WorkByKeyboardWithValidationFocus()
        => RunJourneyAsync(
            new JourneyOptions(
                JavaScriptEnabled: true,
                KeyboardOnly: true,
                EmulateAccessibilityPreferences: false,
                ExerciseOpeningCutover: false,
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
            Assert.Empty(
                Directory.EnumerateFiles(
                    workspace.BrowserArtifactsDirectory,
                    "*",
                    SearchOption.AllDirectories));

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
            Assert.DoesNotContain("BROWSER-OPENING-REFERENCE", hostOutput);
            Assert.DoesNotContain("Synthetic browser opening", hostOutput);
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

        if (options.ExerciseOpeningCutover)
        {
            baseAddress = await RunOpeningCutoverAsync(
                page,
                workspace,
                baseAddress,
                options);
        }

        if (options.ExerciseFundLifecycle)
        {
            baseAddress = await RunFundLifecycleAsync(
                page,
                workspace,
                baseAddress,
                options);
        }

        if (options.ExercisePhysicalGoldLifecycle)
        {
            baseAddress = await RunPhysicalGoldLifecycleAsync(
                page,
                workspace,
                baseAddress,
                options);
        }

        await GoToAsync(page, baseAddress, "/record");
        await AssertPageFrameAsync(page);
        await AssertResponsiveReflowAsync(page, options);
        foreach (var destination in new[]
                 {
                     "/record/physical-gold-purchase",
                     "/record/physical-gold-sale",
                     "/record/physical-gold-transfer"
                 })
        {
            Assert.Equal(
                1,
                await page.Locator($"a[href=\"{destination}\"]").CountAsync());
        }

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

    private static async Task<Uri> RunOpeningCutoverAsync(
        IPage page,
        BrowserTestWorkspace workspace,
        Uri baseAddress,
        JourneyOptions options)
    {
        await GoToAsync(page, baseAddress, "/record/opening-balance");
        await AssertPageFrameAsync(page);
        await AssertResponsiveReflowAsync(page, options);
        /*
         * The primary nav marks the Record section, not one workflow inside
         * it, because M008 adds three more recording destinations.
         */
        Assert.Equal(
            1,
            await page.Locator(
                ".primary-nav a[href=\"/record\"][aria-current=\"page\"]")
                .CountAsync());

        await SelectOptionContainingAsync(
            page.Locator("#Input_AssetId"),
            "TRY_NAKIT");
        await page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions
                {
                    Name = "Varlık seçimini uygula",
                    Exact = true
                })
            .ClickAsync();
        await page.Locator("#Input_Quantity").FillAsync("12345,67");
        await page.Locator("#Input_ExternalReference")
            .FillAsync("BROWSER-OPENING-REFERENCE-CASH");
        await page.Locator("#Input_Note")
            .FillAsync("Synthetic browser opening for cash evidence.");
        var cashReceipt = await ReviewAndPostOpeningAsync(page);
        Assert.Contains(
            "12.345,67 para birimi",
            await page.Locator("main").InnerTextAsync());

        await page.GetByRole(
                AriaRole.Link,
                new PageGetByRoleOptions
                {
                    Name = "Başka bir açılış başlat",
                    Exact = true
                })
            .ClickAsync();
        await SelectOptionContainingAsync(
            page.Locator("#Input_AssetId"),
            "ILK_FON");
        await page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions
                {
                    Name = "Varlık seçimini uygula",
                    Exact = true
                })
            .ClickAsync();
        await page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions
                {
                    Name = "Lot ekle",
                    Exact = true
                })
            .ClickAsync();
        await page.Locator("#Input_Quantity").FillAsync("123,456789");
        await page.Locator("#Input_Lots_0__Quantity").FillAsync("100,000001");
        await page.Locator("#Input_Lots_0__CostBasisStatusCode")
            .SelectOptionAsync("UNKNOWN");
        await page.Locator("#Input_Lots_1__Quantity").FillAsync("23,456787");
        await page.Locator("#Input_Lots_1__CostBasisStatusCode")
            .SelectOptionAsync("UNKNOWN");
        await page.Locator("#Input_ExternalReference")
            .FillAsync("BROWSER-OPENING-REFERENCE-FUND");
        await page.Locator("#Input_Note")
            .FillAsync("Synthetic browser opening for two fund lots.");

        await page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions
                {
                    Name = "Açılışı incele",
                    Exact = true
                })
            .ClickAsync();
        await page.Locator(".validation-summary").WaitForAsync();
        Assert.Contains(
            "tam eşleşmelidir",
            await page.Locator(".validation-summary").InnerTextAsync());
        Assert.Equal(
            "#Input_Lots",
            await page.Locator(".validation-summary a").GetAttributeAsync("href"));
        Assert.Equal(1, await page.Locator("#Input_Lots").CountAsync());
        Assert.Equal(0, await page.Locator("#opening-review-heading").CountAsync());

        await page.Locator("#Input_Lots_1__Quantity").FillAsync("23,456788");
        var fundReceipt = await ReviewAndPostOpeningAsync(page);
        Assert.Contains(
            "123,456789 fon birimi",
            await page.Locator("main").InnerTextAsync());

        await page.GetByRole(
                AriaRole.Link,
                new PageGetByRoleOptions
                {
                    Name = "Başka bir açılış başlat",
                    Exact = true
                })
            .ClickAsync();
        await OpenReferenceCreateAsync(page);
        await page.Locator("#AssetInput_Code").FillAsync("BROWSER_EQUITY");
        await page.Locator("#AssetInput_Name").FillAsync("Browser Test Equity");
        await page.Locator("#AssetInput_TypeCode").SelectOptionAsync("EQUITY");
        await page.Locator("#AssetInput_BaseCurrencyCode").SelectOptionAsync("TRY");
        await page.Locator("#AssetInput_LotTrackingModeCode")
            .SelectOptionAsync("REQUIRED");
        await page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions
                {
                    Name = "Varlığı oluştur ve kullan",
                    Exact = true
                })
            .ClickAsync();
        await page.Locator("#Input_Quantity").FillAsync("17");
        await page.Locator("#Input_Lots_0__Quantity").FillAsync("17");
        await page.Locator("#Input_Lots_0__CostBasisStatusCode")
            .SelectOptionAsync("UNKNOWN");
        await page.Locator("#Input_ExternalReference")
            .FillAsync("BROWSER-OPENING-REFERENCE-EQUITY");
        await page.Locator("#Input_Note")
            .FillAsync("Synthetic browser opening for equity evidence.");
        var equityReceipt = await ReviewAndPostOpeningAsync(page);
        Assert.Contains(
            "17 pay",
            await page.Locator("main").InnerTextAsync());

        await page.GetByRole(
                AriaRole.Link,
                new PageGetByRoleOptions
                {
                    Name = "Başka bir açılış başlat",
                    Exact = true
                })
            .ClickAsync();
        await OpenReferenceCreateAsync(page);
        await page.Locator("#AccountInput_Code").FillAsync("BROWSER_VAULT");
        await page.Locator("#AccountInput_Name").FillAsync("Browser Test Vault");
        await page.Locator("#AccountInput_TypeCode")
            .SelectOptionAsync("PHYSICAL_VAULT");
        await page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions
                {
                    Name = "Hesabı oluştur ve kullan",
                    Exact = true
                })
            .ClickAsync();
        await OpenReferenceCreateAsync(page);
        await page.Locator("#AssetInput_Code").FillAsync("BROWSER_GOLD");
        await page.Locator("#AssetInput_Name").FillAsync("Browser Test Gold");
        await page.Locator("#AssetInput_TypeCode")
            .SelectOptionAsync("PHYSICAL_GOLD");
        await page.Locator("#AssetInput_BaseCurrencyCode").SelectOptionAsync("TRY");
        await page.Locator("#AssetInput_LotTrackingModeCode")
            .SelectOptionAsync("REQUIRED");
        await page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions
                {
                    Name = "Varlığı oluştur ve kullan",
                    Exact = true
                })
            .ClickAsync();
        await SelectOptionContainingAsync(
            page.Locator("#Input_AccountId"),
            "BROWSER_VAULT");
        await page.Locator("#Input_Quantity").FillAsync("25,25");
        await page.Locator("#Input_Lots_0__Quantity").FillAsync("25,25");
        await page.Locator("#Input_Lots_0__CostBasisStatusCode")
            .SelectOptionAsync("UNKNOWN");
        await page.Locator("#Input_Lots_0__FinenessChoiceCode")
            .SelectOptionAsync("22K_916");
        await page.Locator("#Input_Lots_0__PieceCount").FillAsync("2");
        await page.Locator("#Input_Lots_0__Hallmark")
            .FillAsync("BROWSER-916");
        await page.Locator("#Input_Lots_0__Note")
            .FillAsync("Two synthetic matching browser bracelets.");
        await page.Locator("#Input_ExternalReference")
            .FillAsync("BROWSER-OPENING-REFERENCE-GOLD");
        await page.Locator("#Input_Note")
            .FillAsync("Synthetic browser opening for physical inventory.");
        var goldReceipt = await ReviewAndPostOpeningAsync(page);
        Assert.Contains(
            "23,129 g",
            await page.Locator("main").InnerTextAsync());

        await GoToAsync(page, baseAddress, equityReceipt);
        await page.GetByRole(
                AriaRole.Link,
                new PageGetByRoleOptions
                {
                    Name = "Bu açılışı ters kaydet",
                    Exact = true
                })
            .ClickAsync();
        Assert.Contains(
            "Uygun — zıt etkiler",
            await page.Locator("main").InnerTextAsync());
        await page.Locator("#Input_Reason")
            .FillAsync("Synthetic browser correction for equity evidence.");
        await page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions
                {
                    Name = "Kalıcı ters kaydı oluştur",
                    Exact = true
                })
            .ClickAsync();
        await page.Locator("#receipt-heading").WaitForAsync();

        using (var readbackClient = new HttpClient { BaseAddress = baseAddress })
        {
            var equityTransactionId = Guid.Parse(
                equityReceipt.Split('/').Last());
            var detail = await readbackClient.GetFromJsonAsync<
                LedgerTransactionResponse>(
                $"/api/ledger/transactions/{equityTransactionId:D}");
            Assert.NotNull(detail);
            Assert.NotNull(detail.ReversedByTransactionId);
        }

        await page.ReloadAsync(
            new PageReloadOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });
        Assert.Equal(
            1,
            await page.GetByRole(
                    AriaRole.Link,
                    new PageGetByRoleOptions
                    {
                        Name = "Ters kayıt işlemini aç",
                        Exact = true
                    })
                .CountAsync());

        await page.GetByRole(
                AriaRole.Link,
                new PageGetByRoleOptions
                {
                    Name = "Başka bir açılış başlat",
                    Exact = true
                })
            .ClickAsync();
        await SelectOptionContainingAsync(
            page.Locator("#Input_AssetId"),
            "BROWSER_EQUITY");
        await page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions
                {
                    Name = "Varlık seçimini uygula",
                    Exact = true
                })
            .ClickAsync();
        await page.Locator("#Input_Quantity").FillAsync("18");
        await page.Locator("#Input_Lots_0__Quantity").FillAsync("18");
        await page.Locator("#Input_Lots_0__CostBasisStatusCode")
            .SelectOptionAsync("UNKNOWN");
        await page.Locator("#Input_Note")
            .FillAsync("Synthetic browser corrected equity opening.");
        var correctedEquityReceipt = await ReviewAndPostOpeningAsync(page);
        Assert.Contains("18 pay", await page.Locator("main").InnerTextAsync());

        await page.ReloadAsync(
            new PageReloadOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });
        Assert.Equal(
            correctedEquityReceipt,
            new Uri(page.Url).AbsolutePath);
        Assert.Equal(1, await page.Locator("#receipt-heading").CountAsync());

        await page.GetByRole(
                AriaRole.Link,
                new PageGetByRoleOptions
                {
                    Name = "Tam işlem ayrıntısını aç",
                    Exact = true
                })
            .ClickAsync();
        await page.GoBackAsync(
            new PageGoBackOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });
        Assert.Equal(
            correctedEquityReceipt,
            new Uri(page.Url).AbsolutePath);

        await workspace.StopHostAsync();
        baseAddress = await workspace.StartHostAsync();

        foreach (var receiptPath in new[]
                 {
                     cashReceipt,
                     fundReceipt,
                     goldReceipt,
                     correctedEquityReceipt
                 })
        {
            await GoToAsync(page, baseAddress, receiptPath);
            Assert.Equal(1, await page.Locator("#receipt-heading").CountAsync());
        }

        return baseAddress;
    }

    /// <summary>
    /// The ordinary monthly path: cash arrives, two purchases build lots, and
    /// one sale consumes them oldest first.
    /// </summary>
    /// <remarks>
    /// This runs after the opening cutover, so the fund already holds two
    /// undated opening lots with no recorded cost. Selling across all four
    /// lots is what proves the mixed result is reported honestly rather than
    /// treating the missing cost as zero.
    /// </remarks>
    private static async Task<Uri> RunFundLifecycleAsync(
        IPage page,
        BrowserTestWorkspace workspace,
        Uri baseAddress,
        JourneyOptions options)
    {
        await GoToAsync(page, baseAddress, "/record");
        await AssertPageFrameAsync(page);
        await AssertResponsiveReflowAsync(page, options);

        // The hub names each workflow rather than offering one generic form.
        foreach (var destination in new[]
                 {
                     "/record/contribution",
                     "/record/fund-purchase",
                     "/record/fund-sale",
                     "/record/opening-balance"
                 })
        {
            Assert.Equal(
                1,
                await page.Locator($"a[href=\"{destination}\"]").CountAsync());
        }

        await GoToAsync(page, baseAddress, "/record/contribution");
        await AssertPageFrameAsync(page);
        await SelectOptionContainingAsync(
            page.Locator("#Input_AccountId"),
            "ANA_HESAP");
        await SelectOptionContainingAsync(
            page.Locator("#Input_CashAssetId"),
            "TRY_NAKIT");
        await page.Locator("#Input_Amount").FillAsync("5000");
        await page.Locator("#Input_CategoryCode").SelectOptionAsync("SALARY");
        await page.Locator("#Input_Note")
            .FillAsync("Synthetic browser monthly contribution.");
        await ClickFundButtonAsync(page, "Gözden geçir");
        Assert.Contains(
            "5.000,00 TRY",
            await page.Locator("main").InnerTextAsync());
        await ClickFundButtonAsync(page, "Nakit girişini kaydet");
        await page.Locator("main").WaitForAsync();

        var firstPurchase = await RecordFundPurchaseAsync(
            page,
            baseAddress,
            quantity: "10",
            unitPrice: "10",
            consideration: "100",
            executionDate: "2026-09-08",
            reference: "BROWSER-PURCHASE-1");

        Assert.Contains(
            "Açılan lot",
            await page.Locator("main").InnerTextAsync());

        var secondPurchase = await RecordFundPurchaseAsync(
            page,
            baseAddress,
            quantity: "20",
            unitPrice: "12",
            consideration: "240",
            executionDate: "2026-09-09",
            reference: "BROWSER-PURCHASE-2");

        Assert.NotEqual(firstPurchase, secondPurchase);

        /*
         * Sell across every lot the household holds: both undated opening
         * lots first, then both purchases. The undated lots carry no recorded
         * cost, so the result must come back partially known.
         */
        await GoToAsync(page, baseAddress, "/record/fund-sale");
        await AssertPageFrameAsync(page);
        await FillFundTradeScopeAsync(page);
        await page.Locator("#Input_Quantity").FillAsync("140");
        await page.Locator("#Input_UnitPrice").FillAsync("15");
        await page.Locator("#Input_CashConsideration").FillAsync("2100");
        await page.Locator("#Input_ExecutionDate").FillAsync("2026-09-10");
        await page.Locator("#Input_ExternalReference")
            .FillAsync("BROWSER-SALE-1");
        await page.Locator("#Input_Note")
            .FillAsync("Synthetic browser partial liquidation.");
        await ClickFundButtonAsync(page, "Gözden geçir");
        await page.Locator("#fund-sale-review-heading, .validation-summary")
            .First
            .WaitForAsync();

        if (await page.Locator("#fund-sale-review-heading").CountAsync() != 1)
        {
            throw new InvalidOperationException(
                "Fund sale review failed: "
                + string.Join(
                    " | ",
                    await page.Locator(".validation-summary")
                        .AllInnerTextsAsync()));
        }

        var reviewText = await page.Locator("main").InnerTextAsync();

        Assert.Contains("Kullanılacak lotlar", reviewText);
        Assert.Contains("PARTIALLY_KNOWN", reviewText);
        Assert.Contains("ADR009_CUMULATIVE_ROUND_HALF_TO_EVEN_V1", reviewText);
        Assert.Contains("yalnızca maliyeti bilinen kısmı", reviewText);

        // Four lots: two opening, two purchases.
        Assert.Equal(
            4,
            await page.Locator(
                    "input[name^=\"Input.ReviewedPlan\"][name$=\"AssetLotId\"]")
                .CountAsync());

        await ClickFundButtonAsync(page, "Satışı kaydet");
        await page.Locator("#fund-receipt-heading, .validation-summary")
            .First
            .WaitForAsync();

        if (await page.Locator("#fund-receipt-heading").CountAsync() != 1)
        {
            throw new InvalidOperationException(
                "Fund sale post failed: "
                + string.Join(
                    " | ",
                    await page.Locator(".validation-summary")
                        .AllInnerTextsAsync()));
        }

        var saleReceipt = new Uri(page.Url).AbsolutePath;
        var receiptText = await page.Locator("main").InnerTextAsync();

        Assert.Contains("Kullanılan lotlar", receiptText);
        Assert.Contains("PARTIALLY_KNOWN", receiptText);
        Assert.Contains("13,456789 fon birimi", receiptText);

        // Refreshing a receipt must not record anything a second time.
        await page.ReloadAsync(
            new PageReloadOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });
        Assert.Equal(saleReceipt, new Uri(page.Url).AbsolutePath);
        Assert.Equal(
            1,
            await page.Locator("#fund-receipt-heading").CountAsync());

        await RunFundCorrectionAsync(
            page,
            baseAddress,
            saleReceipt,
            options);

        // Everything survives a restart, because nothing is held in memory.
        await workspace.StopHostAsync();
        baseAddress = await workspace.StartHostAsync();

        foreach (var receiptPath in new[]
                 {
                     firstPurchase,
                     secondPurchase,
                     saleReceipt
                 })
        {
            await GoToAsync(page, baseAddress, receiptPath);
            Assert.Equal(
                1,
                await page.Locator("#fund-receipt-heading").CountAsync());
        }

        // The reversed sale still reads back, and says it is not effective.
        Assert.Contains(
            "ters kayıtla iptal edilmiş",
            await page.Locator("main").InnerTextAsync());

        // The host moved to a new port on restart; the caller needs it.
        return baseAddress;
    }

    /// <summary>
    /// Records, moves, corrects, restarts, and independently reads back exact
    /// physical-gold custody using only the reviewed Razor write workflows.
    /// </summary>
    private static async Task<Uri> RunPhysicalGoldLifecycleAsync(
        IPage page,
        BrowserTestWorkspace workspace,
        Uri baseAddress,
        JourneyOptions options)
    {
        await CreateGoldReferencesAsync(page, baseAddress);

        var bracelet = await RecordPhysicalGoldPurchaseAsync(
            page,
            baseAddress,
            grossWeight: "10",
            finenessCode: "916",
            pieceCount: "1",
            unitPrice: "95",
            consideration: "1000",
            executionDate: "2026-09-12",
            reference: "BROWSER-GOLD-PURCHASE-BRACELET",
            hallmark: "BROWSER-916",
            certificate: "BROWSER-CERTIFICATE-1",
            lotNote: "One synthetic 22K bracelet.",
            includePurchaseCosts: true,
            exerciseKeyboardSubmission: true);

        var group = await RecordPhysicalGoldPurchaseAsync(
            page,
            baseAddress,
            grossWeight: "20",
            finenessCode: "750",
            pieceCount: "2",
            unitPrice: "120",
            consideration: "2400",
            executionDate: "2026-09-13",
            reference: "BROWSER-GOLD-PURCHASE-GROUP",
            hallmark: "BROWSER-750",
            certificate: "BROWSER-CERTIFICATE-2",
            lotNote: "Two synthetic homogeneous gold pieces.",
            includePurchaseCosts: false,
            exerciseKeyboardSubmission: false);

        Assert.NotEqual(bracelet.ReceiptPath, group.ReceiptPath);
        Assert.NotEqual(bracelet.AssetLotId, group.AssetLotId);

        var saleReceipt = await RecordPhysicalGoldSaleAsync(
            page,
            baseAddress,
            group.AssetLotId,
            grossWeight: "8",
            pieceCount: "1",
            unitPrice: "150",
            consideration: "1200",
            executionDate: "2026-09-14",
            reference: "BROWSER-GOLD-SALE-ORIGINAL");

        var transferReceipt = await RecordPhysicalGoldTransferAsync(
            page,
            baseAddress,
            bracelet.AssetLotId,
            grossWeight: "10",
            pieceCount: "1",
            executionDate: "2026-09-14");

        await GoToAsync(page, baseAddress, saleReceipt);
        await AssertPageFrameAsync(page);
        await AssertResponsiveReflowAsync(page, options);
        await page.GetByRole(
                AriaRole.Link,
                new PageGetByRoleOptions
                {
                    Name = "Ters kayıt uygunluğunu incele",
                    Exact = true
                })
            .ClickAsync();
        await page.Locator("#gold-reverse-heading").WaitForAsync();
        Assert.Contains(
            "Uygun",
            await page.Locator("main").InnerTextAsync());

        // The correction submission is exercised by keyboard with JavaScript off.
        await page.Locator("#Input_Reason")
            .FillAsync("Synthetic browser correction for the physical-gold sale.");
        await TabToAsync(page, "button[data-final-submit]");
        await page.Keyboard.PressAsync("Enter");
        await page.Locator("#gold-receipt-heading").WaitForAsync();
        Assert.Contains(
            "ayrı bir ters kayıtla düzeltilmiş",
            await page.Locator("main").InnerTextAsync());

        await page.GetByRole(
                AriaRole.Link,
                new PageGetByRoleOptions
                {
                    Name = "Ayrı bir düzeltilmiş işlem başlat",
                    Exact = true
                })
            .ClickAsync();
        await page.Locator("#gold-sale-heading").WaitForAsync();

        var correctedSaleReceipt = await RecordPhysicalGoldSaleAsync(
            page,
            baseAddress,
            group.AssetLotId,
            grossWeight: "6",
            pieceCount: "1",
            unitPrice: "150",
            consideration: "900",
            executionDate: "2026-09-15",
            reference: "BROWSER-GOLD-SALE-CORRECTED",
            navigate: false);

        await page.ReloadAsync(
            new PageReloadOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });
        Assert.Equal(
            correctedSaleReceipt,
            new Uri(page.Url).AbsolutePath);
        Assert.Equal(1, await page.Locator("#gold-receipt-heading").CountAsync());

        await workspace.StopHostAsync();
        baseAddress = await workspace.StartHostAsync();

        foreach (var receiptPath in new[]
                 {
                     bracelet.ReceiptPath,
                     group.ReceiptPath,
                     saleReceipt,
                     transferReceipt,
                     correctedSaleReceipt
                 })
        {
            await GoToAsync(page, baseAddress, receiptPath);
            Assert.Equal(
                1,
                await page.Locator("#gold-receipt-heading").CountAsync());
        }

        await AssertPhysicalGoldReadbackAsync(
            baseAddress,
            bracelet,
            group,
            saleReceipt,
            transferReceipt,
            correctedSaleReceipt);

        return baseAddress;
    }

    private static async Task CreateGoldReferencesAsync(
        IPage page,
        Uri baseAddress)
    {
        await GoToAsync(page, baseAddress, "/record/opening-balance");
        await OpenReferenceCreateAsync(page);
        await page.Locator("#InstitutionInput_Code")
            .FillAsync("BROWSER_JEWELER");
        await page.Locator("#InstitutionInput_Name")
            .FillAsync("Browser Test Jeweler");
        await page.Locator("#InstitutionInput_TypeCode")
            .SelectOptionAsync("JEWELER");
        await page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions
                {
                    Name = "Kurumu oluştur",
                    Exact = true
                })
            .ClickAsync();

        await OpenReferenceCreateAsync(page);
        await page.Locator("#AccountInput_Code")
            .FillAsync("BROWSER_SECOND_VAULT");
        await page.Locator("#AccountInput_Name")
            .FillAsync("Browser Test Second Vault");
        await page.Locator("#AccountInput_TypeCode")
            .SelectOptionAsync("PHYSICAL_VAULT");
        await page.Locator("#AccountInput_OpenedOn")
            .FillAsync("2026-01-01");
        await page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions
                {
                    Name = "Hesabı oluştur ve kullan",
                    Exact = true
                })
            .ClickAsync();
        Assert.Contains(
            "BROWSER_SECOND_VAULT",
            await page.Locator("#Input_AccountId option:checked").InnerTextAsync());
    }

    private static async Task<GoldBrowserReceipt>
        RecordPhysicalGoldPurchaseAsync(
            IPage page,
            Uri baseAddress,
            string grossWeight,
            string finenessCode,
            string pieceCount,
            string unitPrice,
            string consideration,
            string executionDate,
            string reference,
            string hallmark,
            string certificate,
            string lotNote,
            bool includePurchaseCosts,
            bool exerciseKeyboardSubmission)
    {
        await GoToAsync(page, baseAddress, "/record/physical-gold-purchase");
        await FillPhysicalGoldTradeScopeAsync(page);
        await page.Locator("#Input_FinenessChoice")
            .SelectOptionAsync(finenessCode);
        await page.Locator("#Input_PieceCount").FillAsync(pieceCount);
        await page.Locator("#Input_CashConsideration")
            .FillAsync(consideration);
        await page.Locator("#Input_ExecutionDate").FillAsync(executionDate);
        await page.Locator("#Input_ExternalReference").FillAsync(reference);
        await page.Locator("#Input_Note")
            .FillAsync("Synthetic browser physical-gold purchase evidence.");
        await OpenGoldAdvancedAsync(page);
        await page.Locator("#Input_UnitPrice").FillAsync(unitPrice);
        await page.Locator("#Input_Hallmark").FillAsync(hallmark);
        await page.Locator("#Input_CertificateReference")
            .FillAsync(certificate);
        await page.Locator("#Input_LotNote").FillAsync(lotNote);

        if (includePurchaseCosts)
        {
            await page.Locator("#Input_Costs_0__TypeCode")
                .SelectOptionAsync("MAKING_CHARGE");
            await page.Locator("#Input_Costs_0__TreatmentCode")
                .SelectOptionAsync("INCLUDED_IN_CONSIDERATION");
            await page.Locator("#Input_Costs_0__Amount").FillAsync("50");
            await ClickGoldButtonAsync(page, "Bir masraf daha ekle");
            await page.Locator("#Input_Costs_1__TypeCode").WaitForAsync();
            await page.Locator("#Input_Costs_1__TypeCode")
                .SelectOptionAsync("COMMISSION");
            await page.Locator("#Input_Costs_1__TreatmentCode")
                .SelectOptionAsync("ADDITIONAL_CASH_OUTFLOW");
            await page.Locator("#Input_Costs_1__Amount").FillAsync("10");
        }

        await page.Locator("#Input_GrossWeight").FillAsync(grossWeight);
        if (exerciseKeyboardSubmission)
        {
            await TabToAsync(page, "button:has-text(\"Etkiyi gözden geçir\")");
            await page.Keyboard.PressAsync("Enter");
        }
        else
        {
            await ClickGoldButtonAsync(page, "Etkiyi gözden geçir");
        }
        await page.Locator("#gold-purchase-review-heading, .validation-summary")
            .First
            .WaitForAsync();
        Assert.Equal(
            1,
            await page.Locator("#gold-purchase-review-heading").CountAsync());

        var reviewText = await page.Locator("main").InnerTextAsync();
        Assert.Contains("Kesin etkiyi gözden geçirin", reviewText);
        Assert.Contains("BROWSER_GOLD", reviewText);
        if (finenessCode == "916")
        {
            Assert.Contains("9,16 gram saf altın", reviewText);
            Assert.Contains("MAKING_CHARGE", reviewText);
            Assert.Contains("COMMISSION", reviewText);
        }
        else
        {
            Assert.Contains("15 gram saf altın", reviewText);
        }

        await ClickGoldButtonAsync(page, "Altın alımını kaydet");
        await page.Locator("#gold-receipt-heading, .validation-summary")
            .First
            .WaitForAsync();
        Assert.Equal(1, await page.Locator("#gold-receipt-heading").CountAsync());
        var receiptPath = new Uri(page.Url).AbsolutePath;
        var lotIdText = await page
            .Locator("#gold-receipt-movements-heading")
            .Locator("xpath=following::table[1]//tbody/tr[1]/td[1]/code")
            .InnerTextAsync();
        var assetLotId = Guid.Parse(lotIdText.Trim());

        if (exerciseKeyboardSubmission)
        {
            // Returning to the reviewed POST and submitting the same hidden
            // idempotency key must replay the original receipt, not add a lot.
            await page.GoBackAsync(
                new PageGoBackOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded
                });
            await page.Locator("#gold-purchase-review-heading").WaitForAsync();
            await ClickGoldButtonAsync(page, "Altın alımını kaydet");
            await page.Locator("#gold-receipt-heading").WaitForAsync();
            Assert.Equal(receiptPath, new Uri(page.Url).AbsolutePath);
        }

        return new GoldBrowserReceipt(
            receiptPath,
            assetLotId);
    }

    private static async Task<string> RecordPhysicalGoldSaleAsync(
        IPage page,
        Uri baseAddress,
        Guid assetLotId,
        string grossWeight,
        string pieceCount,
        string unitPrice,
        string consideration,
        string executionDate,
        string reference,
        bool navigate = true)
    {
        if (navigate)
        {
            await GoToAsync(page, baseAddress, "/record/physical-gold-sale");
        }

        await FillPhysicalGoldTradeScopeAsync(page);
        await ClickGoldButtonAsync(page, "Bu kapsamdaki lotları getir");
        var lot = page.Locator($"[data-gold-lot=\"{assetLotId:D}\"]");
        Assert.Equal(1, await lot.CountAsync());
        await lot.Locator("input[name$='.GrossWeight']")
            .FillAsync(grossWeight);
        await lot.Locator("input[name$='.PieceCount']")
            .FillAsync(pieceCount);
        await page.Locator("#Input_CashConsideration")
            .FillAsync(consideration);
        await page.Locator("#Input_ExecutionDate").FillAsync(executionDate);
        await page.Locator("#Input_ExternalReference").FillAsync(reference);
        await page.Locator("#Input_Note")
            .FillAsync("Synthetic browser selected-lot gold sale.");
        await OpenGoldAdvancedAsync(page);
        await page.Locator("#Input_UnitPrice").FillAsync(unitPrice);

        await ClickGoldButtonAsync(page, "Etkiyi gözden geçir");
        await page.Locator("#gold-sale-review-heading, .validation-summary")
            .First
            .WaitForAsync();
        Assert.Equal(1, await page.Locator("#gold-sale-review-heading").CountAsync());
        Assert.Equal(
            1,
            await page.Locator(
                    "#gold-sale-review-heading ~ table:first-of-type tbody tr")
                .CountAsync());
        var reviewText = await page.Locator("main").InnerTextAsync();
        Assert.Contains(assetLotId.ToString("D"), reviewText);
        Assert.Contains("ADR009_CUMULATIVE_ROUND_HALF_TO_EVEN_V1", reviewText);
        Assert.Contains("KNOWN", reviewText);

        await ClickGoldButtonAsync(page, "Altın satışını kaydet");
        await page.Locator("#gold-receipt-heading, .validation-summary")
            .First
            .WaitForAsync();
        Assert.Equal(1, await page.Locator("#gold-receipt-heading").CountAsync());
        return new Uri(page.Url).AbsolutePath;
    }

    private static async Task<string> RecordPhysicalGoldTransferAsync(
        IPage page,
        Uri baseAddress,
        Guid assetLotId,
        string grossWeight,
        string pieceCount,
        string executionDate)
    {
        await GoToAsync(page, baseAddress, "/record/physical-gold-transfer");
        await SelectOptionContainingAsync(
            page.Locator("#Input_SourcePortfolioId"),
            "Browser Test Portfolio");
        await SelectOptionContainingAsync(
            page.Locator("#Input_DestinationPortfolioId"),
            "Browser Test Portfolio");
        await SelectOptionContainingAsync(
            page.Locator("#Input_SourceGoldAccountId"),
            "BROWSER_VAULT");
        await SelectOptionContainingAsync(
            page.Locator("#Input_DestinationGoldAccountId"),
            "BROWSER_SECOND_VAULT");
        await SelectOptionContainingAsync(
            page.Locator("#Input_GoldAssetId"),
            "BROWSER_GOLD");
        await ClickGoldButtonAsync(page, "Bu kapsamdaki lotları getir");

        var lot = page.Locator($"[data-gold-lot=\"{assetLotId:D}\"]");
        Assert.Equal(1, await lot.CountAsync());
        await lot.Locator("input[name$='.GrossWeight']")
            .FillAsync(grossWeight);
        await lot.Locator("input[name$='.PieceCount']")
            .FillAsync(pieceCount);
        await page.Locator("#Input_ExecutionDate").FillAsync(executionDate);
        await page.Locator("#Input_ExternalReference")
            .FillAsync("BROWSER-GOLD-TRANSFER");
        await page.Locator("#Input_Note")
            .FillAsync("Synthetic browser exact custody transfer.");
        await ClickGoldButtonAsync(page, "Etkiyi gözden geçir");
        await page.Locator("#gold-transfer-review-heading, .validation-summary")
            .First
            .WaitForAsync();
        Assert.Equal(
            1,
            await page.Locator("#gold-transfer-review-heading").CountAsync());
        var reviewText = await page.Locator("main").InnerTextAsync();
        Assert.Contains(assetLotId.ToString("D"), reviewText);
        Assert.Contains("Browser Test Vault", reviewText);
        Assert.Contains("Browser Test Second Vault", reviewText);
        Assert.Contains("Değişmez", reviewText);

        await ClickGoldButtonAsync(page, "Saklama transferini kaydet");
        await page.Locator("#gold-receipt-heading, .validation-summary")
            .First
            .WaitForAsync();
        Assert.Equal(1, await page.Locator("#gold-receipt-heading").CountAsync());
        var receiptText = await page.Locator("main").InnerTextAsync();
        Assert.Contains("+10 gram — artış", receiptText);
        Assert.Contains("-10 gram — azalış", receiptText);
        return new Uri(page.Url).AbsolutePath;
    }

    private static async Task FillPhysicalGoldTradeScopeAsync(IPage page)
    {
        await SelectOptionContainingAsync(
            page.Locator("#Input_PortfolioId"),
            "Browser Test Portfolio");
        await SelectOptionContainingAsync(
            page.Locator("#Input_GoldAccountId"),
            "BROWSER_VAULT");
        await SelectOptionContainingAsync(
            page.Locator("#Input_CashAccountId"),
            "ANA_HESAP");
        await SelectOptionContainingAsync(
            page.Locator("#Input_GoldAssetId"),
            "BROWSER_GOLD");
        await SelectOptionContainingAsync(
            page.Locator("#Input_CashAssetId"),
            "TRY_NAKIT");
        await SelectOptionContainingAsync(
            page.Locator("#Input_CounterpartyInstitutionId"),
            "BROWSER_JEWELER");
    }

    private static async Task OpenGoldAdvancedAsync(IPage page)
    {
        var details = page.Locator("details.panel");
        if (await details.GetAttributeAsync("open") is null)
        {
            await details.Locator("summary").ClickAsync();
        }
    }

    private static Task ClickGoldButtonAsync(IPage page, string name)
        => page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions
                {
                    Name = name,
                    Exact = true
                })
            .ClickAsync();

    private static async Task AssertPhysicalGoldReadbackAsync(
        Uri baseAddress,
        GoldBrowserReceipt bracelet,
        GoldBrowserReceipt group,
        string originalSaleReceipt,
        string transferReceipt,
        string correctedSaleReceipt)
    {
        using var client = new HttpClient { BaseAddress = baseAddress };
        var households = await client.GetFromJsonAsync<
            NavigationPageResponse<HouseholdNavigationResponse>>(
            "/api/households?pageSize=100");
        var household = Assert.Single(households!.Items);
        var custody = await client.GetFromJsonAsync<
            PhysicalGoldCustodyInventoryResponse>(
            $"/api/households/{household.HouseholdId:D}/physical-gold/custody");
        Assert.NotNull(custody);

        var braceletPosition = Assert.Single(
            custody.Items,
            x => x.AssetLotId == bracelet.AssetLotId);
        Assert.Equal("Browser Test Second Vault", braceletPosition.AccountName);
        Assert.Equal(10_00000000L, braceletPosition.GrossWeightRawE8);
        Assert.Equal(1, braceletPosition.PieceCount);
        Assert.Equal(9.16m, braceletPosition.FineWeightGrams);

        var groupPosition = Assert.Single(
            custody.Items,
            x => x.AssetLotId == group.AssetLotId);
        Assert.Contains("Vault", groupPosition.AccountName, StringComparison.Ordinal);
        Assert.Equal(14_00000000L, groupPosition.GrossWeightRawE8);
        Assert.Equal(1, groupPosition.PieceCount);
        Assert.Equal(10.5m, groupPosition.FineWeightGrams);

        var braceletPurchase = await GetGoldVerificationAsync(
            client,
            household.HouseholdId,
            bracelet.ReceiptPath);
        var groupPurchase = await GetGoldVerificationAsync(
            client,
            household.HouseholdId,
            group.ReceiptPath);
        var originalSale = await GetGoldVerificationAsync(
            client,
            household.HouseholdId,
            originalSaleReceipt);
        var transfer = await GetGoldVerificationAsync(
            client,
            household.HouseholdId,
            transferReceipt);
        var correctedSale = await GetGoldVerificationAsync(
            client,
            household.HouseholdId,
            correctedSaleReceipt);

        Assert.Equal(-101_000, braceletPurchase.Economics.NetCashEffectMinorUnits);
        Assert.Equal(101_000, braceletPurchase.Economics.AcquisitionLotCostMinorUnits);
        Assert.Equal(-240_000, groupPurchase.Economics.NetCashEffectMinorUnits);
        Assert.Equal(120_000, originalSale.Economics.NetCashEffectMinorUnits);
        Assert.NotNull(originalSale.ReversedByTransactionId);
        Assert.False(originalSale.RealizedCost!.SourceSaleIsEffective);
        Assert.Equal(0, transfer.Economics.NetCashEffectMinorUnits);
        Assert.Equal(2, transfer.Allocations.Count);
        Assert.Equal(
            0,
            transfer.Allocations.Sum(x => x.GrossWeightDeltaRawE8));
        Assert.Equal(0, transfer.Allocations.Sum(x => x.PieceDelta));
        Assert.Equal(90_000, correctedSale.Economics.NetCashEffectMinorUnits);
        Assert.Equal(72_000, Assert.Single(correctedSale.RealizedCost!.KnownAmounts).MinorUnits);
        Assert.True(correctedSale.RealizedCost.SourceSaleIsEffective);
    }

    private static async Task<PhysicalGoldActivityVerificationResponse>
        GetGoldVerificationAsync(
            HttpClient client,
            Guid householdId,
            string receiptPath)
    {
        var transactionId = Guid.Parse(
            receiptPath.Split('/', StringSplitOptions.RemoveEmptyEntries)[^2]);
        var response = await client.GetFromJsonAsync<
            PhysicalGoldActivityVerificationResponse>(
            $"/api/households/{householdId:D}/ledger/physical-gold-activities/{transactionId:D}/verification");
        return Assert.IsType<PhysicalGoldActivityVerificationResponse>(response);
    }

    private sealed record GoldBrowserReceipt(
        string ReceiptPath,
        Guid AssetLotId);

    /// <summary>
    /// Corrects a posted fund sale from its receipt, without JavaScript.
    /// </summary>
    /// <remarks>
    /// A correction is the one workflow a household reaches for when
    /// something is already wrong, so it has to work in the plainest possible
    /// browser. The original stays Posted, the reversal is a separate record,
    /// and the consumed lot quantities come back.
    /// </remarks>
    private static async Task RunFundCorrectionAsync(
        IPage page,
        Uri baseAddress,
        string saleReceipt,
        JourneyOptions options)
    {
        await GoToAsync(page, baseAddress, saleReceipt);
        await AssertPageFrameAsync(page);
        await AssertResponsiveReflowAsync(page, options);

        await page.GetByRole(
                AriaRole.Link,
                new PageGetByRoleOptions
                {
                    Name = "Bu işlemi ters kayıtla düzelt",
                    Exact = true
                })
            .ClickAsync();

        await page.Locator("#fund-reverse-heading").WaitForAsync();

        var eligibility = await page.Locator("main").InnerTextAsync();

        Assert.Contains("Uygun", eligibility);
        Assert.Contains("Geri verilecek lotlar", eligibility);

        // Posting without a reason must be refused, with focus on the field.
        await ClickFundButtonAsync(page, "Kalıcı ters kaydı oluştur");
        await page.Locator(".validation-summary").WaitForAsync();

        Assert.Equal(
            "#Input_Reason",
            await page.Locator(".validation-summary a")
                .First
                .GetAttributeAsync("href"));

        await page.Locator("#Input_Reason")
            .FillAsync("Synthetic browser correction for the fund sale.");

        await ClickFundButtonAsync(page, "Kalıcı ters kaydı oluştur");
        await page.Locator("#fund-receipt-heading").WaitForAsync();

        // Back on the receipt, the correction is reported, not offered again.
        var corrected = await page.Locator("main").InnerTextAsync();

        Assert.Contains("zaten ters kayıtla düzeltilmiş", corrected);

        Assert.Equal(
            0,
            await page.GetByRole(
                    AriaRole.Link,
                    new PageGetByRoleOptions
                    {
                        Name = "Bu işlemi ters kayıtla düzelt",
                        Exact = true
                    })
                .CountAsync());

        /*
         * The sale consumed one hundred and forty units, so reversing it puts
         * all of them back. A corrected sale is then reviewed afresh against
         * the restored holding.
         */
        await GoToAsync(page, baseAddress, "/record/fund-sale");
        await FillFundTradeScopeAsync(page);
        await page.Locator("#Input_Quantity").FillAsync("120");
        await page.Locator("#Input_UnitPrice").FillAsync("15");
        await page.Locator("#Input_CashConsideration").FillAsync("1800");
        await page.Locator("#Input_ExecutionDate").FillAsync("2026-09-11");
        await page.Locator("#Input_ExternalReference")
            .FillAsync("BROWSER-SALE-CORRECTED");
        await page.Locator("#Input_Note")
            .FillAsync("Synthetic browser corrected liquidation.");

        await ClickFundButtonAsync(page, "Gözden geçir");
        await page.Locator("#fund-sale-review-heading").WaitForAsync();

        await ClickFundButtonAsync(page, "Satışı kaydet");
        await page.Locator("#fund-receipt-heading").WaitForAsync();

        Assert.NotEqual(saleReceipt, new Uri(page.Url).AbsolutePath);
    }

    private static async Task<string> RecordFundPurchaseAsync(
        IPage page,
        Uri baseAddress,
        string quantity,
        string unitPrice,
        string consideration,
        string executionDate,
        string reference)
    {
        await GoToAsync(page, baseAddress, "/record/fund-purchase");
        await FillFundTradeScopeAsync(page);
        await page.Locator("#Input_Quantity").FillAsync(quantity);
        await page.Locator("#Input_UnitPrice").FillAsync(unitPrice);
        await page.Locator("#Input_CashConsideration").FillAsync(consideration);
        await page.Locator("#Input_ExecutionDate").FillAsync(executionDate);
        await page.Locator("#Input_ExternalReference").FillAsync(reference);
        await page.Locator("#Input_Note")
            .FillAsync("Synthetic browser fund purchase.");

        await ClickFundButtonAsync(page, "Gözden geçir");
        await page.Locator("#fund-review-heading, .validation-summary")
            .First
            .WaitForAsync();

        if (await page.Locator("#fund-review-heading").CountAsync() != 1)
        {
            throw new InvalidOperationException(
                "Fund purchase review failed: "
                + string.Join(
                    " | ",
                    await page.Locator(".validation-summary")
                        .AllInnerTextsAsync()));
        }

        await ClickFundButtonAsync(page, "Alımı kaydet");
        await page.Locator("#fund-receipt-heading, .validation-summary")
            .First
            .WaitForAsync();

        if (await page.Locator("#fund-receipt-heading").CountAsync() != 1)
        {
            throw new InvalidOperationException(
                "Fund purchase post failed: "
                + string.Join(
                    " | ",
                    await page.Locator(".validation-summary")
                        .AllInnerTextsAsync()));
        }

        return new Uri(page.Url).AbsolutePath;
    }

    private static async Task FillFundTradeScopeAsync(IPage page)
    {
        await SelectOptionContainingAsync(
            page.Locator("#Input_FundAccountId"),
            "ANA_HESAP");
        await SelectOptionContainingAsync(
            page.Locator("#Input_CashAccountId"),
            "ANA_HESAP");
        await SelectOptionContainingAsync(
            page.Locator("#Input_FundAssetId"),
            "ILK_FON");
        await SelectOptionContainingAsync(
            page.Locator("#Input_CashAssetId"),
            "TRY_NAKIT");
    }

    private static Task ClickFundButtonAsync(IPage page, string name)
        => page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions
                {
                    Name = name,
                    Exact = true
                })
            .ClickAsync();

    private static async Task<string> ReviewAndPostOpeningAsync(IPage page)
    {
        await page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions
                {
                    Name = "Açılışı incele",
                    Exact = true
                })
            .ClickAsync();
        await page.Locator("#opening-review-heading, .validation-summary")
            .First
            .WaitForAsync();
        if (await page.Locator("#opening-review-heading").CountAsync() != 1)
        {
            var validation = await page.Locator(".validation-summary")
                .AllInnerTextsAsync();
            throw new InvalidOperationException(
                "Opening review failed: " + string.Join(" | ", validation));
        }

        await page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions
                {
                    Name = "İncelenen açılışı kalıcı olarak kaydet",
                    Exact = true
                })
            .ClickAsync();
        await page.Locator("#receipt-heading, .validation-summary")
            .First
            .WaitForAsync();
        if (await page.Locator("#receipt-heading").CountAsync() != 1)
        {
            var validation = await page.Locator(".validation-summary")
                .AllInnerTextsAsync();
            throw new InvalidOperationException(
                "Opening post failed: " + string.Join(" | ", validation));
        }

        return new Uri(page.Url).AbsolutePath;
    }

    private static async Task OpenReferenceCreateAsync(IPage page)
    {
        var details = page.Locator("details.reference-create");

        if (await details.GetAttributeAsync("open") is null)
        {
            await details.Locator("summary").ClickAsync();
        }
    }

    private static async Task SelectOptionContainingAsync(
        ILocator select,
        string expectedText)
    {
        var options = select.Locator("option");
        var count = await options.CountAsync();

        for (var index = 0; index < count; index++)
        {
            var option = options.Nth(index);
            var text = await option.InnerTextAsync();

            if (!text.Contains(expectedText, StringComparison.Ordinal))
            {
                continue;
            }

            var value = await option.GetAttributeAsync("value");
            Assert.False(string.IsNullOrWhiteSpace(value));
            await select.SelectOptionAsync(value);
            return;
        }

        throw new InvalidOperationException(
            $"No option containing '{expectedText}' was available.");
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
        var account = Assert.Single(
            accounts!.Items,
            item => string.Equals(
                item.Code,
                "ANA_HESAP",
                StringComparison.Ordinal));
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
        bool ExerciseOpeningCutover,
        int ViewportWidth,
        int ViewportHeight,
        bool ExerciseFundLifecycle = false,
        bool ExercisePhysicalGoldLifecycle = false);
}
