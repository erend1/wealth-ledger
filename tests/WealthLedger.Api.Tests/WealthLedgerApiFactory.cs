using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WealthLedger.Infrastructure.Persistence;

namespace WealthLedger.Api.Tests;

internal sealed class WealthLedgerApiFactory : WebApplicationFactory<Program>
{
    private readonly string _directoryPath;
    private readonly bool _setupEnabled;

    internal WealthLedgerApiFactory(
        bool setupEnabled = true,
        bool initializeDatabase = true)
    {
        _setupEnabled = setupEnabled;
        _directoryPath = Path.Combine(
            Path.GetTempPath(),
            "WealthLedger.Api.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_directoryPath);

        DatabasePath = Path.Combine(
            _directoryPath,
            "wealthledger.db");

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();

        if (initializeDatabase)
        {
            var options = new DbContextOptionsBuilder<WealthLedgerDbContext>()
                .UseSqlite(connectionString)
                .Options;
            using var context = new WealthLedgerDbContext(options);
            context.Database.Migrate();
        }
    }

    internal string DatabasePath { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(
            (_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Storage:DatabasePath"] = DatabasePath,
                        ["Setup:Enabled"] = _setupEnabled.ToString(),
                        ["urls"] = "http://127.0.0.1:0",
                        ["AllowedHosts"] = "localhost;127.0.0.1;[::1]"
                    });
            });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath, recursive: true);
        }
    }
}
