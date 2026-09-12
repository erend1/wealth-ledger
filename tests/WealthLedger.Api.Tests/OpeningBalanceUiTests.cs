using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WealthLedger.Api.Contracts;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Ledger;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Api.Tests;

public sealed partial class OpeningBalanceUiTests
{
    [Fact]
    public async Task Cash_ReviewWritesNothingThenPostRedirectsAndReplayReadsOneReceipt()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = CreateClient(factory);
        var initial = await GetFormAsync(client, "/record/opening-balance");
        var form = CreateCashForm(factory, initial.Token, initial.CommandKey);

        using var review = await client.PostAsync(
            "/record/opening-balance?handler=Review",
            new FormUrlEncodedContent(form));
        var reviewHtml = WebUtility.HtmlDecode(
            await review.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Contains("Henüz kaydedilmedi", reviewHtml);
        Assert.Contains("12.345,67 para birimi", reviewHtml);
        Assert.Contains("Uygulanamaz", reviewHtml);
        Assert.DoesNotContain("Piyasa değeri", reviewHtml);

        await using (var previewContext = factory.CreateDbContext())
        {
            Assert.Equal(0, await previewContext.LedgerTransactions.CountAsync());
            Assert.Equal(0, await previewContext.CommandReceipts.CountAsync());
        }

        form["__RequestVerificationToken"] = TokenFrom(reviewHtml);

        using var post = await client.PostAsync(
            "/record/opening-balance?handler=Post",
            new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        var receiptPath = Assert.IsType<Uri>(post.Headers.Location).OriginalString;
        Assert.Matches(
            "^/record/opening-balance/[0-9a-f-]{36}$",
            receiptPath);

        using var replay = await client.PostAsync(
            "/record/opening-balance?handler=Post",
            new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Redirect, replay.StatusCode);
        Assert.Equal(receiptPath, replay.Headers.Location!.OriginalString);

        await using (var context = factory.CreateDbContext())
        {
            Assert.Equal(1, await context.LedgerTransactions.CountAsync());
            Assert.Equal(1, await context.TransactionEntries.CountAsync());
            Assert.Equal(0, await context.AssetLots.CountAsync());
            Assert.Equal(0, await context.LotEntryAllocations.CountAsync());
            Assert.Equal(1, await context.CommandReceipts.CountAsync());
            Assert.Equal(
                LedgerOperationCodes.RecordOpeningBalance,
                await context.CommandReceipts
                    .Select(item => item.OperationCode)
                    .SingleAsync());
        }

        using var receipt = await client.GetAsync(receiptPath);
        var receiptHtml = WebUtility.HtmlDecode(
            await receipt.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        Assert.Contains("Açılış kalıcı ledger geçmişinde", receiptHtml);
        Assert.Contains("12.345,67 para birimi", receiptHtml);
        Assert.Contains("Şimdilik tam eşleşiyor", receiptHtml);
        Assert.Contains("bağımsız mutabakat değildir", receiptHtml);

        using var refresh = await client.GetAsync(receiptPath);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);

        var duplicatePage = await GetFormAsync(
            client,
            "/record/opening-balance");
        var duplicateForm = CreateCashForm(
            factory,
            duplicatePage.Token,
            duplicatePage.CommandKey);
        using var duplicateReview = await client.PostAsync(
            "/record/opening-balance?handler=Review",
            new FormUrlEncodedContent(duplicateForm));
        var duplicateHtml = WebUtility.HtmlDecode(
            await duplicateReview.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, duplicateReview.StatusCode);
        Assert.Contains("etkin bir açılış zaten var", duplicateHtml);
        Assert.Contains(receiptPath, duplicateHtml);

        await using var refreshContext = factory.CreateDbContext();
        Assert.Equal(1, await refreshContext.LedgerTransactions.CountAsync());
        Assert.Equal(1, await refreshContext.CommandReceipts.CountAsync());
    }

    [Fact]
    public async Task ForeignCurrency_ReviewEnforcesMinorUnitsThenPostsWithoutLot()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = CreateClient(factory);
        var setup = factory.ReadySetup;

        using var currencyResponse = await client.PutAsJsonAsync(
            "/api/opening-balance-reference-data/currencies/USD",
            new CreateOpeningBalanceCurrencyRequest(
                "Synthetic UI Dollar",
                MinorUnitDigits: 2));
        Assert.Equal(HttpStatusCode.Created, currencyResponse.StatusCode);

        using var assetResponse = await client.PutAsJsonAsync(
            "/api/opening-balance-reference-data/assets/SYNTHETIC_UI_USD",
            new CreateOpeningBalanceAssetRequest(
                "Synthetic UI Dollar Holding",
                "CURRENCY",
                "USD",
                "NONE"));
        var asset = await assetResponse.Content
            .ReadFromJsonAsync<OpeningBalanceAssetResponse>();
        Assert.Equal(HttpStatusCode.Created, assetResponse.StatusCode);
        Assert.NotNull(asset);

        var page = await GetFormAsync(
            client,
            $"/record/opening-balance?accountId={setup.AccountId:D}&assetId={asset!.AssetId:D}");
        var form = new Dictionary<string, string>
        {
            ["Input.PortfolioId"] = setup.PortfolioId.ToString("D"),
            ["Input.AccountId"] = setup.AccountId.ToString("D"),
            ["Input.AssetId"] = asset.AssetId.ToString("D"),
            ["Input.AsOfDate"] = "2026-09-01",
            ["Input.Quantity"] = "1234,567",
            ["Input.ExternalReference"] = "SYNTHETIC-USD-UI",
            ["Input.Note"] = "Synthetic foreign-currency opening for UI verification.",
            ["Input.IdempotencyKey"] = page.CommandKey,
            ["__RequestVerificationToken"] = page.Token
        };

        using var invalidReview = await client.PostAsync(
            "/record/opening-balance?handler=Review",
            new FormUrlEncodedContent(form));
        var invalidHtml = WebUtility.HtmlDecode(
            await invalidReview.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, invalidReview.StatusCode);
        Assert.Contains(
            "seçilen para biriminin ondalık basamak hassasiyetini aşıyor",
            invalidHtml);

        await using (var invalidContext = factory.CreateDbContext())
        {
            Assert.Equal(0, await invalidContext.LedgerTransactions.CountAsync());
        }

        form["Input.Quantity"] = "1234,56";
        form["__RequestVerificationToken"] = TokenFrom(invalidHtml);
        using var review = await client.PostAsync(
            "/record/opening-balance?handler=Review",
            new FormUrlEncodedContent(form));
        var reviewHtml = WebUtility.HtmlDecode(
            await review.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Contains("Synthetic UI Dollar Holding (SYNTHETIC_UI_USD)", reviewHtml);
        Assert.Contains("<code>USD</code>", reviewHtml);
        Assert.Contains("1.234,56 para birimi", reviewHtml);
        Assert.Contains("Uygulanamaz", reviewHtml);

        form["__RequestVerificationToken"] = TokenFrom(reviewHtml);
        using var post = await client.PostAsync(
            "/record/opening-balance?handler=Post",
            new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        var receiptPath = post.Headers.Location!.OriginalString;

        using var receipt = await client.GetAsync(receiptPath);
        var receiptHtml = WebUtility.HtmlDecode(
            await receipt.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        Assert.Contains("<code>USD</code>", receiptHtml);
        Assert.Contains("1.234,56 para birimi", receiptHtml);

        await using var context = factory.CreateDbContext();
        Assert.Equal(1, await context.LedgerTransactions.CountAsync());
        Assert.Equal(
            123_456_000_000,
            await context.TransactionEntries
                .Select(item => item.QuantityDeltaE8)
                .SingleAsync());
        Assert.Equal(0, await context.AssetLots.CountAsync());
        Assert.Equal(0, await context.LotEntryAllocations.CountAsync());
    }

    [Fact]
    public async Task SelectAsset_WithMultipleAccountsRendersTheSingleCompatibleAccount()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = CreateClient(factory);
        var setup = factory.ReadySetup;

        using var vaultResponse = await client.PutAsJsonAsync(
            $"/api/households/{setup.HouseholdId:D}/opening-balance-reference-data/accounts/SYNTHETIC_SELECTION_VAULT",
            new CreateOpeningBalanceAccountRequest(
                InstitutionId: null,
                Name: "Synthetic Selection Vault",
                TypeCode: "PHYSICAL_VAULT",
                OpenedOn: new DateOnly(2026, 1, 1)));
        var vault = await vaultResponse.Content
            .ReadFromJsonAsync<OpeningBalanceAccountResponse>();
        Assert.Equal(HttpStatusCode.Created, vaultResponse.StatusCode);
        Assert.NotNull(vault);

        var page = await GetFormAsync(client, "/record/opening-balance");
        using var selection = await client.PostAsync(
            "/record/opening-balance?handler=SelectAsset",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["Input.PortfolioId"] = setup.PortfolioId.ToString("D"),
                    ["Input.AccountId"] = string.Empty,
                    ["Input.AssetId"] = setup.FundAssetId.ToString("D"),
                    ["Input.AsOfDate"] = "2026-09-01",
                    ["Input.IdempotencyKey"] = page.CommandKey,
                    ["__RequestVerificationToken"] = page.Token
                }));
        var html = WebUtility.HtmlDecode(
            await selection.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, selection.StatusCode);

        var accountSelect = Regex.Match(
            html,
            "<select[^>]*id=\"Input_AccountId\"[^>]*>[\\s\\S]*?</select>",
            RegexOptions.CultureInvariant);
        Assert.True(accountSelect.Success);
        Assert.Matches(
            new Regex(
                $"<option(?=[^>]*value=\"{Regex.Escape(setup.AccountId.ToString("D"))}\")(?=[^>]*selected=\"selected\")[^>]*>",
                RegexOptions.CultureInvariant),
            accountSelect.Value);
        Assert.DoesNotContain(
            vault!.AccountId.ToString("D"),
            accountSelect.Value,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Input_Lots_0__Quantity", html);
    }

    [Fact]
    public async Task Fund_TwoLotsReviewPostReceiptAndBoundedReversalUsePersistedHistory()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = CreateClient(factory);
        var initial = await GetFormAsync(client, "/record/opening-balance");
        var form = CreateFundForm(factory, initial.Token, initial.CommandKey);

        using var review = await client.PostAsync(
            "/record/opening-balance?handler=Review",
            new FormUrlEncodedContent(form));
        var reviewHtml = WebUtility.HtmlDecode(
            await review.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Contains("123,456789 fon birimi", reviewHtml);
        Assert.Contains("100,000001 fon birimi", reviewHtml);
        Assert.Contains("2.500,00 TRY", reviewHtml);
        Assert.Contains("Bilinmiyor", reviewHtml);
        form["__RequestVerificationToken"] = TokenFrom(reviewHtml);

        using var post = await client.PostAsync(
            "/record/opening-balance?handler=Post",
            new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        var receiptPath = post.Headers.Location!.OriginalString;

        Guid openingLotId;
        await using (var context = factory.CreateDbContext())
        {
            Assert.Equal(1, await context.LedgerTransactions.CountAsync());
            Assert.Equal(2, await context.AssetLots.CountAsync());
            Assert.Equal(2, await context.LotEntryAllocations.CountAsync());
            Assert.Equal(1, await context.CommandReceipts.CountAsync());
            openingLotId = await context.AssetLots
                .OrderBy(item => item.CreatedAtUtc)
                .Select(item => item.Id)
                .FirstAsync();
        }

        using var receipt = await client.GetAsync(receiptPath);
        var receiptHtml = WebUtility.HtmlDecode(
            await receipt.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        Assert.Contains("Kalıcı edinim lotları", receiptHtml);
        Assert.Contains("123,456789 fon birimi", receiptHtml);
        Assert.Contains("Bu açılışı ters kaydet", receiptHtml);

        var transactionId = Guid.Parse(receiptPath.Split('/').Last());
        var reversePath = $"{receiptPath}/reverse";
        var blockingTransactionId = await SeedPostedDependentAdjustmentAsync(
            factory,
            openingLotId);

        using var blockedReverse = await client.GetAsync(reversePath);
        var blockedHtml = WebUtility.HtmlDecode(
            await blockedReverse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, blockedReverse.StatusCode);
        Assert.Contains(
            "Sonraki lot kullanımları bu açılışın ters kaydını engelliyor",
            blockedHtml);
        Assert.Contains(
            $"/ledger/{blockingTransactionId:D}",
            blockedHtml);
        Assert.DoesNotContain("Kalıcı ters kaydı oluştur", blockedHtml);

        using (var dependentReversalRequest = new HttpRequestMessage(
                   HttpMethod.Post,
                   $"/api/ledger/transactions/{blockingTransactionId:D}/reversals"))
        {
            dependentReversalRequest.Headers.Add(
                "Idempotency-Key",
                "synthetic-ui-dependent-reversal");
            dependentReversalRequest.Content = JsonContent.Create(
                new ReversePostedTransactionRequest(
                    "Undo synthetic dependent allocation before correcting opening."));
            using var dependentReversalResponse = await client.SendAsync(
                dependentReversalRequest);
            Assert.Equal(
                HttpStatusCode.Created,
                dependentReversalResponse.StatusCode);
        }

        var reverse = await GetFormAsync(client, reversePath);
        Assert.Contains("Uygun — zıt etkiler", reverse.Html);
        Assert.Contains("-123,456789 fon birimi — azalış", reverse.Html);
        Assert.Contains("aynı lotlara", reverse.Html, StringComparison.OrdinalIgnoreCase);

        var reverseForm = new Dictionary<string, string>
        {
            ["Input.Reason"] =
                "Synthetic correction: opening evidence was entered incorrectly.",
            ["Input.IdempotencyKey"] = reverse.CommandKey,
            ["__RequestVerificationToken"] = reverse.Token
        };
        using var reversePost = await client.PostAsync(
            reversePath,
            new FormUrlEncodedContent(reverseForm));

        Assert.Equal(HttpStatusCode.Redirect, reversePost.StatusCode);
        Assert.Equal(receiptPath, reversePost.Headers.Location!.OriginalString);

        await using (var context = factory.CreateDbContext())
        {
            Assert.Equal(4, await context.LedgerTransactions.CountAsync());
            var original = await context.LedgerTransactions.SingleAsync(
                item => item.Id == transactionId);
            Assert.Equal(TransactionStatus.Posted, original.Status);
            Assert.True(await context.LedgerTransactions.AnyAsync(
                item => item.ReversalOfTransactionId == transactionId
                        && item.Status == TransactionStatus.Posted));
        }

        using var reversedReceipt = await client.GetAsync(receiptPath);
        var reversedHtml = WebUtility.HtmlDecode(
            await reversedReceipt.Content.ReadAsStringAsync());
        Assert.Contains("ayrı bir ters kayıtla etkisizleştirildi", reversedHtml);
        Assert.Contains("0 fon birimi", reversedHtml);
        Assert.Contains("Ters kayıt işlemini aç", reversedHtml);
    }

    [Fact]
    public async Task ReferenceCreateFormsCommitOnlyOneMasterFactAndSelectNewAccountOrAsset()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = CreateClient(factory);

        var currencyPage = await GetFormAsync(
            client,
            "/record/opening-balance");
        using var currencyPost = await client.PostAsync(
            "/record/opening-balance?handler=CreateCurrency",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["CurrencyInput.Code"] = "USD",
                    ["CurrencyInput.Name"] = "Synthetic Dollar",
                    ["CurrencyInput.MinorUnitDigits"] = "2",
                    ["__RequestVerificationToken"] = currencyPage.Token
                }));
        Assert.Equal(HttpStatusCode.Redirect, currencyPost.StatusCode);

        var institutionPage = await GetFormAsync(
            client,
            currencyPost.Headers.Location!.OriginalString);
        using var institutionPost = await client.PostAsync(
            "/record/opening-balance?handler=CreateInstitution",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["InstitutionInput.Code"] = "SYNTHETIC_SECOND_BROKER",
                    ["InstitutionInput.Name"] = "Synthetic Second Broker",
                    ["InstitutionInput.TypeCode"] = "BROKER",
                    ["__RequestVerificationToken"] = institutionPage.Token
                }));
        Assert.Equal(HttpStatusCode.Redirect, institutionPost.StatusCode);

        Guid institutionId;
        await using (var context = factory.CreateDbContext())
        {
            institutionId = await context.Institutions
                .Where(item => item.Code == "SYNTHETIC_SECOND_BROKER")
                .Select(item => item.Id)
                .SingleAsync();
            Assert.Equal(0, await context.LedgerTransactions.CountAsync());
        }

        var accountPage = await GetFormAsync(
            client,
            institutionPost.Headers.Location!.OriginalString);
        using var accountPost = await client.PostAsync(
            "/record/opening-balance?handler=CreateAccount",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["AccountInput.Code"] = "SYNTHETIC_SECOND_ACCOUNT",
                    ["AccountInput.Name"] = "Synthetic Second Account",
                    ["AccountInput.TypeCode"] = "INVESTMENT",
                    ["AccountInput.InstitutionId"] = institutionId.ToString("D"),
                    ["AccountInput.OpenedOn"] = "2026-01-01",
                    ["__RequestVerificationToken"] = accountPage.Token
                }));
        Assert.Equal(HttpStatusCode.Redirect, accountPost.StatusCode);
        Assert.Contains(
            "accountId=",
            accountPost.Headers.Location!.OriginalString);

        var assetPage = await GetFormAsync(
            client,
            accountPost.Headers.Location.OriginalString);
        using var assetPost = await client.PostAsync(
            "/record/opening-balance?handler=CreateAsset",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["AssetInput.Code"] = "SYNTHETIC_EQUITY",
                    ["AssetInput.Name"] = "Synthetic Equity",
                    ["AssetInput.TypeCode"] = "EQUITY",
                    ["AssetInput.BaseCurrencyCode"] = "USD",
                    ["AssetInput.LotTrackingModeCode"] = "REQUIRED",
                    ["__RequestVerificationToken"] = assetPage.Token
                }));
        Assert.Equal(HttpStatusCode.Redirect, assetPost.StatusCode);
        Assert.Contains(
            "assetId=",
            assetPost.Headers.Location!.OriginalString);

        await using var finalContext = factory.CreateDbContext();
        Assert.Equal(2, await finalContext.Currencies.CountAsync());
        Assert.Equal(2, await finalContext.Institutions.CountAsync());
        Assert.Equal(2, await finalContext.Accounts.CountAsync());
        Assert.Equal(3, await finalContext.Assets.CountAsync());
        Assert.Equal(0, await finalContext.LedgerTransactions.CountAsync());
        Assert.Equal(0, await finalContext.CommandReceipts.CountAsync());
    }

    [Fact]
    public async Task Gold_ReviewReceiptAndTransactionDetailShowGrossFinenessPiecesAndDerivedFineWeight()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = CreateClient(factory);
        var setup = factory.ReadySetup;

        using var accountResponse = await client.PutAsJsonAsync(
            $"/api/households/{setup.HouseholdId:D}/opening-balance-reference-data/accounts/SYNTHETIC_UI_VAULT",
            new CreateOpeningBalanceAccountRequest(
                InstitutionId: null,
                Name: "Synthetic UI Vault",
                TypeCode: "PHYSICAL_VAULT",
                OpenedOn: new DateOnly(2026, 1, 1)));
        var account = await accountResponse.Content
            .ReadFromJsonAsync<OpeningBalanceAccountResponse>();
        Assert.Equal(HttpStatusCode.Created, accountResponse.StatusCode);

        using var assetResponse = await client.PutAsJsonAsync(
            "/api/opening-balance-reference-data/assets/SYNTHETIC_UI_GOLD",
            new CreateOpeningBalanceAssetRequest(
                "Synthetic UI Gold",
                "PHYSICAL_GOLD",
                "TRY",
                "REQUIRED"));
        var asset = await assetResponse.Content
            .ReadFromJsonAsync<OpeningBalanceAssetResponse>();
        Assert.Equal(HttpStatusCode.Created, assetResponse.StatusCode);

        var page = await GetFormAsync(
            client,
            $"/record/opening-balance?accountId={account!.AccountId:D}&assetId={asset!.AssetId:D}");
        var form = new Dictionary<string, string>
        {
            ["Input.PortfolioId"] = setup.PortfolioId.ToString("D"),
            ["Input.AccountId"] = account.AccountId.ToString("D"),
            ["Input.AssetId"] = asset.AssetId.ToString("D"),
            ["Input.AsOfDate"] = "2026-09-01",
            ["Input.Quantity"] = "24,5",
            ["Input.ExternalReference"] = "SYNTHETIC-GOLD-UI",
            ["Input.Note"] = "Synthetic physical-gold inventory for UI verification.",
            ["Input.IdempotencyKey"] = page.CommandKey,
            ["Input.Lots[0].Quantity"] = "10",
            ["Input.Lots[0].CostBasisStatusCode"] = "UNKNOWN",
            ["Input.Lots[0].FinenessChoiceCode"] = "22K_916",
            ["Input.Lots[0].PieceCount"] = "2",
            ["Input.Lots[0].Hallmark"] = "SYNTHETIC-916",
            ["Input.Lots[0].Note"] = "Two synthetic matching bracelets.",
            ["Input.Lots[1].Quantity"] = "14,5",
            ["Input.Lots[1].CostBasisStatusCode"] = "UNKNOWN",
            ["Input.Lots[1].FinenessChoiceCode"] = "PRECISE",
            ["Input.Lots[1].PreciseFinenessPerMille"] = "999,999",
            ["Input.Lots[1].PieceCount"] = "1",
            ["__RequestVerificationToken"] = page.Token
        };

        using var review = await client.PostAsync(
            "/record/opening-balance?handler=Review",
            new FormUrlEncodedContent(form));
        var reviewHtml = WebUtility.HtmlDecode(
            await review.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Contains("24,5 gram", reviewHtml);
        Assert.Contains("916 ‰ (916000 ppm)", reviewHtml);
        Assert.Contains("999,999 ‰ (999999 ppm)", reviewHtml);
        Assert.Contains("23,6599855 g", reviewHtml);

        form["__RequestVerificationToken"] = TokenFrom(reviewHtml);
        using var post = await client.PostAsync(
            "/record/opening-balance?handler=Post",
            new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        var receiptPath = post.Headers.Location!.OriginalString;

        using var receipt = await client.GetAsync(receiptPath);
        var receiptHtml = WebUtility.HtmlDecode(
            await receipt.Content.ReadAsStringAsync());
        Assert.Contains("23,6599855 g", receiptHtml);
        Assert.Contains("SYNTHETIC-916", receiptHtml);

        var transactionId = receiptPath.Split('/').Last();
        using var detail = await client.GetAsync("/ledger/" + transactionId);
        var detailHtml = WebUtility.HtmlDecode(
            await detail.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Contains("Fiziksel altın lot ayrıntıları", detailHtml);
        Assert.Contains("999999 ppm", detailHtml);
        Assert.Contains("14,4999855 g", detailHtml);

        await using var context = factory.CreateDbContext();
        Assert.Equal(2, await context.PhysicalGoldLotDetails.CountAsync());
    }

    [Fact]
    public async Task Review_LotMismatchReturnsFieldLinkedValidationAndLeaksNoSourceValuesToLogs()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = CreateClient(factory);
        var initial = await GetFormAsync(client, "/record/opening-balance");
        var form = CreateFundForm(factory, initial.Token, initial.CommandKey);
        form["Input.Lots[1].Quantity"] = "23,456787";

        using var response = await client.PostAsync(
            "/record/opening-balance?handler=Review",
            new FormUrlEncodedContent(form));
        var html = WebUtility.HtmlDecode(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("href=\"#Input_Lots\"", html);
        Assert.Contains("tam eşleşmelidir", html);
        Assert.DoesNotContain("Henüz kaydedilmedi", html);

        await using var context = factory.CreateDbContext();
        Assert.Equal(0, await context.LedgerTransactions.CountAsync());
        Assert.Equal(0, await context.CommandReceipts.CountAsync());

        var logs = string.Join(Environment.NewLine, factory.Logs.Messages);
        Assert.DoesNotContain("SYNTHETIC-OPENING-REFERENCE", logs);
        Assert.DoesNotContain("Synthetic opening source note", logs);
        Assert.DoesNotContain("123,456789", logs);
        Assert.DoesNotContain(initial.CommandKey, logs);
    }

    private static Dictionary<string, string> CreateCashForm(
        WealthLedgerApiFactory factory,
        string token,
        string commandKey)
        => new()
        {
            ["Input.PortfolioId"] = factory.ReadySetup.PortfolioId.ToString("D"),
            ["Input.AccountId"] = factory.ReadySetup.AccountId.ToString("D"),
            ["Input.AssetId"] = factory.ReadySetup.CashAssetId.ToString("D"),
            ["Input.AsOfDate"] = "2026-09-01",
            ["Input.Quantity"] = "12345,67",
            ["Input.ExternalReference"] = "SYNTHETIC-OPENING-REFERENCE",
            ["Input.Note"] = "Synthetic opening source note for UI verification.",
            ["Input.IdempotencyKey"] = commandKey,
            ["__RequestVerificationToken"] = token
        };

    private static Dictionary<string, string> CreateFundForm(
        WealthLedgerApiFactory factory,
        string token,
        string commandKey)
        => new()
        {
            ["Input.PortfolioId"] = factory.ReadySetup.PortfolioId.ToString("D"),
            ["Input.AccountId"] = factory.ReadySetup.AccountId.ToString("D"),
            ["Input.AssetId"] = factory.ReadySetup.FundAssetId.ToString("D"),
            ["Input.AsOfDate"] = "2026-09-01",
            ["Input.Quantity"] = "123,456789",
            ["Input.ExternalReference"] = "SYNTHETIC-OPENING-REFERENCE",
            ["Input.Note"] = "Synthetic opening source note for UI verification.",
            ["Input.IdempotencyKey"] = commandKey,
            ["Input.Lots[0].Quantity"] = "100,000001",
            ["Input.Lots[0].AcquiredOn"] = "2025-01-12",
            ["Input.Lots[0].CostBasisStatusCode"] = "KNOWN",
            ["Input.Lots[0].CostAmount"] = "2500,00",
            ["Input.Lots[0].CostCurrencyCode"] = "TRY",
            ["Input.Lots[1].Quantity"] = "23,456788",
            ["Input.Lots[1].CostBasisStatusCode"] = "UNKNOWN",
            ["__RequestVerificationToken"] = token
        };

    private static async Task<FormPage> GetFormAsync(
        HttpClient client,
        string path)
    {
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var rawHtml = await response.Content.ReadAsStringAsync();
        var html = WebUtility.HtmlDecode(rawHtml);

        return new FormPage(
            html,
            TokenFrom(rawHtml),
            HiddenValue(CommandKeyPattern(), rawHtml, "command key"));
    }

    private static async Task<Guid> SeedPostedDependentAdjustmentAsync(
        WealthLedgerApiFactory factory,
        Guid lotId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider
            .GetRequiredService<WealthLedgerDbContext>();
        var setup = factory.ReadySetup;
        var transactionId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var createdAtUtc = DateTime.UtcNow;

        context.LedgerTransactions.Add(
            new LedgerTransactionRow
            {
                Id = transactionId,
                HouseholdId = setup.HouseholdId,
                Type = TransactionType.Adjustment,
                Status = TransactionStatus.Draft,
                ExecutionDate = new DateOnly(2026, 9, 2),
                CreatedAtUtc = createdAtUtc
            });
        context.TransactionEntries.Add(
            new TransactionEntryRow
            {
                Id = entryId,
                TransactionId = transactionId,
                EntrySequence = 0,
                PortfolioId = setup.PortfolioId,
                AccountId = setup.AccountId,
                AssetId = setup.FundAssetId,
                QuantityDeltaE8 = -25_000_000,
                Role = EntryRole.Adjustment,
                CreatedAtUtc = createdAtUtc
            });
        context.LotEntryAllocations.Add(
            new LotEntryAllocationRow
            {
                Id = Guid.NewGuid(),
                AssetLotId = lotId,
                TransactionEntryId = entryId,
                QuantityDeltaE8 = -25_000_000,
                CreatedAtUtc = createdAtUtc
            });

        await context.SaveChangesAsync();
        var transaction = await context.LedgerTransactions.SingleAsync(
            item => item.Id == transactionId);
        transaction.Status = TransactionStatus.Posted;
        transaction.PostedAtUtc = createdAtUtc.AddSeconds(1);
        await context.SaveChangesAsync();

        return transactionId;
    }

    private static string TokenFrom(string html)
        => WebUtility.HtmlDecode(
            HiddenValue(AntiforgeryTokenPattern(), html, "antiforgery token"));

    private static string HiddenValue(
        Regex pattern,
        string html,
        string description)
    {
        var match = pattern.Match(html);
        Assert.True(match.Success, $"The form did not render its {description}.");
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
}
