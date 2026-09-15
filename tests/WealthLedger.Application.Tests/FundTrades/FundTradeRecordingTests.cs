using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.FundTrades;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.Tests.FundTrades;

public sealed class FundPurchaseCompatibilityTests
{
    private const string Key = "purchase-key-1";

    private static readonly DateTimeOffset RecordedAtUtc =
        new(2026, 3, 12, 9, 0, 0, TimeSpan.Zero);

    /*
     * The accepted precedence of Decision 14 over Decision 16: the legacy
     * request shape keeps working, but a submission that names neither a
     * reference nor a note can never be explained afterwards and is refused.
     */
    [Fact]
    public async Task NewSubmission_WithoutProvenance_IsRejected()
    {
        var (useCase, _, posting) = Build();

        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () =>
                    useCase.ExecuteAsync(
                        Key,
                        LegacyCommand() with
                        {
                            ExternalReference = null,
                            Note = null
                        }));

        Assert.Equal(
            FundTradeErrorCodes.ProvenanceRequired,
            exception.ErrorCode);

        Assert.Equal(0, posting.CommitCalls);
    }

    [Fact]
    public async Task LegacyShapedSubmission_KeepsBothLegsInOneAccount()
    {
        var (useCase, _, posting) = Build();

        await useCase.ExecuteAsync(Key, LegacyCommand());

        var transaction = posting.Transaction!;

        var principal =
            transaction.Entries.Single(
                x => x.Role == EntryRole.Principal);

        var consideration =
            transaction.Entries.Single(
                x => x.Role == EntryRole.Consideration);

        Assert.Equal(
            principal.AccountId,
            consideration.AccountId);
    }

    [Fact]
    public async Task SeparateAccounts_PostBothLegsToTheirOwnAccount()
    {
        var (useCase, _, posting) =
            Build(separateAccounts: true);

        await useCase.ExecuteAsync(
            Key,
            LegacyCommand() with
            {
                CashAccountId = FundTradeIds.CashAccount
            });

        var transaction = posting.Transaction!;

        Assert.Equal(
            FundTradeIds.FundAccount,
            transaction.Entries
                .Single(x => x.Role == EntryRole.Principal)
                .AccountId);

        Assert.Equal(
            FundTradeIds.CashAccount,
            transaction.Entries
                .Single(x => x.Role == EntryRole.Consideration)
                .AccountId);
    }

    [Fact]
    public async Task AdditionalCosts_CreateOneFeeAndOneTaxEntry()
    {
        var (useCase, _, posting) = Build();

        await useCase.ExecuteAsync(
            Key,
            LegacyCommand() with
            {
                Costs =
                [
                    new FundTradeCostInput(
                        CostType.Commission,
                        CostTreatment.AdditionalCashOutflow,
                        Money.FromMinorUnits(3_00, FundTradeIds.Try)),

                    new FundTradeCostInput(
                        CostType.Brokerage,
                        CostTreatment.AdditionalCashOutflow,
                        Money.FromMinorUnits(2_00, FundTradeIds.Try)),

                    new FundTradeCostInput(
                        CostType.OtherTax,
                        CostTreatment.AdditionalCashOutflow,
                        Money.FromMinorUnits(1_00, FundTradeIds.Try))
                ]
            });

        var transaction = posting.Transaction!;

        var fee =
            Assert.Single(
                transaction.Entries,
                x => x.Role == EntryRole.Fee);

        var tax =
            Assert.Single(
                transaction.Entries,
                x => x.Role == EntryRole.Tax);

        // Commission and brokerage aggregate into one fee entry.
        Assert.Equal(-5_00_000_000L, fee.QuantityDelta.RawE8);
        Assert.Equal(-1_00_000_000L, tax.QuantityDelta.RawE8);

        // The lot capitalizes the separately settled outflow exactly once.
        Assert.Equal(
            1_006_00,
            posting.NewLot!.CostBasis.Amount!.MinorUnits);
    }

    [Fact]
    public async Task IncludedCosts_DoNotCreateSupportingEntries()
    {
        var (useCase, _, posting) = Build();

        await useCase.ExecuteAsync(
            Key,
            LegacyCommand() with
            {
                Costs =
                [
                    new FundTradeCostInput(
                        CostType.Commission,
                        CostTreatment.IncludedInConsideration,
                        Money.FromMinorUnits(3_00, FundTradeIds.Try))
                ]
            });

        var transaction = posting.Transaction!;

        Assert.DoesNotContain(
            transaction.Entries,
            x => x.Role is EntryRole.Fee or EntryRole.Tax);

        Assert.Equal(
            1_000_00,
            posting.NewLot!.CostBasis.Amount!.MinorUnits);

        // The component is still recorded as an explanation.
        Assert.Single(transaction.Costs);
    }

    [Fact]
    public async Task Version1Receipt_ReplaysForLegacyEquivalentCommand()
    {
        var command = LegacyCommand();

        var v1Fingerprint =
            RecordFundPurchaseCommandFingerprint.Compute(
                command,
                "SHA256",
                version: 1);

        var transactionId = Guid.NewGuid();
        var lotId = Guid.NewGuid();

        var (useCase, submission, posting) =
            Build(
                existingReceipt:
                    new LedgerSubmissionReceipt(
                        new LedgerSubmissionScope(
                            FundTradeIds.Household,
                            LedgerOperationCodes.RecordFundPurchase,
                            Key),
                        v1Fingerprint,
                        transactionId,
                        lotId,
                        RecordedAtUtc));

        var result =
            await useCase.ExecuteAsync(Key, command);

        Assert.Equal(transactionId, result.TransactionId);
        Assert.Equal(lotId, result.AssetLotId);
        Assert.Equal(0, posting.CommitCalls);
        Assert.Equal(1, submission.FindReceiptCalls);
    }

    /*
     * A version-1 receipt cannot represent a separate cash account, so a
     * request carrying one is a different trade rather than a retry. Silently
     * recomputing version 1 would have ignored the new fact.
     */
    [Fact]
    public async Task Version1Receipt_RejectsCommandCarryingNewFacts()
    {
        var legacy = LegacyCommand();

        var v1Fingerprint =
            RecordFundPurchaseCommandFingerprint.Compute(
                legacy,
                "SHA256",
                version: 1);

        var (useCase, _, posting) =
            Build(
                separateAccounts: true,
                existingReceipt:
                    new LedgerSubmissionReceipt(
                        new LedgerSubmissionScope(
                            FundTradeIds.Household,
                            LedgerOperationCodes.RecordFundPurchase,
                            Key),
                        v1Fingerprint,
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        RecordedAtUtc));

        await Assert.ThrowsAsync<IdempotencyConflictException>(
            () =>
                useCase.ExecuteAsync(
                    Key,
                    legacy with
                    {
                        CashAccountId = FundTradeIds.CashAccount
                    }));

        Assert.Equal(0, posting.CommitCalls);
    }

    [Fact]
    public async Task NewSubmission_UsesVersion2Fingerprint()
    {
        var (useCase, _, posting) = Build();

        await useCase.ExecuteAsync(Key, LegacyCommand());

        Assert.Equal(
            2,
            posting.AttemptedReceipt!.Fingerprint.Version);
    }

    [Fact]
    public async Task NegativeCashWithoutNote_IsRejectedAtPostTime()
    {
        var (useCase, _, posting) =
            Build(
                postingStatus:
                    FundSaleCommitStatus.NegativeCashNoteRequired);

        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () =>
                    useCase.ExecuteAsync(
                        Key,
                        LegacyCommand() with
                        {
                            Note = null,
                            ExternalReference = "REF-ONLY"
                        }));

        Assert.Equal(
            FundTradeErrorCodes.NegativeCashNoteRequired,
            exception.ErrorCode);

        // The store was reached, which is what makes the check
        // unbypassable by a direct caller.
        Assert.Equal(1, posting.CommitCalls);
        Assert.False(posting.LastHadExplanatoryNote);
    }

    private static RecordFundPurchaseCommand LegacyCommand()
        => new(
            FundTradeIds.Household,
            FundTradeIds.Portfolio,
            FundTradeIds.FundAccount,
            FundTradeIds.FundAsset,
            FundTradeIds.CashAsset,
            Quantity.FromDecimal(100m),
            UnitPrice.FromDecimal(10m, FundTradeIds.Try),
            Money.FromMinorUnits(1_000_00, FundTradeIds.Try),
            new DateOnly(2026, 3, 10),
            ExternalReference: "REF-1",
            Note: "Monthly fund purchase.");

    private static (
        RecordFundPurchaseUseCase UseCase,
        StubSubmissionStore Submission,
        FundTradePostingStoreFake Posting) Build(
            bool separateAccounts = false,
            LedgerSubmissionReceipt? existingReceipt = null,
            FundSaleCommitStatus postingStatus =
                FundSaleCommitStatus.Committed)
    {
        var references =
            FundTradeReferenceStoreFake.CreateFor(
                FundTradeIds.Household,
                FundTradeIds.Portfolio,
                FundTradeIds.FundAccount,
                separateAccounts
                    ? FundTradeIds.CashAccount
                    : FundTradeIds.FundAccount,
                FundTradeIds.FundAsset,
                FundTradeIds.CashAsset);

        var submission =
            new StubSubmissionStore
            {
                ExistingReceipt = existingReceipt
            };

        var posting =
            new FundTradePostingStoreFake
            {
                NextStatus = postingStatus
            };

        return (
            new RecordFundPurchaseUseCase(
                references,
                submission,
                posting,
                new FundTradeTimeProvider(RecordedAtUtc)),
            submission,
            posting);
    }
}

public sealed class FundSaleRecordingTests
{
    private const string Key = "sale-key-1";

    private static readonly Guid LotOne =
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static readonly Guid LotTwo =
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private static readonly DateTimeOffset RecordedAtUtc =
        new(2026, 3, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sale_PostsNegativePrincipalAndPositiveConsideration()
    {
        var (useCase, _, posting) = Build();

        await useCase.ExecuteAsync(Key, Command());

        var transaction = posting.Transaction!;

        Assert.Equal(
            TransactionType.Sell,
            transaction.Type);

        Assert.Equal(
            -10_00000000L,
            transaction.Entries
                .Single(x => x.Role == EntryRole.Principal)
                .QuantityDelta.RawE8);

        Assert.True(
            transaction.Entries
                .Single(x => x.Role == EntryRole.Consideration)
                .QuantityDelta.IsPositive);
    }

    [Fact]
    public async Task Sale_CarriesReviewedPlanToTheStore()
    {
        var (useCase, _, posting) = Build();

        await useCase.ExecuteAsync(Key, Command());

        Assert.Equal(
            2,
            posting.ReviewedPlan.Count);

        Assert.Equal(
            LotOne,
            posting.ReviewedPlan[0].AssetLotId);
    }

    [Fact]
    public async Task Sale_ReceiptLeavesLotIdNull()
    {
        var (useCase, _, posting) = Build();

        await useCase.ExecuteAsync(Key, Command());

        Assert.Null(
            posting.AttemptedReceipt!.AssetLotId);
    }

    [Fact]
    public async Task Sale_WithoutReviewedPlan_IsRejected()
    {
        var (useCase, _, posting) = Build();

        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () =>
                    useCase.ExecuteAsync(
                        Key,
                        Command() with
                        {
                            ReviewedPlan = null,
                            ReviewedPlanFingerprint = null
                        }));

        Assert.Equal(
            FundTradeErrorCodes.ReviewedPlanRequired,
            exception.ErrorCode);

        Assert.Equal(0, posting.CommitCalls);
    }

    [Fact]
    public async Task Sale_PlanNotReconcilingToQuantity_IsRejected()
    {
        var (useCase, _, posting) = Build();

        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () =>
                    useCase.ExecuteAsync(
                        Key,
                        Command() with
                        {
                            ReviewedPlan =
                            [
                                new ReviewedLotAllocation(
                                    LotOne,
                                    Quantity.FromDecimal(4m))
                            ]
                        }));

        Assert.Equal(
            FundTradeErrorCodes.ReviewedPlanRequired,
            exception.ErrorCode);

        Assert.Equal(0, posting.CommitCalls);
    }

    [Fact]
    public async Task Sale_TamperedPlanFingerprint_IsRejectedBeforeWriting()
    {
        var (useCase, _, posting) = Build();

        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () =>
                    useCase.ExecuteAsync(
                        Key,
                        Command() with
                        {
                            ReviewedPlanFingerprint =
                                "sha256-1-deadbeef"
                        }));

        Assert.Equal(
            FundTradeErrorCodes.StaleReviewedPlan,
            exception.ErrorCode);

        Assert.Equal(0, posting.CommitCalls);
    }

    [Fact]
    public async Task Sale_StaleAtPostTime_WritesNothing()
    {
        var (useCase, _, posting) =
            Build(
                postingStatus:
                    FundSaleCommitStatus.StaleReviewedPlan);

        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () => useCase.ExecuteAsync(Key, Command()));

        Assert.Equal(
            FundTradeErrorCodes.StaleReviewedPlan,
            exception.ErrorCode);

        Assert.Equal(
            FundTradeErrorCategory.Conflict,
            exception.Category);
    }

    [Fact]
    public async Task Sale_InsufficientAtPostTime_ReportsShortfall()
    {
        var (useCase, _, _) =
            Build(
                postingStatus:
                    FundSaleCommitStatus.InsufficientQuantity);

        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () => useCase.ExecuteAsync(Key, Command()));

        Assert.Equal(
            FundTradeErrorCodes.InsufficientFundQuantity,
            exception.ErrorCode);
    }

    /*
     * A sale changes the very quantities its own eligibility depends on, so
     * an honest retry has to be answered from the receipt rather than
     * re-validated against the world the sale itself created.
     */
    [Fact]
    public async Task Sale_EquivalentRetry_ReturnsOriginalWithoutRevalidating()
    {
        var command = Command();

        var fingerprint =
            RecordFundSaleCommandFingerprint.ComputeCurrent(command);

        var transactionId = Guid.NewGuid();

        var (useCase, _, posting) =
            Build(
                existingReceipt:
                    new LedgerSubmissionReceipt(
                        new LedgerSubmissionScope(
                            FundTradeIds.Household,
                            LedgerOperationCodes.RecordFundSale,
                            Key),
                        fingerprint,
                        transactionId,
                        AssetLotId: null,
                        RecordedAtUtc));

        var result =
            await useCase.ExecuteAsync(Key, command);

        Assert.Equal(transactionId, result.TransactionId);
        Assert.Equal(0, posting.CommitCalls);
    }

    [Fact]
    public async Task Sale_SameKeyDifferentCommand_IsConflict()
    {
        var original = Command();

        var fingerprint =
            RecordFundSaleCommandFingerprint.ComputeCurrent(original);

        var (useCase, _, posting) =
            Build(
                existingReceipt:
                    new LedgerSubmissionReceipt(
                        new LedgerSubmissionScope(
                            FundTradeIds.Household,
                            LedgerOperationCodes.RecordFundSale,
                            Key),
                        fingerprint,
                        Guid.NewGuid(),
                        AssetLotId: null,
                        RecordedAtUtc));

        await Assert.ThrowsAsync<IdempotencyConflictException>(
            () =>
                useCase.ExecuteAsync(
                    Key,
                    original with
                    {
                        CashConsideration =
                            Money.FromMinorUnits(
                                999_00,
                                FundTradeIds.Try),
                        Note = "Different amount."
                    }));

        Assert.Equal(0, posting.CommitCalls);
    }

    /*
     * Two genuine sales can share every visible financial fact, so a
     * different key must be allowed to produce a second transaction rather
     * than being guessed at as a duplicate.
     */
    [Fact]
    public async Task Sale_DifferentKeyIdenticalFacts_IsAllowed()
    {
        var (first, _, firstPosting) = Build();
        var (second, _, secondPosting) = Build();

        await first.ExecuteAsync("key-a", Command());
        await second.ExecuteAsync("key-b", Command());

        Assert.Equal(1, firstPosting.CommitCalls);
        Assert.Equal(1, secondPosting.CommitCalls);

        Assert.NotEqual(
            firstPosting.Transaction!.Id,
            secondPosting.Transaction!.Id);
    }

    [Fact]
    public void SaleFingerprint_IgnoresReviewedPlan()
    {
        var command = Command();

        var withDifferentPlan =
            command with
            {
                ReviewedPlan =
                [
                    new ReviewedLotAllocation(
                        LotTwo,
                        Quantity.FromDecimal(10m))
                ],
                ReviewedPlanFingerprint = "sha256-1-other"
            };

        Assert.Equal(
            RecordFundSaleCommandFingerprint.ComputeCurrent(command),
            RecordFundSaleCommandFingerprint.ComputeCurrent(
                withDifferentPlan));
    }

    private static FundSaleCommand Command()
    {
        var scope = FundTradeIds.Scope();

        var plan =
            new List<ReviewedLotAllocation>
            {
                new(LotOne, Quantity.FromDecimal(6m)),
                new(LotTwo, Quantity.FromDecimal(4m))
            };

        return new FundSaleCommand(
            FundTradeIds.Household,
            FundTradeIds.Portfolio,
            FundTradeIds.FundAccount,
            FundTradeIds.CashAccount,
            FundTradeIds.FundAsset,
            FundTradeIds.CashAsset,
            Quantity.FromDecimal(10m),
            UnitPrice.FromDecimal(10m, FundTradeIds.Try),
            Money.FromMinorUnits(100_00, FundTradeIds.Try),
            new DateOnly(2026, 3, 10),
            ExternalReference: "SALE-1",
            Note: "Partial liquidation.",
            ReviewedPlan: plan,
            ReviewedPlanFingerprint:
                FundSalePlanFingerprint.Compute(
                    scope,
                    Quantity.FromDecimal(10m).RawE8,
                    plan));
    }

    private static (
        RecordFundSaleUseCase UseCase,
        StubSubmissionStore Submission,
        FundTradePostingStoreFake Posting) Build(
            LedgerSubmissionReceipt? existingReceipt = null,
            FundSaleCommitStatus postingStatus =
                FundSaleCommitStatus.Committed)
    {
        var submission =
            new StubSubmissionStore
            {
                ExistingReceipt = existingReceipt
            };

        var posting =
            new FundTradePostingStoreFake
            {
                NextStatus = postingStatus
            };

        return (
            new RecordFundSaleUseCase(
                FundTradeReferenceStoreFake.CreateValid(),
                submission,
                posting,
                new FundTradeTimeProvider(RecordedAtUtc)),
            submission,
            posting);
    }
}

internal sealed class StubSubmissionStore : ILedgerSubmissionStore
{
    internal LedgerSubmissionReceipt? ExistingReceipt { get; set; }

    internal int FindReceiptCalls { get; private set; }

    public Task<LedgerSubmissionReceipt?> FindReceiptAsync(
        LedgerSubmissionScope scope,
        CancellationToken cancellationToken = default)
    {
        FindReceiptCalls++;

        return Task.FromResult(
            ExistingReceipt?.Scope == scope
                ? ExistingReceipt
                : null);
    }

    public Task<LedgerSubmissionCommitResult> TryCommitAsync(
        LedgerSubmissionReceipt receipt,
        LedgerTransaction transaction,
        IReadOnlyCollection<AssetLot> newLots,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(
            "Fund trades commit through the fund-trade posting store.");
}
