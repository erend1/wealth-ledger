using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Api.Tests;

/// <summary>
/// Drives M009 through the server-rendered Ready UI without calling JSON APIs.
/// </summary>
public sealed partial class PhysicalGoldUiTests
{
    [Fact]
    public async Task Purchase_ReviewPostReplayAndReceiptUsePersistedExactFacts()
    {
        using var factory = new WealthLedgerApiFactory();
        var references = await SeedReferencesAsync(factory);
        using var client = CreateClient(factory);
        var page = await GetFormAsync(client, "/record/physical-gold-purchase");
        var form = PurchaseForm(factory, references, page);

        using var review = await client.PostAsync(
            "/record/physical-gold-purchase?handler=Review",
            new FormUrlEncodedContent(form));
        var reviewHtml = Decode(await review.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Contains("20 gram", reviewHtml);
        Assert.Contains("916 ‰ (916000 ppm)", reviewHtml);
        Assert.Contains("18,32 gram saf altın", reviewHtml);
        Assert.Contains("2.010,00 TRY", reviewHtml);

        await using (var beforePost = factory.CreateDbContext())
        {
            Assert.False(await beforePost.LedgerTransactions.AnyAsync(x =>
                x.Type == TransactionType.Buy));
            Assert.False(await beforePost.CommandReceipts.AnyAsync(x =>
                x.OperationCode == LedgerOperationCodes.RecordPhysicalGoldPurchase));
        }

        form["__RequestVerificationToken"] = TokenFrom(reviewHtml);
        using var post = await client.PostAsync(
            "/record/physical-gold-purchase?handler=Post",
            new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        var receiptPath = Assert.IsType<Uri>(post.Headers.Location).OriginalString;
        Assert.Matches(
            "^/record/physical-gold/[0-9a-f-]{36}/receipt$",
            receiptPath);

        using var replay = await client.PostAsync(
            "/record/physical-gold-purchase?handler=Post",
            new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, replay.StatusCode);
        Assert.Equal(receiptPath, replay.Headers.Location?.OriginalString);

        using var receipt = await client.GetAsync(receiptPath);
        var receiptHtml = Decode(await receipt.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        Assert.Contains("Kesin lot ve parça hareketleri", receiptHtml);
        Assert.Contains("SYNTHETIC-UI-CERTIFICATE", receiptHtml);
        Assert.Contains("+2", receiptHtml);
        Assert.Contains("18,32 gram saf altın", receiptHtml);

        using var refreshed = await client.GetAsync(receiptPath);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);

        await using var context = factory.CreateDbContext();
        Assert.Equal(1, await context.LedgerTransactions.CountAsync(x =>
            x.Type == TransactionType.Buy));
        Assert.Equal(1, await context.AssetLots.CountAsync(x =>
            x.AssetId == references.GoldAssetId));
        var detail = await context.PhysicalGoldLotDetails.SingleAsync();
        Assert.Equal(916_000, detail.ActualFinenessPpm);
        Assert.Equal(2, detail.PieceCount);
        Assert.Equal(
            2,
            await context.TransactionCostComponents.CountAsync());
    }

    [Fact]
    public async Task SaleReversalTransferAndCorrectedSale_PreserveSelectedLotCustody()
    {
        using var factory = new WealthLedgerApiFactory();
        var references = await SeedReferencesAsync(factory);
        using var client = CreateClient(factory);
        var purchaseReceipt = await PostPurchaseAsync(
            factory,
            client,
            references,
            "ui-lifecycle-purchase");
        Guid lotId;
        await using (var context = factory.CreateDbContext())
        {
            lotId = await context.AssetLots
                .Where(x => x.AssetId == references.GoldAssetId)
                .Select(x => x.Id)
                .SingleAsync();
        }

        var firstSaleReceipt = await PostSaleAsync(
            factory,
            client,
            references,
            lotId,
            grossWeight: "8",
            pieceCount: "1",
            keySuffix: "first-sale");
        using (var saleReceipt = await client.GetAsync(firstSaleReceipt))
        {
            var html = Decode(await saleReceipt.Content.ReadAsStringAsync());
            Assert.Contains("ADR009_CUMULATIVE_ROUND_HALF_TO_EVEN_V1", html);
            Assert.Contains("804,00 TRY", html);
            Assert.Contains("-1", html);
        }

        var reversePath = firstSaleReceipt.Replace(
            "/receipt",
            "/reverse",
            StringComparison.Ordinal);
        var reversePage = await GetFormAsync(client, reversePath);
        var reverseForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = reversePage.Token,
            ["Input.IdempotencyKey"] = reversePage.CommandKey,
            ["Input.Reason"] = "Sentetik satış düzeltmesi."
        };
        using var reverse = await client.PostAsync(
            reversePath + "?handler=Reverse",
            new FormUrlEncodedContent(reverseForm));
        Assert.Equal(HttpStatusCode.Redirect, reverse.StatusCode);
        Assert.Equal(firstSaleReceipt, reverse.Headers.Location?.OriginalString);

        var transferReceipt = await PostTransferAsync(
            factory,
            client,
            references,
            lotId,
            grossWeight: "12",
            pieceCount: "1");
        using (var transfer = await client.GetAsync(transferReceipt))
        {
            var html = Decode(await transfer.Content.ReadAsStringAsync());
            Assert.Contains("Kesin lot ve parça hareketleri", html);
            Assert.Contains(references.SourceVaultId.ToString("D"), html);
            Assert.Contains(references.DestinationVaultId.ToString("D"), html);
            Assert.Contains("-1", html);
            Assert.Contains("+1", html);
        }

        var correctedSaleReceipt = await PostSaleAsync(
            factory,
            client,
            references,
            lotId,
            grossWeight: "8",
            pieceCount: "1",
            keySuffix: "corrected-sale");
        Assert.NotEqual(firstSaleReceipt, correctedSaleReceipt);

        using var purchaseReverse = await client.GetAsync(
            purchaseReceipt.Replace(
                "/receipt",
                "/reverse",
                StringComparison.Ordinal));
        var purchaseReverseHtml = Decode(
            await purchaseReverse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, purchaseReverse.StatusCode);
        Assert.Contains("Sonraki bir hareket", purchaseReverseHtml);
        Assert.DoesNotContain("Kalıcı ters kaydı oluştur", purchaseReverseHtml);

        await using var persisted = factory.CreateDbContext();
        Assert.Equal(1, await persisted.AssetLots.CountAsync(x =>
            x.AssetId == references.GoldAssetId));
        Assert.Equal(2, await persisted.LedgerTransactions.CountAsync(x =>
            x.Type == TransactionType.Sell));
        Assert.Equal(1, await persisted.LedgerTransactions.CountAsync(x =>
            x.Type == TransactionType.Transfer));
        Assert.Equal(1, await persisted.LedgerTransactions.CountAsync(x =>
            x.Type == TransactionType.Reversal));
        var remaining = await persisted.LotEntryAllocations
            .Where(x => x.AssetLotId == lotId)
            .Join(
                persisted.TransactionEntries,
                allocation => allocation.TransactionEntryId,
                entry => entry.Id,
                (allocation, entry) => new
                {
                    entry.AccountId,
                    allocation.QuantityDeltaE8
                })
            .GroupBy(x => x.AccountId)
            .Select(x => new
            {
                AccountId = x.Key,
                Gross = x.Sum(y => y.QuantityDeltaE8)
            })
            .ToListAsync();
        Assert.Equal(
            0,
            remaining.Single(x =>
                x.AccountId == references.SourceVaultId).Gross);
        Assert.Equal(
            12_00000000L,
            remaining.Single(x =>
                x.AccountId == references.DestinationVaultId).Gross);
        Assert.Equal(
            1,
            await persisted.PhysicalGoldLotAllocationDetails
                .Join(
                    persisted.LotEntryAllocations.Where(x =>
                        x.AssetLotId == lotId),
                    detail => detail.LotEntryAllocationId,
                    allocation => allocation.Id,
                    (detail, allocation) => new
                    {
                        allocation.TransactionEntryId,
                        detail.PieceDelta
                    })
                .Join(
                    persisted.TransactionEntries,
                    movement => movement.TransactionEntryId,
                    entry => entry.Id,
                    (movement, entry) => new
                    {
                        entry.AccountId,
                        movement.PieceDelta
                    })
                .Where(x => x.AccountId == references.DestinationVaultId)
                .SumAsync(x => x.PieceDelta));
    }

    [Fact]
    public async Task GoldPages_RequireReadyModeAndAntiforgery()
    {
        using (var readyFactory = new WealthLedgerApiFactory())
        using (var readyClient = CreateClient(readyFactory))
        {
            foreach (var path in new[]
                     {
                         "/record/physical-gold-purchase?handler=Review",
                         "/record/physical-gold-sale?handler=Review",
                         "/record/physical-gold-transfer?handler=Review"
                     })
            {
                using var response = await readyClient.PostAsync(
                    path,
                    new FormUrlEncodedContent([]));
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
        }

        using var factory = new WealthLedgerApiFactory(
            ApiTestStartupMode.WorkspaceUninitialized);
        using var client = CreateClient(factory);
        foreach (var path in new[]
                 {
                     "/record/physical-gold-purchase",
                     "/record/physical-gold-sale",
                     "/record/physical-gold-transfer",
                     $"/record/physical-gold/{Guid.NewGuid():D}/receipt",
                     $"/record/physical-gold/{Guid.NewGuid():D}/reverse"
                 })
        {
            using var response = await client.GetAsync(path);
            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Receipt_RejectsFundAndPagesHaveNoExternalDependencies()
    {
        using var factory = new WealthLedgerApiFactory();
        var fund = await factory.SeedReadyUiLedgerAsync();
        await SeedReferencesAsync(factory);
        using var client = CreateClient(factory);

        using var wrongFamily = await client.GetAsync(
            $"/record/physical-gold/{fund.PurchaseTransactionId:D}/receipt");
        Assert.Equal(HttpStatusCode.NotFound, wrongFamily.StatusCode);

        foreach (var path in new[]
                 {
                     "/record/physical-gold-purchase",
                     "/record/physical-gold-sale",
                     "/record/physical-gold-transfer"
                 })
        {
            using var response = await client.GetAsync(path);
            var html = Decode(await response.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("__RequestVerificationToken", html);
        }
    }

    [Fact]
    public async Task ValidationAndLogs_OmitPrivateGoldEvidence()
    {
        using var factory = new WealthLedgerApiFactory();
        var references = await SeedReferencesAsync(factory);
        using var client = CreateClient(factory);
        var page = await GetFormAsync(client, "/record/physical-gold-purchase");
        var form = PurchaseForm(factory, references, page);
        const string privateNote = "PRIVATE-GOLD-NOTE-PAYLOAD";
        const string privateCertificate = "PRIVATE-GOLD-CERTIFICATE-PAYLOAD";
        const string privateReference = "PRIVATE-GOLD-REFERENCE-PAYLOAD";
        form["Input.Note"] = privateNote;
        form["Input.CertificateReference"] = privateCertificate;
        form["Input.ExternalReference"] = privateReference;
        form["Input.GrossWeight"] = "not-a-number";

        using var response = await client.PostAsync(
            "/record/physical-gold-purchase?handler=Review",
            new FormUrlEncodedContent(form));
        var html = Decode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("Input_GrossWeight", html);
        Assert.Contains("href=\"#Input_GrossWeight\"", html);
        Assert.Contains("class=\"validation-summary\"", html);
        Assert.Contains("tabindex=\"-1\"", html);
        Assert.Contains("Sekiz ondalık", html);
        Assert.Contains(privateNote, html);

        var logs = string.Join(Environment.NewLine, factory.Logs.Messages);
        Assert.DoesNotContain(privateNote, logs);
        Assert.DoesNotContain(privateCertificate, logs);
        Assert.DoesNotContain(privateReference, logs);
        Assert.DoesNotContain("sha256-", logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(
            factory.DatabasePath,
            logs,
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> PostPurchaseAsync(
        WealthLedgerApiFactory factory,
        HttpClient client,
        GoldReferences references,
        string keySuffix)
    {
        var page = await GetFormAsync(client, "/record/physical-gold-purchase");
        var form = PurchaseForm(factory, references, page);
        form["Input.ExternalReference"] = $"SYNTHETIC-{keySuffix}";
        using var review = await client.PostAsync(
            "/record/physical-gold-purchase?handler=Review",
            new FormUrlEncodedContent(form));
        var html = Decode(await review.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        form["__RequestVerificationToken"] = TokenFrom(html);
        using var post = await client.PostAsync(
            "/record/physical-gold-purchase?handler=Post",
            new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        return Assert.IsType<Uri>(post.Headers.Location).OriginalString;
    }

    private static async Task<string> PostSaleAsync(
        WealthLedgerApiFactory factory,
        HttpClient client,
        GoldReferences references,
        Guid lotId,
        string grossWeight,
        string pieceCount,
        string keySuffix)
    {
        var page = await GetFormAsync(client, "/record/physical-gold-sale");
        var form = SaleForm(
            factory,
            references,
            page,
            lotId,
            grossWeight,
            pieceCount,
            keySuffix);
        using var review = await client.PostAsync(
            "/record/physical-gold-sale?handler=Review",
            new FormUrlEncodedContent(form));
        var html = Decode(await review.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Contains(lotId.ToString("D"), html);
        form["__RequestVerificationToken"] = TokenFrom(html);
        form["Input.ReviewedPlanFingerprint"] = HiddenValue(
            PlanFingerprintPattern(),
            html,
            "reviewed plan fingerprint");
        using var post = await client.PostAsync(
            "/record/physical-gold-sale?handler=Post",
            new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        return Assert.IsType<Uri>(post.Headers.Location).OriginalString;
    }

    private static async Task<string> PostTransferAsync(
        WealthLedgerApiFactory factory,
        HttpClient client,
        GoldReferences references,
        Guid lotId,
        string grossWeight,
        string pieceCount)
    {
        var page = await GetFormAsync(client, "/record/physical-gold-transfer");
        var form = TransferForm(
            factory,
            references,
            page,
            lotId,
            grossWeight,
            pieceCount);
        using var review = await client.PostAsync(
            "/record/physical-gold-transfer?handler=Review",
            new FormUrlEncodedContent(form));
        var html = Decode(await review.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        form["__RequestVerificationToken"] = TokenFrom(html);
        form["Input.ReviewedPlanFingerprint"] = HiddenValue(
            PlanFingerprintPattern(),
            html,
            "reviewed plan fingerprint");
        using var post = await client.PostAsync(
            "/record/physical-gold-transfer?handler=Post",
            new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        return Assert.IsType<Uri>(post.Headers.Location).OriginalString;
    }

    private static Dictionary<string, string> PurchaseForm(
        WealthLedgerApiFactory factory,
        GoldReferences references,
        FormPage page)
        => new()
        {
            ["__RequestVerificationToken"] = page.Token,
            ["Input.IdempotencyKey"] = page.CommandKey,
            ["Input.PortfolioId"] = factory.ReadySetup.PortfolioId.ToString("D"),
            ["Input.GoldAccountId"] = references.SourceVaultId.ToString("D"),
            ["Input.CashAccountId"] = factory.ReadySetup.AccountId.ToString("D"),
            ["Input.GoldAssetId"] = references.GoldAssetId.ToString("D"),
            ["Input.CashAssetId"] = factory.ReadySetup.CashAssetId.ToString("D"),
            ["Input.CounterpartyInstitutionId"] = references.CounterpartyId.ToString("D"),
            ["Input.GrossWeight"] = "20",
            ["Input.FinenessChoice"] = "916",
            ["Input.PieceCount"] = "2",
            ["Input.UnitPrice"] = "100",
            ["Input.CashConsideration"] = "2000",
            ["Input.ExecutionDate"] = "2026-09-14",
            ["Input.Hallmark"] = "916",
            ["Input.CertificateReference"] = "SYNTHETIC-UI-CERTIFICATE",
            ["Input.LotNote"] = "Sentetik homojen iki parçalı grup.",
            ["Input.ExternalReference"] = "SYNTHETIC-UI-GOLD-PURCHASE",
            ["Input.Note"] = "Sentetik fiziki altın alımı.",
            ["Input.Costs[0].TypeCode"] = "MAKING_CHARGE",
            ["Input.Costs[0].TreatmentCode"] = "INCLUDED_IN_CONSIDERATION",
            ["Input.Costs[0].Amount"] = "50",
            ["Input.Costs[1].TypeCode"] = "COMMISSION",
            ["Input.Costs[1].TreatmentCode"] = "ADDITIONAL_CASH_OUTFLOW",
            ["Input.Costs[1].Amount"] = "10"
        };

    private static Dictionary<string, string> SaleForm(
        WealthLedgerApiFactory factory,
        GoldReferences references,
        FormPage page,
        Guid lotId,
        string grossWeight,
        string pieceCount,
        string keySuffix)
        => new()
        {
            ["__RequestVerificationToken"] = page.Token,
            ["Input.IdempotencyKey"] = page.CommandKey,
            ["Input.PortfolioId"] = factory.ReadySetup.PortfolioId.ToString("D"),
            ["Input.GoldAccountId"] = references.SourceVaultId.ToString("D"),
            ["Input.CashAccountId"] = factory.ReadySetup.AccountId.ToString("D"),
            ["Input.GoldAssetId"] = references.GoldAssetId.ToString("D"),
            ["Input.CashAssetId"] = factory.ReadySetup.CashAssetId.ToString("D"),
            ["Input.CounterpartyInstitutionId"] = references.CounterpartyId.ToString("D"),
            ["Input.ExecutionDate"] = "2026-09-15",
            ["Input.CashConsideration"] = "1000",
            ["Input.ExternalReference"] = $"SYNTHETIC-UI-{keySuffix}",
            ["Input.Note"] = "Sentetik seçili lot satışı.",
            ["Input.SelectedLots[0].AssetLotId"] = lotId.ToString("D"),
            ["Input.SelectedLots[0].GrossWeight"] = grossWeight,
            ["Input.SelectedLots[0].PieceCount"] = pieceCount
        };

    private static Dictionary<string, string> TransferForm(
        WealthLedgerApiFactory factory,
        GoldReferences references,
        FormPage page,
        Guid lotId,
        string grossWeight,
        string pieceCount)
        => new()
        {
            ["__RequestVerificationToken"] = page.Token,
            ["Input.IdempotencyKey"] = page.CommandKey,
            ["Input.SourcePortfolioId"] = factory.ReadySetup.PortfolioId.ToString("D"),
            ["Input.SourceGoldAccountId"] = references.SourceVaultId.ToString("D"),
            ["Input.DestinationPortfolioId"] = factory.ReadySetup.PortfolioId.ToString("D"),
            ["Input.DestinationGoldAccountId"] = references.DestinationVaultId.ToString("D"),
            ["Input.GoldAssetId"] = references.GoldAssetId.ToString("D"),
            ["Input.ExecutionDate"] = "2026-09-15",
            ["Input.ExternalReference"] = "SYNTHETIC-UI-TRANSFER",
            ["Input.Note"] = "Sentetik saklama transferi.",
            ["Input.SelectedLots[0].AssetLotId"] = lotId.ToString("D"),
            ["Input.SelectedLots[0].GrossWeight"] = grossWeight,
            ["Input.SelectedLots[0].PieceCount"] = pieceCount
        };

    private static async Task<GoldReferences> SeedReferencesAsync(
        WealthLedgerApiFactory factory)
    {
        var sourceVaultId = Guid.NewGuid();
        var destinationVaultId = Guid.NewGuid();
        var goldAssetId = Guid.NewGuid();
        var counterpartyId = Guid.NewGuid();
        await using var context = factory.CreateDbContext();
        context.Accounts.AddRange(
            new AccountRow
            {
                Id = sourceVaultId,
                HouseholdId = factory.ReadySetup.HouseholdId,
                Code = "UI_GOLD_SOURCE",
                Name = "Sentetik Altın Kasası",
                Type = AccountType.PhysicalVault,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            },
            new AccountRow
            {
                Id = destinationVaultId,
                HouseholdId = factory.ReadySetup.HouseholdId,
                Code = "UI_GOLD_DESTINATION",
                Name = "Sentetik Hedef Kasa",
                Type = AccountType.PhysicalVault,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            });
        context.Assets.Add(new AssetRow
        {
            Id = goldAssetId,
            Code = "UI_PHYSICAL_GOLD",
            Name = "Sentetik 22 Ayar Bilezik",
            Type = AssetType.PhysicalGold,
            BaseUnit = AssetUnit.GrossGram,
            BaseCurrencyCode = "TRY",
            LotTrackingMode = LotTrackingMode.Required,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        context.Institutions.Add(new InstitutionRow
        {
            Id = counterpartyId,
            Code = "UI_SYNTHETIC_JEWELER",
            Name = "Sentetik Kuyumcu",
            Type = InstitutionType.Jeweler,
            IsActive = true
        });
        await context.SaveChangesAsync();
        return new GoldReferences(
            sourceVaultId,
            destinationVaultId,
            goldAssetId,
            counterpartyId);
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

    private static HttpClient CreateClient(WealthLedgerApiFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

    private static string Decode(string html) => WebUtility.HtmlDecode(html);

    private static string TokenFrom(string html)
        => Decode(HiddenValue(
            AntiforgeryTokenPattern(),
            html,
            "antiforgery token"));

    private static string HiddenValue(
        Regex pattern,
        string html,
        string description)
    {
        var match = pattern.Match(html);
        Assert.True(match.Success, $"The form did not render its {description}.");
        return Decode(match.Groups[1].Value);
    }

    private sealed record FormPage(
        string Html,
        string Token,
        string CommandKey);

    private sealed record GoldReferences(
        Guid SourceVaultId,
        Guid DestinationVaultId,
        Guid GoldAssetId,
        Guid CounterpartyId);

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
}
