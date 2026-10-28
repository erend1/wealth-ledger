using System.Text;
using Microsoft.Data.Sqlite;

namespace WealthLedger.Infrastructure.Tests.Persistence;

/// <summary>
/// Synthetic helpers for observation and reconciliation tests over real
/// SQLite files.
/// </summary>
internal static class ReconciliationTestData
{
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

    /// <summary>
    /// Every row of every ledger table, so a test can prove that evidence and
    /// reconciliation writes leave financial history byte-for-byte unchanged.
    /// </summary>
    internal static async Task<string> SnapshotLedgerAsync(SqliteTestDatabase database)
    {
        await using var connection = new SqliteConnection(database.ConnectionString);
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
                            System.Globalization.CultureInfo.InvariantCulture)!;
                }

                rows.Add(string.Join('|', values));
            }

            rows.Sort(StringComparer.Ordinal);
            snapshot.Append(table).Append(':').Append(rows.Count).Append('\n');
            snapshot.AppendJoin('\n', rows).Append('\n');
        }

        return snapshot.ToString();
    }
}
