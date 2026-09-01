using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WealthLedger.Api.Contracts;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Ledger;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Api.Tests
{
    public sealed class ReversalApiTests
    {
        [Fact]
        public async Task
            ReversalPreview_EligibleContribution_ReturnsExactInverseEntry()
        {
            using var factory =
                new WealthLedgerApiFactory();

            using var client =
                factory.CreateClient();

            var setup =
                await InitializeCoreLedgerAsync(
                    client);

            var contribution =
                await PostContributionAsync(
                    client,
                    setup,
                    "preview-contribution");

            var response =
                await client.GetAsync(
                    $"/api/ledger/transactions/"
                    + $"{contribution.TransactionId}"
                    + "/reversal-preview");

            Assert.Equal(
                HttpStatusCode.OK,
                response.StatusCode);

            var preview =
                await response.Content
                    .ReadFromJsonAsync<
                        ReversalPreviewResponse>();

            Assert.NotNull(preview);

            Assert.Equal(
                contribution.TransactionId,
                preview!.OriginalTransactionId);

            Assert.True(
                preview.CanReverse);

            Assert.Equal(
                "ELIGIBLE",
                preview.EligibilityCode);

            Assert.Null(
                preview.ExistingReversalTransactionId);

            Assert.Empty(
                preview.BlockingTransactionIds);

            Assert.Empty(
                preview.InverseLotAllocations);

            var inverse =
                Assert.Single(
                    preview.InverseEntries);

            Assert.Equal(
                setup.CashAssetId,
                inverse.AssetId);

            Assert.Equal(
                "PRINCIPAL",
                inverse.EntryRoleCode);

            Assert.Equal(
                -100_000_000_000,
                inverse.QuantityDeltaRawE8);
        }

        [Fact]
        public async Task
            ReversalPreview_EligiblePurchase_ReturnsSameLotInverseAllocation()
        {
            using var factory =
                new WealthLedgerApiFactory();

            using var client =
                factory.CreateClient();

            var setup =
                await InitializeCoreLedgerAsync(
                    client);

            await PostContributionAsync(
                client,
                setup,
                "preview-purchase-contribution");

            var purchase =
                await PostPurchaseAsync(
                    client,
                    setup,
                    "preview-purchase");

            var purchaseRead =
                await GetTransactionAsync(
                    client,
                    purchase.TransactionId);

            var principal =
                purchaseRead.Entries.Single(
                    x =>
                        x.RoleCode
                        == "PRINCIPAL");

            var response =
                await client.GetAsync(
                    $"/api/ledger/transactions/"
                    + $"{purchase.TransactionId}"
                    + "/reversal-preview");

            Assert.Equal(
                HttpStatusCode.OK,
                response.StatusCode);

            var preview =
                await response.Content
                    .ReadFromJsonAsync<
                        ReversalPreviewResponse>();

            Assert.NotNull(preview);
            Assert.True(preview!.CanReverse);

            Assert.Equal(
                "ELIGIBLE",
                preview.EligibilityCode);

            var allocation =
                Assert.Single(
                    preview.InverseLotAllocations);

            Assert.Equal(
                purchase.AssetLotId,
                allocation.AssetLotId);

            Assert.Equal(
                principal.EntryId,
                allocation.OriginalTransactionEntryId);

            Assert.Equal(
                principal.EntrySequence,
                allocation.EntrySequence);

            Assert.Equal(
                -125_000_000,
                allocation.QuantityDeltaRawE8);
        }

        [Fact]
        public async Task
            ReversalPreview_UnknownTarget_ReturnsStable404()
        {
            using var factory =
                new WealthLedgerApiFactory();

            using var client =
                factory.CreateClient();

            var response =
                await client.GetAsync(
                    $"/api/ledger/transactions/"
                    + $"{Guid.NewGuid()}"
                    + "/reversal-preview");

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);

            var problem =
                await ReadProblemAsync(
                    response);

            Assert.Equal(
                "LEDGER_TRANSACTION_NOT_FOUND",
                GetStringExtension(
                    problem,
                    "code"));
        }

        [Fact]
        public async Task
            ReversalPreview_DraftTarget_ReturnsNotPosted()
        {
            using var factory =
                new WealthLedgerApiFactory();

            using var client =
                factory.CreateClient();

            var setup =
                await InitializeCoreLedgerAsync(
                    client);

            var transactionId =
                await SeedDraftAdjustmentAsync(
                    factory,
                    setup);

            var response =
                await client.GetAsync(
                    $"/api/ledger/transactions/"
                    + $"{transactionId}"
                    + "/reversal-preview");

            Assert.Equal(
                HttpStatusCode.OK,
                response.StatusCode);

            var preview =
                await response.Content
                    .ReadFromJsonAsync<
                        ReversalPreviewResponse>();

            Assert.NotNull(preview);

            Assert.False(
                preview!.CanReverse);

            Assert.Equal(
                "NOT_POSTED",
                preview.EligibilityCode);
        }

        [Fact]
        public async Task
            ReversalPreview_UnsupportedPersistedShape_ReturnsUnsupported()
        {
            using var factory =
                new WealthLedgerApiFactory();

            using var client =
                factory.CreateClient();

            var setup =
                await InitializeCoreLedgerAsync(
                    client);

            var transactionId =
                await SeedUnsupportedPostedAdjustmentAsync(
                    factory,
                    setup);

            var response =
                await client.GetAsync(
                    $"/api/ledger/transactions/"
                    + $"{transactionId}"
                    + "/reversal-preview");

            Assert.Equal(
                HttpStatusCode.OK,
                response.StatusCode);

            var preview =
                await response.Content
                    .ReadFromJsonAsync<
                        ReversalPreviewResponse>();

            Assert.NotNull(preview);

            Assert.False(
                preview!.CanReverse);

            Assert.Equal(
                "UNSUPPORTED_PERSISTED_SHAPE",
                preview.EligibilityCode);
        }

        [Fact]
        public async Task
            Reversal_WithoutIdempotencyKey_ReturnsStable400()
        {
            using var factory =
                new WealthLedgerApiFactory();

            using var client =
                factory.CreateClient();

            var setup =
                await InitializeCoreLedgerAsync(
                    client);

            var contribution =
                await PostContributionAsync(
                    client,
                    setup,
                    "header-seed");

            var response =
                await client.PostAsJsonAsync(
                    $"/api/ledger/transactions/"
                    + $"{contribution.TransactionId}"
                    + "/reversals",
                    new ReversePostedTransactionRequest(
                        "Incorrect amount."));

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);

            var problem =
                await ReadProblemAsync(
                    response);

            Assert.Equal(
                "IDEMPOTENCY_KEY_REQUIRED",
                GetStringExtension(
                    problem,
                    "code"));
        }

        [Fact]
        public async Task
            Reversal_WithOverlongIdempotencyKey_ReturnsStable400()
        {
            using var factory =
                new WealthLedgerApiFactory();

            using var client =
                factory.CreateClient();

            var setup =
                await InitializeCoreLedgerAsync(
                    client);

            var contribution =
                await PostContributionAsync(
                    client,
                    setup,
                    "invalid-header-seed");

            var response =
                await SendReversalAsync(
                    client,
                    contribution.TransactionId,
                    new string('x', 257),
                    "Incorrect amount.");

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);

            var problem =
                await ReadProblemAsync(
                    response);

            Assert.Equal(
                "IDEMPOTENCY_KEY_INVALID",
                GetStringExtension(
                    problem,
                    "code"));
        }

        [Theory]
        [InlineData(
            "",
            "REVERSAL_REASON_REQUIRED")]
        [InlineData(
            "   ",
            "REVERSAL_REASON_REQUIRED")]
        [InlineData(
            "Incorrect\namount.",
            "REVERSAL_REASON_INVALID")]
        public async Task
            Reversal_InvalidReason_ReturnsStable400(
                string reason,
                string expectedCode)
        {
            using var factory =
                new WealthLedgerApiFactory();

            using var client =
                factory.CreateClient();

            var setup =
                await InitializeCoreLedgerAsync(
                    client);

            var contribution =
                await PostContributionAsync(
                    client,
                    setup,
                    $"reason-seed-{Guid.NewGuid():N}");

            var response =
                await SendReversalAsync(
                    client,
                    contribution.TransactionId,
                    $"reason-key-{Guid.NewGuid():N}",
                    reason);

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);

            var problem =
                await ReadProblemAsync(
                    response);

            Assert.Equal(
                expectedCode,
                GetStringExtension(
                    problem,
                    "code"));
        }

        [Fact]
        public async Task
            Reversal_OverlongReason_ReturnsStable400()
        {
            using var factory =
                new WealthLedgerApiFactory();

            using var client =
                factory.CreateClient();

            var setup =
                await InitializeCoreLedgerAsync(
                    client);

            var contribution =
                await PostContributionAsync(
                    client,
                    setup,
                    "overlong-reason-seed");

            var response =
                await SendReversalAsync(
                    client,
                    contribution.TransactionId,
                    "overlong-reason-key",
                    new string('x', 2_001));

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);

            var problem =
                await ReadProblemAsync(
                    response);

            Assert.Equal(
                "REVERSAL_REASON_INVALID",
                GetStringExtension(
                    problem,
                    "code"));
        }

        [Fact]
        public async Task
            Reversal_UnknownTarget_ReturnsStable404()
        {
            using var factory =
                new WealthLedgerApiFactory();

            using var client =
                factory.CreateClient();

            var response =
                await SendReversalAsync(
                    client,
                    Guid.NewGuid(),
                    "unknown-target-key",
                    "Correction.");

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);

            var problem =
                await ReadProblemAsync(
                    response);

            Assert.Equal(
                "LEDGER_TRANSACTION_NOT_FOUND",
                GetStringExtension(
                    problem,
                    "code"));
        }

        [Fact]
        public async Task
            PurchaseReversal_RoundTripExactValues()
        {
            using var factory =
                new WealthLedgerApiFactory();

            using var client =
                factory.CreateClient();

            var setup =
                await InitializeCoreLedgerAsync(
                    client);

            await PostContributionAsync(
                client,
                setup,
                "roundtrip-contribution");

            var purchase =
                await PostPurchaseAsync(
                    client,
                    setup,
                    "roundtrip-purchase");

            var originalBefore =
                await GetTransactionAsync(
                    client,
                    purchase.TransactionId);

            var response =
                await SendReversalAsync(
                    client,
                    purchase.TransactionId,
                    "roundtrip-reversal",
                    "   Incorrect purchase quantity.   ");

            Assert.Equal(
                HttpStatusCode.Created,
                response.StatusCode);

            var body =
                await response.Content
                    .ReadFromJsonAsync<
                        ReversePostedTransactionResponse>();

            Assert.NotNull(body);

            Assert.Equal(
                purchase.TransactionId,
                body!.ReversalOfTransactionId);

            Assert.Equal(
                $"/api/ledger/transactions/"
                + $"{body.ReversalTransactionId}",
                response.Headers
                    .Location?
                    .OriginalString);

            var reversal =
                await GetTransactionAsync(
                    client,
                    body.ReversalTransactionId);

            var originalAfter =
                await GetTransactionAsync(
                    client,
                    purchase.TransactionId);

            Assert.Equal(
                "POSTED",
                originalAfter.StatusCode);

            Assert.Null(
                originalAfter.ReversalOfTransactionId);

            Assert.Equal(
                body.ReversalTransactionId,
                originalAfter.ReversedByTransactionId);

            Assert.Equal(
                "REVERSAL",
                reversal.TypeCode);

            Assert.Equal(
                "POSTED",
                reversal.StatusCode);

            Assert.Equal(
                purchase.TransactionId,
                reversal.ReversalOfTransactionId);

            Assert.Null(
                reversal.ReversedByTransactionId);

            Assert.Equal(
                originalBefore.OrderDate,
                reversal.OrderDate);

            Assert.Equal(
                originalBefore.ExecutionDate,
                reversal.ExecutionDate);

            Assert.Equal(
                originalBefore.SettlementDate,
                reversal.SettlementDate);

            Assert.Equal(
                "Incorrect purchase quantity.",
                reversal.Note);

            Assert.Null(
                reversal.ExternalReference);

            Assert.Null(
                reversal.CashFlow);

            Assert.Empty(
                reversal.Costs);

            Assert.Empty(
                reversal.CreatedLots);

            Assert.Equal(
                originalBefore.Entries.Count,
                reversal.Entries.Count);

            var originalEntries =
                originalBefore.Entries
                    .OrderBy(
                        x => x.EntrySequence)
                    .ToArray();

            var reversalEntries =
                reversal.Entries
                    .OrderBy(
                        x => x.EntrySequence)
                    .ToArray();

            for (var index = 0;
                 index < originalEntries.Length;
                 index++)
            {
                var originalEntry =
                    originalEntries[index];

                var reversalEntry =
                    reversalEntries[index];

                Assert.Equal(
                    originalEntry.EntrySequence,
                    reversalEntry.EntrySequence);

                Assert.Equal(
                    originalEntry.PortfolioId,
                    reversalEntry.PortfolioId);

                Assert.Equal(
                    originalEntry.AccountId,
                    reversalEntry.AccountId);

                Assert.Equal(
                    originalEntry.AssetId,
                    reversalEntry.AssetId);

                Assert.Equal(
                    originalEntry.RoleCode,
                    reversalEntry.RoleCode);

                Assert.Equal(
                    originalEntry.UnitPriceRawE8,
                    reversalEntry.UnitPriceRawE8);

                Assert.Equal(
                    originalEntry.PriceCurrencyCode,
                    reversalEntry.PriceCurrencyCode);

                Assert.Equal(
                    -originalEntry.QuantityDeltaRawE8,
                    reversalEntry.QuantityDeltaRawE8);
            }

            var originalAllocation =
                Assert.Single(
                    originalAfter.LotAllocations);

            var reversalAllocation =
                Assert.Single(
                    reversal.LotAllocations);

            Assert.Equal(
                purchase.AssetLotId,
                originalAllocation.AssetLotId);

            Assert.Equal(
                purchase.AssetLotId,
                reversalAllocation.AssetLotId);

            Assert.Equal(
                125_000_000,
                originalAllocation.QuantityDeltaRawE8);

            Assert.Equal(
                -125_000_000,
                reversalAllocation.QuantityDeltaRawE8);

            var fundPosition =
                await GetPositionAsync(
                    client,
                    setup,
                    setup.FundAssetId);

            var cashPosition =
                await GetPositionAsync(
                    client,
                    setup,
                    setup.CashAssetId);

            Assert.Equal(
                0,
                fundPosition.QuantityRawE8);

            Assert.Equal(
                100_000_000_000,
                cashPosition.QuantityRawE8);

            await using var scope =
                factory.Services.CreateAsyncScope();

            var context =
                scope.ServiceProvider
                    .GetRequiredService<
                        WealthLedgerDbContext>();

            Assert.Equal(
                1,
                await context.AssetLots
                    .AsNoTracking()
                    .CountAsync());

            var lotSum =
                await GetPostedLotQuantityAsync(
                    context,
                    purchase.AssetLotId);

            Assert.Equal(
                0,
                lotSum);
        }

        [Fact]
        public async Task
            Reversal_EquivalentReplay_ReturnsSameBodyLocationAndTimestamps()
        {
            using var factory =
                new WealthLedgerApiFactory();

            using var client =
                factory.CreateClient();

            var setup =
                await InitializeCoreLedgerAsync(
                    client);

            await PostContributionAsync(
                client,
                setup,
                "replay-contribution");

            var purchase =
                await PostPurchaseAsync(
                    client,
                    setup,
                    "replay-purchase");

            const string key =
                "reversal-replay-key";

            var firstResponse =
                await SendReversalAsync(
                    client,
                    purchase.TransactionId,
                    key,
                    "Incorrect quantity.");

            Assert.Equal(
                HttpStatusCode.Created,
                firstResponse.StatusCode);

            var firstBody =
                await firstResponse.Content
                    .ReadFromJsonAsync<
                        ReversePostedTransactionResponse>();

            Assert.NotNull(firstBody);

            var firstRead =
                await GetTransactionAsync(
                    client,
                    firstBody!.ReversalTransactionId);

            var firstAllocation =
                Assert.Single(
                    firstRead.LotAllocations);

            var secondResponse =
                await SendReversalAsync(
                    client,
                    purchase.TransactionId,
                    key,
                    "   Incorrect quantity.   ");

            Assert.Equal(
                HttpStatusCode.Created,
                secondResponse.StatusCode);

            var secondBody =
                await secondResponse.Content
                    .ReadFromJsonAsync<
                        ReversePostedTransactionResponse>();

            Assert.NotNull(secondBody);

            Assert.Equal(
                firstBody,
                secondBody);

            Assert.Equal(
                firstResponse.Headers.Location,
                secondResponse.Headers.Location);

            var secondRead =
                await GetTransactionAsync(
                    client,
                    secondBody!.ReversalTransactionId);

            var secondAllocation =
                Assert.Single(
                    secondRead.LotAllocations);

            Assert.Equal(
                firstRead.CreatedAtUtc,
                secondRead.CreatedAtUtc);

            Assert.Equal(
                firstRead.PostedAtUtc,
                secondRead.PostedAtUtc);

            Assert.Equal(
                firstAllocation.AllocationId,
                secondAllocation.AllocationId);

            Assert.Equal(
                firstAllocation.CreatedAtUtc,
                secondAllocation.CreatedAtUtc);

            await using var scope =
                factory.Services.CreateAsyncScope();

            var context =
                scope.ServiceProvider
                    .GetRequiredService<
                        WealthLedgerDbContext>();

            Assert.Equal(
                1,
                await context.LedgerTransactions
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.Type
                            == TransactionType.Reversal
                            && x.ReversalOfTransactionId
                            == purchase.TransactionId));

            Assert.Equal(
                1,
                await context.CommandReceipts
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.OperationCode
                            == LedgerOperationCodes
                                .ReversePostedTransaction
                            && x.IdempotencyKey
                            == key));
        }

        [Fact]
        public async Task
            Reversal_SameKeyChangedReason_ReturnsIdempotencyConflict()
        {
            using var factory =
                new WealthLedgerApiFactory();

            using var client =
                factory.CreateClient();

            var setup =
                await InitializeCoreLedgerAsync(
                    client);

            var contribution =
                await PostContributionAsync(
                    client,
                    setup,
                    "changed-reason-contribution");

            const string key =
                "changed-reason-reversal";

            var first =
                await SendReversalAsync(
                    client,
                    contribution.TransactionId,
                    key,
                    "Incorrect amount.");

            Assert.Equal(
                HttpStatusCode.Created,
                first.StatusCode);

            var conflict =
                await SendReversalAsync(
                    client,
                    contribution.TransactionId,
                    key,
                    "Incorrect date.");

            Assert.Equal(
                HttpStatusCode.Conflict,
                conflict.StatusCode);

            var problem =
                await ReadProblemAsync(
                    conflict);

            Assert.Equal(
                "IDEMPOTENCY_KEY_CONFLICT",
                GetStringExtension(
                    problem,
                    "code"));

            AssertSanitized(
                problem);
        }

        [Fact]
        public async Task
            Reversal_NewKeyAlreadyReversed_ReturnsExistingReversalIdentity()
        {
            using var factory =
                new WealthLedgerApiFactory();

            using var client =
                factory.CreateClient();

            var setup =
                await InitializeCoreLedgerAsync(
                    client);

            var contribution =
                await PostContributionAsync(
                    client,
                    setup,
                    "already-reversed-contribution");

            var first =
                await SendReversalAsync(
                    client,
                    contribution.TransactionId,
                    "already-reversed-first",
                    "Incorrect amount.");

            Assert.Equal(
                HttpStatusCode.Created,
                first.StatusCode);

            var firstBody =
                await first.Content
                    .ReadFromJsonAsync<
                        ReversePostedTransactionResponse>();

            Assert.NotNull(firstBody);

            var second =
                await SendReversalAsync(
                    client,
                    contribution.TransactionId,
                    "already-reversed-second",
                    "Incorrect amount.");

            Assert.Equal(
                HttpStatusCode.Conflict,
                second.StatusCode);

            var problem =
                await ReadProblemAsync(
                    second);

            Assert.Equal(
                "TRANSACTION_ALREADY_REVERSED",
                GetStringExtension(
                    problem,
                    "code"));

            Assert.Equal(
                firstBody!.ReversalTransactionId,
                GetGuidExtension(
                    problem,
                    "existingReversalTransactionId"));

            AssertSanitized(
                problem);

            await using var scope =
                factory.Services.CreateAsyncScope();

            var context =
                scope.ServiceProvider
                    .GetRequiredService<
                        WealthLedgerDbContext>();

            Assert.Equal(
                0,
                await context.CommandReceipts
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.OperationCode
                            == LedgerOperationCodes
                                .ReversePostedTransaction
                            && x.IdempotencyKey
                            == "already-reversed-second"));
        }

        [Fact]
        public async Task
            ReversalPreview_AfterReversal_ReportsAlreadyReversedAndTargetIsReversal()
        {
            using var factory =
                new WealthLedgerApiFactory();

            using var client =
                factory.CreateClient();

            var setup =
                await InitializeCoreLedgerAsync(
                    client);

            var contribution =
                await PostContributionAsync(
                    client,
                    setup,
                    "preview-after-reversal-contribution");

            var reversalResponse =
                await SendReversalAsync(
                    client,
                    contribution.TransactionId,
                    "preview-after-reversal",
                    "Incorrect amount.");

            var reversal =
                await reversalResponse.Content
                    .ReadFromJsonAsync<
                        ReversePostedTransactionResponse>();

            Assert.NotNull(reversal);

            var originalPreviewResponse =
                await client.GetAsync(
                    $"/api/ledger/transactions/"
                    + $"{contribution.TransactionId}"
                    + "/reversal-preview");

            var originalPreview =
                await originalPreviewResponse.Content
                    .ReadFromJsonAsync<
                        ReversalPreviewResponse>();

            Assert.NotNull(originalPreview);

            Assert.Equal(
                "ALREADY_REVERSED",
                originalPreview!.EligibilityCode);

            Assert.Equal(
                reversal!.ReversalTransactionId,
                originalPreview
                    .ExistingReversalTransactionId);

            var reversalPreviewResponse =
                await client.GetAsync(
                    $"/api/ledger/transactions/"
                    + $"{reversal.ReversalTransactionId}"
                    + "/reversal-preview");

            var reversalPreview =
                await reversalPreviewResponse.Content
                    .ReadFromJsonAsync<
                        ReversalPreviewResponse>();

            Assert.NotNull(reversalPreview);

            Assert.Equal(
                "TARGET_IS_REVERSAL",
                reversalPreview!.EligibilityCode);
        }

        [Fact]
        public async Task
            Reversal_DependencyBlocksThenPostedReversalUnblocksOriginal()
        {
            using var factory =
                new WealthLedgerApiFactory();

            using var client =
                factory.CreateClient();

            var setup =
                await InitializeCoreLedgerAsync(
                    client);

            await PostContributionAsync(
                client,
                setup,
                "dependency-contribution");

            var purchase =
                await PostPurchaseAsync(
                    client,
                    setup,
                    "dependency-purchase");

            var dependentTransactionId =
                await SeedPostedDependentAdjustmentAsync(
                    factory,
                    setup,
                    purchase.AssetLotId);

            var previewResponse =
                await client.GetAsync(
                    $"/api/ledger/transactions/"
                    + $"{purchase.TransactionId}"
                    + "/reversal-preview");

            var preview =
                await previewResponse.Content
                    .ReadFromJsonAsync<
                        ReversalPreviewResponse>();

            Assert.NotNull(preview);

            Assert.False(
                preview!.CanReverse);

            Assert.Equal(
                "BLOCKED_BY_DEPENDENCIES",
                preview.EligibilityCode);

            Assert.Equal(
                new[]
                {
                dependentTransactionId
                },
                preview.BlockingTransactionIds);

            const string originalKey =
                "dependency-original-reversal";

            var blockedResponse =
                await SendReversalAsync(
                    client,
                    purchase.TransactionId,
                    originalKey,
                    "Correct purchase after dependency.");

            Assert.Equal(
                HttpStatusCode.Conflict,
                blockedResponse.StatusCode);

            var blockedProblem =
                await ReadProblemAsync(
                    blockedResponse);

            Assert.Equal(
                "REVERSAL_DEPENDENCY_CONFLICT",
                GetStringExtension(
                    blockedProblem,
                    "code"));

            Assert.Equal(
                new[]
                {
                dependentTransactionId
                },
                GetGuidArrayExtension(
                    blockedProblem,
                    "blockingTransactionIds"));

            var blockerReadResponse =
                await client.GetAsync(
                    $"/api/ledger/transactions/"
                    + $"{dependentTransactionId}");

            Assert.Equal(
                HttpStatusCode.OK,
                blockerReadResponse.StatusCode);

            // Reverse the downstream transaction through the same generic
            // public reversal endpoint.
            var childReversalResponse =
                await SendReversalAsync(
                    client,
                    dependentTransactionId,
                    "dependency-child-reversal",
                    "Undo dependent adjustment.");

            Assert.Equal(
                HttpStatusCode.Created,
                childReversalResponse.StatusCode);

            // The previous original reversal command wrote no receipt,
            // therefore retrying the same key is still a legitimate first commit.
            var unblockedResponse =
                await SendReversalAsync(
                    client,
                    purchase.TransactionId,
                    originalKey,
                    "Correct purchase after dependency.");

            Assert.Equal(
                HttpStatusCode.Created,
                unblockedResponse.StatusCode);

            var fundPosition =
                await GetPositionAsync(
                    client,
                    setup,
                    setup.FundAssetId);

            Assert.Equal(
                0,
                fundPosition.QuantityRawE8);

            await using var scope =
                factory.Services.CreateAsyncScope();

            var context =
                scope.ServiceProvider
                    .GetRequiredService<
                        WealthLedgerDbContext>();

            Assert.Equal(
                1,
                await context.CommandReceipts
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.OperationCode
                            == LedgerOperationCodes
                                .ReversePostedTransaction
                            && x.IdempotencyKey
                            == originalKey));

            Assert.Equal(
                0,
                await GetPostedLotQuantityAsync(
                    context,
                    purchase.AssetLotId));
        }

        [Fact]
        public async Task
            Reversal_ThenCorrectedPurchase_CreatesSeparateAuditableFacts()
        {
            using var factory =
                new WealthLedgerApiFactory();

            using var client =
                factory.CreateClient();

            var setup =
                await InitializeCoreLedgerAsync(
                    client);

            await PostContributionAsync(
                client,
                setup,
                "replacement-contribution");

            var wrongPurchase =
                await PostPurchaseAsync(
                    client,
                    setup,
                    "replacement-wrong-purchase");

            var reversalResponse =
                await SendReversalAsync(
                    client,
                    wrongPurchase.TransactionId,
                    "replacement-reversal",
                    "Replace incorrect purchase.");

            Assert.Equal(
                HttpStatusCode.Created,
                reversalResponse.StatusCode);

            var reversal =
                await reversalResponse.Content
                    .ReadFromJsonAsync<
                        ReversePostedTransactionResponse>();

            Assert.NotNull(reversal);

            var correctedPurchase =
                await PostPurchaseAsync(
                    client,
                    setup,
                    "replacement-corrected-purchase");

            Assert.NotEqual(
                wrongPurchase.TransactionId,
                correctedPurchase.TransactionId);

            Assert.NotEqual(
                wrongPurchase.TransactionId,
                reversal!.ReversalTransactionId);

            Assert.NotEqual(
                correctedPurchase.TransactionId,
                reversal.ReversalTransactionId);

            var wrongRead =
                await GetTransactionAsync(
                    client,
                    wrongPurchase.TransactionId);

            var reversalRead =
                await GetTransactionAsync(
                    client,
                    reversal.ReversalTransactionId);

            var correctedRead =
                await GetTransactionAsync(
                    client,
                    correctedPurchase.TransactionId);

            Assert.Equal(
                "POSTED",
                wrongRead.StatusCode);

            Assert.Equal(
                reversal.ReversalTransactionId,
                wrongRead.ReversedByTransactionId);

            Assert.Equal(
                wrongPurchase.TransactionId,
                reversalRead.ReversalOfTransactionId);

            Assert.Null(
                correctedRead.ReversalOfTransactionId);

            Assert.Null(
                correctedRead.ReversedByTransactionId);

            var position =
                await GetPositionAsync(
                    client,
                    setup,
                    setup.FundAssetId);

            Assert.Equal(
                125_000_000,
                position.QuantityRawE8);

            await using var scope =
                factory.Services.CreateAsyncScope();

            var context =
                scope.ServiceProvider
                    .GetRequiredService<
                        WealthLedgerDbContext>();

            Assert.Equal(
                2,
                await context.LedgerTransactions
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.Type
                            == TransactionType.Buy));

            Assert.Equal(
                1,
                await context.LedgerTransactions
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.Type
                            == TransactionType.Reversal
                            && x.ReversalOfTransactionId
                            == wrongPurchase.TransactionId));
        }

        [Fact]
        public async Task
            Reversal_ConcurrentEquivalentRequests_CreateOneReversalAndReturnSameIdentity()
        {
            using var factory =
                new WealthLedgerApiFactory();

            using var setupClient =
                factory.CreateClient();

            var setup =
                await InitializeCoreLedgerAsync(
                    setupClient);

            await PostContributionAsync(
                setupClient,
                setup,
                "concurrent-equivalent-contribution");

            var purchase =
                await PostPurchaseAsync(
                    setupClient,
                    setup,
                    "concurrent-equivalent-purchase");

            using var firstClient =
                factory.CreateClient();

            using var secondClient =
                factory.CreateClient();

            const string key =
                "concurrent-equivalent-reversal";

            var responses =
                await Task.WhenAll(
                    SendReversalAsync(
                        firstClient,
                        purchase.TransactionId,
                        key,
                        "Concurrent reversal."),

                    SendReversalAsync(
                        secondClient,
                        purchase.TransactionId,
                        key,
                        "Concurrent reversal."));

            Assert.All(
                responses,
                response =>
                    Assert.Equal(
                        HttpStatusCode.Created,
                        response.StatusCode));

            var firstBody =
                await responses[0].Content
                    .ReadFromJsonAsync<
                        ReversePostedTransactionResponse>();

            var secondBody =
                await responses[1].Content
                    .ReadFromJsonAsync<
                        ReversePostedTransactionResponse>();

            Assert.NotNull(firstBody);
            Assert.NotNull(secondBody);

            Assert.Equal(
                firstBody!.ReversalTransactionId,
                secondBody!.ReversalTransactionId);

            Assert.Equal(
                responses[0].Headers.Location,
                responses[1].Headers.Location);

            await using var scope =
                factory.Services.CreateAsyncScope();

            var context =
                scope.ServiceProvider
                    .GetRequiredService<
                        WealthLedgerDbContext>();

            Assert.Equal(
                1,
                await context.LedgerTransactions
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.Type
                            == TransactionType.Reversal
                            && x.ReversalOfTransactionId
                            == purchase.TransactionId));

            Assert.Equal(
                1,
                await context.CommandReceipts
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.OperationCode
                            == LedgerOperationCodes
                                .ReversePostedTransaction
                            && x.IdempotencyKey
                            == key));
        }

        [Fact]
        public async Task
            Reversal_ConcurrentDifferentKeys_CreateOneWinnerAndOneStableConflict()
        {
            using var factory =
                new WealthLedgerApiFactory();

            using var setupClient =
                factory.CreateClient();

            var setup =
                await InitializeCoreLedgerAsync(
                    setupClient);

            await PostContributionAsync(
                setupClient,
                setup,
                "concurrent-different-contribution");

            var purchase =
                await PostPurchaseAsync(
                    setupClient,
                    setup,
                    "concurrent-different-purchase");

            using var firstClient =
                factory.CreateClient();

            using var secondClient =
                factory.CreateClient();

            var responses =
                await Task.WhenAll(
                    SendReversalAsync(
                        firstClient,
                        purchase.TransactionId,
                        "different-race-a",
                        "Concurrent reversal."),

                    SendReversalAsync(
                        secondClient,
                        purchase.TransactionId,
                        "different-race-b",
                        "Concurrent reversal."));

            var created =
                Assert.Single(
                    responses,
                    x =>
                        x.StatusCode
                        == HttpStatusCode.Created);

            var conflict =
                Assert.Single(
                    responses,
                    x =>
                        x.StatusCode
                        == HttpStatusCode.Conflict);

            var winner =
                await created.Content
                    .ReadFromJsonAsync<
                        ReversePostedTransactionResponse>();

            Assert.NotNull(winner);

            var problem =
                await ReadProblemAsync(
                    conflict);

            Assert.Equal(
                "TRANSACTION_ALREADY_REVERSED",
                GetStringExtension(
                    problem,
                    "code"));

            Assert.Equal(
                winner!.ReversalTransactionId,
                GetGuidExtension(
                    problem,
                    "existingReversalTransactionId"));

            AssertSanitized(
                problem);

            await using var scope =
                factory.Services.CreateAsyncScope();

            var context =
                scope.ServiceProvider
                    .GetRequiredService<
                        WealthLedgerDbContext>();

            Assert.Equal(
                1,
                await context.LedgerTransactions
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.Type
                            == TransactionType.Reversal
                            && x.ReversalOfTransactionId
                            == purchase.TransactionId));

            Assert.Equal(
                1,
                await context.CommandReceipts
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.OperationCode
                            == LedgerOperationCodes
                                .ReversePostedTransaction
                            && (
                                x.IdempotencyKey
                                    == "different-race-a"
                                || x.IdempotencyKey
                                    == "different-race-b")));
        }

        private static async Task<
            InitializeCoreLedgerResponse>
            InitializeCoreLedgerAsync(
                HttpClient client)
        {
            var response =
                await client.PostAsJsonAsync(
                    "/api/setup/core-ledger",
                    ApiTestData.CreateSetupRequest());

            Assert.Equal(
                HttpStatusCode.Created,
                response.StatusCode);

            var setup =
                await response.Content
                    .ReadFromJsonAsync<
                        InitializeCoreLedgerResponse>();

            return Assert.IsType<
                InitializeCoreLedgerResponse>(
                    setup);
        }

        private static async Task<
            RecordContributionResponse>
            PostContributionAsync(
                HttpClient client,
                InitializeCoreLedgerResponse setup,
                string key)
        {
            using var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    "/api/ledger/contributions");

            request.Headers.Add(
                "Idempotency-Key",
                key);

            request.Content =
                JsonContent.Create(
                    CreateContributionRequest(
                        setup));

            using var response =
                await client.SendAsync(
                    request);

            Assert.Equal(
                HttpStatusCode.Created,
                response.StatusCode);

            var result =
                await response.Content
                    .ReadFromJsonAsync<
                        RecordContributionResponse>();

            return Assert.IsType<
                RecordContributionResponse>(
                    result);
        }

        private static async Task<
            RecordFundPurchaseResponse>
            PostPurchaseAsync(
                HttpClient client,
                InitializeCoreLedgerResponse setup,
                string key)
        {
            using var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    "/api/ledger/fund-purchases");

            request.Headers.Add(
                "Idempotency-Key",
                key);

            request.Content =
                JsonContent.Create(
                    CreateFundPurchaseRequest(
                        setup));

            using var response =
                await client.SendAsync(
                    request);

            Assert.Equal(
                HttpStatusCode.Created,
                response.StatusCode);

            var result =
                await response.Content
                    .ReadFromJsonAsync<
                        RecordFundPurchaseResponse>();

            return Assert.IsType<
                RecordFundPurchaseResponse>(
                    result);
        }

        private static async Task<HttpResponseMessage>
            SendReversalAsync(
                HttpClient client,
                Guid transactionId,
                string idempotencyKey,
                string reason)
        {
            var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    $"/api/ledger/transactions/"
                    + $"{transactionId}"
                    + "/reversals");

            request.Headers.Add(
                "Idempotency-Key",
                idempotencyKey);

            request.Content =
                JsonContent.Create(
                    new ReversePostedTransactionRequest(
                        reason));

            return await client.SendAsync(
                request);
        }

        private static async Task<
            LedgerTransactionResponse>
            GetTransactionAsync(
                HttpClient client,
                Guid transactionId)
        {
            var response =
                await client.GetAsync(
                    $"/api/ledger/transactions/"
                    + $"{transactionId}");

            Assert.Equal(
                HttpStatusCode.OK,
                response.StatusCode);

            var result =
                await response.Content
                    .ReadFromJsonAsync<
                        LedgerTransactionResponse>();

            return Assert.IsType<
                LedgerTransactionResponse>(
                    result);
        }

        private static async Task<
            PositionResponse>
            GetPositionAsync(
                HttpClient client,
                InitializeCoreLedgerResponse setup,
                Guid assetId)
        {
            var response =
                await client.GetAsync(
                    $"/api/households/{setup.HouseholdId}"
                    + $"/portfolios/{setup.PortfolioId}"
                    + $"/accounts/{setup.AccountId}"
                    + $"/positions/{assetId}");

            Assert.Equal(
                HttpStatusCode.OK,
                response.StatusCode);

            var result =
                await response.Content
                    .ReadFromJsonAsync<
                        PositionResponse>();

            return Assert.IsType<
                PositionResponse>(
                    result);
        }

        private static RecordContributionRequest
            CreateContributionRequest(
                InitializeCoreLedgerResponse setup)
        {
            return new RecordContributionRequest(
                setup.HouseholdId,
                setup.PortfolioId,
                setup.AccountId,
                setup.CashAssetId,

                AmountMinorUnits:
                    100_000,

                CurrencyCode:
                    "TRY",

                CashFlowCategoryCode:
                    "ACADEMIC_INCOME",

                ApiTestData.ExecutionDate,
                setup.HouseholdMemberId,

                ExternalReference:
                    "REVERSAL-TEST-CONTRIBUTION",

                Note:
                    "Synthetic reversal test contribution");
        }

        private static RecordFundPurchaseRequest
            CreateFundPurchaseRequest(
                InitializeCoreLedgerResponse setup)
        {
            return new RecordFundPurchaseRequest(
                setup.HouseholdId,
                setup.PortfolioId,
                setup.AccountId,
                setup.FundAssetId,
                setup.CashAssetId,

                FundQuantityRawE8:
                    125_000_000,

                ExecutedUnitPriceRawE8:
                    20_000_000_000,

                PriceCurrencyCode:
                    "TRY",

                CashConsiderationMinorUnits:
                    25_000,

                CashConsiderationCurrencyCode:
                    "TRY",

                ApiTestData.ExecutionDate,

                ExternalReference:
                    "REVERSAL-TEST-PURCHASE",

                Note:
                    "Synthetic reversal test purchase");
        }

        private static async Task<Guid>
            SeedDraftAdjustmentAsync(
                WealthLedgerApiFactory factory,
                InitializeCoreLedgerResponse setup)
        {
            await using var scope =
                factory.Services.CreateAsyncScope();

            var context =
                scope.ServiceProvider
                    .GetRequiredService<
                        WealthLedgerDbContext>();

            var transactionId =
                Guid.NewGuid();

            context.LedgerTransactions.Add(
                new LedgerTransactionRow
                {
                    Id =
                        transactionId,

                    HouseholdId =
                        setup.HouseholdId,

                    Type =
                        TransactionType.Adjustment,

                    Status =
                        TransactionStatus.Draft,

                    ExecutionDate =
                        ApiTestData.ExecutionDate,

                    CreatedAtUtc =
                        DateTime.UtcNow
                });

            await context.SaveChangesAsync();

            return transactionId;
        }

        private static async Task<Guid>
            SeedUnsupportedPostedAdjustmentAsync(
                WealthLedgerApiFactory factory,
                InitializeCoreLedgerResponse setup)
        {
            await using var scope =
                factory.Services.CreateAsyncScope();

            var context =
                scope.ServiceProvider
                    .GetRequiredService<
                        WealthLedgerDbContext>();

            var now =
                DateTime.UtcNow;

            var transactionId =
                Guid.NewGuid();

            var entryId =
                Guid.NewGuid();

            // This graph is structurally valid for SQLite,
            // but the Note is deliberately non-canonical.
            //
            // Reconstitution normalizes it to:
            // "Legacy noncanonical note."
            //
            // and therefore recognizes the persisted shape
            // as unsupported rather than attempting a raw reversal.
            context.LedgerTransactions.Add(
                new LedgerTransactionRow
                {
                    Id =
                        transactionId,

                    HouseholdId =
                        setup.HouseholdId,

                    Type =
                        TransactionType.Adjustment,

                    Status =
                        TransactionStatus.Draft,

                    ExecutionDate =
                        ApiTestData.ExecutionDate,

                    Note =
                        "  Legacy noncanonical note.  ",

                    CreatedAtUtc =
                        now
                });

            context.TransactionEntries.Add(
                new TransactionEntryRow
                {
                    Id =
                        entryId,

                    TransactionId =
                        transactionId,

                    EntrySequence =
                        0,

                    PortfolioId =
                        setup.PortfolioId,

                    AccountId =
                        setup.AccountId,

                    AssetId =
                        setup.CashAssetId,

                    QuantityDeltaE8 =
                        1,

                    Role =
                        EntryRole.Adjustment,

                    CreatedAtUtc =
                        now
                });

            await context.SaveChangesAsync();

            var transaction =
                await context.LedgerTransactions
                    .SingleAsync(
                        x =>
                            x.Id
                            == transactionId);

            transaction.Status =
                TransactionStatus.Posted;

            transaction.PostedAtUtc =
                now.AddSeconds(1);

            await context.SaveChangesAsync();

            return transactionId;
        }

        private static async Task<Guid>
            SeedPostedDependentAdjustmentAsync(
                WealthLedgerApiFactory factory,
                InitializeCoreLedgerResponse setup,
                Guid lotId)
        {
            await using var scope =
                factory.Services.CreateAsyncScope();

            var context =
                scope.ServiceProvider
                    .GetRequiredService<
                        WealthLedgerDbContext>();

            var transactionId =
                Guid.NewGuid();

            var entryId =
                Guid.NewGuid();

            var now =
                DateTime.UtcNow;

            context.LedgerTransactions.Add(
                new LedgerTransactionRow
                {
                    Id =
                        transactionId,

                    HouseholdId =
                        setup.HouseholdId,

                    Type =
                        TransactionType.Adjustment,

                    Status =
                        TransactionStatus.Draft,

                    ExecutionDate =
                        ApiTestData.ExecutionDate,

                    CreatedAtUtc =
                        now
                });

            context.TransactionEntries.Add(
                new TransactionEntryRow
                {
                    Id =
                        entryId,

                    TransactionId =
                        transactionId,

                    EntrySequence =
                        0,

                    PortfolioId =
                        setup.PortfolioId,

                    AccountId =
                        setup.AccountId,

                    AssetId =
                        setup.FundAssetId,

                    QuantityDeltaE8 =
                        -25_000_000,

                    Role =
                        EntryRole.Adjustment,

                    CreatedAtUtc =
                        now
                });

            context.LotEntryAllocations.Add(
                new LotEntryAllocationRow
                {
                    Id =
                        Guid.NewGuid(),

                    AssetLotId =
                        lotId,

                    TransactionEntryId =
                        entryId,

                    QuantityDeltaE8 =
                        -25_000_000,

                    CreatedAtUtc =
                        now
                });

            await context.SaveChangesAsync();

            var transaction =
                await context.LedgerTransactions
                    .SingleAsync(
                        x =>
                            x.Id
                            == transactionId);

            transaction.Status =
                TransactionStatus.Posted;

            transaction.PostedAtUtc =
                now.AddSeconds(1);

            await context.SaveChangesAsync();

            return transactionId;
        }

        private static async Task<
            ProblemDetails>
            ReadProblemAsync(
                HttpResponseMessage response)
        {
            var problem =
                await response.Content
                    .ReadFromJsonAsync<
                        ProblemDetails>();

            return Assert.IsType<
                ProblemDetails>(
                    problem);
        }

        private static string GetStringExtension(
            ProblemDetails problem,
            string key)
        {
            Assert.True(
                problem.Extensions.TryGetValue(
                    key,
                    out var value));

            return value switch
            {
                string text =>
                    text,

                JsonElement
                {
                    ValueKind:
                        JsonValueKind.String
                } element =>
                    element.GetString()
                    ?? throw new InvalidOperationException(
                        $"Problem extension '{key}' was null."),

                _ =>
                    throw new InvalidOperationException(
                        $"Problem extension '{key}' was not a string.")
            };
        }

        private static Guid GetGuidExtension(
            ProblemDetails problem,
            string key)
        {
            return Guid.Parse(
                GetStringExtension(
                    problem,
                    key));
        }

        private static IReadOnlyList<Guid>
            GetGuidArrayExtension(
                ProblemDetails problem,
                string key)
        {
            Assert.True(
                problem.Extensions.TryGetValue(
                    key,
                    out var value));

            if (value is JsonElement
                {
                    ValueKind:
                        JsonValueKind.Array
                } element)
            {
                return element
                    .EnumerateArray()
                    .Select(
                        x =>
                            x.GetGuid())
                    .ToArray();
            }

            if (value
                is IEnumerable<Guid> ids)
            {
                return ids.ToArray();
            }

            throw new InvalidOperationException(
                $"Problem extension '{key}' was not a GUID array.");
        }

        private static void AssertSanitized(
            ProblemDetails problem)
        {
            var text =
                $"{problem.Title} {problem.Detail}";

            Assert.DoesNotContain(
                "SQLite",
                text,
                StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(
                "DbUpdate",
                text,
                StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(
                "TR_LedgerTransaction",
                text,
                StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(
                "ConnectionString",
                text,
                StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(
                "TransactionEntryRow",
                text,
                StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<long>
            GetPostedLotQuantityAsync(
                WealthLedgerDbContext context,
                Guid lotId)
        {
            return await (
                from allocation
                    in context.LotEntryAllocations
                        .AsNoTracking()
                join entry
                    in context.TransactionEntries
                        .AsNoTracking()
                    on allocation.TransactionEntryId
                    equals entry.Id
                join transaction
                    in context.LedgerTransactions
                        .AsNoTracking()
                    on entry.TransactionId
                    equals transaction.Id
                where
                    allocation.AssetLotId
                    == lotId
                    && transaction.Status
                    == TransactionStatus.Posted
                select allocation.QuantityDeltaE8
            ).SumAsync();
        }
    }
}
