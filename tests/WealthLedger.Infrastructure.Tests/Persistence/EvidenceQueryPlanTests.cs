using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WealthLedger.Application.Evidence;
using WealthLedger.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class EvidenceQueryPlanTests
{
    private readonly ITestOutputHelper _output;

    public EvidenceQueryPlanTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task EvidenceReads_UseIndexesAndNeverSelectBytesOutsideDownload()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var fixture = await EvidenceTestData.SeedAsync(database.CreateContext);
        Guid evidenceId;

        await using (var seed = database.CreateContext())
        {
            evidenceId = (await EvidenceTestData.RecordUseCase(seed).ExecuteAsync(
                "evidence-plan",
                EvidenceTestData.Command(
                    institutionId: CoreLedgerTestData.InstitutionId,
                    accountId: CoreLedgerTestData.AccountId,
                    transactionIds: [fixture.FirstPostedId],
                    attachment: EvidenceTestData.PdfUpload()))).EvidenceId;
        }

        var interceptor = new ReaderCommandInterceptor();
        var options = new DbContextOptionsBuilder<WealthLedgerDbContext>()
            .UseSqlite(database.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        await using (var context = new WealthLedgerDbContext(options))
        {
            var store = new EfCoreEvidenceReadStore(context);
            _ = await store.ListAsync(CoreLedgerTestData.HouseholdId, 51, after: null);
            _ = await store.ListAsync(
                CoreLedgerTestData.HouseholdId,
                51,
                new EvidenceCursorKey(
                    EvidenceTestData.EffectiveDate,
                    EvidenceTestData.Now,
                    evidenceId));
            _ = await store.GetAsync(CoreLedgerTestData.HouseholdId, evidenceId);
            _ = await store.ReadAttachmentAsync(CoreLedgerTestData.HouseholdId, evidenceId);
        }

        // First page, cursor page, detail, detail links, attachment download.
        Assert.Equal(5, interceptor.Commands.Count);

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
        }

        foreach (var listCommand in interceptor.Commands.Take(2))
        {
            var details = await ExplainAsync(database, listCommand);

            Assert.Contains(
                details,
                detail => detail.Contains(
                    "IX_EvidenceRecord_Household_Effective_Created_Id",
                    StringComparison.Ordinal));
            Assert.DoesNotContain(
                details,
                detail => detail.Contains("TEMP B-TREE", StringComparison.Ordinal));
        }

        Assert.All(
            interceptor.Commands.Take(4),
            captured => Assert.DoesNotContain(
                "\"Content\"",
                captured.CommandText,
                StringComparison.Ordinal));
        Assert.Contains(
            "\"Content\"",
            interceptor.Commands[^1].CommandText,
            StringComparison.Ordinal);
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
