using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WealthLedger.Infrastructure.Persistence;

namespace WealthLedger.Api.Tests;

internal sealed class WealthLedgerApiFactory : WebApplicationFactory<Program>
{
    private readonly string _directoryPath;

    internal WealthLedgerApiFactory()
    {
        _directoryPath = Path.Combine(
            Path.GetTempPath(),
            "WealthLedger.Api.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_directoryPath);

        var databasePath = Path.Combine(
            _directoryPath,
            "wealthledger.db");

        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
    }

    internal string ConnectionString { get; }

    internal async Task InitializeDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider
            .GetRequiredService<WealthLedgerDbContext>();

        await context.Database.MigrateAsync();
        await ApiTestData.SeedAsync(context);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(
            (_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:WealthLedger"] =
                            ConnectionString
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
