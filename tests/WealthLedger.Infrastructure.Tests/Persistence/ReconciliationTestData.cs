using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Evidence;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Application.PhysicalGold;
using WealthLedger.Application.Reconciliation;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Tests.Persistence;

/// <summary>
/// The synthetic statement every reconciliation test compares.
/// </summary>
/// <remarks>
/// Cash in the primary account is observed at 150.00 while the ledger holds
/// 100.00, the Fund position matches exactly, and the vault's gold matches in
/// gross weight but is counted with two pieces where the ledger holds three.
/// </remarks>
internal sealed record ReconciliationHistory(
    Guid EvidenceId,
    Guid OtherHouseholdPostedId,
    Guid DraftId);

/// <summary>
/// Synthetic helpers for observation and reconciliation tests over real
/// SQLite files. History is posted through the real M007, M009 and M002 use
/// cases.
/// </summary>
internal static class ReconciliationTestData
{
    internal const long Unit = 100_000_000L;

    internal static readonly Guid VaultId =
        Guid.Parse("50000000-0000-0000-0000-0000000000d1");

    internal static readonly DateOnly OpeningDate = new(2026, 9, 1);

    internal static readonly DateOnly ContributionDate = new(2026, 9, 10);

    internal static readonly DateOnly GoldPurchaseDate = new(2026, 9, 12);

    internal static readonly DateOnly CorrectionDate = new(2026, 9, 25);

    internal static readonly DateOnly EvidenceDate = new(2026, 9, 30);

    internal static readonly DateTimeOffset Now =
        new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    private static readonly string[] LedgerTables =
    [
        "LedgerTransaction",
        "TransactionEntry",
        "CashFlowDetail",
        "TransactionCostComponent",
        "AssetLot",
        "LotEntryAllocation",
        "PhysicalGoldLotDetail",
        "PhysicalGoldLotAllocationDetail",
        "PhysicalGoldTradeDetail",
        "CommandReceipt"
    ];

    internal static async Task<ReconciliationHistory> SeedAsync(
        Func<WealthLedgerDbContext> createContext)
    {
        await using (var context = createContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            context.Accounts.Add(new AccountRow
            {
                Id = VaultId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                Code = "RECONCILE_VAULT",
                Name = "Synthetic Reconcile Vault",
                Type = AccountType.PhysicalVault,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            });
            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    Guid.Parse("70000000-0000-0000-0000-0000000000d1"),
                    TransactionType.Contribution));
            await context.SaveChangesAsync();
        }

        await RecordOpeningAsync(
            createContext,
            "reconcile-cash-opening",
            CoreLedgerTestData.DestinationAccountId,
            CoreLedgerTestData.CashAssetId,
            1_000m,
            []);
        await RecordOpeningAsync(
            createContext,
            "reconcile-fund-opening",
            CoreLedgerTestData.AccountId,
            CoreLedgerTestData.FundAssetId,
            10m,
            [
                new OpeningBalanceLotCommand(
                    Quantity.FromDecimal(10m),
                    new DateOnly(2026, 8, 15),
                    CostBasis.Known(Money.FromMinorUnits(10_000, CurrencyCode.TRY)))
            ]);
        await RecordOpeningAsync(
            createContext,
            "reconcile-gold-opening",
            VaultId,
            CoreLedgerTestData.GoldAssetId,
            10m,
            [
                new OpeningBalanceLotCommand(
                    Quantity.FromDecimal(10m),
                    AcquiredOn: null,
                    CostBasis.Unknown(),
                    new PhysicalGoldLotDetail(new Fineness(916_000), pieceCount: 1))
            ]);
        await RecordContributionAsync(
            createContext,
            "reconcile-contribution",
            10_000,
            ContributionDate);
        await RecordGoldPurchaseAsync(createContext);
        var otherPostedId = await RecordContributionAsync(
            createContext,
            "reconcile-other-household",
            5_000,
            ContributionDate,
            CoreLedgerTestData.OtherHouseholdId,
            CoreLedgerTestData.OtherPortfolioId,
            CoreLedgerTestData.OtherAccountId);

        Guid evidenceId;

        await using (var context = createContext())
        {
            evidenceId = (await EvidenceTestData.RecordUseCase(context, Now)
                .ExecuteAsync(
                    "reconcile-statement",
                    EvidenceTestData.Command(
                        label: "Synthetic September statement",
                        effectiveDate: EvidenceDate)))
                .EvidenceId;
        }

        await using (var context = createContext())
        {
            await EvidenceTestData.ObservationUseCase(context, Now)
                .ExecuteAsync(new RecordEvidenceObservationsCommand(
                    CoreLedgerTestData.HouseholdId,
                    evidenceId,
                    [
                        CashObservation,
                        FundObservation,
                        GoldObservation
                    ]));
        }

        return new ReconciliationHistory(
            evidenceId,
            otherPostedId,
            Guid.Parse("70000000-0000-0000-0000-0000000000d1"));
    }

    internal static EvidenceObservationInput CashObservation
        => new(
            CoreLedgerTestData.PortfolioId,
            CoreLedgerTestData.AccountId,
            CoreLedgerTestData.CashAssetId,
            150 * Unit);

    internal static EvidenceObservationInput FundObservation
        => new(
            CoreLedgerTestData.PortfolioId,
            CoreLedgerTestData.AccountId,
            CoreLedgerTestData.FundAssetId,
            10 * Unit);

    internal static EvidenceObservationInput GoldObservation
        => new(
            CoreLedgerTestData.PortfolioId,
            VaultId,
            CoreLedgerTestData.GoldAssetId,
            30 * Unit,
            0,
            2);

    internal static RunReconciliationUseCase RunUseCase(
        WealthLedgerDbContext context,
        DateTimeOffset? now = null)
        => new(
            new EfCoreEvidenceStore(context),
            new EfCoreReconciliationStore(context),
            new EfCoreInventoryReadStore(context),
            new EvidenceTestData.FixedTimeProvider(now ?? Now));

    internal static ResolveReconciliationItemUseCase ResolveUseCase(
        WealthLedgerDbContext context,
        DateTimeOffset? now = null)
        => new(
            new EfCoreReconciliationStore(context),
            new EvidenceTestData.FixedTimeProvider(now ?? Now));

    internal static GetReconciliationRunUseCase GetUseCase(WealthLedgerDbContext context)
        => new(
            new EfCoreReconciliationReadStore(context),
            new EfCoreInventoryReadStore(context));

    internal static ListReconciliationRunsUseCase ListUseCase(WealthLedgerDbContext context)
        => new(new EfCoreReconciliationReadStore(context));

    internal static async Task<Guid> RecordContributionAsync(
        Func<WealthLedgerDbContext> createContext,
        string idempotencyKey,
        long minorUnits,
        DateOnly executionDate,
        Guid? householdId = null,
        Guid? portfolioId = null,
        Guid? accountId = null)
    {
        await using var context = createContext();

        return (await new RecordContributionUseCase(
                new EfCoreLedgerReferenceData(context),
                new EfCoreLedgerPostingStore(context),
                new EvidenceTestData.FixedTimeProvider(Now))
            .ExecuteAsync(
                idempotencyKey,
                new RecordContributionCommand(
                    householdId ?? CoreLedgerTestData.HouseholdId,
                    portfolioId ?? CoreLedgerTestData.PortfolioId,
                    accountId ?? CoreLedgerTestData.AccountId,
                    CoreLedgerTestData.CashAssetId,
                    Money.FromMinorUnits(minorUnits, CurrencyCode.TRY),
                    CashFlowCategory.Other,
                    executionDate)))
            .TransactionId;
    }

    internal static async Task ReverseAsync(
        Func<WealthLedgerDbContext> createContext,
        string idempotencyKey,
        Guid transactionId)
    {
        await using var context = createContext();

        Assert.NotNull(
            await new ReversePostedTransactionUseCase(
                    new EfCoreLedgerReversalStore(context),
                    new EvidenceTestData.FixedTimeProvider(Now.AddHours(5)))
                .ExecuteAsync(
                    idempotencyKey,
                    new ReversePostedTransactionCommand(
                        transactionId,
                        "Synthetic mistaken correction.")));
    }

    /// <summary>
    /// Every row of every ledger table, so a test can prove that evidence and
    /// reconciliation writes leave financial history byte-for-byte unchanged.
    /// </summary>
    internal static async Task<string> SnapshotLedgerAsync(SqliteTestDatabase database)
        => await SnapshotLedgerAsync(database.ConnectionString);

    internal static async Task<string> SnapshotLedgerAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        var snapshot = new StringBuilder();

        foreach (var table in LedgerTables)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM \"{table}\";";
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<string>();

            while (await reader.ReadAsync())
            {
                var values = new string[reader.FieldCount];

                for (var index = 0; index < reader.FieldCount; index++)
                {
                    values[index] = reader.IsDBNull(index)
                        ? "NULL"
                        : Convert.ToString(
                            reader.GetValue(index),
                            CultureInfo.InvariantCulture)!;
                }

                rows.Add(string.Join('|', values));
            }

            rows.Sort(StringComparer.Ordinal);
            snapshot.Append(table).Append(':').Append(rows.Count).Append('\n');
            snapshot.AppendJoin('\n', rows).Append('\n');
        }

        return snapshot.ToString();
    }

    private static async Task RecordOpeningAsync(
        Func<WealthLedgerDbContext> createContext,
        string idempotencyKey,
        Guid accountId,
        Guid assetId,
        decimal quantity,
        IReadOnlyList<OpeningBalanceLotCommand> lots)
    {
        await using var context = createContext();

        await new RecordOpeningBalanceUseCase(
                new EfCoreOpeningBalanceReferenceStore(context),
                new EfCoreOpeningBalanceEffectiveHistoryReadStore(context),
                new EfCoreLedgerPostingStore(context),
                new EfCoreLedgerTransactionReadStore(context),
                new EvidenceTestData.FixedTimeProvider(Now))
            .ExecuteAsync(
                idempotencyKey,
                new RecordOpeningBalanceCommand(
                    CoreLedgerTestData.HouseholdId,
                    CoreLedgerTestData.PortfolioId,
                    accountId,
                    assetId,
                    OpeningDate,
                    Quantity.FromDecimal(quantity),
                    "SYNTHETIC-RECONCILE-OPENING",
                    "Synthetic reconciliation opening source.",
                    lots));
    }

    private static async Task RecordGoldPurchaseAsync(
        Func<WealthLedgerDbContext> createContext)
    {
        await using var context = createContext();
        var posting = new EfCoreLedgerPostingStore(context);

        await new RecordPhysicalGoldPurchaseUseCase(
                new EfCoreOpeningBalanceReferenceStore(context),
                posting,
                posting,
                new EvidenceTestData.FixedTimeProvider(Now))
            .ExecuteAsync(
                "reconcile-gold-purchase",
                new PhysicalGoldPurchaseCommand(
                    CoreLedgerTestData.HouseholdId,
                    CoreLedgerTestData.PortfolioId,
                    VaultId,
                    CoreLedgerTestData.DestinationAccountId,
                    CoreLedgerTestData.GoldAssetId,
                    CoreLedgerTestData.CashAssetId,
                    Quantity.FromDecimal(20m),
                    new Fineness(916_000),
                    2,
                    Money.FromMinorUnits(20_000, CurrencyCode.TRY),
                    GoldPurchaseDate,
                    ExternalReference: "SYNTHETIC-RECONCILE-GOLD-BUY",
                    Note: "Synthetic reconciliation gold purchase."));
    }
}
