using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Infrastructure.Persistence;

namespace WealthLedger.Infrastructure.LocalData;

internal static class SqliteLocalDataConnectionFactory
{
    internal static SqliteConnection CreateConnection(
        string databasePath,
        SqliteOpenMode mode)
        => new(BuildConnectionString(databasePath, mode));

    internal static WealthLedgerDbContext CreateContext(
        string databasePath,
        SqliteOpenMode mode)
    {
        var options = new DbContextOptionsBuilder<WealthLedgerDbContext>()
            .UseSqlite(
                BuildConnectionString(databasePath, mode),
                sqlite => sqlite.MigrationsAssembly(
                    typeof(WealthLedgerDbContext).Assembly.FullName))
            .Options;

        return new WealthLedgerDbContext(options);
    }

    internal static string BuildConnectionString(
        string databasePath,
        SqliteOpenMode mode = SqliteOpenMode.ReadWrite)
        => new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = mode,
            ForeignKeys = true,
            Pooling = false,
            Cache = SqliteCacheMode.Private
        }.ToString();
}
