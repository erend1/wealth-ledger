using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;
using WealthLedger.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class InventoryQueryPlanTests
{
    private readonly ITestOutputHelper _output;

    public InventoryQueryPlanTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Inventory_ProductionQueriesUseExistingIndexesWithoutScans()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using (var seed = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(seed);
            await new RecordOpeningBalanceUseCase(
                    new EfCoreOpeningBalanceReferenceStore(seed),
                    new EfCoreOpeningBalanceEffectiveHistoryReadStore(seed),
                    new EfCoreLedgerPostingStore(seed),
                    new EfCoreLedgerTransactionReadStore(seed),
                    new FixedTimeProvider(
                        new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero)))
                .ExecuteAsync(
                    "inventory-plan-fund-opening",
                    new RecordOpeningBalanceCommand(
                        CoreLedgerTestData.HouseholdId,
                        CoreLedgerTestData.PortfolioId,
                        CoreLedgerTestData.AccountId,
                        CoreLedgerTestData.FundAssetId,
                        new DateOnly(2026, 9, 1),
                        Quantity.FromDecimal(10m),
                        "SYNTHETIC-PLAN-OPENING",
                        "Synthetic query-plan opening.",
                        [
                            new OpeningBalanceLotCommand(
                                Quantity.FromDecimal(10m),
                                AcquiredOn: null,
                                CostBasis.Unknown())
                        ]));
        }

        // EF emits different SQL for null and non-null parameters, so each
        // representative shape is explained on its own.
        (Guid? AccountId, Guid? AssetId, DateOnly? AsOf)[] shapes =
        [
            (null, null, new DateOnly(2026, 9, 30)),
            (null, null, null),
            (
                CoreLedgerTestData.AccountId,
                CoreLedgerTestData.FundAssetId,
                new DateOnly(2026, 9, 30))
        ];

        foreach (var (accountId, assetId, asOf) in shapes)
        {
            var commands = await CaptureInventoryCommandsAsync(
                database,
                accountId,
                assetId,
                asOf);

            // Position entries, lot roots, then lot allocations.
            Assert.Equal(3, commands.Count);

            foreach (var captured in commands)
            {
                var details = await ExplainAsync(database, captured);
                _output.WriteLine(string.Join(Environment.NewLine, details));

                // M010 adds no inventory index: every table access must be a
                // search on an existing index, never a scan or an automatic
                // (temporary) index.
                Assert.DoesNotContain(
                    details,
                    detail => detail.StartsWith("SCAN ", StringComparison.Ordinal));
                Assert.DoesNotContain(
                    details,
                    detail => detail.Contains(
                        "AUTOMATIC",
                        StringComparison.OrdinalIgnoreCase));
            }

            if (accountId is null)
            {
                Assert.Contains(
                    await ExplainAsync(database, commands[0]),
                    detail => detail.Contains(
                        "IX_LedgerTransaction_Household_Status_Date",
                        StringComparison.Ordinal));
            }
        }
    }

    private static async Task<IReadOnlyList<CapturedCommand>>
        CaptureInventoryCommandsAsync(
            SqliteTestDatabase database,
            Guid? accountId,
            Guid? assetId,
            DateOnly? asOf)
    {
        var interceptor = new ReaderCommandInterceptor();
        var options = new DbContextOptionsBuilder<WealthLedgerDbContext>()
            .UseSqlite(database.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        await using var context = new WealthLedgerDbContext(options);
        var store = new EfCoreInventoryReadStore(context);
        _ = await store.ListPositionEntryFactsAsync(
            CoreLedgerTestData.HouseholdId,
            null,
            accountId,
            null,
            assetId,
            asOf);
        _ = await store.ListLotFactsAsync(
            CoreLedgerTestData.HouseholdId,
            assetId,
            asOf);

        return interceptor.Commands;
    }

    private static async Task<IReadOnlyList<string>> ExplainAsync(
        SqliteTestDatabase database,
        CapturedCommand captured)
    {
        await using var connection = new SqliteConnection(
            database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + captured.CommandText;

        foreach (var parameter in captured.Parameters)
        {
            command.Parameters.AddWithValue(
                parameter.Name,
                parameter.Value ?? DBNull.Value);
        }

        var details = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            details.Add(reader.GetString(3));
        }

        return details;
    }

    private sealed record CapturedParameter(string Name, object? Value);

    private sealed record CapturedCommand(
        string CommandText,
        IReadOnlyList<CapturedParameter> Parameters);

    private sealed class ReaderCommandInterceptor : DbCommandInterceptor
    {
        private readonly List<CapturedCommand> _commands = [];

        internal IReadOnlyList<CapturedCommand> Commands => _commands;

        public override ValueTask<InterceptionResult<DbDataReader>>
            ReaderExecutingAsync(
                DbCommand command,
                CommandEventData eventData,
                InterceptionResult<DbDataReader> result,
                CancellationToken cancellationToken = default)
        {
            _commands.Add(
                new CapturedCommand(
                    command.CommandText,
                    command.Parameters
                        .Cast<DbParameter>()
                        .Select(
                            parameter => new CapturedParameter(
                                parameter.ParameterName,
                                parameter.Value))
                        .ToArray()));
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        internal FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
