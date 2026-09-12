using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Navigation;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Application.Positions;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class OpeningBalancePersistenceTests
{
    private static readonly DateTimeOffset RecordedAtUtc =
        new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly AsOfDate =
        new(2026, 8, 31);

    [Fact]
    public async Task FundOpening_PersistsTwoLotsReceiptAndDerivedPosition()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var command = CreateFundCommand();
        RecordOpeningBalanceResult first;

        await using (var context = database.CreateContext())
        {
            first = await CreateUseCase(context).ExecuteAsync(
                "sqlite-fund-opening-001",
                command);
        }

        await using (var restartedContext = database.CreateContext())
        {
            var detail =
                await new EfCoreLedgerTransactionReadStore(restartedContext)
                    .FindByIdAsync(first.TransactionId);

            Assert.NotNull(detail);
            Assert.Equal(2, detail.CreatedLots.Count);
            Assert.Equal(2, detail.LotAllocations.Count);
            Assert.All(
                detail.CreatedLots,
                lot => Assert.Null(lot.PhysicalGoldDetail));
            Assert.Contains(
                detail.CreatedLots,
                lot => lot.CostBasisStatus == CostBasisStatus.Known
                    && lot.OriginalCostBasisMinorUnits == 12_345
                    && lot.CostBasisCurrencyCode == "TRY");
            Assert.Contains(
                detail.CreatedLots,
                lot => lot.CostBasisStatus == CostBasisStatus.Unknown
                    && lot.OriginalCostBasisMinorUnits is null
                    && lot.CostBasisCurrencyCode is null);

            var position =
                await new GetPositionUseCase(
                        new EfCorePostedEntrySource(restartedContext),
                        new EfCoreNavigationScopeReadStore(restartedContext))
                    .ExecuteAsync(
                        new GetPositionQuery(
                            CoreLedgerTestData.HouseholdId,
                            CoreLedgerTestData.PortfolioId,
                            CoreLedgerTestData.AccountId,
                            CoreLedgerTestData.FundAssetId));

            Assert.Equal(command.Quantity.RawE8, position.Quantity.RawE8);
            Assert.Equal(1, position.SourceEntryCount);
        }

        await using (var replayContext = database.CreateContext())
        {
            var replay = await CreateUseCase(replayContext).ExecuteAsync(
                "sqlite-fund-opening-001",
                command with
                {
                    Lots = command.Lots.Reverse().ToArray()
                });

            Assert.Equal(first.TransactionId, replay.TransactionId);
            Assert.Equal(first.AssetLotIds, replay.AssetLotIds);
        }

        await using (var conflictContext = database.CreateContext())
        {
            var exception =
                await Assert.ThrowsAsync<OpeningBalanceException>(
                    () => CreateUseCase(conflictContext).ExecuteAsync(
                        "sqlite-fund-opening-002",
                        command));

            Assert.Equal(
                OpeningBalanceErrorCodes.AlreadyExists,
                exception.ErrorCode);
            Assert.Equal(first.TransactionId, exception.RelatedTransactionId);
        }

        await using var verificationContext = database.CreateContext();
        Assert.Equal(1, await verificationContext.LedgerTransactions.CountAsync());
        Assert.Equal(1, await verificationContext.TransactionEntries.CountAsync());
        Assert.Equal(2, await verificationContext.AssetLots.CountAsync());
        Assert.Equal(2, await verificationContext.LotEntryAllocations.CountAsync());
        Assert.Equal(1, await verificationContext.CommandReceipts.CountAsync());
    }

    [Fact]
    public async Task CashOpening_PersistsNoLotOrCostRows()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var command = new RecordOpeningBalanceCommand(
            CoreLedgerTestData.HouseholdId,
            CoreLedgerTestData.PortfolioId,
            CoreLedgerTestData.AccountId,
            CoreLedgerTestData.CashAssetId,
            AsOfDate,
            Quantity.FromDecimal(12_345.67m),
            "SYNTHETIC-CASH-STATEMENT",
            "Synthetic cash opening source.",
            Lots: []);

        await using (var context = database.CreateContext())
        {
            var result = await CreateUseCase(context).ExecuteAsync(
                "sqlite-cash-opening-001",
                command);

            Assert.Equal(
                command.Quantity.RawE8,
                result.PersistedQuantityRawE8);
            Assert.Empty(result.AssetLotIds);
        }

        await using var verificationContext = database.CreateContext();
        Assert.Equal(1, await verificationContext.LedgerTransactions.CountAsync());
        Assert.Equal(1, await verificationContext.TransactionEntries.CountAsync());
        Assert.Equal(0, await verificationContext.AssetLots.CountAsync());
        Assert.Equal(0, await verificationContext.LotEntryAllocations.CountAsync());
        Assert.Equal(0, await verificationContext.TransactionCostComponents.CountAsync());
        Assert.Equal(0, await verificationContext.CashFlowDetails.CountAsync());
    }

    [Fact]
    public async Task PhysicalGoldOpening_ReadsPersistedDetailAndExactFineWeight()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var vaultAccountId = Guid.NewGuid();

        await using (var seedContext = database.CreateContext())
        {
            seedContext.Accounts.Add(new AccountRow
            {
                Id = vaultAccountId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                InstitutionId = null,
                Code = "SYNTHETIC_VAULT",
                Name = "Synthetic Physical Vault",
                Type = AccountType.PhysicalVault,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            });
            await seedContext.SaveChangesAsync();
        }

        var command = new RecordOpeningBalanceCommand(
            CoreLedgerTestData.HouseholdId,
            CoreLedgerTestData.PortfolioId,
            vaultAccountId,
            CoreLedgerTestData.GoldAssetId,
            AsOfDate,
            Quantity.FromDecimal(25.25m),
            "SYNTHETIC-GOLD-INVENTORY",
            "Synthetic physical inventory opening.",
            [
                new OpeningBalanceLotCommand(
                    Quantity.FromDecimal(25.25m),
                    AcquiredOn: null,
                    CostBasis.Unknown(),
                    new PhysicalGoldLotDetail(
                        Fineness.FromPerMille(916m),
                        pieceCount: 2,
                        hallmark: "SYNTHETIC-916",
                        certificateReference: "SYNTHETIC-CERTIFICATE",
                        note: "Two synthetic matching bracelets."))
            ]);

        Guid transactionId;

        await using (var context = database.CreateContext())
        {
            transactionId =
                (await CreateUseCase(context).ExecuteAsync(
                    "sqlite-gold-opening-001",
                    command))
                .TransactionId;
        }

        await using var readContext = database.CreateContext();
        var detail =
            await new EfCoreLedgerTransactionReadStore(readContext)
                .FindByIdAsync(transactionId);
        var lot = Assert.Single(detail!.CreatedLots);
        var gold = Assert.IsType<LedgerTransactionPhysicalGoldDetail>(
            lot.PhysicalGoldDetail);

        Assert.Equal(916_000, gold.FinenessPartsPerMillion);
        Assert.Equal(2, gold.PieceCount);
        Assert.Equal("SYNTHETIC-916", gold.Hallmark);
        Assert.Equal("SYNTHETIC-CERTIFICATE", gold.CertificateReference);
        Assert.Equal("Two synthetic matching bracelets.", gold.Note);
        Assert.Equal(23.129m, gold.FineWeightGrams);
        Assert.Equal(1, await readContext.PhysicalGoldLotDetails.CountAsync());
    }

    [Fact]
    public async Task ReceiptInsertFailure_RollsBackCompleteOpeningGraph()
    {
        await using var database = await CreateSeededDatabaseAsync();

        await database.ExecuteNonQueryAsync(
            """
            CREATE TRIGGER RejectSyntheticOpeningReceipt
            BEFORE INSERT ON CommandReceipt
            WHEN NEW.OperationCode = 'RECORD_OPENING_BALANCE'
            BEGIN
                SELECT RAISE(ABORT, 'synthetic opening receipt failure');
            END;
            """);

        await using (var context = database.CreateContext())
        {
            await Assert.ThrowsAsync<CoreLedgerPersistenceException>(
                () => CreateUseCase(context).ExecuteAsync(
                    "sqlite-opening-rollback-001",
                    CreateFundCommand()));
        }

        await using var verificationContext = database.CreateContext();
        Assert.Equal(0, await verificationContext.LedgerTransactions.CountAsync());
        Assert.Equal(0, await verificationContext.TransactionEntries.CountAsync());
        Assert.Equal(0, await verificationContext.AssetLots.CountAsync());
        Assert.Equal(0, await verificationContext.LotEntryAllocations.CountAsync());
        Assert.Equal(0, await verificationContext.CommandReceipts.CountAsync());
    }

    [Fact]
    public async Task TriggerSemanticCollision_IsMappedToSafeApplicationConflict()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var command = CreateFundCommand();
        Guid firstTransactionId;

        await using (var firstContext = database.CreateContext())
        {
            firstTransactionId =
                (await CreateUseCase(firstContext).ExecuteAsync(
                    "sqlite-trigger-winner-001",
                    command))
                .TransactionId;
        }

        await using var staleContext = database.CreateContext();
        var references = new EfCoreOpeningBalanceReferenceStore(staleContext);
        var useCase = new RecordOpeningBalanceUseCase(
            references,
            new EmptyEffectiveHistoryStore(),
            new EfCoreLedgerPostingStore(staleContext),
            new EfCoreLedgerTransactionReadStore(staleContext),
            new FixedTimeProvider(RecordedAtUtc));

        var exception = await Assert.ThrowsAsync<OpeningBalanceException>(
            () => useCase.ExecuteAsync(
                "sqlite-trigger-loser-001",
                command));

        Assert.Equal(
            OpeningBalanceErrorCodes.AlreadyExists,
            exception.ErrorCode);
        Assert.Equal(firstTransactionId, exception.RelatedTransactionId);
        Assert.DoesNotContain("SQLite", exception.Message);
        Assert.DoesNotContain("WL_M007", exception.Message);
    }

    [Fact]
    public async Task ConcurrentEquivalentSameKey_ConvergesOnOneOpeningGraph()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var command = CreateFundCommand();
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var firstUseCase = CreateUseCase(firstContext);
        var secondUseCase = CreateUseCase(secondContext);

        var results = await Task.WhenAll(
            firstUseCase.ExecuteAsync(
                "sqlite-concurrent-opening-001",
                command),
            secondUseCase.ExecuteAsync(
                "sqlite-concurrent-opening-001",
                command));

        Assert.Equal(results[0].TransactionId, results[1].TransactionId);

        await using var verificationContext = database.CreateContext();
        Assert.Equal(1, await verificationContext.LedgerTransactions.CountAsync());
        Assert.Equal(1, await verificationContext.TransactionEntries.CountAsync());
        Assert.Equal(2, await verificationContext.AssetLots.CountAsync());
        Assert.Equal(2, await verificationContext.LotEntryAllocations.CountAsync());
        Assert.Equal(1, await verificationContext.CommandReceipts.CountAsync());
    }

    [Fact]
    public async Task ConcurrentDifferentKeys_PersistOneSemanticWinner()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var command = CreateFundCommand();
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var firstUseCase = CreateUseCase(firstContext);
        var secondUseCase = CreateUseCase(secondContext);

        var firstTask = CaptureAsync(
            () => firstUseCase.ExecuteAsync(
                "sqlite-concurrent-semantic-001",
                command));
        var secondTask = CaptureAsync(
            () => secondUseCase.ExecuteAsync(
                "sqlite-concurrent-semantic-002",
                command));
        var outcomes = await Task.WhenAll(firstTask, secondTask);

        Assert.Single(outcomes, outcome => outcome.Result is not null);
        var conflict = Assert.IsType<OpeningBalanceException>(
            outcomes.Single(outcome => outcome.Exception is not null).Exception);
        Assert.Equal(OpeningBalanceErrorCodes.AlreadyExists, conflict.ErrorCode);

        await using var verificationContext = database.CreateContext();
        Assert.Equal(1, await verificationContext.LedgerTransactions.CountAsync());
        Assert.Equal(1, await verificationContext.TransactionEntries.CountAsync());
        Assert.Equal(2, await verificationContext.AssetLots.CountAsync());
        Assert.Equal(2, await verificationContext.LotEntryAllocations.CountAsync());
        Assert.Equal(1, await verificationContext.CommandReceipts.CountAsync());
    }

    private static async Task<SqliteTestDatabase> CreateSeededDatabaseAsync()
    {
        var database = await SqliteTestDatabase.CreateAsync();

        await using var context = database.CreateContext();
        await CoreLedgerTestData.SeedMasterDataAsync(context);

        return database;
    }

    private static RecordOpeningBalanceUseCase CreateUseCase(
        WealthLedgerDbContext context)
    {
        var references =
            new EfCoreOpeningBalanceReferenceStore(context);

        return new RecordOpeningBalanceUseCase(
            references,
            new EfCoreOpeningBalanceEffectiveHistoryReadStore(context),
            new EfCoreLedgerPostingStore(context),
            new EfCoreLedgerTransactionReadStore(context),
            new FixedTimeProvider(RecordedAtUtc));
    }

    private static RecordOpeningBalanceCommand CreateFundCommand()
        => new(
            CoreLedgerTestData.HouseholdId,
            CoreLedgerTestData.PortfolioId,
            CoreLedgerTestData.AccountId,
            CoreLedgerTestData.FundAssetId,
            AsOfDate,
            Quantity.FromDecimal(123.456789m),
            "SYNTHETIC-FUND-STATEMENT",
            "Synthetic fund opening source.",
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

    private static async Task<CapturedOutcome> CaptureAsync(
        Func<Task<RecordOpeningBalanceResult>> action)
    {
        try
        {
            return new CapturedOutcome(await action(), Exception: null);
        }
        catch (Exception exception)
        {
            return new CapturedOutcome(Result: null, Exception: exception);
        }
    }

    private sealed record CapturedOutcome(
        RecordOpeningBalanceResult? Result,
        Exception? Exception);

    private sealed class EmptyEffectiveHistoryStore
        : IOpeningBalanceEffectiveHistoryReadStore
    {
        public Task<OpeningBalanceEffectiveHistory> ReadAsync(
            OpeningBalanceScope scope,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new OpeningBalanceEffectiveHistory(
                EffectiveOpeningTransactionId: null,
                HasOtherEffectiveHistory: false));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        internal FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
            => _utcNow;

        public override TimeZoneInfo LocalTimeZone
            => TimeZoneInfo.Utc;
    }
}
