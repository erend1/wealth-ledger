using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Api.Contracts;
using WealthLedger.Application.Setup;

namespace WealthLedger.Api.Tests;

public sealed class OpeningBalanceApiTests
{
    [Fact]
    public async Task CashPreviewPostReplayAndVerification_AreExactAndRetrySafe()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var request = CreateCashRequest(factory.ReadySetup);

        var previewResponse = await client.PostAsJsonAsync(
            "/api/ledger/opening-balances/preview",
            request);

        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.Content
            .ReadFromJsonAsync<OpeningBalancePreviewResponse>();
        Assert.NotNull(preview);
        Assert.Equal(1_234_567_000_000, preview.QuantityRawE8);
        Assert.Equal(0, preview.AllocationTotalRawE8);
        Assert.Equal("NOT_APPLICABLE", preview.UnlottedCostBasisStatusCode);
        Assert.Null(preview.TotalFineWeightGramsExact);
        Assert.Empty(preview.Lots);
        Assert.True(preview.AllocationsReconcile);

        await using (var previewContext = factory.CreateDbContext())
        {
            Assert.Equal(0, await previewContext.LedgerTransactions.CountAsync());
            Assert.Equal(0, await previewContext.CommandReceipts.CountAsync());
        }

        using var missingKeyResponse = await client.PostAsJsonAsync(
            "/api/ledger/opening-balances",
            request);
        Assert.Equal(HttpStatusCode.BadRequest, missingKeyResponse.StatusCode);
        Assert.Equal(
            "IDEMPOTENCY_KEY_REQUIRED",
            await ReadProblemCodeAsync(missingKeyResponse));

        using var firstResponse = await PostOpeningAsync(
            client,
            request,
            "synthetic-cash-opening-001");
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var first = await firstResponse.Content
            .ReadFromJsonAsync<RecordOpeningBalanceResponse>();
        Assert.NotNull(first);
        Assert.Empty(first.AssetLotIds);
        Assert.Equal(request.QuantityRawE8, first.SubmittedQuantityRawE8);
        Assert.Equal(request.QuantityRawE8, first.PersistedQuantityRawE8);
        Assert.Equal(
            $"/api/ledger/transactions/{first.TransactionId:D}",
            firstResponse.Headers.Location?.OriginalString);

        using var replayResponse = await PostOpeningAsync(
            client,
            request,
            "synthetic-cash-opening-001");
        Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
        var replay = await replayResponse.Content
            .ReadFromJsonAsync<RecordOpeningBalanceResponse>();
        Assert.Equal(first.TransactionId, replay!.TransactionId);

        var verification = await client.GetFromJsonAsync<
            OpeningBalanceVerificationResponse>(first.VerificationLocation);
        Assert.NotNull(verification);
        Assert.Equal(first.TransactionId, verification.Transaction.TransactionId);
        Assert.Equal("OPENING_BALANCE", verification.Transaction.TypeCode);
        Assert.Equal(request.QuantityRawE8, verification.PersistedQuantityRawE8);
        Assert.Equal(request.QuantityRawE8, verification.CurrentPositionRawE8);
        Assert.True(verification.CurrentPositionEqualsOpeningQuantity);
        Assert.False(verification.IsIndependentlyReconciled);

        using var duplicateResponse = await PostOpeningAsync(
            client,
            request,
            "synthetic-cash-opening-002");
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        Assert.Equal(
            "OPENING_BALANCE_ALREADY_EXISTS",
            await ReadProblemCodeAsync(duplicateResponse));

        await using var verificationContext = factory.CreateDbContext();
        Assert.Equal(1, await verificationContext.LedgerTransactions.CountAsync());
        Assert.Equal(1, await verificationContext.TransactionEntries.CountAsync());
        Assert.Equal(1, await verificationContext.CommandReceipts.CountAsync());
    }

    [Fact]
    public async Task FundOpening_PreservesKnownAndUnknownLotEvidence()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var setup = factory.ReadySetup;
        var request = new OpeningBalanceRequest(
            setup.HouseholdId,
            setup.PortfolioId,
            setup.AccountId,
            setup.FundAssetId,
            new DateOnly(2026, 8, 31),
            QuantityRawE8: 12_345_678_900,
            ExternalReference: "SYNTHETIC-FUND-STATEMENT",
            Note: "Synthetic fund opening source.",
            Lots:
            [
                new OpeningBalanceLotRequest(
                    2_345_678_900,
                    new DateOnly(2025, 1, 10),
                    "KNOWN",
                    OriginalCostBasisMinorUnits: 12_345,
                    CostBasisCurrencyCode: "TRY",
                    PhysicalGoldDetail: null),
                new OpeningBalanceLotRequest(
                    10_000_000_000,
                    AcquiredOn: null,
                    "UNKNOWN",
                    OriginalCostBasisMinorUnits: null,
                    CostBasisCurrencyCode: null,
                    PhysicalGoldDetail: null)
            ]);

        var previewResponse = await client.PostAsJsonAsync(
            "/api/ledger/opening-balances/preview",
            request);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.Content
            .ReadFromJsonAsync<OpeningBalancePreviewResponse>();
        Assert.NotNull(preview);
        Assert.Equal(request.QuantityRawE8, preview.AllocationTotalRawE8);
        Assert.Contains(preview.Lots, lot => lot.CostBasisStatusCode == "KNOWN");
        Assert.Contains(preview.Lots, lot => lot.CostBasisStatusCode == "UNKNOWN");
        Assert.All(preview.Lots, lot => Assert.Null(lot.FineWeightGramsExact));

        using var response = await PostOpeningAsync(
            client,
            request,
            "synthetic-fund-opening-api-001");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var posted = await response.Content
            .ReadFromJsonAsync<RecordOpeningBalanceResponse>();
        Assert.Equal(2, posted!.AssetLotIds.Count);

        var detail = await client.GetFromJsonAsync<LedgerTransactionResponse>(
            response.Headers.Location);
        Assert.NotNull(detail);
        Assert.Equal(2, detail.CreatedLots.Count);
        Assert.All(detail.CreatedLots, lot => Assert.Null(lot.PhysicalGoldDetail));
        Assert.All(detail.Entries, entry => Assert.Null(entry.UnitPriceRawE8));
    }

    [Fact]
    public async Task GoldReferenceCreationAndOpening_ReturnExactFineWeightString()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var setup = factory.ReadySetup;

        var accountResponse = await client.PutAsJsonAsync(
            $"/api/households/{setup.HouseholdId:D}/opening-balance-reference-data/accounts/SYNTHETIC_VAULT",
            new CreateOpeningBalanceAccountRequest(
                InstitutionId: null,
                Name: "Synthetic Physical Vault",
                TypeCode: "PHYSICAL_VAULT",
                OpenedOn: new DateOnly(2026, 1, 1)));
        Assert.Equal(HttpStatusCode.Created, accountResponse.StatusCode);
        var account = await accountResponse.Content
            .ReadFromJsonAsync<OpeningBalanceAccountResponse>();

        var assetResponse = await client.PutAsJsonAsync(
            "/api/opening-balance-reference-data/assets/SYNTHETIC_GOLD",
            new CreateOpeningBalanceAssetRequest(
                "Synthetic Physical Gold",
                "PHYSICAL_GOLD",
                "TRY",
                "REQUIRED"));
        Assert.Equal(HttpStatusCode.Created, assetResponse.StatusCode);
        var asset = await assetResponse.Content
            .ReadFromJsonAsync<OpeningBalanceAssetResponse>();

        var request = new OpeningBalanceRequest(
            setup.HouseholdId,
            setup.PortfolioId,
            account!.AccountId,
            asset!.AssetId,
            new DateOnly(2026, 8, 31),
            QuantityRawE8: 2_525_000_000,
            ExternalReference: "SYNTHETIC-GOLD-INVENTORY",
            Note: "Synthetic physical inventory opening.",
            Lots:
            [
                new OpeningBalanceLotRequest(
                    2_525_000_000,
                    AcquiredOn: null,
                    "UNKNOWN",
                    OriginalCostBasisMinorUnits: null,
                    CostBasisCurrencyCode: null,
                    new OpeningBalancePhysicalGoldDetailRequest(
                        FinenessPartsPerMillion: 916_000,
                        PieceCount: 2,
                        Hallmark: "SYNTHETIC-916",
                        CertificateReference: "SYNTHETIC-CERTIFICATE",
                        Note: "Two synthetic matching bracelets."))
            ]);

        var previewResponse = await client.PostAsJsonAsync(
            "/api/ledger/opening-balances/preview",
            request);
        var preview = await previewResponse.Content
            .ReadFromJsonAsync<OpeningBalancePreviewResponse>();
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        Assert.Equal("23.129", preview!.TotalFineWeightGramsExact);
        Assert.Equal("23.129", Assert.Single(preview.Lots).FineWeightGramsExact);

        using var postResponse = await PostOpeningAsync(
            client,
            request,
            "synthetic-gold-opening-api-001");
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        var detail = await client.GetFromJsonAsync<LedgerTransactionResponse>(
            postResponse.Headers.Location);
        var gold = Assert.IsType<LedgerTransactionPhysicalGoldResponse>(
            Assert.Single(detail!.CreatedLots).PhysicalGoldDetail);
        Assert.Equal(916_000, gold.FinenessPartsPerMillion);
        Assert.Equal(2, gold.PieceCount);
        Assert.Equal("23.129", gold.FineWeightGramsExact);
    }

    [Fact]
    public async Task NarrowReferencePut_IsNaturallyIdempotentAndConflictsSafely()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        const string path =
            "/api/opening-balance-reference-data/currencies/USD";
        var request = new CreateOpeningBalanceCurrencyRequest(
            "Synthetic US Dollar",
            MinorUnitDigits: 2);

        var first = await client.PutAsJsonAsync(path, request);
        var retry = await client.PutAsJsonAsync(path, request);
        var conflict = await client.PutAsJsonAsync(
            path,
            request with { MinorUnitDigits = 3 });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.False((await retry.Content.ReadFromJsonAsync<
            OpeningBalanceCurrencyResponse>())!.WasCreated);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(
            "OPENING_BALANCE_REFERENCE_CONFLICT",
            await ReadProblemCodeAsync(conflict));
    }

    [Fact]
    public async Task InvalidLotCostShape_ReturnsStableUnprocessableEntity()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var setup = factory.ReadySetup;
        var request = new OpeningBalanceRequest(
            setup.HouseholdId,
            setup.PortfolioId,
            setup.AccountId,
            setup.FundAssetId,
            new DateOnly(2026, 8, 31),
            QuantityRawE8: 100_000_000,
            ExternalReference: null,
            Note: "Synthetic invalid cost source.",
            Lots:
            [
                new OpeningBalanceLotRequest(
                    100_000_000,
                    AcquiredOn: null,
                    "UNKNOWN",
                    OriginalCostBasisMinorUnits: 1,
                    CostBasisCurrencyCode: "TRY",
                    PhysicalGoldDetail: null)
            ]);

        var response = await client.PostAsJsonAsync(
            "/api/ledger/opening-balances/preview",
            request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(
            "OPENING_BALANCE_COST_BASIS_INVALID",
            await ReadProblemCodeAsync(response));
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SQLite", text);
        Assert.DoesNotContain(factory.DatabasePath, text);
    }

    [Theory]
    [InlineData((int)ApiTestStartupMode.StorageUninitialized)]
    [InlineData((int)ApiTestStartupMode.WorkspaceUninitialized)]
    [InlineData((int)ApiTestStartupMode.InitialBackupRequired)]
    [InlineData((int)ApiTestStartupMode.Blocked)]
    public async Task OpeningBalanceApi_IsMappedOnlyInReadyMode(
        int modeValue)
    {
        using var factory = new WealthLedgerApiFactory(
            (ApiTestStartupMode)modeValue);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/ledger/opening-balances/preview",
            new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static OpeningBalanceRequest CreateCashRequest(
        InitializeCoreLedgerResponse setup)
        => new(
            setup.HouseholdId,
            setup.PortfolioId,
            setup.AccountId,
            setup.CashAssetId,
            new DateOnly(2026, 8, 31),
            QuantityRawE8: 1_234_567_000_000,
            ExternalReference: "SYNTHETIC-CASH-STATEMENT",
            Note: "Synthetic cash opening source.",
            Lots: []);

    private static async Task<HttpResponseMessage> PostOpeningAsync(
        HttpClient client,
        OpeningBalanceRequest request,
        string idempotencyKey)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/ledger/opening-balances")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(message);
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
}
