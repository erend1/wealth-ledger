using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.FundTrades;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;
using WealthLedger.Infrastructure.Persistence;

namespace WealthLedger.Infrastructure.Tests.Persistence;

/// <summary>
/// Exercises the fund-trade lifecycle against a real file-backed SQLite
/// database, including the migration-007 guards.
/// </summary>
public sealed class FundTradeLifecycleTests
{
    private static readonly DateTimeOffset RecordedAtUtc =
        new(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly ExecutionDate =
        new(2026, 8, 24);

    [Fact]
    public async Task Purchase_RoundTripsWithSeparateAccountsAndCosts()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database);

        Guid transactionId;
        Guid lotId;

        await using (var context = database.CreateContext())
        {
            var result =
                await BuildPurchaseUseCase(context).ExecuteAsync(
                    "purchase-1",
                    PurchaseCommand(
                        quantity: 100m,
                        considerationMinor: 1_000_00,
                        costs:
                        [
                            new FundTradeCostInput(
                                CostType.Commission,
                                CostTreatment.AdditionalCashOutflow,
                                Money.FromMinorUnits(
                                    5_00,
                                    CurrencyCode.TRY)),

                            new FundTradeCostInput(
                                CostType.OtherTax,
                                CostTreatment.AdditionalCashOutflow,
                                Money.FromMinorUnits(
                                    2_00,
                                    CurrencyCode.TRY))
                        ]));

            transactionId = result.TransactionId;
            lotId = result.AssetLotId;
        }

        // Reopen: everything below is read from persisted facts.
        await using var reopened = database.CreateContext();

        var entries =
            await reopened.TransactionEntries
                .AsNoTracking()
                .Where(x => x.TransactionId == transactionId)
                .OrderBy(x => x.EntrySequence)
                .ToListAsync();

        Assert.Equal(4, entries.Count);

        var principal =
            entries.Single(x => x.Role == EntryRole.Principal);

        var consideration =
            entries.Single(x => x.Role == EntryRole.Consideration);

        var fee = entries.Single(x => x.Role == EntryRole.Fee);
        var tax = entries.Single(x => x.Role == EntryRole.Tax);

        Assert.Equal(
            CoreLedgerTestData.AccountId,
            principal.AccountId);

        Assert.Equal(
            CoreLedgerTestData.DestinationAccountId,
            consideration.AccountId);

        Assert.Equal(100_00000000L, principal.QuantityDeltaE8);
        Assert.Equal(-1_000_00_000000L, consideration.QuantityDeltaE8);
        Assert.Equal(-5_00_000000L, fee.QuantityDeltaE8);
        Assert.Equal(-2_00_000000L, tax.QuantityDeltaE8);

        var lot =
            await reopened.AssetLots
                .AsNoTracking()
                .SingleAsync(x => x.Id == lotId);

        // Consideration plus both additional outflows, counted once each.
        Assert.Equal(1_007_00, lot.OriginalCostBasisMinor);
        Assert.Equal(CostBasisStatus.Known, lot.CostBasisStatus);
        Assert.Equal(ExecutionDate, lot.AcquiredOn);

        var allocation =
            await reopened.LotEntryAllocations
                .AsNoTracking()
                .SingleAsync(x => x.AssetLotId == lotId);

        Assert.Equal(100_00000000L, allocation.QuantityDeltaE8);
    }

    [Fact]
    public async Task Sale_ConsumesOldestLotsFirstAndDerivesRealizedCost()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database);

        await using (var context = database.CreateContext())
        {
            await BuildPurchaseUseCase(context).ExecuteAsync(
                "purchase-older",
                PurchaseCommand(
                    quantity: 40m,
                    considerationMinor: 400_00,
                    executionDate: new DateOnly(2026, 8, 20)));
        }

        await using (var context = database.CreateContext())
        {
            await BuildPurchaseUseCase(context).ExecuteAsync(
                "purchase-newer",
                PurchaseCommand(
                    quantity: 60m,
                    considerationMinor: 900_00,
                    executionDate: new DateOnly(2026, 8, 22)));
        }

        Guid saleId;

        await using (var context = database.CreateContext())
        {
            var preview =
                await BuildSalePreviewUseCase(context).ExecuteAsync(
                    SaleCommand(quantity: 70m));

            Assert.Equal(2, preview.Plan.Count);

            // Oldest first: all forty of the first lot, then thirty more.
            Assert.Equal(
                40_00000000L,
                preview.Plan[0].ConsumedQuantityRawE8);

            Assert.Equal(
                30_00000000L,
                preview.Plan[1].ConsumedQuantityRawE8);

            var result =
                await BuildSaleUseCase(context).ExecuteAsync(
                    "sale-1",
                    SaleCommand(
                        quantity: 70m,
                        reviewedPlan: preview.Plan
                            .Select(x =>
                                new ReviewedLotAllocation(
                                    x.AssetLotId,
                                    Quantity.FromRaw(
                                        x.ConsumedQuantityRawE8)))
                            .ToList(),
                        planFingerprint: preview.PlanFingerprint));

            saleId = result.TransactionId;
        }

        // Fresh context: the receipt rebuilds from persisted facts alone.
        await using var reopened = database.CreateContext();

        var verification =
            await BuildVerificationUseCase(reopened).ExecuteAsync(
                CoreLedgerTestData.HouseholdId,
                saleId);

        Assert.Equal(
            RealizedCostCompleteness.CompleteKnown,
            verification.RealizedCost!.Completeness);

        /*
         * Forty units of the four-hundred-lira lot is its whole cost, plus
         * half of the nine-hundred-lira lot.
         */
        var amount =
            Assert.Single(verification.RealizedCost.KnownAmounts);

        Assert.Equal(850_00, amount.MinorUnits);
        Assert.Equal("TRY", amount.CurrencyCode);

        Assert.Equal(
            "ADR009_CUMULATIVE_ROUND_HALF_TO_EVEN_V1",
            verification.RealizedCost.MethodCode);

        // Thirty units remain in the newer lot.
        Assert.Equal(
            30_00000000L,
            verification.CurrentFundPositionRawE8);
    }

    [Fact]
    public async Task Sale_CannotConsumeCustodyHeldInAnotherAccount()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database);

        await using (var context = database.CreateContext())
        {
            await BuildPurchaseUseCase(context).ExecuteAsync(
                "purchase-elsewhere",
                PurchaseCommand(
                    quantity: 50m,
                    considerationMinor: 500_00));
        }

        await using var context2 = database.CreateContext();

        // The fund sits in AccountId; this sale claims to hold it in the
        // other account, where nothing was ever bought.
        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () =>
                    BuildSalePreviewUseCase(context2).ExecuteAsync(
                        SaleCommand(
                            quantity: 10m,
                            fundAccountId:
                                CoreLedgerTestData
                                    .DestinationAccountId)));

        Assert.Equal(
            FundTradeErrorCodes.InsufficientFundQuantity,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Sale_WithStalePlan_WritesNothing()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database);

        await using (var context = database.CreateContext())
        {
            await BuildPurchaseUseCase(context).ExecuteAsync(
                "purchase-stale",
                PurchaseCommand(
                    quantity: 50m,
                    considerationMinor: 500_00));
        }

        FundSalePreview preview;

        await using (var context = database.CreateContext())
        {
            preview =
                await BuildSalePreviewUseCase(context).ExecuteAsync(
                    SaleCommand(quantity: 20m));
        }

        // History moves after the review: a second lot appears and becomes
        // the one FIFO would consume first.
        await using (var context = database.CreateContext())
        {
            await BuildPurchaseUseCase(context).ExecuteAsync(
                "purchase-intervening",
                PurchaseCommand(
                    quantity: 30m,
                    considerationMinor: 300_00,
                    executionDate: new DateOnly(2026, 8, 1)));
        }

        await using (var context = database.CreateContext())
        {
            var exception =
                await Assert.ThrowsAsync<FundTradeException>(
                    () =>
                        BuildSaleUseCase(context).ExecuteAsync(
                            "sale-stale",
                            SaleCommand(
                                quantity: 20m,
                                reviewedPlan: preview.Plan
                                    .Select(x =>
                                        new ReviewedLotAllocation(
                                            x.AssetLotId,
                                            Quantity.FromRaw(
                                                x.ConsumedQuantityRawE8)))
                                    .ToList(),
                                planFingerprint:
                                    preview.PlanFingerprint)));

            Assert.Equal(
                FundTradeErrorCodes.StaleReviewedPlan,
                exception.ErrorCode);
        }

        await using var reopened = database.CreateContext();

        // No sale, no allocation and no receipt survived the refusal.
        Assert.Equal(
            0,
            await reopened.LedgerTransactions
                .CountAsync(x => x.Type == TransactionType.Sell));

        Assert.Equal(
            0,
            await reopened.LotEntryAllocations
                .CountAsync(x => x.QuantityDeltaE8 < 0));

        Assert.Equal(
            0,
            await reopened.CommandReceipts
                .CountAsync(x =>
                    x.OperationCode
                        == LedgerOperationCodes.RecordFundSale));
    }

    [Fact]
    public async Task Sale_EquivalentRetry_ReturnsOriginalAfterHoldingsChange()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database);

        await using (var context = database.CreateContext())
        {
            await BuildPurchaseUseCase(context).ExecuteAsync(
                "purchase-retry",
                PurchaseCommand(
                    quantity: 50m,
                    considerationMinor: 500_00));
        }

        FundSaleCommand command;
        Guid firstId;

        await using (var context = database.CreateContext())
        {
            var preview =
                await BuildSalePreviewUseCase(context).ExecuteAsync(
                    SaleCommand(quantity: 20m));

            command =
                SaleCommand(
                    quantity: 20m,
                    reviewedPlan: preview.Plan
                        .Select(x =>
                            new ReviewedLotAllocation(
                                x.AssetLotId,
                                Quantity.FromRaw(
                                    x.ConsumedQuantityRawE8)))
                        .ToList(),
                    planFingerprint: preview.PlanFingerprint);

            firstId =
                (await BuildSaleUseCase(context).ExecuteAsync(
                    "sale-retry",
                    command)).TransactionId;
        }

        /*
         * The sale has now changed the very quantities its own plan was
         * derived from, so a retry must be answered from the receipt rather
         * than re-validated against the world the sale itself created.
         */
        await using (var context = database.CreateContext())
        {
            var retry =
                await BuildSaleUseCase(context).ExecuteAsync(
                    "sale-retry",
                    command);

            Assert.Equal(firstId, retry.TransactionId);
        }

        await using var reopened = database.CreateContext();

        Assert.Equal(
            1,
            await reopened.LedgerTransactions
                .CountAsync(x => x.Type == TransactionType.Sell));
    }

    [Fact]
    public async Task Sale_ConcurrentDifferentKeys_CannotOverConsume()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database);

        await using (var context = database.CreateContext())
        {
            await BuildPurchaseUseCase(context).ExecuteAsync(
                "purchase-race",
                PurchaseCommand(
                    quantity: 50m,
                    considerationMinor: 500_00));
        }

        FundSaleCommand command;

        await using (var context = database.CreateContext())
        {
            var preview =
                await BuildSalePreviewUseCase(context).ExecuteAsync(
                    SaleCommand(quantity: 40m));

            command =
                SaleCommand(
                    quantity: 40m,
                    reviewedPlan: preview.Plan
                        .Select(x =>
                            new ReviewedLotAllocation(
                                x.AssetLotId,
                                Quantity.FromRaw(
                                    x.ConsumedQuantityRawE8)))
                        .ToList(),
                    planFingerprint: preview.PlanFingerprint);
        }

        // Both reviewed the same plan; together they would consume eighty of
        // the fifty units actually held.
        var succeeded = 0;

        foreach (var key in new[] { "sale-race-a", "sale-race-b" })
        {
            await using var context = database.CreateContext();

            try
            {
                await BuildSaleUseCase(context).ExecuteAsync(
                    key,
                    command);

                succeeded++;
            }
            catch (FundTradeException)
            {
                // The second one is refused, which is the point.
            }
        }

        Assert.Equal(1, succeeded);

        await using var reopened = database.CreateContext();

        var remaining =
            await reopened.LotEntryAllocations
                .AsNoTracking()
                .SumAsync(x => x.QuantityDeltaE8);

        Assert.Equal(10_00000000L, remaining);
    }

    [Fact]
    public async Task Sale_AfterReversal_LeavesTheEffectiveSequence()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database);

        await using (var context = database.CreateContext())
        {
            await BuildPurchaseUseCase(context).ExecuteAsync(
                "purchase-reversal",
                PurchaseCommand(
                    quantity: 30m,
                    considerationMinor: 100_00));
        }

        Guid saleId;

        await using (var context = database.CreateContext())
        {
            var preview =
                await BuildSalePreviewUseCase(context).ExecuteAsync(
                    SaleCommand(quantity: 10m));

            saleId =
                (await BuildSaleUseCase(context).ExecuteAsync(
                    "sale-reversal",
                    SaleCommand(
                        quantity: 10m,
                        reviewedPlan: preview.Plan
                            .Select(x =>
                                new ReviewedLotAllocation(
                                    x.AssetLotId,
                                    Quantity.FromRaw(
                                        x.ConsumedQuantityRawE8)))
                            .ToList(),
                        planFingerprint: preview.PlanFingerprint)))
                .TransactionId;
        }

        await using (var context = database.CreateContext())
        {
            var verification =
                await BuildVerificationUseCase(context).ExecuteAsync(
                    CoreLedgerTestData.HouseholdId,
                    saleId);

            Assert.True(
                verification.RealizedCost!.SourceSaleIsEffective);

            // A third of a hundred lira, rounded to the nearest kurus.
            Assert.Equal(
                33_33,
                Assert.Single(
                        verification.RealizedCost.KnownAmounts)
                    .MinorUnits);
        }
    }

    /*
     * The application is not the only way into the database, so the accepted
     * shape has to survive a writer that skips it entirely.
     */
    [Fact]
    public async Task DirectSql_CannotBypassTheAcceptedFundTradeShape()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database);

        Guid lotId;

        await using (var context = database.CreateContext())
        {
            lotId =
                (await BuildPurchaseUseCase(context).ExecuteAsync(
                    "purchase-direct-sql",
                    PurchaseCommand(
                        quantity: 50m,
                        considerationMinor: 500_00)))
                .AssetLotId;
        }

        await using var raw = database.CreateContext();

        var transactionId = Guid.NewGuid();
        var principalId = Guid.NewGuid();

        // A hand-written sale that claims to consume more than the lot holds.
        await raw.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "LedgerTransaction"
                ("Id","HouseholdId","TransactionTypeCode","StatusCode",
                 "OrderDate","ExecutionDate","SettlementDate",
                 "ExternalReference","Note","ReversalOfTransactionId",
                 "CreatedAtUtc","PostedAtUtc")
            VALUES ({0},{1},'SELL','DRAFT',NULL,'2026-08-24',NULL,
                    'DIRECT',NULL,NULL,'2026-08-24T10:00:00.0000000Z',NULL);
            """,
            transactionId,
            CoreLedgerTestData.HouseholdId);

        await raw.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "TransactionEntry"
                ("Id","TransactionId","EntrySequence","PortfolioId",
                 "AccountId","AssetId","QuantityDeltaE8","EntryRoleCode",
                 "UnitPriceE8","PriceCurrencyCode","CreatedAtUtc")
            VALUES ({0},{1},0,{2},{3},{4},-9900000000,'PRINCIPAL',
                    1000000000,'TRY','2026-08-24T10:00:00.0000000Z');
            """,
            principalId,
            transactionId,
            CoreLedgerTestData.PortfolioId,
            CoreLedgerTestData.AccountId,
            CoreLedgerTestData.FundAssetId);

        await raw.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "TransactionEntry"
                ("Id","TransactionId","EntrySequence","PortfolioId",
                 "AccountId","AssetId","QuantityDeltaE8","EntryRoleCode",
                 "UnitPriceE8","PriceCurrencyCode","CreatedAtUtc")
            VALUES ({0},{1},1,{2},{3},{4},990000000,'CONSIDERATION',
                    NULL,NULL,'2026-08-24T10:00:00.0000000Z');
            """,
            Guid.NewGuid(),
            transactionId,
            CoreLedgerTestData.PortfolioId,
            CoreLedgerTestData.DestinationAccountId,
            CoreLedgerTestData.CashAssetId);

        // The existing lot guard refuses the over-consuming allocation.
        var allocationFailure =
            await Assert.ThrowsAnyAsync<Exception>(
                () =>
                    raw.Database.ExecuteSqlRawAsync(
                        """
                        INSERT INTO "LotEntryAllocation"
                            ("Id","AssetLotId","TransactionEntryId",
                             "QuantityDeltaE8","CreatedAtUtc")
                        VALUES ({0},{1},{2},-9900000000,
                                '2026-08-24T10:00:00.0000000Z');
                        """,
                        Guid.NewGuid(),
                        lotId,
                        principalId));

        Assert.Contains(
            "negative",
            allocationFailure.ToString(),
            StringComparison.OrdinalIgnoreCase);

        // And posting a sale that reconciles to nothing is refused too.
        var postFailure =
            await Assert.ThrowsAnyAsync<Exception>(
                () =>
                    raw.Database.ExecuteSqlRawAsync(
                        """
                        UPDATE "LedgerTransaction"
                        SET "StatusCode" = 'POSTED',
                            "PostedAtUtc" = '2026-08-24T10:00:00.0000000Z'
                        WHERE "Id" = {0};
                        """,
                        transactionId));

        Assert.Contains(
            "FUND_TRADE_",
            postFailure.ToString(),
            StringComparison.Ordinal);

        await using var reopened = database.CreateContext();

        Assert.Equal(
            50_00000000L,
            await reopened.LotEntryAllocations
                .AsNoTracking()
                .SumAsync(x => x.QuantityDeltaE8));
    }

    /*
     * Buy and Sell are not fund-specific transaction types. An equity trade
     * uses exactly the same shape, and the fund verification read model
     * explains fund-specific facts such as FIFO lot consumption and ADR-009
     * realized cost.
     *
     * Reporting an equity trade through it would present a fund explanation
     * of something that is not one, so it must fail closed with the same
     * not-found answer any unreadable transaction gets.
     */
    [Theory]
    [InlineData(TransactionType.Buy)]
    [InlineData(TransactionType.Sell)]
    public async Task Verification_RejectsNonFundTrades(
        TransactionType tradeType)
    {
        await using var database = await SqliteTestDatabase.CreateAsync(
            "20260913054039_007_FundTradeLifecycleGuards");
        await SeedAsync(database);

        var transactionId =
            await SeedEquityTradeAsync(database, tradeType);

        await using (var upgrade = database.CreateContext())
        {
            await upgrade.Database.MigrateAsync();
        }

        await using var context = database.CreateContext();

        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () =>
                    BuildVerificationUseCase(context).ExecuteAsync(
                        CoreLedgerTestData.HouseholdId,
                        transactionId));

        Assert.Equal(
            FundTradeErrorCategory.NotFound,
            exception.Category);

        Assert.Equal(
            FundTradeErrorCodes.NotFound,
            exception.ErrorCode);

        // The refusal names nothing private about the transaction.
        Assert.DoesNotContain(
            transactionId.ToString("D"),
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "EQUITY-TRADE",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verification_RejectsAnotherHouseholdsFundTrade()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database);

        Guid transactionId;

        await using (var context = database.CreateContext())
        {
            transactionId =
                (await BuildPurchaseUseCase(context).ExecuteAsync(
                    "purchase-cross-household",
                    PurchaseCommand(
                        quantity: 10m,
                        considerationMinor: 100_00)))
                .TransactionId;
        }

        await using var reader = database.CreateContext();

        var exception =
            await Assert.ThrowsAsync<FundTradeException>(
                () =>
                    BuildVerificationUseCase(reader).ExecuteAsync(
                        CoreLedgerTestData.OtherHouseholdId,
                        transactionId));

        // The same answer as for a transaction that does not exist.
        Assert.Equal(
            FundTradeErrorCategory.NotFound,
            exception.Category);

        Assert.Equal(
            FundTradeErrorCodes.NotFound,
            exception.ErrorCode);
    }

    /// <summary>
    /// Writes a posted equity Buy or Sell directly, bypassing the fund
    /// writers, which cannot produce one.
    /// </summary>
    private static async Task<Guid> SeedEquityTradeAsync(
        SqliteTestDatabase database,
        TransactionType tradeType)
    {
        var transactionId = Guid.NewGuid();
        var principalId = Guid.NewGuid();
        var isBuy = tradeType == TransactionType.Buy;

        await using var context = database.CreateContext();

        context.Assets.Add(
            new WealthLedger.Infrastructure.Persistence.Rows.AssetRow
            {
                Id = EquityAssetId,
                Code = "EQUITY_TEST",
                Name = "Synthetic Equity",
                Type = Domain.Assets.AssetType.Equity,
                BaseUnit = Domain.Assets.AssetUnit.Share,
                BaseCurrencyCode = "TRY",
                // Optional tracking keeps the fixture focused: this test is
                // about the asset type, not about equity lot lineage.
                LotTrackingMode = Domain.Assets.LotTrackingMode.Optional,
                IsActive = true,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });

        context.LedgerTransactions.Add(
            new WealthLedger.Infrastructure.Persistence.Rows.LedgerTransactionRow
            {
                Id = transactionId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                Type = tradeType,
                Status = TransactionStatus.Draft,
                ExecutionDate = ExecutionDate,
                ExternalReference = "EQUITY-TRADE",
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });

        context.TransactionEntries.AddRange(
            new WealthLedger.Infrastructure.Persistence.Rows.TransactionEntryRow
            {
                Id = principalId,
                TransactionId = transactionId,
                EntrySequence = 0,
                PortfolioId = CoreLedgerTestData.PortfolioId,
                AccountId = CoreLedgerTestData.AccountId,
                AssetId = EquityAssetId,
                QuantityDeltaE8 = isBuy ? 10_00000000L : -10_00000000L,
                Role = EntryRole.Principal,
                UnitPriceE8 = 10_00000000L,
                PriceCurrencyCode = "TRY",
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            },
            new WealthLedger.Infrastructure.Persistence.Rows.TransactionEntryRow
            {
                Id = Guid.NewGuid(),
                TransactionId = transactionId,
                EntrySequence = 1,
                PortfolioId = CoreLedgerTestData.PortfolioId,
                AccountId = CoreLedgerTestData.DestinationAccountId,
                AssetId = CoreLedgerTestData.CashAssetId,
                QuantityDeltaE8 =
                    isBuy ? -100_000_000_000L : 100_000_000_000L,
                Role = EntryRole.Consideration,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });

        await context.SaveChangesAsync();

        /*
     * The M008 trigger only governs fund trades, so this historical equity
     * trade can be posted under migration 007. Migration 008 then upgrades
     * that history while refusing any new unsupported Buy/Sell principal
     * family. The read model must continue to fail closed for the historical
     * non-Fund activity.
         */
        await context.Database.ExecuteSqlRawAsync(
            """
            UPDATE "LedgerTransaction"
            SET "StatusCode" = 'POSTED', "PostedAtUtc" = {1}
            WHERE "ExternalReference" = {0};
            """,
            "EQUITY-TRADE",
            "2026-08-24T10:00:00.0000000Z");

        return transactionId;
    }

    private static readonly Guid EquityAssetId =
        Guid.Parse("60000000-0000-0000-0000-0000000000e1");

    private static async Task SeedAsync(SqliteTestDatabase database)
    {
        await using var context = database.CreateContext();
        await CoreLedgerTestData.SeedMasterDataAsync(context);
    }

    private static RecordFundPurchaseUseCase BuildPurchaseUseCase(
        WealthLedgerDbContext context)
    {
        var store = new EfCoreLedgerPostingStore(context);

        return new RecordFundPurchaseUseCase(
            new EfCoreOpeningBalanceReferenceStore(context),
            store,
            store,
            new FixedTimeProvider(RecordedAtUtc));
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

    private static RecordFundSaleUseCase BuildSaleUseCase(
        WealthLedgerDbContext context)
    {
        var store = new EfCoreLedgerPostingStore(context);

        return new RecordFundSaleUseCase(
            new EfCoreOpeningBalanceReferenceStore(context),
            store,
            store,
            new FixedTimeProvider(RecordedAtUtc));
    }

    private static GetFundTradeVerificationUseCase BuildVerificationUseCase(
        WealthLedgerDbContext context)
        => new(
            new EfCoreFundTradeVerificationReadStore(context),
            new EfCoreFundRealizedCostReadStore(context),
            new EfCoreOpeningBalanceReferenceStore(context),
            new FixedTimeProvider(RecordedAtUtc));

    private static RecordFundPurchaseCommand PurchaseCommand(
        decimal quantity,
        long considerationMinor,
        DateOnly? executionDate = null,
        IReadOnlyList<FundTradeCostInput>? costs = null)
        => new(
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
            executionDate ?? ExecutionDate,
            ExternalReference: "FUND-TRADE-TEST",
            Note: "Synthetic fund trade.",
            CashAccountId: CoreLedgerTestData.DestinationAccountId,
            Costs: costs);

    private static FundSaleCommand SaleCommand(
        decimal quantity,
        IReadOnlyList<ReviewedLotAllocation>? reviewedPlan = null,
        string? planFingerprint = null,
        Guid? fundAccountId = null)
        => new(
            CoreLedgerTestData.HouseholdId,
            CoreLedgerTestData.PortfolioId,
            fundAccountId ?? CoreLedgerTestData.AccountId,
            CoreLedgerTestData.DestinationAccountId,
            CoreLedgerTestData.FundAssetId,
            CoreLedgerTestData.CashAssetId,
            Quantity.FromDecimal(quantity),
            UnitPrice.FromDecimal(10m, CurrencyCode.TRY),
            Money.FromMinorUnits(
                (long)(quantity * 10m * 100m),
                CurrencyCode.TRY),
            ExecutionDate,
            ExternalReference: "FUND-SALE-TEST",
            Note: "Synthetic fund sale.",
            ReviewedPlan: reviewedPlan,
            ReviewedPlanFingerprint: planFingerprint);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        internal FixedTimeProvider(DateTimeOffset utcNow)
            => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
