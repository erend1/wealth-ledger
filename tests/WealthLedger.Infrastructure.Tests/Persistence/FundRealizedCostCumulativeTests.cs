using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.FundTrades;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;
using WealthLedger.Infrastructure.Persistence;

namespace WealthLedger.Infrastructure.Tests.Persistence;

/// <summary>
/// Pins ADR-009 cumulative apportionment across every surface that reports
/// realized cost.
/// </summary>
/// <remarks>
/// The dangerous mistake is calculating each sale's share independently as
/// <c>round_even(C * q / Q)</c>. That looks right for the first sale from a
/// lot and drifts afterwards, so every case below uses a lot that has already
/// been partially sold.
///
/// Preview, posted verification, receipt rendering and restart readback must
/// all agree, because a user who reviews one number and is shown another on
/// the receipt has no way to tell which is real.
/// </remarks>
public sealed class FundRealizedCostCumulativeTests
{
    private static readonly DateTimeOffset RecordedAtUtc =
        new(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly ExecutionDate =
        new(2026, 8, 24);

    /*
     * Three units costing one hundred minor units is the smallest case where
     * the two methods disagree.
     *
     * Cumulative:  round_even(100*1/3)=33, then round_even(100*2/3)-33=34.
     * Independent: round_even(100*1/3)=33 twice, losing a minor unit, or
     *              against remaining quantity round_even(100*1/2)=50.
     */
    [Fact]
    public async Task SecondPartialSale_UsesCumulativeApportionment()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database);

        await PurchaseAsync(
            database,
            "purchase-thirds",
            quantity: 3m,
            considerationMinor: 100);

        var first = await SellAsync(database, "sale-thirds-1", 1m);

        Assert.Equal(33, await KnownCostAsync(database, first));

        // Preview the second sale before posting it.
        long previewed;

        await using (var context = database.CreateContext())
        {
            var preview =
                await BuildSalePreviewUseCase(context).ExecuteAsync(
                    SaleCommand(1m));

            previewed =
                Assert.Single(preview.RealizedCost.KnownAmounts)
                    .MinorUnits;
        }

        // The cumulative rule assigns the residue to this second sale.
        Assert.Equal(34, previewed);

        var second = await SellAsync(database, "sale-thirds-2", 1m);

        // What the receipt shows must equal what review promised.
        Assert.Equal(previewed, await KnownCostAsync(database, second));

        // And the third sale closes the lot, conserving the whole cost.
        var third = await SellAsync(database, "sale-thirds-3", 1m);
        var thirdCost = await KnownCostAsync(database, third);

        Assert.Equal(33, thirdCost);
        Assert.Equal(100, 33 + previewed + thirdCost);
    }

    /*
     * A midpoint case. Two units costing five minor units puts the running
     * total on an exact half, so the tie-to-even rule decides the split.
     */
    [Fact]
    public async Task MidpointResidue_MatchesTieToEvenAcrossBothSales()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database);

        await PurchaseAsync(
            database,
            "purchase-midpoint",
            quantity: 2m,
            considerationMinor: 5);

        var first = await SellAsync(database, "sale-midpoint-1", 1m);

        // round_even(5 * 1 / 2) is exactly 2.5, which resolves to even 2.
        Assert.Equal(2, await KnownCostAsync(database, first));

        long previewed;

        await using (var context = database.CreateContext())
        {
            var preview =
                await BuildSalePreviewUseCase(context).ExecuteAsync(
                    SaleCommand(1m));

            previewed =
                Assert.Single(preview.RealizedCost.KnownAmounts)
                    .MinorUnits;
        }

        /*
         * Independent rounding would repeat the 2 and lose a minor unit.
         * Cumulative gives 5 - 2 = 3.
         */
        Assert.Equal(3, previewed);

        var second = await SellAsync(database, "sale-midpoint-2", 1m);

        Assert.Equal(previewed, await KnownCostAsync(database, second));
        Assert.Equal(5, 2 + previewed);
    }

    [Fact]
    public async Task ReversedEarlierSale_LeavesTheCumulativeSequence()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database);

        await PurchaseAsync(
            database,
            "purchase-reversed",
            quantity: 3m,
            considerationMinor: 100);

        var first = await SellAsync(database, "sale-reversed-1", 1m);
        var second = await SellAsync(database, "sale-reversed-2", 1m);

        Assert.Equal(33, await KnownCostAsync(database, first));
        Assert.Equal(34, await KnownCostAsync(database, second));

        await ReverseAsync(database, first);

        /*
         * With the first sale gone the second becomes first in the effective
         * sequence, so the residue moves off it. This is the accepted cost of
         * conserving the original amount exactly.
         */
        Assert.Equal(33, await KnownCostAsync(database, second));

        // The reversed sale no longer carries realized cost at all.
        await using var context = database.CreateContext();

        var reversedVerification =
            await BuildVerificationUseCase(context).ExecuteAsync(
                CoreLedgerTestData.HouseholdId,
                first);

        Assert.False(
            reversedVerification.RealizedCost!.SourceSaleIsEffective);

        Assert.Empty(reversedVerification.RealizedCost.KnownAmounts);
    }

    [Fact]
    public async Task RestartReadback_ReproducesTheSameApportionment()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database);

        await PurchaseAsync(
            database,
            "purchase-restart",
            quantity: 7m,
            considerationMinor: 1_234);

        var first = await SellAsync(database, "sale-restart-1", 2m);
        var second = await SellAsync(database, "sale-restart-2", 3m);

        var firstCost = await KnownCostAsync(database, first);
        var secondCost = await KnownCostAsync(database, second);

        // round_even(1234*2/7)=353, round_even(1234*5/7)=881, so 881-353=528.
        Assert.Equal(353, firstCost);
        Assert.Equal(528, secondCost);

        // A brand-new context is the closest thing to a restart in process.
        await using var reopened = database.CreateContext();

        var verification =
            await BuildVerificationUseCase(reopened).ExecuteAsync(
                CoreLedgerTestData.HouseholdId,
                second);

        Assert.Equal(
            secondCost,
            Assert.Single(verification.RealizedCost!.KnownAmounts)
                .MinorUnits);

        // Closing the lot conserves the whole cost exactly.
        var third = await SellAsync(database, "sale-restart-3", 2m);

        Assert.Equal(
            1_234,
            firstCost + secondCost + await KnownCostAsync(database, third));
    }

    private static async Task<long> KnownCostAsync(
        SqliteTestDatabase database,
        Guid saleTransactionId)
    {
        await using var context = database.CreateContext();

        var verification =
            await BuildVerificationUseCase(context).ExecuteAsync(
                CoreLedgerTestData.HouseholdId,
                saleTransactionId);

        return Assert.Single(
                verification.RealizedCost!.KnownAmounts)
            .MinorUnits;
    }

    private static async Task PurchaseAsync(
        SqliteTestDatabase database,
        string key,
        decimal quantity,
        long considerationMinor)
    {
        await using var context = database.CreateContext();
        var store = new EfCoreLedgerPostingStore(context);

        await new RecordFundPurchaseUseCase(
                new EfCoreOpeningBalanceReferenceStore(context),
                store,
                store,
                new FixedTimeProvider(RecordedAtUtc))
            .ExecuteAsync(
                key,
                new RecordFundPurchaseCommand(
                    CoreLedgerTestData.HouseholdId,
                    CoreLedgerTestData.PortfolioId,
                    CoreLedgerTestData.AccountId,
                    CoreLedgerTestData.FundAssetId,
                    CoreLedgerTestData.CashAssetId,
                    Quantity.FromDecimal(quantity),
                    UnitPrice.FromDecimal(
                        decimal.Round(
                            considerationMinor / 100m / quantity,
                            8),
                        CurrencyCode.TRY),
                    Money.FromMinorUnits(
                        considerationMinor,
                        CurrencyCode.TRY),
                    ExecutionDate,
                    ExternalReference: "CUMULATIVE-TEST",
                    Note: "Synthetic cumulative-cost fixture.",
                    CashAccountId:
                        CoreLedgerTestData.DestinationAccountId));
    }

    /*
     * Each sale posts at a distinct time. ADR-009 orders the effective
     * sequence by posting time and falls back to transaction id, so sales
     * sharing one timestamp would be ordered by a random identifier and the
     * apportionment would not be reproducible.
     */
    private static int _saleSequence;

    private static async Task<Guid> SellAsync(
        SqliteTestDatabase database,
        string key,
        decimal quantity)
    {
        var postedAtUtc =
            RecordedAtUtc.AddMinutes(++_saleSequence);

        await using var context = database.CreateContext();

        var preview =
            await BuildSalePreviewUseCase(context).ExecuteAsync(
                SaleCommand(quantity));

        var store = new EfCoreLedgerPostingStore(context);

        var result =
            await new RecordFundSaleUseCase(
                    new EfCoreOpeningBalanceReferenceStore(context),
                    store,
                    store,
                    new FixedTimeProvider(postedAtUtc))
                .ExecuteAsync(
                    key,
                    SaleCommand(
                        quantity,
                        preview.Plan
                            .Select(x =>
                                new ReviewedLotAllocation(
                                    x.AssetLotId,
                                    Quantity.FromRaw(
                                        x.ConsumedQuantityRawE8)))
                            .ToList(),
                        preview.PlanFingerprint));

        return result.TransactionId;
    }

    private static async Task ReverseAsync(
        SqliteTestDatabase database,
        Guid transactionId)
    {
        await using var context = database.CreateContext();

        await new ReversePostedTransactionUseCase(
                new EfCoreLedgerReversalStore(context),
                new FixedTimeProvider(RecordedAtUtc.AddDays(1)))
            .ExecuteAsync(
                $"reverse-{transactionId:N}",
                new ReversePostedTransactionCommand(
                    transactionId,
                    "Synthetic cumulative-cost correction."));
    }

    private static PreviewFundSaleUseCase BuildSalePreviewUseCase(
        WealthLedgerDbContext context)
    {
        var store = new EfCoreLedgerPostingStore(context);

        return new PreviewFundSaleUseCase(
            new EfCoreOpeningBalanceReferenceStore(context),
            new EfCoreFundLotCustodyReadStore(store),
            new EfCoreFundRealizedCostReadStore(context),
            new LotAllocationService(),
            new FixedTimeProvider(RecordedAtUtc));
    }

    private static GetFundTradeVerificationUseCase BuildVerificationUseCase(
        WealthLedgerDbContext context)
        => new(
            new EfCoreFundTradeVerificationReadStore(context),
            new EfCoreFundRealizedCostReadStore(context),
            new EfCoreOpeningBalanceReferenceStore(context),
            new FixedTimeProvider(RecordedAtUtc));

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
            UnitPrice.FromDecimal(10m, CurrencyCode.TRY),
            Money.FromMinorUnits(
                (long)(quantity * 10m * 100m),
                CurrencyCode.TRY),
            ExecutionDate,
            ExternalReference: "CUMULATIVE-SALE",
            Note: "Synthetic cumulative-cost sale.",
            ReviewedPlan: reviewedPlan,
            ReviewedPlanFingerprint: planFingerprint);

    private static async Task SeedAsync(SqliteTestDatabase database)
    {
        await using var context = database.CreateContext();
        await CoreLedgerTestData.SeedMasterDataAsync(context);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        internal FixedTimeProvider(DateTimeOffset utcNow)
            => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
