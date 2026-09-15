using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.Common;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.FundTrades;
using WealthLedger.Domain.Common;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;
using WealthLedger.Infrastructure.Persistence;

namespace WealthLedger.Infrastructure.Tests.Persistence;

/// <summary>
/// Races two genuinely concurrent fund sales against one holding.
/// </summary>
/// <remarks>
/// Each contender runs on its own connection and its own DbContext with its
/// own idempotency key, and both start from the same barrier. This is a real
/// race against a file-backed database, not a sequential simulation: the
/// interleaving is decided by SQLite and the thread pool.
///
/// Two sales of forty units against a holding of fifty cannot both be right.
/// Exactly one must commit, the loser must leave nothing behind, and the
/// caller must see a sanitized application-level answer rather than a
/// provider exception.
/// </remarks>
public sealed class FundSaleConcurrencyTests
{
    private static readonly DateTimeOffset RecordedAtUtc =
        new(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly ExecutionDate =
        new(2026, 8, 24);

    [Fact]
    public async Task ConcurrentSales_CannotOverConsumeTheSameHolding()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
        }

        await PurchaseAsync(database, quantity: 50m);

        var plan = await ReviewPlanAsync(database, quantity: 40m);

        // Both contenders reviewed the same plan and wait on one barrier.
        using var barrier = new Barrier(2);

        var attempts =
            new[] { "race-key-a", "race-key-b" }
                .Select(key =>
                    Task.Run(async () =>
                        await AttemptSaleAsync(
                            database,
                            key,
                            plan,
                            barrier)))
                .ToArray();

        var outcomes = await Task.WhenAll(attempts);

        var committed =
            outcomes.Where(x => x.TransactionId is not null).ToArray();

        var refused =
            outcomes.Where(x => x.TransactionId is null).ToArray();

        // Forty plus forty cannot come out of fifty.
        Assert.Single(committed);
        Assert.Single(refused);

        /*
         * The loser must be told in the repository's own vocabulary. A
         * SqliteException or DbUpdateException escaping here would mean the
         * Infrastructure boundary leaked.
         */
        var failure = refused[0].Failure!;

        Assert.IsNotType<SqliteException>(failure);
        Assert.IsNotType<DbUpdateException>(failure);

        Assert.True(
            failure is FundTradeException
                or IdempotencyConflictException
                or ApplicationRuleViolationException
                or DomainRuleViolationException
                or CoreLedgerPersistenceException,
            $"Unexpected failure type leaked to the caller: {failure.GetType()}");

        await using var verification = database.CreateContext();

        // Exactly one sale exists, and it is the committed one.
        var sales =
            await verification.LedgerTransactions
                .AsNoTracking()
                .Where(x => x.Type == TransactionType.Sell)
                .ToListAsync();

        Assert.Single(sales);
        Assert.Equal(committed[0].TransactionId, sales[0].Id);
        Assert.Equal(TransactionStatus.Posted, sales[0].Status);

        // The loser left no allocation, no receipt and no orphan entry.
        Assert.Equal(
            1,
            await verification.CommandReceipts.CountAsync(x =>
                x.OperationCode == LedgerOperationCodes.RecordFundSale));

        Assert.Equal(
            1,
            await verification.LotEntryAllocations.CountAsync(x =>
                x.QuantityDeltaE8 < 0));

        Assert.Equal(
            0,
            await verification.TransactionEntries.CountAsync(x =>
                !verification.LedgerTransactions.Any(
                    tx => tx.Id == x.TransactionId)));

        // Ten units remain, and the derived balance is still reconstructable.
        var remaining =
            await verification.LotEntryAllocations
                .AsNoTracking()
                .SumAsync(x => x.QuantityDeltaE8);

        Assert.Equal(10_00000000L, remaining);
        Assert.True(remaining >= 0);
    }

    /// <summary>
    /// Runs one contender on its own context and connection.
    /// </summary>
    private static async Task<SaleOutcome> AttemptSaleAsync(
        SqliteTestDatabase database,
        string idempotencyKey,
        ReviewedPlan plan,
        Barrier barrier)
    {
        await using var context = database.CreateContext();
        var store = new EfCoreLedgerPostingStore(context);

        var useCase =
            new RecordFundSaleUseCase(
                new EfCoreOpeningBalanceReferenceStore(context),
                store,
                store,
                new FixedTimeProvider(RecordedAtUtc));

        var command =
            SaleCommand(
                40m,
                plan.Allocations,
                plan.Fingerprint);

        // Both contenders reach the write at the same moment.
        barrier.SignalAndWait();

        try
        {
            var result =
                await useCase.ExecuteAsync(
                    idempotencyKey,
                    command);

            return new SaleOutcome(result.TransactionId, null);
        }
        catch (Exception exception)
        {
            return new SaleOutcome(null, exception);
        }
    }

    private static async Task<ReviewedPlan> ReviewPlanAsync(
        SqliteTestDatabase database,
        decimal quantity)
    {
        await using var context = database.CreateContext();
        var store = new EfCoreLedgerPostingStore(context);

        var preview =
            await new PreviewFundSaleUseCase(
                    new EfCoreOpeningBalanceReferenceStore(context),
                    new EfCoreFundLotCustodyReadStore(store),
                    new EfCoreFundRealizedCostReadStore(context),
                    new LotAllocationService(),
                    new FixedTimeProvider(RecordedAtUtc))
                .ExecuteAsync(SaleCommand(quantity));

        return new ReviewedPlan(
            preview.Plan
                .Select(x =>
                    new ReviewedLotAllocation(
                        x.AssetLotId,
                        Quantity.FromRaw(x.ConsumedQuantityRawE8)))
                .ToList(),
            preview.PlanFingerprint);
    }

    private static async Task PurchaseAsync(
        SqliteTestDatabase database,
        decimal quantity)
    {
        await using var context = database.CreateContext();
        var store = new EfCoreLedgerPostingStore(context);

        await new RecordFundPurchaseUseCase(
                new EfCoreOpeningBalanceReferenceStore(context),
                store,
                store,
                new FixedTimeProvider(RecordedAtUtc))
            .ExecuteAsync(
                "race-purchase",
                new RecordFundPurchaseCommand(
                    CoreLedgerTestData.HouseholdId,
                    CoreLedgerTestData.PortfolioId,
                    CoreLedgerTestData.AccountId,
                    CoreLedgerTestData.FundAssetId,
                    CoreLedgerTestData.CashAssetId,
                    Quantity.FromDecimal(quantity),
                    UnitPrice.FromDecimal(10m, CurrencyCode.TRY),
                    Money.FromMinorUnits(
                        (long)(quantity * 10m * 100m),
                        CurrencyCode.TRY),
                    ExecutionDate,
                    ExternalReference: "RACE-PURCHASE",
                    Note: "Synthetic concurrency fixture.",
                    CashAccountId:
                        CoreLedgerTestData.DestinationAccountId));
    }

    private static FundSaleCommand SaleCommand(
        decimal quantity,
        IReadOnlyList<ReviewedLotAllocation>? reviewedPlan = null,
        string? planFingerprint = null)
        => new(
            CoreLedgerTestData.HouseholdId,
            CoreLedgerTestData.PortfolioId,
            CoreLedgerTestData.AccountId,
            CoreLedgerTestData.DestinationAccountId,
            CoreLedgerTestData.FundAssetId,
            CoreLedgerTestData.CashAssetId,
            Quantity.FromDecimal(quantity),
            UnitPrice.FromDecimal(12m, CurrencyCode.TRY),
            Money.FromMinorUnits(
                (long)(quantity * 12m * 100m),
                CurrencyCode.TRY),
            ExecutionDate,
            ExternalReference: "RACE-SALE",
            Note: "Synthetic concurrent sale.",
            ReviewedPlan: reviewedPlan,
            ReviewedPlanFingerprint: planFingerprint);

    private sealed record ReviewedPlan(
        IReadOnlyList<ReviewedLotAllocation> Allocations,
        string Fingerprint);

    private sealed record SaleOutcome(
        Guid? TransactionId,
        Exception? Failure);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        internal FixedTimeProvider(DateTimeOffset utcNow)
            => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
