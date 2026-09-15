using Microsoft.EntityFrameworkCore;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Tests.Persistence;

/// <summary>
/// Proves migration 007 refuses invalid Fund Buy and Sell graphs written
/// straight to SQLite.
/// </summary>
/// <remarks>
/// The application is not the only way into the file. A restore, a repair
/// script or a future tool can write rows directly, so the accepted shape has
/// to hold at the draft-to-posted boundary rather than only in C#.
///
/// Each case builds an otherwise valid trade and breaks exactly one rule, so
/// a passing test means that rule specifically is enforced.
/// </remarks>
public sealed class FundTradeDirectSqlGuardTests
{
    private const string PostedAtUtc = "2026-08-24T10:00:00.0000000Z";

    private const string DirectReference = "DIRECT-SQL";

    private static readonly DateOnly ExecutionDate = new(2026, 8, 24);

    [Fact]
    public async Task ValidFundPurchase_Posts()
    {
        await using var database = await CreateSeededAsync();

        Assert.Null(await TryPostPurchaseAsync(database, _ => { }));
    }

    [Fact]
    public async Task CashLegInAnotherHousehold_IsRefused()
    {
        await using var database = await CreateSeededAsync();

        var failure = await TryPostPurchaseAsync(
            database,
            graph => graph.Consideration.AccountId =
                CoreLedgerTestData.OtherAccountId);

        // M001 already refuses a cross-household entry as it is written.
        AssertRefused(failure);
    }

    [Fact]
    public async Task EntriesInDifferentPortfolios_AreRefused()
    {
        await using var database = await CreateSeededAsync();

        var failure = await TryPostPurchaseAsync(
            database,
            graph => graph.Consideration.PortfolioId =
                CoreLedgerTestData.OtherPortfolioId);

        AssertRefused(failure);
    }

    [Fact]
    public async Task FundLegInACashAccount_IsRefused()
    {
        await using var database = await CreateSeededAsync();

        var failure = await TryPostPurchaseAsync(
            database,
            graph => graph.Principal.AccountId = CashOnlyAccountId);

        AssertRefused(failure, "FUND_TRADE_REFERENCE_INVALID");
    }

    [Fact]
    public async Task CashLegInAPhysicalVaultAccount_IsRefused()
    {
        await using var database = await CreateSeededAsync();

        var failure = await TryPostPurchaseAsync(
            database,
            graph => graph.Consideration.AccountId = VaultAccountId);

        AssertRefused(failure, "FUND_TRADE_REFERENCE_INVALID");
    }

    [Fact]
    public async Task CashLegUsingALotTrackedAsset_IsRefused()
    {
        await using var database = await CreateSeededAsync();

        var failure = await TryPostPurchaseAsync(
            database,
            graph => graph.Consideration.AssetId =
                CoreLedgerTestData.OtherFundAssetId);

        AssertRefused(failure, "FUND_TRADE_");
    }

    [Fact]
    public async Task MismatchedPriceCurrency_IsRefused()
    {
        await using var database = await CreateSeededAsync();

        var failure = await TryPostPurchaseAsync(
            database,
            graph => graph.Principal.PriceCurrencyCode = "USD");

        AssertRefused(failure, "FUND_TRADE_CURRENCY_MISMATCH");
    }

    [Fact]
    public async Task MismatchedCostComponentCurrency_IsRefused()
    {
        await using var database = await CreateSeededAsync();

        var failure = await TryPostPurchaseAsync(
            database,
            graph => graph.Cost = new TransactionCostComponentRow
            {
                Id = Guid.NewGuid(),
                TransactionId = graph.Transaction.Id,
                Type = CostType.Commission,
                Treatment = CostTreatment.InformationalOnly,
                AmountMinor = 100,
                CurrencyCode = "USD",
                Note = null
            });

        AssertRefused(failure, "FUND_TRADE_CURRENCY_MISMATCH");
    }

    [Fact]
    public async Task InactiveAsset_IsRefused()
    {
        await using var database = await CreateSeededAsync();

        await using (var context = database.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                """UPDATE "Asset" SET "IsActive" = 0 WHERE "Id" = {0};""",
                CoreLedgerTestData.FundAssetId);
        }

        var failure = await TryPostPurchaseAsync(database, _ => { });

        AssertRefused(failure, "FUND_TRADE_REFERENCE_INVALID");
    }

    [Fact]
    public async Task InactiveInstitutionOnTheFundAccount_IsRefused()
    {
        await using var database = await CreateSeededAsync();

        await using (var context = database.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                """UPDATE "Institution" SET "IsActive" = 0 WHERE "Id" = {0};""",
                CoreLedgerTestData.InstitutionId);
        }

        var failure = await TryPostPurchaseAsync(database, _ => { });

        AssertRefused(failure, "FUND_TRADE_REFERENCE_INVALID");
    }

    [Fact]
    public async Task ExecutionDateBeforeAccountOpening_IsRefused()
    {
        await using var database = await CreateSeededAsync();

        await using (var context = database.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                """UPDATE "Account" SET "OpenedOn" = '2026-12-01' WHERE "Id" = {0};""",
                CoreLedgerTestData.AccountId);
        }

        var failure = await TryPostPurchaseAsync(database, _ => { });

        AssertRefused(failure, "FUND_TRADE_DATE_INVALID");
    }

    [Fact]
    public async Task PurchaseWithInvertedSigns_IsRefused()
    {
        await using var database = await CreateSeededAsync();

        var failure = await TryPostPurchaseAsync(
            database,
            graph =>
            {
                graph.Principal.QuantityDeltaE8 = -100_00000000L;
                graph.Consideration.QuantityDeltaE8 = 100_000_000_000L;
            });

        // M001 refuses a lot opened by a negative entry before posting.
        AssertRefused(failure);
    }

    /*
     * Different accounts at different institutions are legitimate: a fund can
     * be held at a broker while its cash sits at a bank. The guard must not
     * quietly require one account or one institution.
     */
    [Fact]
    public async Task SeparateAccountsAtDifferentInstitutions_Post()
    {
        await using var database = await CreateSeededAsync();

        Assert.Null(
            await TryPostPurchaseAsync(
                database,
                graph => graph.Consideration.AccountId =
                    SecondInstitutionCashAccountId));
    }

    /// <summary>
    /// Asserts the graph was refused somewhere on its way to Posted.
    /// </summary>
    /// <remarks>
    /// Some rules belong to M008's trigger and name a FUND_TRADE code. Others
    /// were already enforced by the M001 entry and lot triggers and refuse the
    /// write earlier with their own message. Both outcomes satisfy the
    /// invariant, so a specific code is required only where M008 owns the rule.
    /// </remarks>
    private static void AssertRefused(
        string? failure,
        string? expectedCode = null)
    {
        Assert.NotNull(failure);

        if (expectedCode is not null)
        {
            Assert.Contains(
                expectedCode,
                failure,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Writes a fund purchase directly, applies one mutation, then attempts
    /// the draft-to-posted transition.
    /// </summary>
    /// <returns>The refusal message, or null when the trade posted.</returns>
    private static async Task<string?> TryPostPurchaseAsync(
        SqliteTestDatabase database,
        Action<DirectPurchaseGraph> mutate)
    {
        var transactionId = Guid.NewGuid();
        var principalId = Guid.NewGuid();

        var graph = new DirectPurchaseGraph
        {
            Transaction = new LedgerTransactionRow
            {
                Id = transactionId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                Type = TransactionType.Buy,
                Status = TransactionStatus.Draft,
                ExecutionDate = ExecutionDate,
                ExternalReference = DirectReference,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            },
            Principal = new TransactionEntryRow
            {
                Id = principalId,
                TransactionId = transactionId,
                EntrySequence = 0,
                PortfolioId = CoreLedgerTestData.PortfolioId,
                AccountId = CoreLedgerTestData.AccountId,
                AssetId = CoreLedgerTestData.FundAssetId,
                QuantityDeltaE8 = 100_00000000L,
                Role = EntryRole.Principal,
                UnitPriceE8 = 10_00000000L,
                PriceCurrencyCode = "TRY",
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            },
            Consideration = new TransactionEntryRow
            {
                Id = Guid.NewGuid(),
                TransactionId = transactionId,
                EntrySequence = 1,
                PortfolioId = CoreLedgerTestData.PortfolioId,
                AccountId = CoreLedgerTestData.DestinationAccountId,
                AssetId = CoreLedgerTestData.CashAssetId,
                QuantityDeltaE8 = -100_000_000_000L,
                Role = EntryRole.Consideration,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            },
            Lot = new AssetLotRow
            {
                Id = Guid.NewGuid(),
                AssetId = CoreLedgerTestData.FundAssetId,
                OpeningTransactionEntryId = principalId,
                AcquiredOn = ExecutionDate,
                OriginalCostBasisMinor = 100_000,
                CostBasisCurrencyCode = "TRY",
                CostBasisStatus = Domain.Lots.CostBasisStatus.Known,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            }
        };

        mutate(graph);

        /*
         * Some invalid graphs are refused as soon as they are written, by the
         * M001 entry and lot triggers, and others only at the draft-to-posted
         * transition. Both are captured here: what matters is that an invalid
         * graph cannot end up Posted, not which boundary stops it.
         */
        try
        {
            await using (var writeContext = database.CreateContext())
            {
                writeContext.LedgerTransactions.Add(graph.Transaction);
                writeContext.TransactionEntries.Add(graph.Principal);
                writeContext.TransactionEntries.Add(graph.Consideration);
                writeContext.AssetLots.Add(graph.Lot);

                if (graph.Cost is not null)
                {
                    writeContext.TransactionCostComponents.Add(graph.Cost);
                }

                writeContext.LotEntryAllocations.Add(
                    new LotEntryAllocationRow
                    {
                        Id = Guid.NewGuid(),
                        AssetLotId = graph.Lot.Id,
                        TransactionEntryId = graph.Principal.Id,
                        QuantityDeltaE8 = graph.Principal.QuantityDeltaE8,
                        CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
                    });

                await writeContext.SaveChangesAsync();
            }

            await using var postContext = database.CreateContext();

            /*
             * The draft is addressed by its external reference rather than by
             * a Guid parameter. Guids are stored as text, so a raw-SQL Guid
             * parameter can silently match nothing, and a zero-row update
             * would look like a trade the guards accepted.
             */
            var affected =
                await postContext.Database.ExecuteSqlRawAsync(
                    """
                    UPDATE "LedgerTransaction"
                    SET "StatusCode" = 'POSTED', "PostedAtUtc" = {1}
                    WHERE "ExternalReference" = {0}
                      AND "StatusCode" = 'DRAFT';
                    """,
                    DirectReference,
                    PostedAtUtc);

            Assert.Equal(1, affected);

            return null;
        }
        catch (Exception exception)
        {
            return exception.ToString();
        }
    }

    private static readonly Guid CashOnlyAccountId =
        Guid.Parse("51000000-0000-0000-0000-000000000001");

    private static readonly Guid VaultAccountId =
        Guid.Parse("51000000-0000-0000-0000-000000000002");

    private static readonly Guid SecondInstitutionId =
        Guid.Parse("31000000-0000-0000-0000-000000000001");

    private static readonly Guid SecondInstitutionCashAccountId =
        Guid.Parse("51000000-0000-0000-0000-000000000003");

    private static async Task<SqliteTestDatabase> CreateSeededAsync()
    {
        var database = await SqliteTestDatabase.CreateAsync();

        await using var context = database.CreateContext();
        await CoreLedgerTestData.SeedMasterDataAsync(context);

        context.Institutions.Add(
            new InstitutionRow
            {
                Id = SecondInstitutionId,
                Code = "SECOND_INSTITUTION",
                Name = "Second Test Institution",
                Type = InstitutionType.Bank,
                IsActive = true
            });

        context.Accounts.AddRange(
            new AccountRow
            {
                Id = CashOnlyAccountId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                Code = "CASH_ONLY",
                Name = "Cash Only Account",
                Type = AccountType.Cash,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            },
            new AccountRow
            {
                Id = VaultAccountId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                Code = "VAULT_ONLY",
                Name = "Vault Account",
                Type = AccountType.PhysicalVault,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            },
            new AccountRow
            {
                Id = SecondInstitutionCashAccountId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                InstitutionId = SecondInstitutionId,
                Code = "OTHER_BANK_CASH",
                Name = "Cash At Another Institution",
                Type = AccountType.Cash,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            });

        await context.SaveChangesAsync();

        return database;
    }

    private sealed class DirectPurchaseGraph
    {
        public required LedgerTransactionRow Transaction { get; init; }

        public required TransactionEntryRow Principal { get; init; }

        public required TransactionEntryRow Consideration { get; init; }

        public required AssetLotRow Lot { get; init; }

        public TransactionCostComponentRow? Cost { get; set; }
    }
}
