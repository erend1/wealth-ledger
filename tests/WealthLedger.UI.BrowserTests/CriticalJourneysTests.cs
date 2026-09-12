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
                ViewportHeight: 844));

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
        Assert.Equal(
            1,
            await page.Locator(
                ".primary-nav a[href=\"/record/opening-balance\"][aria-current=\"page\"]")
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
        int ViewportHeight);
}
