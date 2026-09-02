using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WealthLedger.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class LedgerNavigationQueryPlanTests
{
    private readonly ITestOutputHelper _output;

    public LedgerNavigationQueryPlanTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Navigation_QueryPlanUsesRecentLedgerCompositeIndex()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var details = await ExplainActualLedgerPageQueryAsync(database);

        _output.WriteLine(string.Join(Environment.NewLine, details));

        Assert.Contains(
            details,
            detail => detail.Contains(
                "IX_LedgerTransaction_Household_Status_Posted_Id",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            details,
            detail => detail.Contains(
                "TEMP B-TREE",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Navigation_PreMigrationQueryPlanRequiresTheCompositeIndex()
    {
        await using var database = await SqliteTestDatabase.CreateAsync(
            "20260831113310_003_ReversalDependencySemantics");
        var details = await ExplainActualLedgerPageQueryAsync(database);

        _output.WriteLine(string.Join(Environment.NewLine, details));
        Assert.Contains(
            details,
            detail => detail.Contains(
                "IX_LedgerTransaction_Household_Status_Date",
                StringComparison.Ordinal));
        Assert.Contains(
            details,
            detail => detail.Contains(
                "TEMP B-TREE FOR ORDER BY",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Navigation_MigrationUpAndDownChangeOnlyIndexAndPreserveData()
    {
        const string previousMigration =
            "20260831113310_003_ReversalDependencySemantics";
        await using var database = await SqliteTestDatabase.CreateAsync(
            previousMigration);

        await using (var seedContext = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(seedContext);
        }

        Assert.Equal(0L, await CountNavigationIndexesAsync(database));

        await using (var upContext = database.CreateContext())
        {
            await upContext.Database.MigrateAsync();
        }

        Assert.Equal(1L, await CountNavigationIndexesAsync(database));
        Assert.Equal(2L, await CountHouseholdsAsync(database));

        await using (var downContext = database.CreateContext())
        {
            await downContext.Database.MigrateAsync(previousMigration);
        }

        Assert.Equal(0L, await CountNavigationIndexesAsync(database));
        Assert.Equal(2L, await CountHouseholdsAsync(database));
    }

    private static async Task<long> CountNavigationIndexesAsync(
        SqliteTestDatabase database)
        => Convert.ToInt64(
            await database.ExecuteScalarAsync(
                """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'index'
                  AND name = 'IX_LedgerTransaction_Household_Status_Posted_Id';
                """));

    private static async Task<long> CountHouseholdsAsync(
        SqliteTestDatabase database)
        => Convert.ToInt64(
            await database.ExecuteScalarAsync(
                "SELECT COUNT(*) FROM Household;"));

    private static async Task<IReadOnlyList<string>>
        ExplainActualLedgerPageQueryAsync(SqliteTestDatabase database)
    {
        var interceptor = new FirstReaderCommandInterceptor();
        var options = new DbContextOptionsBuilder<WealthLedgerDbContext>()
            .UseSqlite(database.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        await using (var context = new WealthLedgerDbContext(options))
        {
            _ = await new EfCoreLedgerNavigationReadStore(context)
                .ListRecentPostedTransactionsAsync(
                    CoreLedgerTestData.HouseholdId,
                    take: 101,
                    after: null);
        }

        var captured = Assert.IsType<CapturedCommand>(interceptor.Command);
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

    private sealed class FirstReaderCommandInterceptor : DbCommandInterceptor
    {
        internal CapturedCommand? Command { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>>
            ReaderExecutingAsync(
                DbCommand command,
                CommandEventData eventData,
                InterceptionResult<DbDataReader> result,
                CancellationToken cancellationToken = default)
        {
            Command ??= new CapturedCommand(
                command.CommandText,
                command.Parameters
                    .Cast<DbParameter>()
                    .Select(
                        parameter => new CapturedParameter(
                            parameter.ParameterName,
                            parameter.Value))
                    .ToArray());
            return ValueTask.FromResult(result);
        }
    }
}
