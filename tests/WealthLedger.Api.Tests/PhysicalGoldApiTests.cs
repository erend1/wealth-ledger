using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Api.Contracts;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.PhysicalGold;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Api.Tests;

public sealed class PhysicalGoldApiTests
{
    private static readonly DateOnly ExecutionDate =
        new(2026, 9, 14);

    [Fact]
    public async Task CompleteLifecycle_PreviewsPostsReplaysReadsAndReverses()
    {
        using var factory = new WealthLedgerApiFactory();
        var gold = await SeedGoldReferencesAsync(factory);
        using var client = CreateClient(factory);
        var purchaseRequest = PurchaseRequest(factory.ReadySetup, gold);

        using var purchasePreviewResponse = await client.PostAsJsonAsync(
            "/api/ledger/physical-gold-purchases/preview",
            purchaseRequest);
        Assert.Equal(HttpStatusCode.OK, purchasePreviewResponse.StatusCode);
        var purchasePreview = await purchasePreviewResponse.Content
            .ReadFromJsonAsync<PhysicalGoldPurchasePreviewResponse>();
        Assert.NotNull(purchasePreview);
        Assert.Equal(20_00000000L, purchasePreview.GrossWeightRawE8);
        Assert.Equal(916_000, purchasePreview.FinenessPpm);
        Assert.Equal(18.32m, purchasePreview.FineWeightGrams);
        Assert.Equal(201_000, purchasePreview.Economics.AcquisitionLotCostMinorUnits);

        await using (var beforePost = factory.CreateDbContext())
        {
            Assert.False(await beforePost.LedgerTransactions.AnyAsync(x =>
                x.Type == Domain.Ledger.TransactionType.Buy));
        }

        using var purchaseResponse = await PostWithKeyAsync(
            client,
            "/api/ledger/physical-gold-purchases",
            purchaseRequest,
            "api-gold-purchase-1");
        Assert.Equal(HttpStatusCode.Created, purchaseResponse.StatusCode);
        var purchase = await purchaseResponse.Content
            .ReadFromJsonAsync<RecordPhysicalGoldActivityResponse>();
        Assert.NotNull(purchase);
        Assert.NotNull(purchase.AssetLotId);
        Assert.Equal(purchase.TransactionLocation, purchaseResponse.Headers.Location?.OriginalString);
        Assert.Contains(purchase.TransactionId.ToString("D"), purchase.VerificationLocation);

        using var replayResponse = await PostWithKeyAsync(
            client,
            "/api/ledger/physical-gold-purchases",
            purchaseRequest,
            "api-gold-purchase-1");
        var replay = await replayResponse.Content
            .ReadFromJsonAsync<RecordPhysicalGoldActivityResponse>();
        Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
        Assert.Equal(purchase.TransactionId, replay!.TransactionId);
        Assert.Equal(purchase.AssetLotId, replay.AssetLotId);

        var changedPurchase = purchaseRequest with
        {
            CashConsiderationMinorUnits = 200_001
        };
        using var conflictResponse = await PostWithKeyAsync(
            client,
            "/api/ledger/physical-gold-purchases",
            changedPurchase,
            "api-gold-purchase-1");
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
        Assert.Equal(
            IdempotencyConflictException.ErrorCode,
            await ReadProblemCodeAsync(conflictResponse));

        using var purchaseVerificationResponse = await client.GetAsync(
            purchase.VerificationLocation);
        var purchaseVerification = await purchaseVerificationResponse.Content
            .ReadFromJsonAsync<PhysicalGoldActivityVerificationResponse>();
        Assert.Equal(HttpStatusCode.OK, purchaseVerificationResponse.StatusCode);
        Assert.Equal("BUY", purchaseVerification!.TransactionTypeCode);
        Assert.Equal(2, purchaseVerification.PieceCount);
        Assert.Equal(gold.CounterpartyId, purchaseVerification.CounterpartyInstitutionId);
        Assert.Equal("Synthetic Jeweler", purchaseVerification.CounterpartyName);
        Assert.Equal("916", purchaseVerification.Allocations.Single().Hallmark);

        var saleRequest = SaleRequest(
            factory.ReadySetup,
            gold,
            purchase.AssetLotId.Value,
            8_00000000L,
            1);
        using var salePreviewResponse = await client.PostAsJsonAsync(
            "/api/ledger/physical-gold-sales/preview",
            saleRequest);
        var salePreview = await salePreviewResponse.Content
            .ReadFromJsonAsync<PhysicalGoldSalePreviewResponse>();
        Assert.Equal(HttpStatusCode.OK, salePreviewResponse.StatusCode);
        Assert.NotNull(salePreview);
        Assert.Single(salePreview.Plan);
        Assert.Equal(7.328m, salePreview.Plan[0].MovedFineWeightGrams);
        Assert.Equal(80_400, salePreview.RealizedCost.KnownAmounts.Single().MinorUnits);
        Assert.False(string.IsNullOrWhiteSpace(salePreview.PlanFingerprint));

        saleRequest = saleRequest with
        {
            ReviewedPlanFingerprint = salePreview.PlanFingerprint
        };
        using var saleResponse = await PostWithKeyAsync(
            client,
            "/api/ledger/physical-gold-sales",
            saleRequest,
            "api-gold-sale-1");
        var sale = await saleResponse.Content
            .ReadFromJsonAsync<RecordPhysicalGoldActivityResponse>();
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        Assert.Null(sale!.AssetLotId);

        using var reversalPreviewResponse = await client.GetAsync(
            $"/api/ledger/transactions/{sale.TransactionId:D}/reversal-preview");
        var reversalPreview = await reversalPreviewResponse.Content
            .ReadFromJsonAsync<ReversalPreviewResponse>();
        Assert.Equal(HttpStatusCode.OK, reversalPreviewResponse.StatusCode);
        var inverseSale = Assert.Single(reversalPreview!.InverseLotAllocations);
        Assert.Equal(8_00000000L, inverseSale.QuantityDeltaRawE8);
        Assert.Equal(1, inverseSale.PhysicalGoldPieceDelta);

        using var reverseResponse = await PostWithKeyAsync(
            client,
            $"/api/ledger/transactions/{sale.TransactionId:D}/reversals",
            new ReversePostedTransactionRequest(
                "Synthetic sale correction."),
            "api-gold-sale-reversal-1");
        Assert.Equal(HttpStatusCode.Created, reverseResponse.StatusCode);

        var transferRequest = TransferRequest(
            factory.ReadySetup,
            gold,
            purchase.AssetLotId.Value,
            12_00000000L,
            1);
        using var transferPreviewResponse = await client.PostAsJsonAsync(
            "/api/ledger/physical-gold-transfers/preview",
            transferRequest);
        var transferPreview = await transferPreviewResponse.Content
            .ReadFromJsonAsync<PhysicalGoldTransferPreviewResponse>();
        Assert.Equal(HttpStatusCode.OK, transferPreviewResponse.StatusCode);
        Assert.Equal(10.992m, transferPreview!.FineWeightGrams);
        transferRequest = transferRequest with
        {
            ReviewedPlanFingerprint = transferPreview.PlanFingerprint
        };

        using var transferResponse = await PostWithKeyAsync(
            client,
            "/api/ledger/physical-gold-transfers",
            transferRequest,
            "api-gold-transfer-1");
        var transfer = await transferResponse.Content
            .ReadFromJsonAsync<RecordPhysicalGoldActivityResponse>();
        Assert.Equal(HttpStatusCode.Created, transferResponse.StatusCode);

        using var transferVerificationResponse = await client.GetAsync(
            transfer!.VerificationLocation);
        var transferVerification = await transferVerificationResponse.Content
            .ReadFromJsonAsync<PhysicalGoldActivityVerificationResponse>();
        Assert.Equal(HttpStatusCode.OK, transferVerificationResponse.StatusCode);
        Assert.Equal("TRANSFER", transferVerification!.TransactionTypeCode);
        Assert.Equal(
            [-1, 1],
            transferVerification.Allocations
                .Select(x => x.PieceDelta)
                .Order()
                .ToArray());

        using var custodyResponse = await client.GetAsync(
            $"/api/households/{factory.ReadySetup.HouseholdId:D}/physical-gold/custody");
        var custody = await custodyResponse.Content
            .ReadFromJsonAsync<PhysicalGoldCustodyInventoryResponse>();
        Assert.Equal(HttpStatusCode.OK, custodyResponse.StatusCode);
        Assert.Equal(2, custody!.Items.Count);
        var sourcePosition = Assert.Single(
            custody.Items,
            x => x.AccountId == gold.SourceVaultId);
        Assert.Equal(8_00000000L, sourcePosition.GrossWeightRawE8);
        Assert.Equal(1, sourcePosition.PieceCount);
        var destinationPosition = Assert.Single(
            custody.Items,
            x => x.AccountId == gold.DestinationVaultId);
        Assert.Equal(12_00000000L, destinationPosition.GrossWeightRawE8);
        Assert.Equal(1, destinationPosition.PieceCount);

        await using var persisted = factory.CreateDbContext();
        Assert.Equal(1, await persisted.AssetLots.CountAsync(x =>
            x.AssetId == gold.GoldAssetId));
        Assert.Equal(4, await persisted.CommandReceipts.CountAsync(x =>
            x.OperationCode == LedgerOperationCodes.RecordPhysicalGoldPurchase
            || x.OperationCode == LedgerOperationCodes.RecordPhysicalGoldSale
            || x.OperationCode == LedgerOperationCodes.RecordPhysicalGoldTransfer
            || x.OperationCode == LedgerOperationCodes.ReversePostedTransaction));
    }

    [Theory]
    [InlineData(0L, 916_000, "PHYSICAL_GOLD_QUANTITY_INVALID")]
    [InlineData(1_00000000L, 0, "PHYSICAL_GOLD_FINENESS_INVALID")]
    public async Task PurchasePreview_InvalidExactEvidenceReturnsStableValidation(
        long grossWeightRawE8,
        int finenessPpm,
        string expectedCode)
    {
        using var factory = new WealthLedgerApiFactory();
        var gold = await SeedGoldReferencesAsync(factory);
        using var client = CreateClient(factory);
        var request = PurchaseRequest(factory.ReadySetup, gold) with
        {
            GrossWeightRawE8 = grossWeightRawE8,
            FinenessPpm = finenessPpm
        };

        using var response = await client.PostAsJsonAsync(
            "/api/ledger/physical-gold-purchases/preview",
            request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(expectedCode, await ReadProblemCodeAsync(response));
    }

    [Fact]
    public async Task Posting_RequiresExactlyOneIdempotencyKey()
    {
        using var factory = new WealthLedgerApiFactory();
        var gold = await SeedGoldReferencesAsync(factory);
        using var client = CreateClient(factory);
        var request = PurchaseRequest(factory.ReadySetup, gold);

        using var missing = await client.PostAsJsonAsync(
            "/api/ledger/physical-gold-purchases",
            request);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        using var duplicateRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/ledger/physical-gold-purchases")
        {
            Content = JsonContent.Create(request)
        };
        duplicateRequest.Headers.TryAddWithoutValidation(
            "Idempotency-Key",
            new[] { "duplicate-a", "duplicate-b" });
        using var duplicate = await client.SendAsync(duplicateRequest);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        await using var context = factory.CreateDbContext();
        Assert.False(await context.CommandReceipts.AnyAsync(x =>
            x.OperationCode == LedgerOperationCodes.RecordPhysicalGoldPurchase));
    }

    [Fact]
    public async Task Purchase_NonGoldAssetAndPartialPriceAreRejectedWithoutWrites()
    {
        using var factory = new WealthLedgerApiFactory();
        var gold = await SeedGoldReferencesAsync(factory);
        using var client = CreateClient(factory);

        var nonGold = PurchaseRequest(factory.ReadySetup, gold) with
        {
            GoldAssetId = factory.ReadySetup.FundAssetId
        };
        using var nonGoldResponse = await PostWithKeyAsync(
            client,
            "/api/ledger/physical-gold-purchases",
            nonGold,
            "api-non-gold");
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            nonGoldResponse.StatusCode);
        Assert.Equal(
            PhysicalGoldErrorCodes.ReferenceShapeInvalid,
            await ReadProblemCodeAsync(nonGoldResponse));

        var partialPrice = PurchaseRequest(factory.ReadySetup, gold) with
        {
            ExecutedUnitPriceRawE8 = 10_00000000L,
            PriceCurrencyCode = null
        };
        using var partialPriceResponse = await PostWithKeyAsync(
            client,
            "/api/ledger/physical-gold-purchases",
            partialPrice,
            "api-partial-price");
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            partialPriceResponse.StatusCode);
        Assert.Equal(
            PhysicalGoldErrorCodes.PriceInvalid,
            await ReadProblemCodeAsync(partialPriceResponse));

        await using var context = factory.CreateDbContext();
        Assert.False(await context.CommandReceipts.AnyAsync(x =>
            x.IdempotencyKey == "api-non-gold"
            || x.IdempotencyKey == "api-partial-price"));
    }

    [Fact]
    public async Task StaleSaleReturnsSanitizedConflictAndLeavesNoPartialGraph()
    {
        using var factory = new WealthLedgerApiFactory();
        var gold = await SeedGoldReferencesAsync(factory);
        using var client = CreateClient(factory);
        var purchase = await PostAndReadAsync(
            client,
            "/api/ledger/physical-gold-purchases",
            PurchaseRequest(factory.ReadySetup, gold),
            "api-stale-purchase");
        var lotId = purchase.AssetLotId!.Value;

        var sale = SaleRequest(
            factory.ReadySetup,
            gold,
            lotId,
            15_00000000L,
            1);
        using (var previewResponse = await client.PostAsJsonAsync(
                   "/api/ledger/physical-gold-sales/preview",
                   sale))
        {
            var preview = await previewResponse.Content
                .ReadFromJsonAsync<PhysicalGoldSalePreviewResponse>();
            sale = sale with
            {
                ReviewedPlanFingerprint = preview!.PlanFingerprint
            };
        }

        var transfer = TransferRequest(
            factory.ReadySetup,
            gold,
            lotId,
            15_00000000L,
            1);
        using (var previewResponse = await client.PostAsJsonAsync(
                   "/api/ledger/physical-gold-transfers/preview",
                   transfer))
        {
            var preview = await previewResponse.Content
                .ReadFromJsonAsync<PhysicalGoldTransferPreviewResponse>();
            transfer = transfer with
            {
                ReviewedPlanFingerprint = preview!.PlanFingerprint
            };
        }

        using var transferResponse = await PostWithKeyAsync(
            client,
            "/api/ledger/physical-gold-transfers",
            transfer,
            "api-stale-transfer");
        Assert.Equal(HttpStatusCode.Created, transferResponse.StatusCode);

        using var staleResponse = await PostWithKeyAsync(
            client,
            "/api/ledger/physical-gold-sales",
            sale,
            "api-stale-sale");
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        Assert.Equal(
            PhysicalGoldErrorCodes.InsufficientGrossWeight,
            await ReadProblemCodeAsync(staleResponse));
        var problemText = await staleResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(factory.DatabasePath, problemText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SYNTHETIC-CERT-PRIVATE", problemText, StringComparison.Ordinal);
        Assert.DoesNotContain(sale.ReviewedPlanFingerprint!, problemText, StringComparison.Ordinal);
        Assert.DoesNotContain("SQLite", problemText, StringComparison.OrdinalIgnoreCase);

        await using var context = factory.CreateDbContext();
        Assert.False(await context.CommandReceipts.AnyAsync(x =>
            x.IdempotencyKey == "api-stale-sale"));
        Assert.False(await context.LedgerTransactions.AnyAsync(x =>
            x.Type == Domain.Ledger.TransactionType.Sell));
    }

    [Fact]
    public async Task VerificationBoundariesRejectFundAndOtherHousehold()
    {
        using var factory = new WealthLedgerApiFactory();
        var fund = await factory.SeedReadyUiLedgerAsync();
        var gold = await SeedGoldReferencesAsync(factory);
        using var client = CreateClient(factory);
        var purchase = await PostAndReadAsync(
            client,
            "/api/ledger/physical-gold-purchases",
            PurchaseRequest(factory.ReadySetup, gold),
            "api-boundary-gold-purchase");

        using var fundAsGold = await client.GetAsync(
            $"/api/households/{factory.ReadySetup.HouseholdId:D}/ledger/physical-gold-activities/{fund.PurchaseTransactionId:D}/verification");
        Assert.Equal(HttpStatusCode.NotFound, fundAsGold.StatusCode);
        Assert.Equal(
            PhysicalGoldErrorCodes.NotFound,
            await ReadProblemCodeAsync(fundAsGold));

        using var goldAsFund = await client.GetAsync(
            $"/api/households/{factory.ReadySetup.HouseholdId:D}/ledger/fund-trades/{purchase.TransactionId:D}/verification");
        Assert.Equal(HttpStatusCode.NotFound, goldAsFund.StatusCode);

        using var otherHousehold = await client.GetAsync(
            $"/api/households/{Guid.NewGuid():D}/ledger/physical-gold-activities/{purchase.TransactionId:D}/verification");
        Assert.Equal(HttpStatusCode.NotFound, otherHousehold.StatusCode);
        Assert.Equal(
            PhysicalGoldErrorCodes.NotFound,
            await ReadProblemCodeAsync(otherHousehold));
    }

    [Fact]
    public async Task EquivalentRetryWinsBeforeReferenceRevalidation()
    {
        using var factory = new WealthLedgerApiFactory();
        var gold = await SeedGoldReferencesAsync(factory);
        using var client = CreateClient(factory);
        var request = PurchaseRequest(factory.ReadySetup, gold);
        var original = await PostAndReadAsync(
            client,
            "/api/ledger/physical-gold-purchases",
            request,
            "api-replay-after-archive");

        await using (var context = factory.CreateDbContext())
        {
            (await context.Assets.SingleAsync(x =>
                    x.Id == gold.GoldAssetId))
                .IsActive = false;
            (await context.Institutions.SingleAsync(x =>
                    x.Id == gold.CounterpartyId))
                .IsActive = false;
            await context.SaveChangesAsync();
        }

        using var replayResponse = await PostWithKeyAsync(
            client,
            "/api/ledger/physical-gold-purchases",
            request,
            "api-replay-after-archive");
        var replay = await replayResponse.Content
            .ReadFromJsonAsync<RecordPhysicalGoldActivityResponse>();

        Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
        Assert.Equal(original.TransactionId, replay!.TransactionId);
        Assert.Equal(original.AssetLotId, replay.AssetLotId);
    }

    [Fact]
    public async Task RoutesAreNotMappedOutsideReadyMode()
    {
        using var factory = new WealthLedgerApiFactory(
            ApiTestStartupMode.WorkspaceUninitialized);
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync(
            "/api/ledger/physical-gold-purchases/preview",
            new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static HttpClient CreateClient(WealthLedgerApiFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

    private static async Task<GoldReferences> SeedGoldReferencesAsync(
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
                Code = $"API_GOLD_SOURCE_{sourceVaultId:N}",
                Name = "Synthetic Gold Source",
                Type = AccountType.PhysicalVault,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            },
            new AccountRow
            {
                Id = destinationVaultId,
                HouseholdId = factory.ReadySetup.HouseholdId,
                Code = $"API_GOLD_DEST_{destinationVaultId:N}",
                Name = "Synthetic Gold Destination",
                Type = AccountType.PhysicalVault,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            });
        context.Assets.Add(new AssetRow
        {
            Id = goldAssetId,
            Code = $"API_GOLD_{goldAssetId:N}",
            Name = "Synthetic Physical Gold",
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
            Code = $"API_JEWELER_{counterpartyId:N}",
            Name = "Synthetic Jeweler",
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

    private static PhysicalGoldPurchaseRequest PurchaseRequest(
        InitializeCoreLedgerResponse setup,
        GoldReferences gold)
        => new(
            setup.HouseholdId,
            setup.PortfolioId,
            gold.SourceVaultId,
            setup.AccountId,
            gold.GoldAssetId,
            setup.CashAssetId,
            20_00000000L,
            916_000,
            2,
            200_000,
            "TRY",
            ExecutionDate,
            CounterpartyInstitutionId: gold.CounterpartyId,
            Costs:
            [
                new PhysicalGoldCostRequest(
                    "MAKING_CHARGE",
                    "INCLUDED_IN_CONSIDERATION",
                    5_000,
                    "TRY"),
                new PhysicalGoldCostRequest(
                    "COMMISSION",
                    "ADDITIONAL_CASH_OUTFLOW",
                    1_000,
                    "TRY")
            ],
            Hallmark: "916",
            CertificateReference: "SYNTHETIC-CERT-PRIVATE",
            ExternalReference: "SYNTHETIC-API-GOLD-PURCHASE",
            Note: "Synthetic API purchase explanation.");

    private static PhysicalGoldSaleRequest SaleRequest(
        InitializeCoreLedgerResponse setup,
        GoldReferences gold,
        Guid lotId,
        long grossWeightRawE8,
        int pieceCount)
        => new(
            setup.HouseholdId,
            setup.PortfolioId,
            gold.SourceVaultId,
            setup.AccountId,
            gold.GoldAssetId,
            setup.CashAssetId,
            grossWeightRawE8,
            pieceCount,
            [new PhysicalGoldSelectedLotRequest(
                lotId,
                grossWeightRawE8,
                pieceCount)],
            100_000,
            "TRY",
            ExecutionDate,
            CounterpartyInstitutionId: gold.CounterpartyId,
            ExternalReference: "SYNTHETIC-API-GOLD-SALE",
            Note: "Synthetic API sale explanation.");

    private static PhysicalGoldTransferRequest TransferRequest(
        InitializeCoreLedgerResponse setup,
        GoldReferences gold,
        Guid lotId,
        long grossWeightRawE8,
        int pieceCount)
        => new(
            setup.HouseholdId,
            setup.PortfolioId,
            gold.SourceVaultId,
            setup.PortfolioId,
            gold.DestinationVaultId,
            gold.GoldAssetId,
            grossWeightRawE8,
            pieceCount,
            [new PhysicalGoldSelectedLotRequest(
                lotId,
                grossWeightRawE8,
                pieceCount)],
            ExecutionDate,
            ExternalReference: "SYNTHETIC-API-GOLD-TRANSFER",
            Note: "Synthetic API transfer explanation.");

    private static async Task<HttpResponseMessage> PostWithKeyAsync<T>(
        HttpClient client,
        string uri,
        T body,
        string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    private static async Task<RecordPhysicalGoldActivityResponse>
        PostAndReadAsync<T>(
            HttpClient client,
            string uri,
            T body,
            string idempotencyKey)
    {
        using var response = await PostWithKeyAsync(
            client,
            uri,
            body,
            idempotencyKey);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content
            .ReadFromJsonAsync<RecordPhysicalGoldActivityResponse>())!;
    }

    private static async Task<string?> ReadProblemCodeAsync(
        HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var code)
            ? code.GetString()
            : null;
    }

    private sealed record GoldReferences(
        Guid SourceVaultId,
        Guid DestinationVaultId,
        Guid GoldAssetId,
        Guid CounterpartyId);
}
