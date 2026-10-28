using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WealthLedger.Application.Reconciliation;
using WealthLedger.Infrastructure.Persistence;
using Xunit.Abstractions;
using static WealthLedger.Infrastructure.Tests.Persistence.ReconciliationTestData;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class ReconciliationQueryPlanTests
{
    private readonly ITestOutputHelper _output;

    public ReconciliationQueryPlanTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ReconciliationReads_UseIndexesWithoutScansOrSorts()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var history = await SeedAsync(database.CreateContext);
        Guid runId;

        await using (var seed = database.CreateContext())
        {
            runId = (await RunUseCase(seed).ExecuteAsync(
                    "plan-run",
                    new RunReconciliationCommand(CoreLedgerTestData.HouseholdId, history.EvidenceId)))
                .ReconciliationRunId;
        }

        var interceptor = new ReaderCommandInterceptor();
        var options = new DbContextOptionsBuilder<WealthLedgerDbContext>()
            .UseSqlite(database.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;
        var cursor = new ReconciliationRunCursorKey(Now, runId);

        await using (var context = new WealthLedgerDbContext(options))
        {
            var readStore = new EfCoreReconciliationReadStore(context);
            var store = new EfCoreReconciliationStore(context);
            _ = await readStore.ListRunsAsync(CoreLedgerTestData.HouseholdId, null, 51, after: null);
            _ = await readStore.ListRunsAsync(CoreLedgerTestData.HouseholdId, null, 51, cursor);
            _ = await readStore.ListRunsAsync(CoreLedgerTestData.HouseholdId, history.EvidenceId, 51, cursor);
            _ = await readStore.GetRunAsync(CoreLedgerTestData.HouseholdId, runId);
            _ = await store.FindRunByKeyAsync(CoreLedgerTestData.HouseholdId, "plan-run");
            _ = await store.FindItemAsync(Guid.NewGuid());
        }

        // Three list shapes, run header, run items, retry key, item lookup.
        Assert.Equal(7, interceptor.Commands.Count);

        foreach (var captured in interceptor.Commands)
        {
            var details = await ExplainAsync(database, captured);
            _output.WriteLine(captured.CommandText);
            _output.WriteLine(string.Join(Environment.NewLine, details));

            Assert.DoesNotContain(
                details,
                detail => detail.StartsWith("SCAN ", StringComparison.Ordinal));
            Assert.DoesNotContain(
                details,
                detail => detail.Contains("AUTOMATIC", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                details,
                detail => detail.Contains("TEMP B-TREE", StringComparison.Ordinal));
        }

        foreach (var (command, index) in new[]
                 {
                     (interceptor.Commands[0], "IX_ReconciliationRun_Household_RunAt_Id"),
                     (interceptor.Commands[1], "IX_ReconciliationRun_Household_RunAt_Id"),
                     (interceptor.Commands[2], "IX_ReconciliationRun_Evidence_RunAt_Id"),
                     (interceptor.Commands[5], "UX_ReconciliationRun_Household_IdempotencyKey")
                 })
        {
            Assert.Contains(
                await ExplainAsync(database, command),
                detail => detail.Contains(index, StringComparison.Ordinal));
        }
    }

    private static async Task<IReadOnlyList<string>> ExplainAsync(
        SqliteTestDatabase database,
        CapturedCommand captured)
    {
        await using var connection = new SqliteConnection(database.ConnectionString);
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
                        .Select(parameter => new CapturedParameter(
                            parameter.ParameterName,
                            parameter.Value))
                        .ToArray()));
            return ValueTask.FromResult(result);
        }
    }
}
