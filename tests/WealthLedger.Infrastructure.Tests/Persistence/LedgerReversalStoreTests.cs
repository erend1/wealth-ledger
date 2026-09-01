using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Tests.Persistence
{
    public sealed class LedgerReversalStoreTests
    {
        private const string FingerprintValue =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        private static readonly DateTimeOffset RecordedAtUtc =
            new(
                2026,
                8,
                31,
                12,
                0,
                0,
                TimeSpan.Zero);

        [Fact]
        public async Task
            LoadCandidate_PostedContribution_ReconstructsCashFlow()
        {
            await using var database =
                await SqliteTestDatabase.CreateAsync();

            await using (var seedContext =
                         database.CreateContext())
            {
                await CoreLedgerTestData.SeedMasterDataAsync(
                    seedContext);

                await SeedPostedContributionAsync(
                    seedContext);
            }

            await using var readContext =
                database.CreateContext();

            var store =
                new EfCoreLedgerReversalStore(
                    readContext);

            var transactionId =
                await readContext.LedgerTransactions
                    .AsNoTracking()
                    .Where(
                        x =>
                            x.Type
                            == TransactionType.Contribution)
                    .Select(x => x.Id)
                    .SingleAsync();

            var candidate =
                await store.LoadCandidateAsync(
                    transactionId);

            Assert.NotNull(candidate);
            Assert.NotNull(candidate!.Original);

            Assert.Equal(
                transactionId,
                candidate.TransactionId);

            Assert.Equal(
                TransactionStatus.Posted,
                candidate.Status);

            Assert.Equal(
                TransactionType.Contribution,
                candidate.Type);

            Assert.Null(
                candidate.ExistingReversalTransactionId);

            Assert.Empty(
                candidate.BlockingTransactionIds);

            Assert.Empty(
                candidate.AffectedLots);

            Assert.NotNull(
                candidate.Original!.CashFlowDetail);

            Assert.Equal(
                CashFlowCategory.AcademicIncome,
                candidate.Original
                    .CashFlowDetail!
                    .Category);

            Assert.Equal(
                CoreLedgerTestData.HouseholdMemberId,
                candidate.Original
                    .CashFlowDetail!
                    .HouseholdMemberId);

            var entry =
                Assert.Single(
                    candidate.Original.Entries);

            Assert.Equal(
                CoreLedgerTestData.CashAssetId,
                entry.AssetId);

            Assert.Equal(
                100_000_000_000,
                entry.QuantityDelta.RawE8);
        }

        [Fact]
        public async Task
            LoadCandidate_PostedPurchase_ReconstructsEntriesCostsAndLotHistory()
        {
            await using var database =
                await SqliteTestDatabase.CreateAsync();

            PostedPurchaseFixture fixture;

            await using (var seedContext =
                         database.CreateContext())
            {
                await CoreLedgerTestData.SeedMasterDataAsync(
                    seedContext);

                fixture =
                    await SeedPostedPurchaseAsync(
                        seedContext);
            }

            await using var readContext =
                database.CreateContext();

            var store =
                new EfCoreLedgerReversalStore(
                    readContext);

            var candidate =
                await store.LoadCandidateAsync(
                    fixture.TransactionId);

            Assert.NotNull(candidate);
            Assert.NotNull(candidate!.Original);

            Assert.Equal(
                TransactionStatus.Posted,
                candidate.Status);

            Assert.Equal(
                TransactionType.Buy,
                candidate.Type);

            Assert.Equal(
                fixture.TransactionId,
                candidate.Original!.Id);

            Assert.Equal(
                CoreLedgerTestData.HouseholdId,
                candidate.Original.HouseholdId);

            Assert.Equal(
                CoreLedgerTestData.ExecutionDate,
                candidate.Original.ExecutionDate);

            Assert.Equal(
                "BUY-SEED",
                candidate.Original.ExternalReference);

            Assert.Equal(
                "Seed purchase",
                candidate.Original.Note);

            Assert.Equal(
                2,
                candidate.Original.Entries.Count);

            var principal =
                candidate.Original.Entries
                    .Single(
                        x =>
                            x.Role
                            == EntryRole.Principal);

            Assert.Equal(
                fixture.PrincipalEntryId,
                principal.Id);

            Assert.Equal(
                125_000_000,
                principal.QuantityDelta.RawE8);

            Assert.NotNull(
                principal.UnitPrice);

            Assert.Equal(
                20_000_000_000,
                principal.UnitPrice!.RawE8);

            Assert.Equal(
                CurrencyCode.TRY,
                principal.UnitPrice.Currency);

            var cost =
                Assert.Single(
                    candidate.Original.Costs);

            Assert.Equal(
                CostType.Commission,
                cost.Type);

            Assert.Equal(
                CostTreatment.IncludedInConsideration,
                cost.Treatment);

            Assert.Equal(
                100,
                cost.Amount.MinorUnits);

            var lot =
                Assert.Single(
                    candidate.AffectedLots);

            Assert.Equal(
                fixture.LotId,
                lot.Id);

            Assert.Equal(
                CoreLedgerTestData.FundAssetId,
                lot.AssetId);

            Assert.Equal(
                fixture.PrincipalEntryId,
                lot.OpeningTransactionEntryId);

            Assert.Equal(
                CostBasisStatus.Known,
                lot.CostBasis.Status);

            Assert.NotNull(
                lot.CostBasis.Amount);

            Assert.Equal(
                25_000,
                lot.CostBasis.Amount!.MinorUnits);

            Assert.Equal(
                125_000_000,
                lot.CurrentQuantity.RawE8);

            var allocation =
                Assert.Single(
                    lot.Allocations);

            Assert.Equal(
                fixture.OpeningAllocationId,
                allocation.Id);

            Assert.Equal(
                fixture.LotId,
                allocation.AssetLotId);

            Assert.Equal(
                fixture.PrincipalEntryId,
                allocation.TransactionEntryId);

            Assert.Equal(
                125_000_000,
                allocation.QuantityDelta.RawE8);
        }

        [Fact]
        public async Task
            LoadCandidate_OutstandingDependencyReturnsBlocker_AndPostedReversalRemovesIt()
        {
            await using var database =
                await SqliteTestDatabase.CreateAsync();

            PostedPurchaseFixture purchase;
            PostedDependencyFixture dependency;

            await using (var seedContext =
                         database.CreateContext())
            {
                await CoreLedgerTestData.SeedMasterDataAsync(
                    seedContext);

                purchase =
                    await SeedPostedPurchaseAsync(
                        seedContext);

                dependency =
                    await SeedPostedDependencyAsync(
                        seedContext,
                        purchase.LotId);
            }

            await using (var blockedContext =
                         database.CreateContext())
            {
                var blockedStore =
                    new EfCoreLedgerReversalStore(
                        blockedContext);

                var blocked =
                    await blockedStore.LoadCandidateAsync(
                        purchase.TransactionId);

                Assert.NotNull(blocked);

                Assert.Equal(
                    new[]
                    {
                    dependency.TransactionId
                    },
                    blocked!.BlockingTransactionIds);
            }

            await using (var dependencyContext =
                         database.CreateContext())
            {
                var dependencyStore =
                    new EfCoreLedgerReversalStore(
                        dependencyContext);

                var candidate =
                    await dependencyStore.LoadCandidateAsync(
                        dependency.TransactionId);

                Assert.NotNull(candidate);
                Assert.NotNull(candidate!.Original);

                var reversal =
                    BuildPostedReversal(
                        candidate,
                        Guid.NewGuid(),
                        "Reverse downstream dependency.");

                var receipt =
                    CreateReceipt(
                        reversal.Id,
                        "dependency-reversal-001");

                var result =
                    await dependencyStore.TryCommitAsync(
                        receipt,
                        reversal,
                        candidate.AffectedLots);

                Assert.IsType<
                    ReversalCommitResult.Committed>(
                    result);
            }

            await using (var unblockedContext =
                         database.CreateContext())
            {
                var unblockedStore =
                    new EfCoreLedgerReversalStore(
                        unblockedContext);

                var unblocked =
                    await unblockedStore.LoadCandidateAsync(
                        purchase.TransactionId);

                Assert.NotNull(unblocked);
                Assert.Empty(
                    unblocked!.BlockingTransactionIds);

                var lot =
                    Assert.Single(
                        unblocked.AffectedLots);

                Assert.Equal(
                    125_000_000,
                    lot.CurrentQuantity.RawE8);
            }
        }

        [Fact]
        public async Task
            TryCommit_PurchaseReversal_PersistsTransactionAllocationAndReceiptAtomically()
        {
            await using var database =
                await SqliteTestDatabase.CreateAsync();

            PostedPurchaseFixture purchase;

            await using (var seedContext =
                         database.CreateContext())
            {
                await CoreLedgerTestData.SeedMasterDataAsync(
                    seedContext);

                purchase =
                    await SeedPostedPurchaseAsync(
                        seedContext);
            }

            Guid reversalId;

            await using (var writeContext =
                         database.CreateContext())
            {
                var store =
                    new EfCoreLedgerReversalStore(
                        writeContext);

                var candidate =
                    await store.LoadCandidateAsync(
                        purchase.TransactionId);

                Assert.NotNull(candidate);
                Assert.NotNull(candidate!.Original);

                var reversal =
                    BuildPostedReversal(
                        candidate,
                        Guid.NewGuid(),
                        "Incorrect purchase.");

                reversalId =
                    reversal.Id;

                var receipt =
                    CreateReceipt(
                        reversal.Id,
                        "purchase-reversal-001");

                var result =
                    await store.TryCommitAsync(
                        receipt,
                        reversal,
                        candidate.AffectedLots);

                var committed =
                    Assert.IsType<
                        ReversalCommitResult.Committed>(
                        result);

                Assert.Equal(
                    reversalId,
                    committed.Receipt.TransactionId);
            }

            await using var verificationContext =
                database.CreateContext();

            var reversalRow =
                await verificationContext
                    .LedgerTransactions
                    .AsNoTracking()
                    .SingleAsync(
                        x => x.Id == reversalId);

            Assert.Equal(
                TransactionType.Reversal,
                reversalRow.Type);

            Assert.Equal(
                TransactionStatus.Posted,
                reversalRow.Status);

            Assert.Equal(
                purchase.TransactionId,
                reversalRow.ReversalOfTransactionId);

            var reversalEntries =
                await verificationContext
                    .TransactionEntries
                    .AsNoTracking()
                    .Where(
                        x =>
                            x.TransactionId
                            == reversalId)
                    .OrderBy(
                        x => x.EntrySequence)
                    .ToArrayAsync();

            Assert.Equal(
                2,
                reversalEntries.Length);

            var reversalPrincipal =
                reversalEntries.Single(
                    x =>
                        x.Role
                        == EntryRole.Principal);

            Assert.Equal(
                -125_000_000,
                reversalPrincipal.QuantityDeltaE8);

            var reversalAllocation =
                await verificationContext
                    .LotEntryAllocations
                    .AsNoTracking()
                    .SingleAsync(
                        x =>
                            x.TransactionEntryId
                            == reversalPrincipal.Id);

            Assert.Equal(
                purchase.LotId,
                reversalAllocation.AssetLotId);

            Assert.Equal(
                -125_000_000,
                reversalAllocation.QuantityDeltaE8);

            Assert.Equal(
                1,
                await verificationContext.AssetLots
                    .AsNoTracking()
                    .CountAsync());

            var lot =
                await verificationContext.AssetLots
                    .AsNoTracking()
                    .SingleAsync();

            Assert.Equal(
                purchase.LotId,
                lot.Id);

            Assert.Equal(
                CostBasisStatus.Known,
                lot.CostBasisStatus);

            Assert.Equal(
                25_000,
                lot.OriginalCostBasisMinor);

            Assert.Equal(
                "TRY",
                lot.CostBasisCurrencyCode);

            Assert.Equal(
                1,
                await verificationContext.CommandReceipts
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.OperationCode
                            == LedgerOperationCodes
                                .ReversePostedTransaction
                            && x.IdempotencyKey
                            == "purchase-reversal-001"));

            var lotSum =
                await GetPostedLotQuantityAsync(
                    verificationContext,
                    purchase.LotId);

            Assert.Equal(
                0,
                lotSum);
        }

        [Fact]
        public async Task
            TryCommit_PostingFailure_RollsBackReversalEntriesReceiptAndAllocations()
        {
            await using var database =
                await SqliteTestDatabase.CreateAsync();

            PostedPurchaseFixture purchase;

            await using (var seedContext =
                         database.CreateContext())
            {
                await CoreLedgerTestData.SeedMasterDataAsync(
                    seedContext);

                purchase =
                    await SeedPostedPurchaseAsync(
                        seedContext);
            }

            var reversalId =
                Guid.NewGuid();

            await using (var writeContext =
                         database.CreateContext())
            {
                var store =
                    new EfCoreLedgerReversalStore(
                        writeContext);

                var candidate =
                    await store.LoadCandidateAsync(
                        purchase.TransactionId);

                Assert.NotNull(candidate);
                Assert.NotNull(candidate!.Original);

                // Intentionally create the exact inverse entries,
                // but DO NOT add the required same-lot inverse allocation.
                // Domain reversal can post, but SQLite must reject the
                // persisted graph when the final status changes to POSTED.
                var reversal =
                    LedgerTransaction.CreateReversal(
                        reversalId,
                        candidate.Original!,
                        RecordedAtUtc,
                        "Forced atomic rollback.");

                reversal.Post(
                    RecordedAtUtc);

                var receipt =
                    CreateReceipt(
                        reversalId,
                        "forced-reversal-failure");

                await Assert.ThrowsAsync<
                    CoreLedgerPersistenceException>(
                    () =>
                        store.TryCommitAsync(
                            receipt,
                            reversal,
                            Array.Empty<AssetLot>()));
            }

            await using var verificationContext =
                database.CreateContext();

            Assert.Equal(
                0,
                await verificationContext
                    .LedgerTransactions
                    .AsNoTracking()
                    .CountAsync(
                        x => x.Id == reversalId));

            Assert.Equal(
                0,
                await verificationContext
                    .TransactionEntries
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.TransactionId
                            == reversalId));

            Assert.Equal(
                0,
                await verificationContext
                    .CommandReceipts
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.ResultTransactionId
                            == reversalId));

            Assert.Equal(
                1,
                await verificationContext
                    .LotEntryAllocations
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.AssetLotId
                            == purchase.LotId));
        }

        [Fact]
        public async Task
            SameKey_ConcurrentReversalStores_PersistOneGraphAndOneReceipt()
        {
            await using var database =
                await SqliteTestDatabase.CreateAsync();

            PostedPurchaseFixture purchase;

            await using (var seedContext =
                         database.CreateContext())
            {
                await CoreLedgerTestData.SeedMasterDataAsync(
                    seedContext);

                purchase =
                    await SeedPostedPurchaseAsync(
                        seedContext);
            }

            await using var firstContext =
                database.CreateContext();

            await using var secondContext =
                database.CreateContext();

            var firstStore =
                new EfCoreLedgerReversalStore(
                    firstContext);

            var secondStore =
                new EfCoreLedgerReversalStore(
                    secondContext);

            var firstCandidate =
                await firstStore.LoadCandidateAsync(
                    purchase.TransactionId);

            var secondCandidate =
                await secondStore.LoadCandidateAsync(
                    purchase.TransactionId);

            Assert.NotNull(firstCandidate);
            Assert.NotNull(secondCandidate);

            var firstReversal =
                BuildPostedReversal(
                    firstCandidate!,
                    Guid.NewGuid(),
                    "Concurrent equivalent reversal.");

            var secondReversal =
                BuildPostedReversal(
                    secondCandidate!,
                    Guid.NewGuid(),
                    "Concurrent equivalent reversal.");

            const string key =
                "concurrent-same-key-reversal";

            var firstReceipt =
                CreateReceipt(
                    firstReversal.Id,
                    key);

            var secondReceipt =
                CreateReceipt(
                    secondReversal.Id,
                    key);

            var results =
                await Task.WhenAll(
                    firstStore.TryCommitAsync(
                        firstReceipt,
                        firstReversal,
                        firstCandidate.AffectedLots),
                    secondStore.TryCommitAsync(
                        secondReceipt,
                        secondReversal,
                        secondCandidate.AffectedLots));

            Assert.Single(
                results,
                x =>
                    x is ReversalCommitResult.Committed);

            Assert.Single(
                results,
                x =>
                    x is ReversalCommitResult.ReceiptWinner);

            var committed =
                Assert.IsType<
                    ReversalCommitResult.Committed>(
                        results.Single(
                            x =>
                                x is ReversalCommitResult
                                    .Committed));

            var replay =
                Assert.IsType<
                    ReversalCommitResult.ReceiptWinner>(
                        results.Single(
                            x =>
                                x is ReversalCommitResult
                                    .ReceiptWinner));

            Assert.Equal(
                committed.Receipt.TransactionId,
                replay.Receipt.TransactionId);

            await using var verificationContext =
                database.CreateContext();

            Assert.Equal(
                1,
                await verificationContext
                    .LedgerTransactions
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.Type
                            == TransactionType.Reversal
                            && x.ReversalOfTransactionId
                            == purchase.TransactionId));

            Assert.Equal(
                1,
                await verificationContext
                    .CommandReceipts
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
            DifferentKeys_ConcurrentReversalStores_PersistOneWinnerAndNoLosingReceipt()
        {
            await using var database =
                await SqliteTestDatabase.CreateAsync();

            PostedPurchaseFixture purchase;

            await using (var seedContext =
                         database.CreateContext())
            {
                await CoreLedgerTestData.SeedMasterDataAsync(
                    seedContext);

                purchase =
                    await SeedPostedPurchaseAsync(
                        seedContext);
            }

            await using var firstContext =
                database.CreateContext();

            await using var secondContext =
                database.CreateContext();

            var firstStore =
                new EfCoreLedgerReversalStore(
                    firstContext);

            var secondStore =
                new EfCoreLedgerReversalStore(
                    secondContext);

            var firstCandidate =
                await firstStore.LoadCandidateAsync(
                    purchase.TransactionId);

            var secondCandidate =
                await secondStore.LoadCandidateAsync(
                    purchase.TransactionId);

            Assert.NotNull(firstCandidate);
            Assert.NotNull(secondCandidate);

            var firstReversal =
                BuildPostedReversal(
                    firstCandidate!,
                    Guid.NewGuid(),
                    "Concurrent different-key reversal.");

            var secondReversal =
                BuildPostedReversal(
                    secondCandidate!,
                    Guid.NewGuid(),
                    "Concurrent different-key reversal.");

            var results =
                await Task.WhenAll(
                    firstStore.TryCommitAsync(
                        CreateReceipt(
                            firstReversal.Id,
                            "different-key-a"),
                        firstReversal,
                        firstCandidate.AffectedLots),
                    secondStore.TryCommitAsync(
                        CreateReceipt(
                            secondReversal.Id,
                            "different-key-b"),
                        secondReversal,
                        secondCandidate.AffectedLots));

            Assert.Single(
                results,
                x =>
                    x is ReversalCommitResult.Committed);

            Assert.Single(
                results,
                x =>
                    x is ReversalCommitResult.AlreadyReversed);

            var committed =
                Assert.IsType<
                    ReversalCommitResult.Committed>(
                        results.Single(
                            x =>
                                x is ReversalCommitResult
                                    .Committed));

            var loser =
                Assert.IsType<
                    ReversalCommitResult.AlreadyReversed>(
                        results.Single(
                            x =>
                                x is ReversalCommitResult
                                    .AlreadyReversed));

            Assert.Equal(
                committed.Receipt.TransactionId,
                loser.ExistingReversalTransactionId);

            await using var verificationContext =
                database.CreateContext();

            Assert.Equal(
                1,
                await verificationContext
                    .LedgerTransactions
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.Type
                            == TransactionType.Reversal
                            && x.ReversalOfTransactionId
                            == purchase.TransactionId));

            Assert.Equal(
                1,
                await verificationContext
                    .CommandReceipts
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.OperationCode
                            == LedgerOperationCodes
                                .ReversePostedTransaction
                            && (
                                x.IdempotencyKey
                                    == "different-key-a"
                                || x.IdempotencyKey
                                    == "different-key-b")));
        }

        [Fact]
        public async Task
            Readback_AfterFreshContext_PreservesBothReverseLinksAndOppositeAllocations()
        {
            await using var database =
                await SqliteTestDatabase.CreateAsync();

            PostedPurchaseFixture purchase;

            await using (var seedContext =
                         database.CreateContext())
            {
                await CoreLedgerTestData.SeedMasterDataAsync(
                    seedContext);

                purchase =
                    await SeedPostedPurchaseAsync(
                        seedContext);
            }

            Guid reversalId;

            await using (var writeContext =
                         database.CreateContext())
            {
                var store =
                    new EfCoreLedgerReversalStore(
                        writeContext);

                var candidate =
                    await store.LoadCandidateAsync(
                        purchase.TransactionId);

                Assert.NotNull(candidate);

                var reversal =
                    BuildPostedReversal(
                        candidate!,
                        Guid.NewGuid(),
                        "Readback verification.");

                reversalId =
                    reversal.Id;

                var result =
                    await store.TryCommitAsync(
                        CreateReceipt(
                            reversalId,
                            "readback-reversal"),
                        reversal,
                        candidate.AffectedLots);

                Assert.IsType<
                    ReversalCommitResult.Committed>(
                    result);
            }

            // Fresh DbContext proves this is persisted state,
            // not tracked in-memory aggregate state.
            await using var readContext =
                database.CreateContext();

            var readStore =
                new EfCoreLedgerTransactionReadStore(
                    readContext);

            var original =
                await readStore.FindByIdAsync(
                    purchase.TransactionId);

            var reversalRead =
                await readStore.FindByIdAsync(
                    reversalId);

            Assert.NotNull(original);
            Assert.NotNull(reversalRead);

            Assert.Equal(
                reversalId,
                original!.ReversedByTransactionId);

            Assert.Null(
                original.ReversalOfTransactionId);

            Assert.Equal(
                purchase.TransactionId,
                reversalRead!.ReversalOfTransactionId);

            Assert.Null(
                reversalRead.ReversedByTransactionId);

            var originalAllocation =
                Assert.Single(
                    original.LotAllocations);

            var reversalAllocation =
                Assert.Single(
                    reversalRead.LotAllocations);

            Assert.Equal(
                purchase.LotId,
                originalAllocation.AssetLotId);

            Assert.Equal(
                purchase.LotId,
                reversalAllocation.AssetLotId);

            Assert.Equal(
                125_000_000,
                originalAllocation.QuantityDeltaRawE8);

            Assert.Equal(
                -125_000_000,
                reversalAllocation.QuantityDeltaRawE8);

            Assert.Single(
                original.CreatedLots);

            Assert.Empty(
                reversalRead.CreatedLots);

            Assert.Equal(
                TransactionStatus.Posted,
                original.Status);

            Assert.Equal(
                TransactionStatus.Posted,
                reversalRead.Status);
        }


        [Fact]
        public async Task
    TryCommit_DependencyIntroducedAfterCandidateLoad_ReturnsConflictAndRollsBack()
        {
            await using var database =
                await SqliteTestDatabase.CreateAsync();

            PostedPurchaseFixture purchase;

            await using (var seedContext =
                         database.CreateContext())
            {
                await CoreLedgerTestData.SeedMasterDataAsync(
                    seedContext);

                purchase =
                    await SeedPostedPurchaseAsync(
                        seedContext);
            }

            await using var reversalContext =
                database.CreateContext();

            var store =
                new EfCoreLedgerReversalStore(
                    reversalContext);

            // Eligibility is evaluated from a state with no blockers.
            var candidate =
                await store.LoadCandidateAsync(
                    purchase.TransactionId);

            Assert.NotNull(candidate);
            Assert.NotNull(candidate!.Original);
            Assert.Empty(
                candidate.BlockingTransactionIds);

            var reversal =
                BuildPostedReversal(
                    candidate,
                    Guid.NewGuid(),
                    "Dependency raced with reversal.");

            var receipt =
                CreateReceipt(
                    reversal.Id,
                    "dependency-race-reversal");

            // Simulate another writer introducing a valid posted
            // dependency after eligibility/candidate loading.
            PostedDependencyFixture dependency;

            await using (var dependencyContext =
                         database.CreateContext())
            {
                dependency =
                    await SeedPostedPositiveDependencyAsync(
                        dependencyContext,
                        purchase.LotId);
            }

            // The stale candidate still believes the reversal is eligible.
            // SQLite must be the final authority and reject the posting.
            var result =
                await store.TryCommitAsync(
                    receipt,
                    reversal,
                    candidate.AffectedLots);

            var conflict =
                Assert.IsType<
                    ReversalCommitResult.DependencyConflict>(
                        result);

            Assert.Equal(
                new[]
                {
            dependency.TransactionId
                },
                conflict.BlockingTransactionIds);

            // Fresh context proves the failed reversal transaction was
            // completely rolled back while the independently committed
            // dependency remains.
            await using var verificationContext =
                database.CreateContext();

            Assert.Equal(
                0,
                await verificationContext
                    .LedgerTransactions
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.Id
                            == reversal.Id));

            Assert.Equal(
                0,
                await verificationContext
                    .TransactionEntries
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.TransactionId
                            == reversal.Id));

            Assert.Equal(
                0,
                await verificationContext
                    .CommandReceipts
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.ResultTransactionId
                            == reversal.Id));

            Assert.Equal(
                0,
                await (
                    from allocation
                        in verificationContext
                            .LotEntryAllocations
                            .AsNoTracking()
                    join entry
                        in verificationContext
                            .TransactionEntries
                            .AsNoTracking()
                        on allocation.TransactionEntryId
                        equals entry.Id
                    where
                        entry.TransactionId
                        == reversal.Id
                    select allocation.Id
                ).CountAsync());

            // Only the original opening allocation and the newly
            // introduced dependency remain on the lot.
            Assert.Equal(
                2,
                await verificationContext
                    .LotEntryAllocations
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.AssetLotId
                            == purchase.LotId));

            var persistedDependency =
                await verificationContext
                    .LedgerTransactions
                    .AsNoTracking()
                    .SingleAsync(
                        x =>
                            x.Id
                            == dependency.TransactionId);

            Assert.Equal(
                TransactionStatus.Posted,
                persistedDependency.Status);
        }

        private static async Task<Guid>
            SeedPostedContributionAsync(
                WealthLedgerDbContext context)
        {
            var transactionId =
                Guid.NewGuid();

            var entryId =
                Guid.NewGuid();

            var row =
                CoreLedgerTestData.CreateDraftTransaction(
                    transactionId,
                    TransactionType.Contribution);

            row.ExternalReference =
                "CONTRIBUTION-SEED";

            row.Note =
                "Seed contribution";

            context.LedgerTransactions.Add(
                row);

            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    entryId,
                    transactionId,
                    0,
                    CoreLedgerTestData.CashAssetId,
                    100_000_000_000,
                    EntryRole.Principal));

            context.CashFlowDetails.Add(
                new CashFlowDetailRow
                {
                    TransactionId =
                        transactionId,

                    Category =
                        CashFlowCategory.AcademicIncome,

                    HouseholdMemberId =
                        CoreLedgerTestData.HouseholdMemberId
                });

            await context.SaveChangesAsync();

            await CoreLedgerTestData.PostAsync(
                context,
                transactionId);

            return transactionId;
        }

        private static async Task<PostedPurchaseFixture>
            SeedPostedPurchaseAsync(
                WealthLedgerDbContext context)
        {
            var transactionId =
                Guid.NewGuid();

            var principalEntryId =
                Guid.NewGuid();

            var considerationEntryId =
                Guid.NewGuid();

            var lotId =
                Guid.NewGuid();

            var allocationId =
                Guid.NewGuid();

            var transaction =
                CoreLedgerTestData.CreateDraftTransaction(
                    transactionId,
                    TransactionType.Buy);

            transaction.ExternalReference =
                "BUY-SEED";

            transaction.Note =
                "Seed purchase";

            context.LedgerTransactions.Add(
                transaction);

            context.TransactionEntries.AddRange(
                CoreLedgerTestData.CreateEntry(
                    principalEntryId,
                    transactionId,
                    0,
                    CoreLedgerTestData.FundAssetId,
                    125_000_000,
                    EntryRole.Principal,
                    unitPriceE8:
                        20_000_000_000,
                    priceCurrencyCode:
                        "TRY"),

                CoreLedgerTestData.CreateEntry(
                    considerationEntryId,
                    transactionId,
                    1,
                    CoreLedgerTestData.CashAssetId,
                    -25_000_000_000,
                    EntryRole.Consideration));

            context.TransactionCostComponents.Add(
                new TransactionCostComponentRow
                {
                    Id =
                        Guid.NewGuid(),

                    TransactionId =
                        transactionId,

                    Type =
                        CostType.Commission,

                    Treatment =
                        CostTreatment.IncludedInConsideration,

                    AmountMinor =
                        100,

                    CurrencyCode =
                        "TRY",

                    Note =
                        "Seed commission"
                });

            context.AssetLots.Add(
                new AssetLotRow
                {
                    Id =
                        lotId,

                    AssetId =
                        CoreLedgerTestData.FundAssetId,

                    OpeningTransactionEntryId =
                        principalEntryId,

                    AcquiredOn =
                        CoreLedgerTestData.ExecutionDate,

                    OriginalCostBasisMinor =
                        25_000,

                    CostBasisCurrencyCode =
                        "TRY",

                    CostBasisStatus =
                        CostBasisStatus.Known,

                    CreatedAtUtc =
                        CoreLedgerTestData.CreatedAtUtc
                });

            context.LotEntryAllocations.Add(
                new LotEntryAllocationRow
                {
                    Id =
                        allocationId,

                    AssetLotId =
                        lotId,

                    TransactionEntryId =
                        principalEntryId,

                    QuantityDeltaE8 =
                        125_000_000,

                    CreatedAtUtc =
                        CoreLedgerTestData.CreatedAtUtc
                });

            await context.SaveChangesAsync();

            await CoreLedgerTestData.PostAsync(
                context,
                transactionId);

            return new PostedPurchaseFixture(
                transactionId,
                principalEntryId,
                considerationEntryId,
                lotId,
                allocationId);
        }

        private static async Task<PostedDependencyFixture>
            SeedPostedDependencyAsync(
                WealthLedgerDbContext context,
                Guid lotId)
        {
            var transactionId =
                Guid.NewGuid();

            var entryId =
                Guid.NewGuid();

            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    transactionId,
                    TransactionType.Adjustment));

            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    entryId,
                    transactionId,
                    0,
                    CoreLedgerTestData.FundAssetId,
                    -40_000_000,
                    EntryRole.Adjustment));

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
                        -40_000_000,

                    CreatedAtUtc =
                        CoreLedgerTestData.CreatedAtUtc
                });

            await context.SaveChangesAsync();

            await CoreLedgerTestData.PostAsync(
                context,
                transactionId);

            return new PostedDependencyFixture(
                transactionId,
                entryId);
        }

        private static LedgerTransaction BuildPostedReversal(
            ReversalCandidate candidate,
            Guid reversalId,
            string reason)
        {
            var original =
                candidate.Original
                ?? throw new InvalidOperationException(
                    "Candidate must contain reconstructed original state.");

            var reversal =
                LedgerTransaction.CreateReversal(
                    reversalId,
                    original,
                    RecordedAtUtc,
                    reason);

            var originalEntries =
                original.Entries
                    .ToDictionary(
                        x => x.Id);

            var reversalEntries =
                reversal.Entries
                    .ToDictionary(
                        x => x.Sequence);

            foreach (var lot
                     in candidate.AffectedLots)
            {
                var sourceAllocations =
                    lot.Allocations
                        .Where(
                            x =>
                                originalEntries.ContainsKey(
                                    x.TransactionEntryId))
                        .ToArray();

                foreach (var allocation
                         in sourceAllocations)
                {
                    var originalEntry =
                        originalEntries[
                            allocation.TransactionEntryId];

                    var reversalEntry =
                        reversalEntries[
                            originalEntry.Sequence];

                    lot.Allocate(
                        reversalEntry,
                        allocation.QuantityDelta
                            .Negate());
                }
            }

            reversal.Post(
                RecordedAtUtc);

            return reversal;
        }

        private static LedgerSubmissionReceipt CreateReceipt(
            Guid reversalTransactionId,
            string idempotencyKey)
        {
            return new LedgerSubmissionReceipt(
                new LedgerSubmissionScope(
                    CoreLedgerTestData.HouseholdId,
                    LedgerOperationCodes
                        .ReversePostedTransaction,
                    idempotencyKey),

                new CommandFingerprint(
                    "SHA256",
                    1,
                    FingerprintValue),

                reversalTransactionId,

                AssetLotId:
                    null,

                CreatedAtUtc:
                    RecordedAtUtc);
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

        private static async Task<PostedDependencyFixture>
    SeedPostedPositiveDependencyAsync(
        WealthLedgerDbContext context,
        Guid lotId)
        {
            var transactionId =
                Guid.NewGuid();

            var entryId =
                Guid.NewGuid();

            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    transactionId,
                    TransactionType.Adjustment));

            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    entryId,
                    transactionId,
                    0,
                    CoreLedgerTestData.FundAssetId,
                    40_000_000,
                    EntryRole.Adjustment));

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
                        40_000_000,

                    CreatedAtUtc =
                        CoreLedgerTestData.CreatedAtUtc
                });

            await context.SaveChangesAsync();

            await CoreLedgerTestData.PostAsync(
                context,
                transactionId);

            return new PostedDependencyFixture(
                transactionId,
                entryId);
        }

        private sealed record PostedPurchaseFixture(
            Guid TransactionId,
            Guid PrincipalEntryId,
            Guid ConsiderationEntryId,
            Guid LotId,
            Guid OpeningAllocationId);

        private sealed record PostedDependencyFixture(
            Guid TransactionId,
            Guid EntryId);
    }
}
