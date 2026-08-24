using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Infrastructure.Persistence;

namespace WealthLedger.Infrastructure.Tests.Persistence;

internal sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly string _directoryPath;

    private SqliteTestDatabase(string directoryPath, string connectionString)
    {
        _directoryPath = directoryPath;
        ConnectionString = connectionString;
    }

    internal string ConnectionString { get; }

    internal static async Task<SqliteTestDatabase> CreateAsync()
    {
        var directoryPath = Path.Combine(
            Path.GetTempPath(),
            "WealthLedger.Infrastructure.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directoryPath);

        var databasePath = Path.Combine(directoryPath, "wealthledger.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();

        var database = new SqliteTestDatabase(directoryPath, connectionString);

        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();

        return database;
    }

    internal WealthLedgerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<WealthLedgerDbContext>()
            .UseSqlite(ConnectionString)
            .Options;

        return new WealthLedgerDbContext(options);
    }

    internal async Task<int> ExecuteNonQueryAsync(
        string sql,
        params SqliteParameter[] parameters)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);

        return await command.ExecuteNonQueryAsync();
    }

    internal async Task<object?> ExecuteScalarAsync(
        string sql,
        params SqliteParameter[] parameters)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);

        return await command.ExecuteScalarAsync();
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
