using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace WealthLedger.Api.Tests;

internal sealed class WealthLedgerApiFactory : WebApplicationFactory<Program>
{
    private readonly string _directoryPath;
    private readonly bool _setupEnabled;
    private readonly bool _applyMigrationsOnStartup;

    internal WealthLedgerApiFactory(
        bool setupEnabled = true,
        bool applyMigrationsOnStartup = true)
    {
        _setupEnabled = setupEnabled;
        _applyMigrationsOnStartup = applyMigrationsOnStartup;
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
                            ConnectionString,
                        ["Database:ApplyMigrationsOnStartup"] =
                            _applyMigrationsOnStartup.ToString(),
                        ["Setup:Enabled"] = _setupEnabled.ToString()
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
