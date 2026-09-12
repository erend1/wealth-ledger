using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.Tests.OpeningBalances;

public sealed class OpeningBalanceUseCaseTests
{
    private static readonly Guid HouseholdId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");

    private static readonly Guid PortfolioId =
        Guid.Parse("20000000-0000-0000-0000-000000000001");

    private static readonly Guid AccountId =
        Guid.Parse("30000000-0000-0000-0000-000000000001");

    private static readonly Guid InstitutionId =
        Guid.Parse("40000000-0000-0000-0000-000000000001");

    private static readonly Guid AssetId =
        Guid.Parse("50000000-0000-0000-0000-000000000001");

    private static readonly DateTimeOffset RecordedAtUtc =
        new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly AsOfDate =
        new(2026, 8, 31);

    [Fact]
    public async Task Preview_CashPreservesExactMinorUnitsWithoutLots()
    {
        var references = CreateReferences(
            AssetType.Cash,
            AccountType.Cash,
            AssetUnit.CurrencyUnit,
            LotTrackingMode.None,
            CurrencyCode.TRY,
            institutionId: null);
        var history = new HistoryStoreFake();
        var useCase = CreatePreviewUseCase(references, history);

        var result = await useCase.ExecuteAsync(
            CreateCommand(
                Quantity.FromDecimal(12_345.67m),
                lots: []));

        Assert.Equal(1_234_567_000_000, result.QuantityRawE8);
        Assert.Equal(0, result.AllocationTotalRawE8);
        Assert.True(result.AllocationsReconcile);
        Assert.Equal(
            CostBasisStatus.NotApplicable,
            result.UnlottedCostBasisStatus);
        Assert.Empty(result.Lots);
        Assert.Null(result.TotalFineWeightGrams);
        Assert.Contains(
            OpeningBalanceCommandEvaluator
                .IndependentReconciliationRequiredWarning,
            result.WarningCodes);
        Assert.Equal(0, history.WriteCount);
    }

    [Fact]
    public async Task Preview_ForeignCurrencyRequiresDifferentHouseholdCurrency()
    {
        var references = CreateReferences(
            AssetType.Currency,
            AccountType.Cash,
            AssetUnit.CurrencyUnit,
            LotTrackingMode.None,
            CurrencyCode.USD,
            institutionId: null);
        references.Currencies[CurrencyCode.USD] =
            new OpeningBalanceCurrencyReference(
                CurrencyCode.USD,
                "Synthetic US Dollar",
                2);

        var result = await CreatePreviewUseCase(
                references,
                new HistoryStoreFake())
            .ExecuteAsync(
                CreateCommand(
                    Quantity.FromDecimal(1_234.56m),
                    lots: []));

        Assert.Equal(AssetType.Currency, result.AssetType);
        Assert.Equal("USD", result.AssetBaseCurrencyCode);
        Assert.Equal(123_456_000_000, result.QuantityRawE8);
    }

    [Fact]
    public async Task Preview_FundReconcilesKnownAndUnknownLotsExactly()
    {
        var references = CreateFundReferences();
        var command = CreateFundCommand();

        var result = await CreatePreviewUseCase(
                references,
                new HistoryStoreFake())
            .ExecuteAsync(command);

        Assert.Equal(12_345_678_900, result.QuantityRawE8);
        Assert.Equal(result.QuantityRawE8, result.AllocationTotalRawE8);
        Assert.Equal(2, result.Lots.Count);
        Assert.Contains(
            result.Lots,
            lot => lot.CostBasisStatus == CostBasisStatus.Known
                && lot.OriginalCostBasisMinorUnits == 12_345);
        Assert.Contains(
            result.Lots,
            lot => lot.CostBasisStatus == CostBasisStatus.Unknown
                && lot.OriginalCostBasisMinorUnits is null);
        Assert.Null(result.UnlottedCostBasisStatus);
    }

    [Fact]
    public async Task Preview_EquityPreservesUnknownCostAsAbsent()
    {
        var references = CreateReferences(
            AssetType.Equity,
            AccountType.Investment,
            AssetUnit.Share,
            LotTrackingMode.Required,
            CurrencyCode.TRY,
            InstitutionId);

        var result = await CreatePreviewUseCase(
                references,
                new HistoryStoreFake())
            .ExecuteAsync(
                CreateCommand(
                    Quantity.FromDecimal(17m),
                    [
                        new OpeningBalanceLotCommand(
                            Quantity.FromDecimal(17m),
                            AcquiredOn: null,
                            CostBasis.Unknown())
                    ]));

        var lot = Assert.Single(result.Lots);
        Assert.Equal(CostBasisStatus.Unknown, lot.CostBasisStatus);
        Assert.Null(lot.OriginalCostBasisMinorUnits);
        Assert.Null(lot.CostBasisCurrencyCode);
    }

    [Fact]
    public async Task Preview_PhysicalGoldDerivesFineWeightWithoutPersistingIt()
    {
        var references = CreateReferences(
            AssetType.PhysicalGold,
            AccountType.PhysicalVault,
            AssetUnit.GrossGram,
            LotTrackingMode.Required,
            CurrencyCode.TRY,
            institutionId: null);
        var goldDetail =
            new PhysicalGoldLotDetail(
                Fineness.FromPerMille(916m),
                pieceCount: 2,
                hallmark: "SYNTHETIC-916",
                certificateReference: "SYNTHETIC-CERTIFICATE",
                note: "Two synthetic matching bracelets.");

        var result = await CreatePreviewUseCase(
                references,
                new HistoryStoreFake())
            .ExecuteAsync(
                CreateCommand(
                    Quantity.FromDecimal(25.25m),
                    [
                        new OpeningBalanceLotCommand(
                            Quantity.FromDecimal(25.25m),
                            AcquiredOn: null,
                            CostBasis.Unknown(),
                            goldDetail)
                    ]));

        var lot = Assert.Single(result.Lots);
        Assert.Equal(916_000, lot.FinenessPartsPerMillion);
        Assert.Equal(2, lot.PieceCount);
        Assert.Equal(23.129m, lot.FineWeightGrams);
        Assert.Equal(23.129m, result.TotalFineWeightGrams);
    }

    [Fact]
    public async Task Preview_RejectsPartialOptionalLotAllocation()
    {
        var references = CreateFundReferences(
            LotTrackingMode.Optional);
        var command = CreateFundCommand() with
        {
            Lots =
            [
                new OpeningBalanceLotCommand(
                    Quantity.FromDecimal(100m),
                    AcquiredOn: null,
                    CostBasis.Unknown())
            ]
        };

        var exception =
            await Assert.ThrowsAsync<OpeningBalanceException>(
                () => CreatePreviewUseCase(
                        references,
                        new HistoryStoreFake())
                    .ExecuteAsync(command));

        Assert.Equal(
            OpeningBalanceErrorCodes.LotTotalMismatch,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Preview_RejectsAcquisitionDateAfterAsOfDate()
    {
        var references = CreateFundReferences();
        var command = CreateFundCommand();
        command = command with
        {
            Lots =
            [
                command.Lots[0] with
                {
                    AcquiredOn = AsOfDate.AddDays(1)
                },
                command.Lots[1]
            ]
        };

        var exception =
            await Assert.ThrowsAsync<OpeningBalanceException>(
                () => CreatePreviewUseCase(
                        references,
                        new HistoryStoreFake())
                    .ExecuteAsync(command));

        Assert.Equal(
            OpeningBalanceErrorCodes.AcquisitionDateAfterAsOf,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Preview_RejectsFutureAsOfDateUsingOperatingTimeZone()
    {
        var references = CreateFundReferences();
        var command = CreateFundCommand() with
        {
            AsOfDate = new DateOnly(2026, 9, 11)
        };
        var timeProvider = new FixedTimeProvider(
            new DateTimeOffset(
                2026,
                9,
                10,
                21,
                30,
                0,
                TimeSpan.Zero),
            TimeZoneInfo.Utc);

        var exception =
            await Assert.ThrowsAsync<OpeningBalanceException>(
                () => new PreviewOpeningBalanceUseCase(
                        references,
                        new HistoryStoreFake(),
                        timeProvider)
                    .ExecuteAsync(command));

        Assert.Equal(
            OpeningBalanceErrorCodes.AsOfDateInFuture,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Preview_RejectsFractionalCurrencyMinorUnit()
    {
        var references = CreateReferences(
            AssetType.Cash,
            AccountType.Cash,
            AssetUnit.CurrencyUnit,
            LotTrackingMode.None,
            CurrencyCode.TRY,
            institutionId: null);

        var exception =
            await Assert.ThrowsAsync<OpeningBalanceException>(
                () => CreatePreviewUseCase(
                        references,
                        new HistoryStoreFake())
                    .ExecuteAsync(
                        CreateCommand(
                            Quantity.FromRaw(1),
                            lots: [])));

        Assert.Equal(
            OpeningBalanceErrorCodes.QuantityInvalid,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Preview_WhenEffectiveOpeningExists_ReturnsRelatedTransaction()
    {
        var existingTransactionId = Guid.NewGuid();
        var history = new HistoryStoreFake
        {
            Result = new OpeningBalanceEffectiveHistory(
                existingTransactionId,
                HasOtherEffectiveHistory: false)
        };

        var exception =
            await Assert.ThrowsAsync<OpeningBalanceException>(
                () => CreatePreviewUseCase(
                        CreateFundReferences(),
                        history)
                    .ExecuteAsync(CreateFundCommand()));

        Assert.Equal(
            OpeningBalanceErrorCategory.Conflict,
            exception.Category);
        Assert.Equal(
            OpeningBalanceErrorCodes.AlreadyExists,
            exception.ErrorCode);
        Assert.Equal(existingTransactionId, exception.RelatedTransactionId);
    }

    [Fact]
    public async Task Record_PostsOneEntryAndMultipleLotsAtomically()
    {
        var references = CreateFundReferences();
        var history = new HistoryStoreFake();
        var store = new SubmissionStoreFake();
        var useCase = CreateRecordUseCase(references, history, store);
        var command = CreateFundCommand();

        var result = await useCase.ExecuteAsync(
            "synthetic-opening-001",
            command);

        var transaction = Assert.IsType<LedgerTransaction>(store.Transaction);
        var entry = Assert.Single(transaction.Entries);

        Assert.Equal(TransactionType.OpeningBalance, transaction.Type);
        Assert.Equal(TransactionStatus.Posted, transaction.Status);
        Assert.Equal(AsOfDate, transaction.ExecutionDate);
        Assert.Null(transaction.OrderDate);
        Assert.Null(transaction.SettlementDate);
        Assert.Equal("Synthetic opening source.", transaction.Note);
        Assert.Empty(transaction.Costs);
        Assert.Null(transaction.CashFlowDetail);
        Assert.Equal(EntryRole.Principal, entry.Role);
        Assert.Equal(command.Quantity.RawE8, entry.QuantityDelta.RawE8);
        Assert.Null(entry.UnitPrice);
        Assert.Equal(2, store.Lots.Count);
        Assert.All(
            store.Lots,
            lot => Assert.Equal(
                entry.Id,
                lot.OpeningTransactionEntryId));
        Assert.Equal(
            command.Quantity.RawE8,
            store.Lots.Sum(
                lot => lot.Allocations.Single().QuantityDelta.RawE8));
        Assert.Equal(
            LedgerOperationCodes.RecordOpeningBalance,
            store.Receipt?.Scope.OperationCode);
        Assert.Null(store.Receipt?.AssetLotId);
        Assert.Equal(result.TransactionId, transaction.Id);
        Assert.Equal(2, result.AssetLotIds.Count);
        Assert.Equal(result.SubmittedQuantityRawE8, result.PersistedQuantityRawE8);
    }

    [Fact]
    public async Task Record_EquivalentReplayPrecedesCurrentSemanticState()
    {
        var references = CreateFundReferences();
        var history = new HistoryStoreFake();
        var store = new SubmissionStoreFake();
        var useCase = CreateRecordUseCase(references, history, store);
        var command = CreateFundCommand();

        var first = await useCase.ExecuteAsync(
            "synthetic-opening-replay-001",
            command);

        history.Result = new OpeningBalanceEffectiveHistory(
            first.TransactionId,
            HasOtherEffectiveHistory: false);
        references.ThrowOnRead = true;

        var replay = await useCase.ExecuteAsync(
            "synthetic-opening-replay-001",
            command with
            {
                Lots = command.Lots.Reverse().ToArray()
            });

        Assert.Equal(first.TransactionId, replay.TransactionId);
        Assert.Equal(first.AssetLotIds, replay.AssetLotIds);
        Assert.Equal(
            first.SubmittedQuantityRawE8,
            replay.SubmittedQuantityRawE8);
        Assert.Equal(
            first.PersistedQuantityRawE8,
            replay.PersistedQuantityRawE8);
        Assert.Equal(1, history.ReadCount);
        Assert.Equal(1, store.CommitCount);
    }

    [Fact]
    public async Task Record_SameKeyWithChangedFactsConflictsWithoutSecondWrite()
    {
        var references = CreateFundReferences();
        var history = new HistoryStoreFake();
        var store = new SubmissionStoreFake();
        var useCase = CreateRecordUseCase(references, history, store);
        var command = CreateFundCommand();

        await useCase.ExecuteAsync(
            "synthetic-opening-conflict-001",
            command);

        await Assert.ThrowsAsync<IdempotencyConflictException>(
            () => useCase.ExecuteAsync(
                "synthetic-opening-conflict-001",
                command with
                {
                    Note = "Different synthetic source."
                }));

        Assert.Equal(1, store.CommitCount);
    }

    private static PreviewOpeningBalanceUseCase CreatePreviewUseCase(
        ReferenceReadStoreFake references,
        HistoryStoreFake history)
        => new(
            references,
            history,
            new FixedTimeProvider(
                RecordedAtUtc,
                TimeZoneInfo.Utc));

    private static RecordOpeningBalanceUseCase CreateRecordUseCase(
        ReferenceReadStoreFake references,
        HistoryStoreFake history,
        SubmissionStoreFake store)
        => new(
            references,
            history,
            store,
            store,
            new FixedTimeProvider(
                RecordedAtUtc,
                TimeZoneInfo.Utc));

    private static RecordOpeningBalanceCommand CreateFundCommand()
        => CreateCommand(
            Quantity.FromDecimal(123.456789m),
            [
                new OpeningBalanceLotCommand(
                    Quantity.FromDecimal(23.456789m),
                    new DateOnly(2025, 1, 10),
                    CostBasis.Known(
                        Money.FromMinorUnits(
                            12_345,
                            CurrencyCode.TRY))),
                new OpeningBalanceLotCommand(
                    Quantity.FromDecimal(100m),
                    AcquiredOn: null,
                    CostBasis.Unknown())
            ]);

    private static RecordOpeningBalanceCommand CreateCommand(
        Quantity quantity,
        IReadOnlyList<OpeningBalanceLotCommand> lots)
        => new(
            HouseholdId,
            PortfolioId,
            AccountId,
            AssetId,
            AsOfDate,
            quantity,
            "SYNTHETIC-STATEMENT",
            "Synthetic opening source.",
            lots);

    private static ReferenceReadStoreFake CreateFundReferences(
        LotTrackingMode lotTrackingMode = LotTrackingMode.Required)
        => CreateReferences(
            AssetType.Fund,
            AccountType.Investment,
            AssetUnit.FundUnit,
            lotTrackingMode,
            CurrencyCode.TRY,
            InstitutionId);

    private static ReferenceReadStoreFake CreateReferences(
        AssetType assetType,
        AccountType accountType,
        AssetUnit assetUnit,
        LotTrackingMode lotTrackingMode,
        CurrencyCode baseCurrency,
        Guid? institutionId)
    {
        var references = new ReferenceReadStoreFake
        {
            Household =
                new OpeningBalanceHouseholdReference(
                    HouseholdId,
                    CurrencyCode.TRY),
            Portfolio =
                new OpeningBalancePortfolioReference(
                    PortfolioId,
                    HouseholdId,
                    "CORE",
                    "Synthetic Core Portfolio",
                    PortfolioStatus.Active),
            Account =
                new OpeningBalanceAccountReference(
                    AccountId,
                    HouseholdId,
                    institutionId,
                    "SYNTHETIC_ACCOUNT",
                    "Synthetic Account",
                    accountType,
                    IsActive: true,
                    OpenedOn: new DateOnly(2026, 1, 1),
                    ClosedOn: null),
            Asset =
                new OpeningBalanceAssetReference(
                    AssetId,
                    "SYNTHETIC_ASSET",
                    "Synthetic Asset",
                    assetType,
                    assetUnit,
                    baseCurrency,
                    lotTrackingMode,
                    IsActive: true,
                    RecordedAtUtc.AddYears(-1)),
            Institution = institutionId is null
                ? null
                : new OpeningBalanceInstitutionReference(
                    institutionId.Value,
                    "SYNTHETIC_INSTITUTION",
                    "Synthetic Institution",
                    InstitutionType.Broker,
                    IsActive: true)
        };

        references.Currencies[CurrencyCode.TRY] =
            new OpeningBalanceCurrencyReference(
                CurrencyCode.TRY,
                "Synthetic Turkish Lira",
                2);

        return references;
    }

    private sealed class ReferenceReadStoreFake
        : IOpeningBalanceReferenceReadStore
    {
        internal OpeningBalanceHouseholdReference? Household { get; init; }

        internal OpeningBalancePortfolioReference? Portfolio { get; init; }

        internal OpeningBalanceAccountReference? Account { get; init; }

        internal OpeningBalanceAssetReference? Asset { get; init; }

        internal OpeningBalanceInstitutionReference? Institution { get; init; }

        internal Dictionary<CurrencyCode, OpeningBalanceCurrencyReference>
            Currencies
        { get; } = [];

        internal bool ThrowOnRead { get; set; }

        public Task<OpeningBalanceHouseholdReference?> FindHouseholdAsync(
            Guid householdId,
            CancellationToken cancellationToken = default)
        {
            EnsureReadable();
            return Task.FromResult(
                Household?.HouseholdId == householdId
                    ? Household
                    : null);
        }

        public Task<OpeningBalanceCurrencyReference?> FindCurrencyAsync(
            CurrencyCode code,
            CancellationToken cancellationToken = default)
        {
            EnsureReadable();
            Currencies.TryGetValue(code, out var currency);
            return Task.FromResult(currency);
        }

        public Task<OpeningBalanceInstitutionReference?> FindInstitutionAsync(
            Guid institutionId,
            CancellationToken cancellationToken = default)
        {
            EnsureReadable();
            return Task.FromResult(
                Institution?.InstitutionId == institutionId
                    ? Institution
                    : null);
        }

        public Task<OpeningBalancePortfolioReference?> FindPortfolioAsync(
            Guid portfolioId,
            CancellationToken cancellationToken = default)
        {
            EnsureReadable();
            return Task.FromResult(
                Portfolio?.PortfolioId == portfolioId
                    ? Portfolio
                    : null);
        }

        public Task<OpeningBalanceAccountReference?> FindAccountAsync(
            Guid accountId,
            CancellationToken cancellationToken = default)
        {
            EnsureReadable();
            return Task.FromResult(
                Account?.AccountId == accountId
                    ? Account
                    : null);
        }

        public Task<OpeningBalanceAssetReference?> FindAssetAsync(
            Guid assetId,
            CancellationToken cancellationToken = default)
        {
            EnsureReadable();
            return Task.FromResult(
                Asset?.AssetId == assetId
                    ? Asset
                    : null);
        }

        private void EnsureReadable()
        {
            if (ThrowOnRead)
            {
                throw new InvalidOperationException(
                    "Receipt replay must not re-read live references.");
            }
        }
    }

    private sealed class HistoryStoreFake
        : IOpeningBalanceEffectiveHistoryReadStore
    {
        internal OpeningBalanceEffectiveHistory Result { get; set; } =
            new(
                EffectiveOpeningTransactionId: null,
                HasOtherEffectiveHistory: false);

        internal int ReadCount { get; private set; }

        internal int WriteCount => 0;

        public Task<OpeningBalanceEffectiveHistory> ReadAsync(
            OpeningBalanceScope scope,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult(Result);
        }
    }

    private sealed class SubmissionStoreFake
        : ILedgerSubmissionStore,
          ILedgerTransactionReadStore
    {
        private LedgerSubmissionReceipt? _persistedReceipt;
        private LedgerTransactionDetail? _persistedDetail;

        internal LedgerTransaction? Transaction { get; private set; }

        internal IReadOnlyList<AssetLot> Lots { get; private set; } = [];

        internal LedgerSubmissionReceipt? Receipt { get; private set; }

        internal int CommitCount { get; private set; }

        public Task<LedgerSubmissionReceipt?> FindReceiptAsync(
            LedgerSubmissionScope scope,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                _persistedReceipt?.Scope == scope
                    ? _persistedReceipt
                    : null);

        public Task<LedgerSubmissionCommitResult> TryCommitAsync(
            LedgerSubmissionReceipt receipt,
            LedgerTransaction transaction,
            IReadOnlyCollection<AssetLot> newLots,
            CancellationToken cancellationToken = default)
        {
            CommitCount++;
            Receipt = receipt;
            Transaction = transaction;
            Lots = newLots.ToArray();
            _persistedReceipt = receipt;
            _persistedDetail = BuildDetail(transaction, Lots);

            return Task.FromResult(
                new LedgerSubmissionCommitResult(
                    WasCommitted: true,
                    receipt));
        }

        public Task<LedgerTransactionDetail?> FindByIdAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                _persistedDetail?.TransactionId == transactionId
                    ? _persistedDetail
                    : null);

        private static LedgerTransactionDetail BuildDetail(
            LedgerTransaction transaction,
            IReadOnlyList<AssetLot> lots)
        {
            var entries = transaction.Entries
                .OrderBy(entry => entry.Sequence)
                .Select(entry => new LedgerTransactionEntryDetail(
                    entry.Id,
                    entry.Sequence,
                    entry.PortfolioId,
                    entry.AccountId,
                    entry.AssetId,
                    entry.QuantityDelta.RawE8,
                    entry.Role,
                    entry.UnitPrice?.RawE8,
                    entry.UnitPrice?.Currency.Value,
                    transaction.CreatedAtUtc))
                .ToArray();

            var createdLots = lots
                .Select(lot => new LedgerTransactionCreatedLotDetail(
                    lot.Id,
                    lot.AssetId,
                    lot.OpeningTransactionEntryId,
                    lot.AcquiredOn,
                    lot.CostBasis.Amount?.MinorUnits,
                    lot.CostBasis.Amount?.Currency.Value,
                    lot.CostBasis.Status,
                    lot.CreatedAtUtc))
                .ToArray();

            var allocations = lots
                .SelectMany(lot => lot.Allocations)
                .Select(allocation => new LedgerTransactionLotAllocationDetail(
                    allocation.Id,
                    allocation.AssetLotId,
                    allocation.TransactionEntryId,
                    allocation.QuantityDelta.RawE8,
                    transaction.CreatedAtUtc))
                .ToArray();

            return new LedgerTransactionDetail(
                transaction.Id,
                transaction.HouseholdId,
                transaction.Type,
                transaction.Status,
                transaction.OrderDate,
                transaction.ExecutionDate,
                transaction.SettlementDate,
                transaction.ExternalReference,
                transaction.Note,
                transaction.ReversalOfTransactionId,
                ReversedByTransactionId: null,
                transaction.CreatedAtUtc,
                transaction.PostedAtUtc,
                entries,
                CashFlow: null,
                Costs: [],
                createdLots,
                allocations);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        private readonly TimeZoneInfo _localTimeZone;

        internal FixedTimeProvider(
            DateTimeOffset utcNow,
            TimeZoneInfo localTimeZone)
        {
            _utcNow = utcNow;
            _localTimeZone = localTimeZone;
        }

        public override DateTimeOffset GetUtcNow()
            => _utcNow;

        public override TimeZoneInfo LocalTimeZone
            => _localTimeZone;
    }
}
