using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;
using WealthLedger.Infrastructure.Persistence;

namespace WealthLedger.Infrastructure.Tests.Persistence
{
    public sealed class LedgerSubmissionStoreTests
    {
        private const string IdempotencyKey =
            "submission-test-001";

        private const string FingerprintValue =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        private static readonly DateTimeOffset RecordedAtUtc =
            new(
                2026,
                8,
                27,
                8,
                0,
                0,
                TimeSpan.Zero);

        [Fact]
        public async Task Receipt_RoundTripsAcrossFreshDbContext()
        {
            await using var database =
                await SqliteTestDatabase.CreateAsync();

            await using (var seedContext = database.CreateContext())
            {
                await CoreLedgerTestData.SeedMasterDataAsync(
                    seedContext);
            }

            var transactionId = Guid.NewGuid();

            var transaction =
                CreatePostedContribution(transactionId);

            var receipt =
                CreateReceipt(
                    transactionId,
                    LedgerOperationCodes.RecordContribution,
                    IdempotencyKey);

            await using (var writeContext = database.CreateContext())
            {
                var store =
                    new EfCoreLedgerPostingStore(writeContext);

                var result =
                    await store.TryCommitAsync(
                        receipt,
                        transaction,
                        Array.Empty<AssetLot>());

                Assert.True(result.WasCommitted);

                Assert.Equal(
                    transactionId,
                    result.Receipt.TransactionId);
            }

            await using var readContext =
                database.CreateContext();

            var readStore =
                new EfCoreLedgerPostingStore(readContext);

            var loaded =
                await readStore.FindReceiptAsync(
                    receipt.Scope);

            Assert.NotNull(loaded);

            Assert.Equal(
                receipt.Scope.HouseholdId,
                loaded.Scope.HouseholdId);

            Assert.Equal(
                receipt.Scope.OperationCode,
                loaded.Scope.OperationCode);

            Assert.Equal(
                receipt.Scope.IdempotencyKey,
                loaded.Scope.IdempotencyKey);

            Assert.Equal(
                receipt.Fingerprint,
                loaded.Fingerprint);

            Assert.Equal(
                receipt.TransactionId,
                loaded.TransactionId);

            Assert.Null(loaded.AssetLotId);

            Assert.Equal(
                receipt.CreatedAtUtc,
                loaded.CreatedAtUtc);

            var persistedTransaction =
                await readContext.LedgerTransactions
                    .AsNoTracking()
                    .SingleAsync(
                        x => x.Id == transactionId);

            Assert.Equal(
                TransactionStatus.Posted,
                persistedTransaction.Status);
        }

        [Fact]
        public async Task SameScopedKey_SecondSubmissionReturnsWinnerAndRollsBackLosingGraph()
        {
            await using var database =
                await SqliteTestDatabase.CreateAsync();

            await using (var seedContext = database.CreateContext())
            {
                await CoreLedgerTestData.SeedMasterDataAsync(
                    seedContext);
            }

            var firstTransaction =
                CreatePostedContribution(
                    Guid.NewGuid());

            var firstReceipt =
                CreateReceipt(
                    firstTransaction.Id,
                    LedgerOperationCodes.RecordContribution,
                    IdempotencyKey);

            await using (var firstContext = database.CreateContext())
            {
                var firstStore =
                    new EfCoreLedgerPostingStore(firstContext);

                var result =
                    await firstStore.TryCommitAsync(
                        firstReceipt,
                        firstTransaction,
                        Array.Empty<AssetLot>());

                Assert.True(result.WasCommitted);
            }

            var losingTransaction =
                CreatePostedContribution(
                    Guid.NewGuid());

            var losingReceipt =
                CreateReceipt(
                    losingTransaction.Id,
                    LedgerOperationCodes.RecordContribution,
                    IdempotencyKey);

            LedgerSubmissionCommitResult losingResult;

            await using (var losingContext = database.CreateContext())
            {
                var losingStore =
                    new EfCoreLedgerPostingStore(
                        losingContext);

                losingResult =
                    await losingStore.TryCommitAsync(
                        losingReceipt,
                        losingTransaction,
                        Array.Empty<AssetLot>());
            }

            Assert.False(
                losingResult.WasCommitted);

            Assert.Equal(
                firstTransaction.Id,
                losingResult.Receipt.TransactionId);

            await using var verificationContext =
                database.CreateContext();

            var receiptCount =
                await verificationContext.CommandReceipts
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.HouseholdId
                                == CoreLedgerTestData.HouseholdId
                            && x.OperationCode
                                == LedgerOperationCodes.RecordContribution
                            && x.IdempotencyKey
                                == IdempotencyKey);

            var relevantTransactionIds =
                new[]
                {
                firstTransaction.Id,
                losingTransaction.Id
                };

            var transactionIds =
                await verificationContext.LedgerTransactions
                    .AsNoTracking()
                    .Where(
                        x => relevantTransactionIds.Contains(x.Id))
                    .Select(x => x.Id)
                    .ToListAsync();

            var entryCount =
                await verificationContext.TransactionEntries
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            relevantTransactionIds.Contains(
                                x.TransactionId));

            var cashFlowCount =
                await verificationContext.CashFlowDetails
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            relevantTransactionIds.Contains(
                                x.TransactionId));

            Assert.Equal(
                1,
                receiptCount);

            Assert.Single(
                transactionIds);

            Assert.Equal(
                firstTransaction.Id,
                transactionIds[0]);

            Assert.Equal(
                1,
                entryCount);

            Assert.Equal(
                1,
                cashFlowCount);
        }

        [Fact]
        public async Task SameKey_DifferentOperationCode_UsesSeparateScope()
        {
            await using var database =
                await SqliteTestDatabase.CreateAsync();

            await using (var seedContext = database.CreateContext())
            {
                await CoreLedgerTestData.SeedMasterDataAsync(
                    seedContext);
            }

            var firstTransaction =
                CreatePostedContribution(
                    Guid.NewGuid());

            var secondTransaction =
                CreatePostedContribution(
                    Guid.NewGuid());

            var firstReceipt =
                CreateReceipt(
                    firstTransaction.Id,
                    LedgerOperationCodes.RecordContribution,
                    IdempotencyKey);

            const string otherOperationCode =
                "TEST_OTHER_OPERATION";

            var secondReceipt =
                CreateReceipt(
                    secondTransaction.Id,
                    otherOperationCode,
                    IdempotencyKey);

            await using (var firstContext = database.CreateContext())
            {
                var store =
                    new EfCoreLedgerPostingStore(firstContext);

                var result =
                    await store.TryCommitAsync(
                        firstReceipt,
                        firstTransaction,
                        Array.Empty<AssetLot>());

                Assert.True(result.WasCommitted);
            }

            await using (var secondContext = database.CreateContext())
            {
                var store =
                    new EfCoreLedgerPostingStore(secondContext);

                var result =
                    await store.TryCommitAsync(
                        secondReceipt,
                        secondTransaction,
                        Array.Empty<AssetLot>());

                Assert.True(result.WasCommitted);
            }

            await using var verificationContext =
                database.CreateContext();

            var receiptCount =
                await verificationContext.CommandReceipts
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.HouseholdId
                                == CoreLedgerTestData.HouseholdId
                            && x.IdempotencyKey
                                == IdempotencyKey);

            Assert.Equal(
                2,
                receiptCount);
        }

        [Fact]
        public async Task FailureAfterReceiptInsert_RollsBackReceiptAndLedgerGraph()
        {
            await using var database =
                await SqliteTestDatabase.CreateAsync();

            var transactionId =
                Guid.NewGuid();

            await using (var context = database.CreateContext())
            {
                await CoreLedgerTestData.SeedMasterDataAsync(
                    context);

                var transaction =
                    LedgerTransaction.CreateDraft(
                        transactionId,
                        CoreLedgerTestData.HouseholdId,
                        TransactionType.Buy,
                        RecordedAtUtc,
                        executionDate:
                            CoreLedgerTestData.ExecutionDate);

                transaction.AddEntry(
                    CoreLedgerTestData.PortfolioId,
                    CoreLedgerTestData.AccountId,
                    CoreLedgerTestData.FundAssetId,
                    QuantityDelta.FromRaw(100),
                    EntryRole.Principal,
                    UnitPrice.FromRaw(
                        50,
                        CurrencyCode.TRY));

                transaction.AddEntry(
                    CoreLedgerTestData.PortfolioId,
                    CoreLedgerTestData.AccountId,
                    CoreLedgerTestData.CashAssetId,
                    QuantityDelta.FromRaw(-100),
                    EntryRole.Consideration);

                transaction.Post(
                    RecordedAtUtc);

                var receipt =
                    CreateReceipt(
                        transactionId,
                        LedgerOperationCodes.RecordFundPurchase,
                        "atomic-failure-test");

                var store =
                    new EfCoreLedgerPostingStore(
                        context);

                await Assert.ThrowsAsync<
                    CoreLedgerPersistenceException>(
                    () => store.TryCommitAsync(
                        receipt,
                        transaction,
                        Array.Empty<AssetLot>()));
            }

            await using var verificationContext =
                database.CreateContext();

            var transactionCount =
                await verificationContext.LedgerTransactions
                    .AsNoTracking()
                    .CountAsync(
                        x => x.Id == transactionId);

            var entryCount =
                await verificationContext.TransactionEntries
                    .AsNoTracking()
                    .CountAsync(
                        x => x.TransactionId == transactionId);

            var receiptCount =
                await verificationContext.CommandReceipts
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.ResultTransactionId
                                == transactionId);

            Assert.Equal(
                0,
                transactionCount);

            Assert.Equal(
                0,
                entryCount);

            Assert.Equal(
                0,
                receiptCount);
        }

        [Fact]
        public async Task ConcurrentEquivalentSubmissions_PersistExactlyOneLedgerGraph()
        {
            await using var database =
                await SqliteTestDatabase.CreateAsync();

            await using (var seedContext = database.CreateContext())
            {
                await CoreLedgerTestData.SeedMasterDataAsync(
                    seedContext);
            }

            var firstTransaction =
                CreatePostedContribution(
                    Guid.NewGuid());

            var secondTransaction =
                CreatePostedContribution(
                    Guid.NewGuid());

            var firstReceipt =
                CreateReceipt(
                    firstTransaction.Id,
                    LedgerOperationCodes.RecordContribution,
                    "concurrent-test");

            var secondReceipt =
                CreateReceipt(
                    secondTransaction.Id,
                    LedgerOperationCodes.RecordContribution,
                    "concurrent-test");

            await using var firstContext =
                database.CreateContext();

            await using var secondContext =
                database.CreateContext();

            var firstStore =
                new EfCoreLedgerPostingStore(
                    firstContext);

            var secondStore =
                new EfCoreLedgerPostingStore(
                    secondContext);

            var firstTask =
                firstStore.TryCommitAsync(
                    firstReceipt,
                    firstTransaction,
                    Array.Empty<AssetLot>());

            var secondTask =
                secondStore.TryCommitAsync(
                    secondReceipt,
                    secondTransaction,
                    Array.Empty<AssetLot>());

            var results =
                await Task.WhenAll(
                    firstTask,
                    secondTask);

            Assert.Single(
                results, x => x.WasCommitted);

            Assert.Single(
                results, x => !x.WasCommitted);

            Assert.Equal(
                results[0].Receipt.TransactionId,
                results[1].Receipt.TransactionId);

            await using var verificationContext =
                database.CreateContext();

            var scopeReceipt =
                await verificationContext.CommandReceipts
                    .AsNoTracking()
                    .SingleAsync(
                        x =>
                            x.HouseholdId
                                == CoreLedgerTestData.HouseholdId
                            && x.OperationCode
                                == LedgerOperationCodes.RecordContribution
                            && x.IdempotencyKey
                                == "concurrent-test");

            var candidateIds =
                new[]
                {
                firstTransaction.Id,
                secondTransaction.Id
                };

            var transactionIds =
                await verificationContext.LedgerTransactions
                    .AsNoTracking()
                    .Where(
                        x => candidateIds.Contains(x.Id))
                    .Select(x => x.Id)
                    .ToListAsync();

            var entryCount =
                await verificationContext.TransactionEntries
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            candidateIds.Contains(
                                x.TransactionId));

            var cashFlowCount =
                await verificationContext.CashFlowDetails
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            candidateIds.Contains(
                                x.TransactionId));

            Assert.Single(
                transactionIds);

            Assert.Equal(
                scopeReceipt.ResultTransactionId,
                transactionIds[0]);

            Assert.Equal(
                scopeReceipt.ResultTransactionId,
                results[0].Receipt.TransactionId);

            Assert.Equal(
                1,
                entryCount);

            Assert.Equal(
                1,
                cashFlowCount);
        }

        private static LedgerTransaction
            CreatePostedContribution(
                Guid transactionId)
        {
            var transaction =
                LedgerTransaction.CreateDraft(
                    transactionId,
                    CoreLedgerTestData.HouseholdId,
                    TransactionType.Contribution,
                    RecordedAtUtc,
                    executionDate:
                        CoreLedgerTestData.ExecutionDate);

            transaction.AddEntry(
                CoreLedgerTestData.PortfolioId,
                CoreLedgerTestData.AccountId,
                CoreLedgerTestData.CashAssetId,
                QuantityDelta.FromRaw(
                    100_000_000),
                EntryRole.Principal);

            transaction.AttachCashFlowDetail(
                CashFlowCategory.Other);

            transaction.Post(
                RecordedAtUtc);

            return transaction;
        }

        private static LedgerSubmissionReceipt
            CreateReceipt(
                Guid transactionId,
                string operationCode,
                string idempotencyKey)
        {
            return new LedgerSubmissionReceipt(
                new LedgerSubmissionScope(
                    CoreLedgerTestData.HouseholdId,
                    operationCode,
                    idempotencyKey),

                new CommandFingerprint(
                    "SHA256",
                    1,
                    FingerprintValue),

                transactionId,

                AssetLotId: null,

                CreatedAtUtc:
                    RecordedAtUtc);
        }
    }
}
