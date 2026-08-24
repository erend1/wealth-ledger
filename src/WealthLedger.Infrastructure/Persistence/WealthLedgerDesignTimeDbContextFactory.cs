using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WealthLedger.Infrastructure.Persistence;

public sealed class WealthLedgerDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<WealthLedgerDbContext>
{
    public WealthLedgerDbContext CreateDbContext(string[] args)
    {
        var connectionString = args.FirstOrDefault()
            ?? "Data Source=wealthledger.design.db;Foreign Keys=True";

        var options = new DbContextOptionsBuilder<WealthLedgerDbContext>()
            .UseSqlite(
                connectionString,
                sqlite => sqlite.MigrationsAssembly(
                    typeof(WealthLedgerDbContext).Assembly.FullName))
            .Options;

        return new WealthLedgerDbContext(options);
    }
}
