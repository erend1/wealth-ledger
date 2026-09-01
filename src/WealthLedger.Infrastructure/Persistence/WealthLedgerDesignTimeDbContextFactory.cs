using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WealthLedger.Infrastructure.Persistence;

public sealed class WealthLedgerDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<WealthLedgerDbContext>
{
    public WealthLedgerDbContext CreateDbContext(string[] args)
    {
        var syntheticDirectory = Path.Combine(
            Path.GetTempPath(),
            "WealthLedger.DesignTime",
            $"{Environment.ProcessId}-{Guid.NewGuid():N}");
        var syntheticDatabasePath = Path.Combine(
            syntheticDirectory,
            "wealthledger.design.db");
        Directory.CreateDirectory(syntheticDirectory);
        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = syntheticDatabasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();

        var options = new DbContextOptionsBuilder<WealthLedgerDbContext>()
            .UseSqlite(
                connectionString,
                sqlite => sqlite.MigrationsAssembly(
                    typeof(WealthLedgerDbContext).Assembly.FullName))
            .Options;

        return new WealthLedgerDbContext(options);
    }
}
