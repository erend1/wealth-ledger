using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.Tests.CoreLedger
{
    public sealed class ReversalUseCaseTests
    {
        private static readonly Guid HouseholdId =
            Guid.Parse(
                "10000000-0000-0000-0000-000000000001");

        private static readonly Guid PortfolioId =
            Guid.Parse(
                "20000000-0000-0000-0000-000000000001");

        private static readonly Guid AccountId =
            Guid.Parse(
                "30000000-0000-0000-0000-000000000001");

        private static readonly Guid CashAssetId =
            Guid.Parse(
                "40000000-0000-0000-0000-000000000001");

        private static readonly Guid FundAssetId =
            Guid.Parse(
                "40000000-0000-0000-0000-000000000002");

        private static readonly Guid HouseholdMemberId =
            Guid.Parse(
                "50000000-0000-0000-0000-000000000001");

        private static readonly DateTimeOffset
            OriginalCreatedAtUtc =
                new(
                    2026,
                    8,
                    20,
                    9,
                    0,
                    0,
                    TimeSpan.Zero);

        private static readonly DateTimeOffset
            OriginalPostedAtUtc =
                new(
                    2026,
                    8,
                    20,
                    9,
                    5,
                    0,
                    TimeSpan.Zero);

        private static readonly DateTimeOffset
            RecordedAtUtc =
                new(
                    2026,
                    8,
                    31,
                    10,
                    0,
                    0,
                    TimeSpan.Zero);

        private static readonly DateOnly ExecutionDate =
            new(
                2026,
                8,
                20);

        private const string IdempotencyKey =
            "reversal-2026-08-31-001";

        // ------------------------------------------------------------
        // PREVIEW
        // ------------------------------------------------------------

        [Fact]
        public async Task
            Preview_Contribution_ReturnsExactInverseEntry()
        {
            var original =
                CreatePostedContribution();

            var store =
                new StubLedgerReversalStore
                {
                    Candidate =
                        CreateEligibleCandidate(
                            original)
                };

            var useCase =
                new PreviewPostedTransactionReversalUseCase(
                    store);

            var result =
                await useCase.ExecuteAsync(
                    original.Id);

            Assert.NotNull(result);

            Assert.True(
                result!.CanReverse);

            Assert.Equal(
                ReversalEligibilityCode.Eligible,
                result.EligibilityCode);

            Assert.Null(
                result.ExistingReversalTransactionId);

            Assert.Empty(
                result.BlockingTransactionIds);

            Assert.Empty(
                result.InverseLotAllocations);

            var originalEntry =
                Assert.Single(
                    original.Entries);

            var inverse =
                Assert.Single(
                    result.InverseEntries);

            Assert.Equal(
                originalEntry.Sequence,
                inverse.Sequence);

            Assert.Equal(
                originalEntry.PortfolioId,
                inverse.PortfolioId);

            Assert.Equal(
                originalEntry.AccountId,
                inverse.AccountId);

            Assert.Equal(
                originalEntry.AssetId,
                inverse.AssetId);

            Assert.Equal(
                originalEntry.Role,
                inverse.Role);

            Assert.Equal(
                originalEntry.UnitPrice,
                inverse.UnitPrice);

            Assert.Equal(
                originalEntry.QuantityDelta
                    .Negate(),
                inverse.QuantityDelta);
        }

        [Fact]
        public async Task
            Preview_Acquisition_ReturnsSameLotInverseAllocation()
        {
            var fixture =
                CreatePostedBuyWithLot();

            var store =
                new StubLedgerReversalStore
                {
                    Candidate =
                        CreateEligibleCandidate(
                            fixture.Transaction,
                            [fixture.Lot])
                };

            var useCase =
                new PreviewPostedTransactionReversalUseCase(
                    store);

            var result =
                await useCase.ExecuteAsync(
                    fixture.Transaction.Id);

            Assert.NotNull(result);
            Assert.True(result!.CanReverse);

            var principal =
                fixture.Transaction.Entries.Single(
                    x =>
                        x.Role
                        == EntryRole.Principal);

            var inverseAllocation =
                Assert.Single(
                    result.InverseLotAllocations);

            Assert.Equal(
                fixture.Lot.Id,
                inverseAllocation.AssetLotId);

            Assert.Equal(
                principal.Id,
                inverseAllocation
                    .OriginalTransactionEntryId);

            Assert.Equal(
                principal.Sequence,
                inverseAllocation.EntrySequence);

            Assert.Equal(
                principal.QuantityDelta.Negate(),
                inverseAllocation.QuantityDelta);
        }

        [Fact]
        public async Task
            Preview_AllocatedDisposal_ReturnsRestoringAllocation()
        {
            var fixture =
                CreatePostedSellWithLot();

            Assert.Equal(
                600m,
                fixture.Lot.CurrentQuantity.ToDecimal());

            var store =
                new StubLedgerReversalStore
                {
                    Candidate =
                        CreateEligibleCandidate(
                            fixture.Transaction,
                            [fixture.Lot])
                };

            var useCase =
                new PreviewPostedTransactionReversalUseCase(
                    store);

            var result =
                await useCase.ExecuteAsync(
                    fixture.Transaction.Id);

            Assert.NotNull(result);
            Assert.True(result!.CanReverse);

            var allocation =
                Assert.Single(
                    result.InverseLotAllocations);

            Assert.Equal(
                400m,
                allocation.QuantityDelta.ToDecimal());
        }

        [Fact]
        public async Task
            Preview_UnknownTarget_ReturnsNull()
        {
            var store =
                new StubLedgerReversalStore
                {
                    Candidate = null
                };

            var useCase =
                new PreviewPostedTransactionReversalUseCase(
                    store);

            var result =
                await useCase.ExecuteAsync(
                    Guid.NewGuid());

            Assert.Null(result);

            Assert.Equal(
                1,
                store.LoadCandidateCalls);
        }

        [Fact]
        public async Task
            Preview_NonPosted_ReturnsNotPosted()
        {
            var transactionId =
                Guid.NewGuid();

            var store =
                new StubLedgerReversalStore
                {
                    Candidate =
                        new ReversalCandidate(
                            transactionId,
                            HouseholdId,
                            TransactionStatus.Draft,
                            TransactionType.Adjustment,
                            ExistingReversalTransactionId:
                                null,
                            BlockingTransactionIds:
                                [],
                            Original:
                                null,
                            AffectedLots:
                                [])
                };

            var useCase =
                new PreviewPostedTransactionReversalUseCase(
                    store);

            var result =
                await useCase.ExecuteAsync(
                    transactionId);

            Assert.NotNull(result);
            Assert.False(result!.CanReverse);

            Assert.Equal(
                ReversalEligibilityCode.NotPosted,
                result.EligibilityCode);
        }

        [Fact]
        public async Task
            Preview_ReversalTarget_ReturnsTargetIsReversal()
        {
            var transactionId =
                Guid.NewGuid();

            var store =
                new StubLedgerReversalStore
                {
                    Candidate =
                        new ReversalCandidate(
                            transactionId,
                            HouseholdId,
                            TransactionStatus.Posted,
                            TransactionType.Reversal,
                            ExistingReversalTransactionId:
                                null,
                            BlockingTransactionIds:
                                [],
                            Original:
                                null,
                            AffectedLots:
                                [])
                };

            var useCase =
                new PreviewPostedTransactionReversalUseCase(
                    store);

            var result =
                await useCase.ExecuteAsync(
                    transactionId);

            Assert.NotNull(result);
            Assert.False(result!.CanReverse);

            Assert.Equal(
                ReversalEligibilityCode.TargetIsReversal,
                result.EligibilityCode);
        }

        [Fact]
        public async Task
            Preview_AlreadyReversed_ReturnsExistingReversalId()
        {
            var transactionId =
                Guid.NewGuid();

            var reversalId =
                Guid.NewGuid();

            var store =
                new StubLedgerReversalStore
                {
                    Candidate =
                        new ReversalCandidate(
                            transactionId,
                            HouseholdId,
                            TransactionStatus.Posted,
                            TransactionType.Adjustment,
                            ExistingReversalTransactionId:
                                reversalId,
                            BlockingTransactionIds:
                                [],
                            Original:
                                null,
                            AffectedLots:
                                [])
                };

            var useCase =
                new PreviewPostedTransactionReversalUseCase(
                    store);

            var result =
                await useCase.ExecuteAsync(
                    transactionId);

            Assert.NotNull(result);

            Assert.False(
                result!.CanReverse);

            Assert.Equal(
                ReversalEligibilityCode.AlreadyReversed,
                result.EligibilityCode);

            Assert.Equal(
                reversalId,
                result.ExistingReversalTransactionId);
        }

        [Fact]
        public async Task
            Preview_Dependencies_ReturnsDistinctSortedBlockers()
        {
            var original =
                CreatePostedContribution();

            var blockerA =
                Guid.Parse(
                    "80000000-0000-0000-0000-000000000001");

            var blockerB =
                Guid.Parse(
                    "80000000-0000-0000-0000-000000000002");

            var blockerC =
                Guid.Parse(
                    "80000000-0000-0000-0000-000000000003");

            var store =
                new StubLedgerReversalStore
                {
                    Candidate =
                        CreateEligibleCandidate(
                            original,
                            blockers:
                            [
                                blockerC,
                                blockerA,
                                blockerB,
                                blockerA,
                                blockerC
                            ])
                };

            var useCase =
                new PreviewPostedTransactionReversalUseCase(
                    store);

            var result =
                await useCase.ExecuteAsync(
                    original.Id);

            Assert.NotNull(result);
            Assert.False(result!.CanReverse);

            Assert.Equal(
                ReversalEligibilityCode
                    .BlockedByDependencies,
                result.EligibilityCode);

            Assert.Equal(
                new[]
                {
                    blockerA,
                    blockerB,
                    blockerC
                },
                result.BlockingTransactionIds);
        }

        [Fact]
        public async Task
            Preview_MissingReconstructedOriginal_ReturnsUnsupported()
        {
            var transactionId =
                Guid.NewGuid();

            var store =
                new StubLedgerReversalStore
                {
                    Candidate =
                        new ReversalCandidate(
                            transactionId,
                            HouseholdId,
                            TransactionStatus.Posted,
                            TransactionType.Adjustment,
                            ExistingReversalTransactionId:
                                null,
                            BlockingTransactionIds:
                                [],
                            Original:
                                null,
                            AffectedLots:
                                [])
                };

            var useCase =
                new PreviewPostedTransactionReversalUseCase(
                    store);

            var result =
                await useCase.ExecuteAsync(
                    transactionId);

            Assert.NotNull(result);
            Assert.False(result!.CanReverse);

            Assert.Equal(
                ReversalEligibilityCode
                    .UnsupportedPersistedShape,
                result.EligibilityCode);
        }

        [Fact]
        public async Task
            Preview_LongMinValue_ReturnsUnsupported()
        {
            var original =
                CreatePostedAdjustment(
                    QuantityDelta.FromRaw(
                        long.MinValue));

            var store =
                new StubLedgerReversalStore
                {
                    Candidate =
                        CreateEligibleCandidate(
                            original)
                };

            var useCase =
                new PreviewPostedTransactionReversalUseCase(
                    store);

            var result =
                await useCase.ExecuteAsync(
                    original.Id);

            Assert.NotNull(result);
            Assert.False(result!.CanReverse);

            Assert.Equal(
                ReversalEligibilityCode
                    .UnsupportedPersistedShape,
                result.EligibilityCode);
        }

        // ------------------------------------------------------------
        // FIRST SUCCESSFUL REVERSAL
        // ------------------------------------------------------------

        [Fact]
        public async Task
            Reverse_FirstSubmission_CreatesPostsAndCommitsExactReversal()
        {
            var original =
                CreatePostedContribution();

            var store =
                new StubLedgerReversalStore
                {
                    TargetIdentity =
                        new ReversalTargetIdentity(
                            original.Id,
                            HouseholdId),

                    Candidate =
                        CreateEligibleCandidate(
                            original)
                };

            var useCase =
                new ReversePostedTransactionUseCase(
                    store,
                    new FixedTimeProvider(
                        RecordedAtUtc));

            var result =
                await useCase.ExecuteAsync(
                    IdempotencyKey,
                    new ReversePostedTransactionCommand(
                        original.Id,
                        "  Incorrect contribution amount.  "));

            Assert.NotNull(result);

            var reversal =
                Assert.IsType<LedgerTransaction>(
                    store.AttemptedReversal);

            var receipt =
                Assert.IsType<LedgerSubmissionReceipt>(
                    store.AttemptedReceipt);

            Assert.Equal(
                1,
                store.TryCommitCalls);

            Assert.Equal(
                reversal.Id,
                result!.ReversalTransactionId);

            Assert.Equal(
                original.Id,
                result.ReversalOfTransactionId);

            Assert.Equal(
                TransactionType.Reversal,
                reversal.Type);

            Assert.Equal(
                TransactionStatus.Posted,
                reversal.Status);

            Assert.Equal(
                original.Id,
                reversal.ReversalOfTransactionId);

            Assert.Equal(
                RecordedAtUtc,
                reversal.CreatedAtUtc);

            Assert.Equal(
                RecordedAtUtc,
                reversal.PostedAtUtc);

            Assert.Equal(
                original.OrderDate,
                reversal.OrderDate);

            Assert.Equal(
                original.ExecutionDate,
                reversal.ExecutionDate);

            Assert.Equal(
                original.SettlementDate,
                reversal.SettlementDate);

            Assert.Equal(
                "Incorrect contribution amount.",
                reversal.Note);

            Assert.Null(
                reversal.ExternalReference);

            Assert.Null(
                reversal.CashFlowDetail);

            Assert.Empty(
                reversal.Costs);

            var originalEntries =
                original.Entries
                    .OrderBy(x => x.Sequence)
                    .ToArray();

            var reversalEntries =
                reversal.Entries
                    .OrderBy(x => x.Sequence)
                    .ToArray();

            Assert.Equal(
                originalEntries.Length,
                reversalEntries.Length);

            for (var index = 0;
                 index < originalEntries.Length;
                 index++)
            {
                var originalEntry =
                    originalEntries[index];

                var reversalEntry =
                    reversalEntries[index];

                Assert.Equal(
                    originalEntry.Sequence,
                    reversalEntry.Sequence);

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
                    originalEntry.Role,
                    reversalEntry.Role);

                Assert.Equal(
                    originalEntry.UnitPrice,
                    reversalEntry.UnitPrice);

                Assert.Equal(
                    originalEntry.QuantityDelta.Negate(),
                    reversalEntry.QuantityDelta);
            }

            Assert.Equal(
                HouseholdId,
                receipt.Scope.HouseholdId);

            Assert.Equal(
                LedgerOperationCodes
                    .ReversePostedTransaction,
                receipt.Scope.OperationCode);

            Assert.Equal(
                IdempotencyKey,
                receipt.Scope.IdempotencyKey);

            Assert.Equal(
                reversal.Id,
                receipt.TransactionId);

            Assert.Null(
                receipt.AssetLotId);

            Assert.Equal(
                RecordedAtUtc,
                receipt.CreatedAtUtc);
        }

        // ------------------------------------------------------------
        // LOT MUTATION
        // ------------------------------------------------------------

        [Fact]
        public async Task
            Reverse_Acquisition_AppendsOppositeAllocationToExistingLot()
        {
            var fixture =
                CreatePostedBuyWithLot();

            Assert.Equal(
                1000m,
                fixture.Lot.CurrentQuantity.ToDecimal());

            var store =
                new StubLedgerReversalStore
                {
                    TargetIdentity =
                        new ReversalTargetIdentity(
                            fixture.Transaction.Id,
                            HouseholdId),

                    Candidate =
                        CreateEligibleCandidate(
                            fixture.Transaction,
                            [fixture.Lot])
                };

            var useCase =
                new ReversePostedTransactionUseCase(
                    store,
                    new FixedTimeProvider(
                        RecordedAtUtc));

            await useCase.ExecuteAsync(
                IdempotencyKey,
                new ReversePostedTransactionCommand(
                    fixture.Transaction.Id,
                    "Incorrect acquisition."));

            var reversal =
                Assert.IsType<LedgerTransaction>(
                    store.AttemptedReversal);

            var reversalPrincipal =
                reversal.Entries.Single(
                    x =>
                        x.Role
                        == EntryRole.Principal);

            Assert.Single(
                store.AttemptedLots);

            Assert.Equal(
                fixture.Lot.Id,
                Assert.Single(
                    store.AttemptedLots).Id);

            Assert.Equal(
                0m,
                fixture.Lot.CurrentQuantity.ToDecimal());

            Assert.Equal(
                2,
                fixture.Lot.Allocations.Count);

            var reversalAllocation =
                fixture.Lot.Allocations.Single(
                    x =>
                        x.TransactionEntryId
                        == reversalPrincipal.Id);

            Assert.Equal(
                -1000m,
                reversalAllocation
                    .QuantityDelta
                    .ToDecimal());

            Assert.Equal(
                CostBasisStatus.Known,
                fixture.Lot.CostBasis.Status);
        }

        [Fact]
        public async Task
            Reverse_Disposal_RestoresSameLotQuantity()
        {
            var fixture =
                CreatePostedSellWithLot();

            Assert.Equal(
                600m,
                fixture.Lot.CurrentQuantity.ToDecimal());

            var store =
                new StubLedgerReversalStore
                {
                    TargetIdentity =
                        new ReversalTargetIdentity(
                            fixture.Transaction.Id,
                            HouseholdId),

                    Candidate =
                        CreateEligibleCandidate(
                            fixture.Transaction,
                            [fixture.Lot])
                };

            var useCase =
                new ReversePostedTransactionUseCase(
                    store,
                    new FixedTimeProvider(
                        RecordedAtUtc));

            await useCase.ExecuteAsync(
                IdempotencyKey,
                new ReversePostedTransactionCommand(
                    fixture.Transaction.Id,
                    "Incorrect disposal."));

            var reversal =
                Assert.IsType<LedgerTransaction>(
                    store.AttemptedReversal);

            var reversalPrincipal =
                reversal.Entries.Single(
                    x =>
                        x.Role
                        == EntryRole.Principal);

            Assert.Equal(
                1000m,
                fixture.Lot.CurrentQuantity.ToDecimal());

            var reversalAllocation =
                fixture.Lot.Allocations.Single(
                    x =>
                        x.TransactionEntryId
                        == reversalPrincipal.Id);

            Assert.Equal(
                400m,
                reversalAllocation
                    .QuantityDelta
                    .ToDecimal());
        }

        // ------------------------------------------------------------
        // RECEIPT-FIRST IDEMPOTENCY
        // ------------------------------------------------------------

        [Fact]
        public async Task
            Reverse_EquivalentReplay_ReturnsReceiptBeforeLoadingCurrentCandidate()
        {
            var original =
                CreatePostedContribution();

            var reversalId =
                Guid.Parse(
                    "90000000-0000-0000-0000-000000000001");

            var command =
                new ReversePostedTransactionCommand(
                    original.Id,
                    "Incorrect amount.");

            var scope =
                CreateScope();

            var store =
                new StubLedgerReversalStore
                {
                    TargetIdentity =
                        new ReversalTargetIdentity(
                            original.Id,
                            HouseholdId),

                    ExistingReceipt =
                        CreateReceipt(
                            scope,
                            command,
                            reversalId),

                    Candidate =
                        new ReversalCandidate(
                            original.Id,
                            HouseholdId,
                            TransactionStatus.Posted,
                            original.Type,
                            ExistingReversalTransactionId:
                                reversalId,
                            BlockingTransactionIds:
                                [],
                            Original:
                                null,
                            AffectedLots:
                                [])
                };

            var useCase =
                new ReversePostedTransactionUseCase(
                    store,
                    new FixedTimeProvider(
                        RecordedAtUtc));

            var result =
                await useCase.ExecuteAsync(
                    IdempotencyKey,
                    command);

            Assert.NotNull(result);

            Assert.Equal(
                reversalId,
                result!.ReversalTransactionId);

            Assert.Equal(
                1,
                store.FindTargetIdentityCalls);

            Assert.Equal(
                1,
                store.FindReceiptCalls);

            Assert.Equal(
                0,
                store.LoadCandidateCalls);

            Assert.Equal(
                0,
                store.TryCommitCalls);
        }

        [Fact]
        public async Task
            Reverse_SameKeyDifferentReason_ThrowsIdempotencyConflict()
        {
            var original =
                CreatePostedContribution();

            var originalCommand =
                new ReversePostedTransactionCommand(
                    original.Id,
                    "Incorrect amount.");

            var changedCommand =
                originalCommand with
                {
                    Reason =
                        "Incorrect date."
                };

            var store =
                new StubLedgerReversalStore
                {
                    TargetIdentity =
                        new ReversalTargetIdentity(
                            original.Id,
                            HouseholdId),

                    ExistingReceipt =
                        CreateReceipt(
                            CreateScope(),
                            originalCommand,
                            Guid.NewGuid())
                };

            var useCase =
                new ReversePostedTransactionUseCase(
                    store,
                    new FixedTimeProvider(
                        RecordedAtUtc));

            await Assert.ThrowsAsync<
                IdempotencyConflictException>(
                () =>
                    useCase.ExecuteAsync(
                        IdempotencyKey,
                        changedCommand));

            Assert.Equal(
                0,
                store.LoadCandidateCalls);

            Assert.Equal(
                0,
                store.TryCommitCalls);
        }

        [Fact]
        public async Task
            Reverse_SameScopedKeyDifferentTarget_ThrowsIdempotencyConflict()
        {
            var originalA =
                CreatePostedContribution(
                    Guid.Parse(
                        "60000000-0000-0000-0000-000000000010"));

            var originalB =
                CreatePostedContribution(
                    Guid.Parse(
                        "60000000-0000-0000-0000-000000000011"));

            var commandA =
                new ReversePostedTransactionCommand(
                    originalA.Id,
                    "Incorrect amount.");

            var commandB =
                new ReversePostedTransactionCommand(
                    originalB.Id,
                    "Incorrect amount.");

            var store =
                new StubLedgerReversalStore
                {
                    TargetIdentity =
                        new ReversalTargetIdentity(
                            originalB.Id,
                            HouseholdId),

                    ExistingReceipt =
                        CreateReceipt(
                            CreateScope(),
                            commandA,
                            Guid.NewGuid())
                };

            var useCase =
                new ReversePostedTransactionUseCase(
                    store,
                    new FixedTimeProvider(
                        RecordedAtUtc));

            await Assert.ThrowsAsync<
                IdempotencyConflictException>(
                () =>
                    useCase.ExecuteAsync(
                        IdempotencyKey,
                        commandB));

            Assert.Equal(
                0,
                store.LoadCandidateCalls);

            Assert.Equal(
                0,
                store.TryCommitCalls);
        }

        [Fact]
        public async Task
            Reverse_SurroundingReasonWhitespace_ReplaysEquivalentCommand()
        {
            var original =
                CreatePostedContribution();

            var storedCommand =
                new ReversePostedTransactionCommand(
                    original.Id,
                    "Incorrect amount.");

            var winnerId =
                Guid.NewGuid();

            var store =
                new StubLedgerReversalStore
                {
                    TargetIdentity =
                        new ReversalTargetIdentity(
                            original.Id,
                            HouseholdId),

                    ExistingReceipt =
                        CreateReceipt(
                            CreateScope(),
                            storedCommand,
                            winnerId)
                };

            var useCase =
                new ReversePostedTransactionUseCase(
                    store,
                    new FixedTimeProvider(
                        RecordedAtUtc));

            var result =
                await useCase.ExecuteAsync(
                    IdempotencyKey,
                    new ReversePostedTransactionCommand(
                        original.Id,
                        "   Incorrect amount.   "));

            Assert.NotNull(result);

            Assert.Equal(
                winnerId,
                result!.ReversalTransactionId);

            Assert.Equal(
                0,
                store.LoadCandidateCalls);

            Assert.Equal(
                0,
                store.TryCommitCalls);
        }

        // ------------------------------------------------------------
        // ELIGIBILITY REJECTION
        // ------------------------------------------------------------

        [Fact]
        public async Task
            Reverse_AlreadyReversedCandidate_IsRejected()
        {
            var original =
                CreatePostedContribution();

            var existingReversalId =
                Guid.NewGuid();

            var store =
                new StubLedgerReversalStore
                {
                    TargetIdentity =
                        new ReversalTargetIdentity(
                            original.Id,
                            HouseholdId),

                    Candidate =
                        new ReversalCandidate(
                            original.Id,
                            HouseholdId,
                            TransactionStatus.Posted,
                            original.Type,
                            existingReversalId,
                            [],
                            Original:
                                null,
                            AffectedLots:
                                [])
                };

            var useCase =
                new ReversePostedTransactionUseCase(
                    store,
                    new FixedTimeProvider(
                        RecordedAtUtc));

            var exception =
                await Assert.ThrowsAsync<
                    ReversalCommandRejectedException>(
                    () =>
                        useCase.ExecuteAsync(
                            IdempotencyKey,
                            new ReversePostedTransactionCommand(
                                original.Id,
                                "Incorrect amount.")));

            Assert.Equal(
                ReversalEligibilityCode.AlreadyReversed,
                exception.EligibilityCode);

            Assert.Equal(
                existingReversalId,
                exception.ExistingReversalTransactionId);

            Assert.Equal(
                0,
                store.TryCommitCalls);
        }

        [Fact]
        public async Task
            Reverse_BlockedCandidate_IsRejectedWithSortedBlockers()
        {
            var original =
                CreatePostedContribution();

            var blockerA =
                Guid.Parse(
                    "80000000-0000-0000-0000-000000000001");

            var blockerB =
                Guid.Parse(
                    "80000000-0000-0000-0000-000000000002");

            var blockerC =
                Guid.Parse(
                    "80000000-0000-0000-0000-000000000003");

            var store =
                new StubLedgerReversalStore
                {
                    TargetIdentity =
                        new ReversalTargetIdentity(
                            original.Id,
                            HouseholdId),

                    Candidate =
                        CreateEligibleCandidate(
                            original,
                            blockers:
                            [
                                blockerC,
                                blockerB,
                                blockerA,
                                blockerB
                            ])
                };

            var useCase =
                new ReversePostedTransactionUseCase(
                    store,
                    new FixedTimeProvider(
                        RecordedAtUtc));

            var exception =
                await Assert.ThrowsAsync<
                    ReversalCommandRejectedException>(
                    () =>
                        useCase.ExecuteAsync(
                            IdempotencyKey,
                            new ReversePostedTransactionCommand(
                                original.Id,
                                "Incorrect amount.")));

            Assert.Equal(
                ReversalEligibilityCode
                    .BlockedByDependencies,
                exception.EligibilityCode);

            Assert.Equal(
                new[]
                {
                    blockerA,
                    blockerB,
                    blockerC
                },
                exception.BlockingTransactionIds);

            Assert.Equal(
                0,
                store.TryCommitCalls);
        }

        [Fact]
        public async Task
            Reverse_UnsupportedCandidate_IsRejected()
        {
            var transactionId =
                Guid.NewGuid();

            var store =
                new StubLedgerReversalStore
                {
                    TargetIdentity =
                        new ReversalTargetIdentity(
                            transactionId,
                            HouseholdId),

                    Candidate =
                        new ReversalCandidate(
                            transactionId,
                            HouseholdId,
                            TransactionStatus.Posted,
                            TransactionType.Adjustment,
                            ExistingReversalTransactionId:
                                null,
                            BlockingTransactionIds:
                                [],
                            Original:
                                null,
                            AffectedLots:
                                [])
                };

            var useCase =
                new ReversePostedTransactionUseCase(
                    store,
                    new FixedTimeProvider(
                        RecordedAtUtc));

            var exception =
                await Assert.ThrowsAsync<
                    ReversalCommandRejectedException>(
                    () =>
                        useCase.ExecuteAsync(
                            IdempotencyKey,
                            new ReversePostedTransactionCommand(
                                transactionId,
                                "Unsupported history.")));

            Assert.Equal(
                ReversalEligibilityCode
                    .UnsupportedPersistedShape,
                exception.EligibilityCode);

            Assert.Equal(
                0,
                store.TryCommitCalls);
        }

        // ------------------------------------------------------------
        // COMMIT RACES
        // ------------------------------------------------------------

        [Fact]
        public async Task
            Reverse_ReceiptWinner_ReturnsWinnerReversal()
        {
            var original =
                CreatePostedContribution();

            var command =
                new ReversePostedTransactionCommand(
                    original.Id,
                    "Incorrect amount.");

            var winnerId =
                Guid.NewGuid();

            var winnerReceipt =
                CreateReceipt(
                    CreateScope(),
                    command,
                    winnerId);

            var store =
                new StubLedgerReversalStore
                {
                    TargetIdentity =
                        new ReversalTargetIdentity(
                            original.Id,
                            HouseholdId),

                    Candidate =
                        CreateEligibleCandidate(
                            original),

                    CommitResult =
                        new ReversalCommitResult
                            .ReceiptWinner(
                                winnerReceipt)
                };

            var useCase =
                new ReversePostedTransactionUseCase(
                    store,
                    new FixedTimeProvider(
                        RecordedAtUtc));

            var result =
                await useCase.ExecuteAsync(
                    IdempotencyKey,
                    command);

            Assert.NotNull(result);

            Assert.Equal(
                winnerId,
                result!.ReversalTransactionId);

            Assert.Equal(
                1,
                store.TryCommitCalls);
        }

        [Fact]
        public async Task
            Reverse_CommitAlreadyReversed_ThrowsWithWinnerId()
        {
            var original =
                CreatePostedContribution();

            var winnerId =
                Guid.NewGuid();

            var store =
                new StubLedgerReversalStore
                {
                    TargetIdentity =
                        new ReversalTargetIdentity(
                            original.Id,
                            HouseholdId),

                    Candidate =
                        CreateEligibleCandidate(
                            original),

                    CommitResult =
                        new ReversalCommitResult
                            .AlreadyReversed(
                                winnerId)
                };

            var useCase =
                new ReversePostedTransactionUseCase(
                    store,
                    new FixedTimeProvider(
                        RecordedAtUtc));

            var exception =
                await Assert.ThrowsAsync<
                    ReversalCommandRejectedException>(
                    () =>
                        useCase.ExecuteAsync(
                            IdempotencyKey,
                            new ReversePostedTransactionCommand(
                                original.Id,
                                "Incorrect amount.")));

            Assert.Equal(
                ReversalEligibilityCode.AlreadyReversed,
                exception.EligibilityCode);

            Assert.Equal(
                winnerId,
                exception.ExistingReversalTransactionId);

            Assert.Equal(
                1,
                store.TryCommitCalls);
        }

        [Fact]
        public async Task
            Reverse_CommitDependencyConflict_ReturnsSortedBlockers()
        {
            var original =
                CreatePostedContribution();

            var blockerA =
                Guid.Parse(
                    "80000000-0000-0000-0000-000000000001");

            var blockerB =
                Guid.Parse(
                    "80000000-0000-0000-0000-000000000002");

            var blockerC =
                Guid.Parse(
                    "80000000-0000-0000-0000-000000000003");

            var store =
                new StubLedgerReversalStore
                {
                    TargetIdentity =
                        new ReversalTargetIdentity(
                            original.Id,
                            HouseholdId),

                    Candidate =
                        CreateEligibleCandidate(
                            original),

                    CommitResult =
                        new ReversalCommitResult
                            .DependencyConflict(
                            [
                                blockerC,
                                blockerA,
                                blockerB,
                                blockerA
                            ])
                };

            var useCase =
                new ReversePostedTransactionUseCase(
                    store,
                    new FixedTimeProvider(
                        RecordedAtUtc));

            var exception =
                await Assert.ThrowsAsync<
                    ReversalCommandRejectedException>(
                    () =>
                        useCase.ExecuteAsync(
                            IdempotencyKey,
                            new ReversePostedTransactionCommand(
                                original.Id,
                                "Incorrect amount.")));

            Assert.Equal(
                ReversalEligibilityCode
                    .BlockedByDependencies,
                exception.EligibilityCode);

            Assert.Equal(
                new[]
                {
                    blockerA,
                    blockerB,
                    blockerC
                },
                exception.BlockingTransactionIds);
        }

        // ------------------------------------------------------------
        // DEFENSIVE PORT-CONTRACT TESTS
        // ------------------------------------------------------------

        [Fact]
        public async Task
            Reverse_ReceiptWithUnexpectedLotId_IsRejected()
        {
            var original =
                CreatePostedContribution();

            var command =
                new ReversePostedTransactionCommand(
                    original.Id,
                    "Incorrect amount.");

            var validReceipt =
                CreateReceipt(
                    CreateScope(),
                    command,
                    Guid.NewGuid());

            var invalidReceipt =
                validReceipt with
                {
                    AssetLotId =
                        Guid.NewGuid()
                };

            var store =
                new StubLedgerReversalStore
                {
                    TargetIdentity =
                        new ReversalTargetIdentity(
                            original.Id,
                            HouseholdId),

                    ExistingReceipt =
                        invalidReceipt
                };

            var useCase =
                new ReversePostedTransactionUseCase(
                    store,
                    new FixedTimeProvider(
                        RecordedAtUtc));

            await Assert.ThrowsAsync<
                InvalidOperationException>(
                () =>
                    useCase.ExecuteAsync(
                        IdempotencyKey,
                        command));

            Assert.Equal(
                0,
                store.LoadCandidateCalls);

            Assert.Equal(
                0,
                store.TryCommitCalls);
        }

        [Fact]
        public async Task
            Reverse_CommittedResultWithDifferentReceipt_Throws()
        {
            var original =
                CreatePostedContribution();

            var command =
                new ReversePostedTransactionCommand(
                    original.Id,
                    "Incorrect amount.");

            var store =
                new StubLedgerReversalStore
                {
                    TargetIdentity =
                        new ReversalTargetIdentity(
                            original.Id,
                            HouseholdId),

                    Candidate =
                        CreateEligibleCandidate(
                            original),

                    CommitResult =
                        new ReversalCommitResult
                            .Committed(
                                CreateReceipt(
                                    CreateScope(),
                                    command,
                                    Guid.NewGuid()))
                };

            var useCase =
                new ReversePostedTransactionUseCase(
                    store,
                    new FixedTimeProvider(
                        RecordedAtUtc));

            await Assert.ThrowsAsync<
                InvalidOperationException>(
                () =>
                    useCase.ExecuteAsync(
                        IdempotencyKey,
                        command));

            Assert.Equal(
                1,
                store.TryCommitCalls);
        }

        [Fact]
        public async Task
            Reverse_ReplayWithUnsupportedStoredFingerprintVersion_Throws()
        {
            var original =
                CreatePostedContribution();

            var command =
                new ReversePostedTransactionCommand(
                    original.Id,
                    "Incorrect amount.");

            var store =
                new StubLedgerReversalStore
                {
                    TargetIdentity =
                        new ReversalTargetIdentity(
                            original.Id,
                            HouseholdId),

                    ExistingReceipt =
                        new LedgerSubmissionReceipt(
                            CreateScope(),
                            new CommandFingerprint(
                                "SHA256",
                                999,
                                "irrelevant"),
                            Guid.NewGuid(),
                            AssetLotId:
                                null,
                            CreatedAtUtc:
                                RecordedAtUtc)
                };

            var useCase =
                new ReversePostedTransactionUseCase(
                    store,
                    new FixedTimeProvider(
                        RecordedAtUtc));

            await Assert.ThrowsAsync<
                NotSupportedException>(
                () =>
                    useCase.ExecuteAsync(
                        IdempotencyKey,
                        command));

            Assert.Equal(
                0,
                store.LoadCandidateCalls);

            Assert.Equal(
                0,
                store.TryCommitCalls);
        }

        // ------------------------------------------------------------
        // HELPERS
        // ------------------------------------------------------------

        private static ReversalCandidate
            CreateEligibleCandidate(
                LedgerTransaction original,
                IReadOnlyCollection<AssetLot>? affectedLots = null,
                IReadOnlyList<Guid>? blockers = null)
        {
            return new ReversalCandidate(
                original.Id,
                original.HouseholdId,
                original.Status,
                original.Type,
                ExistingReversalTransactionId:
                    null,
                BlockingTransactionIds:
                    blockers ?? [],
                Original:
                    original,
                AffectedLots:
                    affectedLots ?? []);
        }

        private static LedgerSubmissionScope CreateScope()
        {
            return new LedgerSubmissionScope(
                HouseholdId,
                LedgerOperationCodes
                    .ReversePostedTransaction,
                IdempotencyKey);
        }

        private static LedgerSubmissionReceipt CreateReceipt(
            LedgerSubmissionScope scope,
            ReversePostedTransactionCommand command,
            Guid reversalTransactionId)
        {
            return new LedgerSubmissionReceipt(
                scope,
                ReversePostedTransactionCommandFingerprint
                    .ComputeCurrent(command),
                reversalTransactionId,
                AssetLotId:
                    null,
                CreatedAtUtc:
                    RecordedAtUtc);
        }

        private static LedgerTransaction
            CreatePostedContribution(
                Guid? transactionId = null)
        {
            return LedgerTransaction.ReconstitutePosted(
                transactionId
                    ?? Guid.Parse(
                        "60000000-0000-0000-0000-000000000100"),
                HouseholdId,
                TransactionType.Contribution,
                OriginalCreatedAtUtc,
                OriginalPostedAtUtc,
                orderDate:
                    null,
                executionDate:
                    ExecutionDate,
                settlementDate:
                    null,
                reversalOfTransactionId:
                    null,
                externalReference:
                    "PAYROLL-001",
                note:
                    "Original contribution",
                entries:
                [
                    new LedgerTransactionEntrySnapshot(
                        Guid.Parse(
                            "61000000-0000-0000-0000-000000000100"),
                        0,
                        PortfolioId,
                        AccountId,
                        CashAssetId,
                        QuantityDelta.FromDecimal(
                            1_000m),
                        EntryRole.Principal,
                        null)
                ],
                costs:
                    [],
                cashFlowDetail:
                    new LedgerCashFlowSnapshot(
                        CashFlowCategory.Salary,
                        HouseholdMemberId));
        }

        private static LedgerTransaction
            CreatePostedAdjustment(
                QuantityDelta quantityDelta)
        {
            return LedgerTransaction.ReconstitutePosted(
                Guid.Parse(
                    "60000000-0000-0000-0000-000000000200"),
                HouseholdId,
                TransactionType.Adjustment,
                OriginalCreatedAtUtc,
                OriginalPostedAtUtc,
                orderDate:
                    null,
                executionDate:
                    ExecutionDate,
                settlementDate:
                    null,
                reversalOfTransactionId:
                    null,
                externalReference:
                    null,
                note:
                    null,
                entries:
                [
                    new LedgerTransactionEntrySnapshot(
                        Guid.Parse(
                            "61000000-0000-0000-0000-000000000200"),
                        0,
                        PortfolioId,
                        AccountId,
                        FundAssetId,
                        quantityDelta,
                        EntryRole.Adjustment,
                        null)
                ],
                costs:
                    [],
                cashFlowDetail:
                    null);
        }

        private static (
            LedgerTransaction Transaction,
            AssetLot Lot)
            CreatePostedBuyWithLot()
        {
            var transactionId =
                Guid.Parse(
                    "60000000-0000-0000-0000-000000000300");

            var principalEntryId =
                Guid.Parse(
                    "61000000-0000-0000-0000-000000000301");

            var considerationEntryId =
                Guid.Parse(
                    "61000000-0000-0000-0000-000000000302");

            var lotId =
                Guid.Parse(
                    "70000000-0000-0000-0000-000000000300");

            var transaction =
                LedgerTransaction.ReconstitutePosted(
                    transactionId,
                    HouseholdId,
                    TransactionType.Buy,
                    OriginalCreatedAtUtc,
                    OriginalPostedAtUtc,
                    orderDate:
                        null,
                    executionDate:
                        ExecutionDate,
                    settlementDate:
                        null,
                    reversalOfTransactionId:
                        null,
                    externalReference:
                        "BUY-001",
                    note:
                        "Original purchase",
                    entries:
                    [
                        new LedgerTransactionEntrySnapshot(
                            principalEntryId,
                            0,
                            PortfolioId,
                            AccountId,
                            FundAssetId,
                            QuantityDelta.FromDecimal(
                                1_000m),
                            EntryRole.Principal,
                            UnitPrice.FromDecimal(
                                5m,
                                CurrencyCode.TRY)),

                        new LedgerTransactionEntrySnapshot(
                            considerationEntryId,
                            1,
                            PortfolioId,
                            AccountId,
                            CashAssetId,
                            QuantityDelta.FromDecimal(
                                -5_000m),
                            EntryRole.Consideration,
                            null)
                    ],
                    costs:
                        [],
                    cashFlowDetail:
                        null);

            var lot =
                AssetLot.Reconstitute(
                    lotId,
                    FundAssetId,
                    principalEntryId,
                    ExecutionDate,
                    CostBasis.Known(
                        Money.FromMinorUnits(
                            500_000,
                            CurrencyCode.TRY)),
                    physicalGoldDetail:
                        null,
                    createdAtUtc:
                        OriginalCreatedAtUtc,
                    allocations:
                    [
                        new AssetLotAllocationSnapshot(
                            Guid.Parse(
                                "71000000-0000-0000-0000-000000000301"),
                            principalEntryId,
                            QuantityDelta.FromDecimal(
                                1_000m))
                    ]);

            return (
                transaction,
                lot);
        }

        private static (
            LedgerTransaction Transaction,
            AssetLot Lot)
            CreatePostedSellWithLot()
        {
            var transactionId =
                Guid.Parse(
                    "60000000-0000-0000-0000-000000000400");

            var principalEntryId =
                Guid.Parse(
                    "61000000-0000-0000-0000-000000000401");

            var considerationEntryId =
                Guid.Parse(
                    "61000000-0000-0000-0000-000000000402");

            var openingEntryId =
                Guid.Parse(
                    "61000000-0000-0000-0000-000000000499");

            var lotId =
                Guid.Parse(
                    "70000000-0000-0000-0000-000000000400");

            var transaction =
                LedgerTransaction.ReconstitutePosted(
                    transactionId,
                    HouseholdId,
                    TransactionType.Sell,
                    OriginalCreatedAtUtc,
                    OriginalPostedAtUtc,
                    orderDate:
                        null,
                    executionDate:
                        ExecutionDate,
                    settlementDate:
                        null,
                    reversalOfTransactionId:
                        null,
                    externalReference:
                        "SELL-001",
                    note:
                        "Original disposal",
                    entries:
                    [
                        new LedgerTransactionEntrySnapshot(
                            principalEntryId,
                            0,
                            PortfolioId,
                            AccountId,
                            FundAssetId,
                            QuantityDelta.FromDecimal(
                                -400m),
                            EntryRole.Principal,
                            UnitPrice.FromDecimal(
                                5m,
                                CurrencyCode.TRY)),

                        new LedgerTransactionEntrySnapshot(
                            considerationEntryId,
                            1,
                            PortfolioId,
                            AccountId,
                            CashAssetId,
                            QuantityDelta.FromDecimal(
                                2_000m),
                            EntryRole.Consideration,
                            null)
                    ],
                    costs:
                        [],
                    cashFlowDetail:
                        null);

            var lot =
                AssetLot.Reconstitute(
                    lotId,
                    FundAssetId,
                    openingEntryId,
                    new DateOnly(
                        2026,
                        7,
                        1),
                    CostBasis.Known(
                        Money.FromMinorUnits(
                            500_000,
                            CurrencyCode.TRY)),
                    physicalGoldDetail:
                        null,
                    createdAtUtc:
                        OriginalCreatedAtUtc.AddDays(
                            -30),
                    allocations:
                    [
                        new AssetLotAllocationSnapshot(
                            Guid.Parse(
                                "71000000-0000-0000-0000-000000000401"),
                            openingEntryId,
                            QuantityDelta.FromDecimal(
                                1_000m)),

                        new AssetLotAllocationSnapshot(
                            Guid.Parse(
                                "71000000-0000-0000-0000-000000000402"),
                            principalEntryId,
                            QuantityDelta.FromDecimal(
                                -400m))
                    ]);

            return (
                transaction,
                lot);
        }

        // ------------------------------------------------------------
        // STUB STORE
        // ------------------------------------------------------------

        private sealed class StubLedgerReversalStore
            : ILedgerReversalStore
        {
            internal ReversalTargetIdentity?
                TargetIdentity
            {
                get;
                set;
            }

            internal LedgerSubmissionReceipt?
                ExistingReceipt
            {
                get;
                set;
            }

            internal ReversalCandidate?
                Candidate
            {
                get;
                set;
            }

            internal ReversalCommitResult?
                CommitResult
            {
                get;
                set;
            }

            internal LedgerSubmissionScope?
                LastLookupScope
            {
                get;
                private set;
            }

            internal LedgerSubmissionReceipt?
                AttemptedReceipt
            {
                get;
                private set;
            }

            internal LedgerTransaction?
                AttemptedReversal
            {
                get;
                private set;
            }

            internal IReadOnlyCollection<AssetLot>
                AttemptedLots
            {
                get;
                private set;
            } = [];

            internal int FindTargetIdentityCalls
            {
                get;
                private set;
            }

            internal int FindReceiptCalls
            {
                get;
                private set;
            }

            internal int LoadCandidateCalls
            {
                get;
                private set;
            }

            internal int TryCommitCalls
            {
                get;
                private set;
            }

            public Task<ReversalTargetIdentity?>
                FindTargetIdentityAsync(
                    Guid transactionId,
                    CancellationToken cancellationToken = default)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                FindTargetIdentityCalls++;

                return Task.FromResult(
                    TargetIdentity);
            }

            public Task<LedgerSubmissionReceipt?>
                FindReceiptAsync(
                    LedgerSubmissionScope scope,
                    CancellationToken cancellationToken = default)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                FindReceiptCalls++;
                LastLookupScope = scope;

                return Task.FromResult(
                    ExistingReceipt);
            }

            public Task<ReversalCandidate?>
                LoadCandidateAsync(
                    Guid transactionId,
                    CancellationToken cancellationToken = default)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                LoadCandidateCalls++;

                return Task.FromResult(
                    Candidate);
            }

            public Task<ReversalCommitResult>
                TryCommitAsync(
                    LedgerSubmissionReceipt receipt,
                    LedgerTransaction reversal,
                    IReadOnlyCollection<AssetLot> affectedLots,
                    CancellationToken cancellationToken = default)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                TryCommitCalls++;

                AttemptedReceipt =
                    receipt;

                AttemptedReversal =
                    reversal;

                AttemptedLots =
                    affectedLots;

                return Task.FromResult(
                    CommitResult
                    ?? new ReversalCommitResult
                        .Committed(
                            receipt));
            }
        }

        private sealed class FixedTimeProvider
            : TimeProvider
        {
            private readonly DateTimeOffset
                _utcNow;

            internal FixedTimeProvider(
                DateTimeOffset utcNow)
            {
                _utcNow =
                    utcNow;
            }

            public override DateTimeOffset GetUtcNow()
            {
                return _utcNow;
            }
        }
    }
}