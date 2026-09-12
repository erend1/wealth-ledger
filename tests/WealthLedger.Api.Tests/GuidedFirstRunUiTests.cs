using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.LocalData;

namespace WealthLedger.Api.Tests;

public sealed partial class GuidedFirstRunUiTests
{
    [Fact]
    public async Task StartupModes_ExposeOnlyTheirAcceptedBoundaryRoutes()
    {
        await AssertRouteExposureAsync(
            ApiTestStartupMode.Blocked,
            expectedPage: "/blocked",
            expectedSetupRedirect: null);

        await AssertRouteExposureAsync(
            ApiTestStartupMode.StorageUninitialized,
            expectedPage: "/setup/storage",
            expectedSetupRedirect: "/setup/storage");

        await AssertRouteExposureAsync(
            ApiTestStartupMode.WorkspaceUninitialized,
            expectedPage: "/setup/workspace",
            expectedSetupRedirect: "/setup/workspace");

        await AssertRouteExposureAsync(
            ApiTestStartupMode.InitialBackupRequired,
            expectedPage: "/setup/backup",
            expectedSetupRedirect: "/setup/backup",
            completionRedirectExpected: true);

        await AssertRouteExposureAsync(
            ApiTestStartupMode.Ready,
            expectedPage: null,
            expectedSetupRedirect: null,
            ledgerExpected: HttpStatusCode.OK,
            readyPagesExpected: true);
    }

    [Fact]
    public async Task BlockedPage_IsReadOnlyAndOmitsPrivateDiagnostics()
    {
        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.Blocked);
        using var client = CreateClient(factory);

        using var getResponse =
            await client.GetAsync("/blocked");
        var html = await getResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Contains("InvalidInputOrConfiguration", html);
        Assert.Contains("salt okunur", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(factory.DatabasePath, html);
        Assert.DoesNotContain(factory.BackupDirectory, html);
        Assert.DoesNotContain("Data Source=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQLite", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", html, StringComparison.OrdinalIgnoreCase);

        using var postResponse =
            await client.PostAsync(
                "/blocked",
                new FormUrlEncodedContent([]));

        Assert.Equal(
            HttpStatusCode.MethodNotAllowed,
            postResponse.StatusCode);
        AssertPrivateDiagnosticsAbsent(factory);

        await AssertMasterCountsAsync(
            factory,
            expectedCoreRows: 0);
    }

    [Fact]
    public async Task StorageGet_ReviewsResolvedPathsWithoutCreatingStorage()
    {
        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.StorageUninitialized);
        using var client = CreateClient(factory);

        using var response =
            await client.GetAsync("/setup/storage");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(factory.DatabasePath, html);
        Assert.Contains(factory.BackupDirectory, html);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.DoesNotContain("name=\"DatabasePath\"", html);
        Assert.DoesNotContain("name=\"BackupDirectory\"", html);
        Assert.DoesNotContain("connection string", html, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(factory.DatabasePath));

        Assert.Equal(
            "default-src 'self'; object-src 'none'; base-uri 'self'; "
            + "form-action 'self'; frame-ancestors 'none'",
            response.Headers.GetValues("Content-Security-Policy").Single());

        using var styleRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/_content/WealthLedger.UI/css/setup.css");
        styleRequest.Headers.AcceptEncoding.ParseAdd("identity");
        using var styleResponse = await client.SendAsync(styleRequest);
        var style = await styleResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, styleResponse.StatusCode);
        Assert.Equal(
            "text/css",
            styleResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "nosniff",
            styleResponse.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(
            "default-src 'self'; object-src 'none'; base-uri 'self'; "
            + "form-action 'self'; frame-ancestors 'none'",
            styleResponse.Headers.GetValues("Content-Security-Policy").Single());
        Assert.DoesNotContain("url(http", style, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StoragePost_WithoutAntiforgery_DoesNotInitialize()
    {
        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.StorageUninitialized);
        using var client = CreateClient(factory);

        using var response = await client.PostAsync(
            "/setup/storage",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["ConfirmInitialization"] = "true"
                }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(File.Exists(factory.DatabasePath));
    }

    [Fact]
    public async Task StoragePost_RequiresServerValidatedPathReview()
    {
        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.StorageUninitialized);
        using var client = CreateClient(factory);
        var token = await GetAntiforgeryTokenAsync(
            client,
            "/setup/storage");

        using var response = await client.PostAsync(
            "/setup/storage",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token
                }));
        var html = WebUtility.HtmlDecode(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("gözden geçirdiğinizi onaylayın", html);
        Assert.False(File.Exists(factory.DatabasePath));
    }

    [Fact]
    public async Task StoragePost_InitializesCreateOnlyStorageAndUsesPrg()
    {
        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.StorageUninitialized);
        using var client = CreateClient(factory);
        var token = await GetAntiforgeryTokenAsync(
            client,
            "/setup/storage");

        using var response = await PostStorageAsync(
            client,
            token);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(
            "/setup/storage",
            response.Headers.Location?.OriginalString);
        Assert.True(File.Exists(factory.DatabasePath));

        using var redirected =
            await client.GetAsync(response.Headers.Location);
        var html = await redirected.Content.ReadAsStringAsync();
        var decodedHtml = WebUtility.HtmlDecode(html);

        Assert.Equal(HttpStatusCode.OK, redirected.StatusCode);
        Assert.Contains("Depolama oluşturuldu", decodedHtml);
        Assert.Contains(
            "yeniden başlat",
            decodedHtml,
            StringComparison.OrdinalIgnoreCase);

        using var workspaceResponse =
            await client.GetAsync("/setup/workspace");
        using var ledgerResponse =
            await client.GetAsync("/api/households");

        Assert.Equal(HttpStatusCode.NotFound, workspaceResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ledgerResponse.StatusCode);
    }

    [Fact]
    public async Task StoragePost_RetryObservesCompatibleCreatedStorage()
    {
        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.StorageUninitialized);
        using var client = CreateClient(factory);
        var token = await GetAntiforgeryTokenAsync(
            client,
            "/setup/storage");

        using var first = await PostStorageAsync(client, token);
        using var retry = await PostStorageAsync(client, token);

        Assert.Equal(HttpStatusCode.Found, first.StatusCode);
        Assert.Equal(HttpStatusCode.Found, retry.StatusCode);

        await using var context = factory.CreateDbContext();
        Assert.Equal(
            6,
            (await context.Database
                .GetAppliedMigrationsAsync())
            .Count());
    }

    [Fact]
    public async Task StoragePost_WhenOwnershipIsBusy_ReturnsSanitizedGuidance()
    {
        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.StorageUninitialized);
        using var client = CreateClient(factory);
        var token = await GetAntiforgeryTokenAsync(
            client,
            "/setup/storage");
        await using var ownership =
            factory.AcquireDatabaseOwnership();

        using var response = await PostStorageAsync(
            client,
            token);
        var html = await response.Content.ReadAsStringAsync();
        var decodedHtml = WebUtility.HtmlDecode(html);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Başka bir yerel veri işlemi", decodedHtml);
        Assert.DoesNotContain("SqliteException", decodedHtml);
        Assert.DoesNotContain(
            "Data Source=",
            decodedHtml,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at WealthLedger", decodedHtml);
        Assert.False(File.Exists(factory.DatabasePath));
        AssertPrivateDiagnosticsAbsent(factory);
    }

    [Fact]
    public async Task WorkspaceGet_RendersHumanInputsWithoutRawIdentifiersOrWrites()
    {
        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.WorkspaceUninitialized);
        using var client = CreateClient(factory);

        using var response =
            await client.GetAsync("/setup/workspace");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Input.HouseholdName", html);
        Assert.Contains("Input.FundAssetName", html);
        Assert.Contains("value=\"TRY\"", html);
        Assert.Contains("value=\"2\"", html);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.DoesNotMatch(
            "name=\"[^\"]*(Guid|Id)\"",
            html);
        Assert.DoesNotContain("RawE8", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MinorUnits", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionString", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MigrationId", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localStorage", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);

        await AssertMasterCountsAsync(
            factory,
            expectedCoreRows: 0);
    }

    [Fact]
    public async Task WorkspacePost_WithoutAntiforgery_DoesNotInitialize()
    {
        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.WorkspaceUninitialized);
        using var client = CreateClient(factory);

        using var response = await client.PostAsync(
            "/setup/workspace",
            new FormUrlEncodedContent(CreateWorkspaceForm()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertMasterCountsAsync(
            factory,
            expectedCoreRows: 0);
    }

    [Fact]
    public async Task WorkspacePost_InvalidInputReturnsSafeValidationAndNoPartialGraph()
    {
        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.WorkspaceUninitialized);
        using var client = CreateClient(factory);
        var token = await GetAntiforgeryTokenAsync(
            client,
            "/setup/workspace");
        var form = CreateWorkspaceForm(token);
        form["Input.HouseholdName"] = string.Empty;
        form["Input.InstitutionTypeCode"] = "NOT_A_TYPE";
        form["Input.FundAssetCode"] = "bad code";

        using var response = await client.PostAsync(
            "/setup/workspace",
            new FormUrlEncodedContent(form));
        var html = await response.Content.ReadAsStringAsync();
        var decodedHtml = WebUtility.HtmlDecode(html);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Bu alan zorunludur", decodedHtml);
        Assert.Contains("Desteklenen bir kurum türü", decodedHtml);
        Assert.Contains("yalnızca A–Z", decodedHtml);
        Assert.DoesNotContain("ArgumentException", decodedHtml);
        Assert.DoesNotContain("SqliteException", decodedHtml);

        await AssertMasterCountsAsync(
            factory,
            expectedCoreRows: 0);
    }

    [Fact]
    public async Task WorkspacePost_EncodesInvalidStableCodeAndDoesNotLogInput()
    {
        const string privateInputMarker =
            "PRIVATE_INPUT_MARKER<script>alert(1)</script>";

        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.WorkspaceUninitialized);
        using var client = CreateClient(factory);
        var token = await GetAntiforgeryTokenAsync(
            client,
            "/setup/workspace");
        var form = CreateWorkspaceForm(token);
        form["Input.InstitutionCode"] = privateInputMarker;

        using var response = await client.PostAsync(
            "/setup/workspace",
            new FormUrlEncodedContent(form));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("PRIVATE_INPUT_MARKER", html);
        Assert.DoesNotContain(
            factory.Logs.Messages,
            message => message.Contains(
                privateInputMarker,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task WorkspacePost_InitializesAtomicallyAndUsesPrg()
    {
        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.WorkspaceUninitialized);
        using var client = CreateClient(factory);
        var token = await GetAntiforgeryTokenAsync(
            client,
            "/setup/workspace");

        using var response = await client.PostAsync(
            "/setup/workspace",
            new FormUrlEncodedContent(
                CreateWorkspaceForm(token)));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(
            "/setup/workspace",
            response.Headers.Location?.OriginalString);

        await AssertMasterCountsAsync(
            factory,
            expectedCoreRows: 8);

        await using (var context = factory.CreateDbContext())
        {
            Assert.Empty(await context.LedgerTransactions.ToListAsync());
            Assert.Empty(await context.AssetLots.ToListAsync());
            Assert.Empty(await context.CommandReceipts.ToListAsync());
        }

        Assert.False(
            Directory.Exists(factory.BackupDirectory)
            && Directory.EnumerateFiles(
                    factory.BackupDirectory,
                    "*.wlbackup",
                    SearchOption.TopDirectoryOnly)
                .Any());

        using var redirected =
            await client.GetAsync(response.Headers.Location);
        var html = await redirected.Content.ReadAsStringAsync();
        var decodedHtml = WebUtility.HtmlDecode(html);

        Assert.Equal(HttpStatusCode.OK, redirected.StatusCode);
        Assert.Contains("Çalışma alanı oluşturuldu", decodedHtml);
        Assert.Contains(
            "yeniden başlat",
            decodedHtml,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "İlk doğrulanmış yedek henüz oluşturulmadı",
            decodedHtml);

        using var ledgerResponse =
            await client.GetAsync("/api/households");
        Assert.Equal(HttpStatusCode.NotFound, ledgerResponse.StatusCode);
    }

    [Fact]
    public async Task WorkspacePost_RetryObservesCompletedAtomicGraph()
    {
        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.WorkspaceUninitialized);
        using var client = CreateClient(factory);
        var token = await GetAntiforgeryTokenAsync(
            client,
            "/setup/workspace");
        var form = CreateWorkspaceForm(token);

        using var first = await client.PostAsync(
            "/setup/workspace",
            new FormUrlEncodedContent(form));
        using var retry = await client.PostAsync(
            "/setup/workspace",
            new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Found, first.StatusCode);
        Assert.Equal(HttpStatusCode.Found, retry.StatusCode);
        await AssertMasterCountsAsync(
            factory,
            expectedCoreRows: 8);
    }

    [Fact]
    public async Task WorkspacePost_WhenConcurrentOwnerExistsReturnsSanitizedBusyResult()
    {
        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.WorkspaceUninitialized);
        using var client = CreateClient(factory);
        var token = await GetAntiforgeryTokenAsync(
            client,
            "/setup/workspace");
        await using var ownership =
            factory.AcquireDatabaseOwnership();

        using var response = await client.PostAsync(
            "/setup/workspace",
            new FormUrlEncodedContent(
                CreateWorkspaceForm(token)));
        var html = await response.Content.ReadAsStringAsync();
        var decodedHtml = WebUtility.HtmlDecode(html);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Başka bir yerel veri işlemi", decodedHtml);
        Assert.DoesNotContain(factory.DatabasePath, decodedHtml);
        Assert.DoesNotContain("SqliteException", decodedHtml);
        Assert.DoesNotContain(
            "Data Source=",
            decodedHtml,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at WealthLedger", decodedHtml);
        AssertPrivateDiagnosticsAbsent(factory);

        await AssertMasterCountsAsync(
            factory,
            expectedCoreRows: 0);
    }

    [Fact]
    public async Task BackupGet_ReviewsStatusWithoutCreatingDirectoryOrPackage()
    {
        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.InitialBackupRequired);

        Assert.False(Directory.Exists(factory.BackupDirectory));

        using var client = CreateClient(factory);
        using var response = await client.GetAsync("/setup/backup");
        var html = WebUtility.HtmlDecode(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(factory.BackupDirectory, html);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.Contains("İlk doğrulanmış yedeği", html);
        Assert.DoesNotContain("name=\"BackupDirectory\"", html);
        Assert.DoesNotContain("localStorage", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Data Source=", html, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(factory.BackupDirectory));
        Assert.Empty(factory.GetBackupPackagePaths());
    }

    [Fact]
    public async Task BackupPost_WithoutAntiforgeryCreatesNothing()
    {
        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.InitialBackupRequired);
        using var client = CreateClient(factory);

        using var response = await client.PostAsync(
            "/setup/backup",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["ConfirmBackupCreation"] = "true"
                }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(Directory.Exists(factory.BackupDirectory));
        Assert.Empty(factory.GetBackupPackagePaths());
    }

    [Fact]
    public async Task BackupPost_CreatesMatchedVerifiedGenerationAndUsesPrg()
    {
        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.InitialBackupRequired);
        using var client = CreateClient(factory);
        var token = await GetAntiforgeryTokenAsync(
            client,
            "/setup/backup");

        using var response = await PostBackupAsync(client, token);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(
            "/setup/complete",
            response.Headers.Location?.OriginalString);

        var packages = factory.GetBackupPackagePaths();
        Assert.Single(packages);
        Assert.True(File.Exists(packages[0]));

        var status = await factory.ReadLocalDataStatusAsync();
        Assert.NotNull(status.LatestVerifiedBackup);
        Assert.Equal(
            LocalBackupWorkspaceBinding.Matched,
            status.LatestVerifiedBackup!.WorkspaceBinding);
        Assert.Equal(12, status.LatestVerifiedBackup.DigestPrefix.Length);
    }

    [Fact]
    public async Task BackupPost_AcknowledgementFlagsDoNotWithholdCompletion()
    {
        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.InitialBackupRequired,
                destinationSeparationConfirmed: false,
                destinationEncryptionConfirmed: false);
        using var client = CreateClient(factory);
        var token = await GetAntiforgeryTokenAsync(
            client,
            "/setup/backup");

        using var response = await PostBackupAsync(client, token);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(
            "/setup/complete",
            response.Headers.Location?.OriginalString);

        var status = await factory.ReadLocalDataStatusAsync();
        Assert.NotNull(status.LatestVerifiedBackup);
        Assert.Equal(
            LocalBackupWorkspaceBinding.Matched,
            status.LatestVerifiedBackup!.WorkspaceBinding);
        Assert.False(status.LocalProtectionReady);
    }

    [Fact]
    public async Task BackupPost_RetryCreatesAnotherImmutableGeneration()
    {
        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.InitialBackupRequired);
        using var client = CreateClient(factory);
        var token = await GetAntiforgeryTokenAsync(
            client,
            "/setup/backup");

        using var first = await PostBackupAsync(client, token);
        var firstPackages = factory.GetBackupPackagePaths();
        var firstPath = Assert.Single(firstPackages);
        var firstLength = new FileInfo(firstPath).Length;

        using var retry = await PostBackupAsync(client, token);
        var retryPackages = factory.GetBackupPackagePaths();

        Assert.Equal(HttpStatusCode.Found, first.StatusCode);
        Assert.Equal(HttpStatusCode.Found, retry.StatusCode);
        Assert.Equal(2, retryPackages.Length);
        Assert.Contains(firstPath, retryPackages);
        Assert.Equal(firstLength, new FileInfo(firstPath).Length);
        Assert.All(retryPackages, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public async Task BackupPost_WhenOwnershipIsBusyReturnsSanitizedGuidance()
    {
        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.InitialBackupRequired);
        using var client = CreateClient(factory);
        var token = await GetAntiforgeryTokenAsync(
            client,
            "/setup/backup");
        await using var ownership = factory.AcquireDatabaseOwnership();

        using var response = await PostBackupAsync(client, token);
        var html = WebUtility.HtmlDecode(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Başka bir yerel veri işlemi", html);
        Assert.DoesNotContain("SqliteException", html);
        Assert.DoesNotContain("Data Source=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at WealthLedger", html);
        Assert.Empty(factory.GetBackupPackagePaths());
        AssertPrivateDiagnosticsAbsent(factory);
    }

    [Fact]
    public async Task BackupPost_VerificationFailureIsSanitizedAndPublishesNothing()
    {
        const string privateFailure =
            "PRIVATE_BACKUP_FAILURE Data Source=Synthetic";

        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.InitialBackupRequired,
                backupCreationFailure: new LocalDataFailure(
                    LocalDataFailureCategory.InvalidBackup,
                    privateFailure));
        using var client = CreateClient(factory);
        var token = await GetAntiforgeryTokenAsync(
            client,
            "/setup/backup");

        using var response = await PostBackupAsync(client, token);
        var html = WebUtility.HtmlDecode(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("bağımsız doğrulamayı tamamlamadı", html);
        Assert.DoesNotContain(privateFailure, html);
        Assert.DoesNotContain("Data Source=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            factory.Logs.Messages,
            message => message.Contains(
                privateFailure,
                StringComparison.Ordinal));
        Assert.Empty(factory.GetBackupPackagePaths());
    }

    [Fact]
    public async Task UnrelatedVerifiedPackageDoesNotSatisfyCompletion()
    {
        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.InitialBackupRequired);
        var unrelatedPackage =
            factory.CreateUnrelatedVerifiedBackup();
        using var client = CreateClient(factory);

        using var backupResponse =
            await client.GetAsync("/setup/backup");
        var backupHtml = WebUtility.HtmlDecode(
            await backupResponse.Content.ReadAsStringAsync());
        using var completeResponse =
            await client.GetAsync("/setup/complete");

        Assert.Equal(HttpStatusCode.OK, backupResponse.StatusCode);
        Assert.Contains("Eşleşmeyen paket sayısı", backupHtml);
        Assert.Contains(">1<", backupHtml);
        Assert.Equal(HttpStatusCode.Found, completeResponse.StatusCode);
        Assert.Equal(
            "/setup/backup",
            completeResponse.Headers.Location?.OriginalString);
        Assert.Equal([unrelatedPackage], factory.GetBackupPackagePaths());

        var status = await factory.ReadLocalDataStatusAsync();
        Assert.Null(status.LatestVerifiedBackup);
        Assert.Equal(1, status.UnrelatedVerifiedBackupCount);
    }

    [Fact]
    public async Task CompletePageRequiresCleanRestartAndDoesNotPromoteCurrentHost()
    {
        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.InitialBackupRequired);
        using var client = CreateClient(factory);
        var token = await GetAntiforgeryTokenAsync(
            client,
            "/setup/backup");

        using var postResponse = await PostBackupAsync(client, token);
        using var completeResponse =
            await client.GetAsync(postResponse.Headers.Location);
        var html = WebUtility.HtmlDecode(
            await completeResponse.Content.ReadAsStringAsync());
        using var ledgerResponse =
            await client.GetAsync("/api/households");
        using var completePost = await client.PostAsync(
            "/setup/complete",
            new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        Assert.Contains("temiz biçimde yeniden başlatın", html);
        Assert.Contains("InitialBackupRequired", html);
        Assert.Contains("Çalışma alanı ilişkisi", html);
        Assert.Contains("Eşleşti", html);
        Assert.Equal(HttpStatusCode.NotFound, ledgerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, completePost.StatusCode);
    }

    [Fact]
    public async Task ReadyAndBlockedHosts_CannotUseSetupMutationRoutesDirectly()
    {
        foreach (var mode in new[]
                 {
                     ApiTestStartupMode.Ready,
                     ApiTestStartupMode.Blocked
                 })
        {
            using var factory = new WealthLedgerApiFactory(mode);
            using var client = CreateClient(factory);

            using var storageResponse = await client.PostAsync(
                "/setup/storage",
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["ConfirmInitialization"] = "true"
                    }));
            using var workspaceResponse = await client.PostAsync(
                "/setup/workspace",
                new FormUrlEncodedContent(CreateWorkspaceForm()));
            using var backupResponse = await client.PostAsync(
                "/setup/backup",
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["ConfirmBackupCreation"] = "true"
                    }));

            Assert.Equal(HttpStatusCode.NotFound, storageResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, workspaceResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, backupResponse.StatusCode);
        }
    }

    [Fact]
    public async Task BrowserWorkspaceSetup_IsIndependentFromDefaultOffJsonSetupRoute()
    {
        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.WorkspaceUninitialized,
                setupEnabled: false);
        using var client = CreateClient(factory);

        using var browserResponse =
            await client.GetAsync("/setup/workspace");
        using var jsonResponse = await client.PostAsync(
            "/api/setup/core-ledger",
            new StringContent(string.Empty));

        Assert.Equal(HttpStatusCode.OK, browserResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, jsonResponse.StatusCode);
    }

    [Fact]
    public async Task SetupPages_UseOnlyAntiforgeryCookieAndNoAuthoritativeClientState()
    {
        using var factory =
            new WealthLedgerApiFactory(
                ApiTestStartupMode.WorkspaceUninitialized);
        using var client = CreateClient(factory);

        using var response =
            await client.GetAsync("/setup/workspace");
        var html = await response.Content.ReadAsStringAsync();
        var cookies = response.Headers.TryGetValues(
            "Set-Cookie",
            out var values)
            ? values.ToArray()
            : [];

        Assert.DoesNotContain("localStorage", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wizardState", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            cookies,
            cookie => cookie.Contains(
                ".AspNetCore.Session",
                StringComparison.OrdinalIgnoreCase));
        Assert.All(
            cookies,
            cookie => Assert.Contains(
                "antiforgery",
                cookie,
                StringComparison.OrdinalIgnoreCase));
    }

    private static async Task AssertRouteExposureAsync(
        ApiTestStartupMode mode,
        string? expectedPage,
        string? expectedSetupRedirect,
        bool completionRedirectExpected = false,
        HttpStatusCode ledgerExpected = HttpStatusCode.NotFound,
        bool readyPagesExpected = false)
    {
        using var factory = new WealthLedgerApiFactory(mode);
        using var client = CreateClient(factory);

        foreach (var path in new[]
                 {
                     "/blocked",
                     "/setup/storage",
                     "/setup/workspace",
                     "/setup/backup",
                     "/setup/complete"
                 })
        {
            using var response = await client.GetAsync(path);

            if (completionRedirectExpected
                && string.Equals(
                    path,
                    "/setup/complete",
                    StringComparison.Ordinal))
            {
                Assert.Equal(HttpStatusCode.Found, response.StatusCode);
                Assert.Equal(
                    "/setup/backup",
                    response.Headers.Location?.OriginalString);
                continue;
            }

            var expected = string.Equals(
                path,
                expectedPage,
                StringComparison.Ordinal)
                ? HttpStatusCode.OK
                : HttpStatusCode.NotFound;

            Assert.Equal(expected, response.StatusCode);
        }

        using (var setupResponse = await client.GetAsync("/setup"))
        {
            if (expectedSetupRedirect is null)
            {
                Assert.Equal(
                    HttpStatusCode.NotFound,
                    setupResponse.StatusCode);
            }
            else
            {
                Assert.Equal(HttpStatusCode.Found, setupResponse.StatusCode);
                Assert.Equal(
                    expectedSetupRedirect,
                    setupResponse.Headers.Location?.OriginalString);
            }
        }

        foreach (var (path, readyStatus) in new[]
                 {
                     ("/", HttpStatusCode.OK),
                     ("/ledger", HttpStatusCode.OK),
                     ("/ledger/not-a-guid", HttpStatusCode.BadRequest),
                     ("/settings", HttpStatusCode.OK),
                     ("/settings/master-data", HttpStatusCode.OK),
                     ("/settings/data-safety", HttpStatusCode.OK)
                 })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(
                readyPagesExpected
                    ? readyStatus
                    : HttpStatusCode.NotFound,
                response.StatusCode);
        }

        using var ledgerResponse =
            await client.GetAsync("/api/households");
        Assert.Equal(ledgerExpected, ledgerResponse.StatusCode);
    }

    private static HttpClient CreateClient(
        WealthLedgerApiFactory factory)
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

    private static Task<HttpResponseMessage> PostStorageAsync(
        HttpClient client,
        string token)
        => client.PostAsync(
            "/setup/storage",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["ConfirmInitialization"] = "true",
                    ["__RequestVerificationToken"] = token
                }));

    private static Task<HttpResponseMessage> PostBackupAsync(
        HttpClient client,
        string token)
        => client.PostAsync(
            "/setup/backup",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["ConfirmBackupCreation"] = "true",
                    ["__RequestVerificationToken"] = token
                }));

    private static Dictionary<string, string> CreateWorkspaceForm(
        string? token = null)
    {
        var form = new Dictionary<string, string>
        {
            ["Input.BaseCurrencyCode"] = "TRY",
            ["Input.BaseCurrencyName"] = "Synthetic Currency",
            ["Input.MinorUnitDigits"] = "2",
            ["Input.HouseholdName"] = "Synthetic Household",
            ["Input.HouseholdMemberDisplayName"] = "Synthetic Member",
            ["Input.InstitutionCode"] = "SYNTHETIC_INSTITUTION",
            ["Input.InstitutionName"] = "Synthetic Institution",
            ["Input.InstitutionTypeCode"] = "BROKER",
            ["Input.PortfolioCode"] = "CORE",
            ["Input.PortfolioName"] = "Synthetic Portfolio",
            ["Input.AccountCode"] = "PRIMARY",
            ["Input.AccountName"] = "Synthetic Account",
            ["Input.AccountTypeCode"] = "INVESTMENT",
            ["Input.AccountOpenedOn"] = "2026-01-01",
            ["Input.CashAssetCode"] = "SYNTHETIC_CASH",
            ["Input.CashAssetName"] = "Synthetic Cash",
            ["Input.FundAssetCode"] = "SYNTHETIC_FUND",
            ["Input.FundAssetName"] = "Synthetic Fund"
        };

        if (token is not null)
        {
            form["__RequestVerificationToken"] = token;
        }

        return form;
    }

    private static async Task AssertMasterCountsAsync(
        WealthLedgerApiFactory factory,
        int expectedCoreRows)
    {
        if (!File.Exists(factory.DatabasePath))
        {
            Assert.Equal(0, expectedCoreRows);
            return;
        }

        await using var context = factory.CreateDbContext();
        var coreRows =
            await context.Currencies.CountAsync()
            + await context.Households.CountAsync()
            + await context.HouseholdMembers.CountAsync()
            + await context.Institutions.CountAsync()
            + await context.Portfolios.CountAsync()
            + await context.Accounts.CountAsync()
            + await context.Assets.CountAsync();

        Assert.Equal(expectedCoreRows, coreRows);
    }

    private static void AssertPrivateDiagnosticsAbsent(
        WealthLedgerApiFactory factory)
    {
        Assert.DoesNotContain(
            factory.Logs.Messages,
            message => message.Contains(
                factory.DatabasePath,
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            factory.Logs.Messages,
            message => message.Contains(
                factory.BackupDirectory,
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            factory.Logs.Messages,
            message => message.Contains(
                "Data Source=",
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            factory.Logs.Messages,
            message => message.Contains(
                "SqliteException",
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            factory.Logs.Messages,
            message => message.Contains(
                " at WealthLedger",
                StringComparison.Ordinal));
    }

    [GeneratedRegex(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex AntiforgeryTokenPattern();
}
