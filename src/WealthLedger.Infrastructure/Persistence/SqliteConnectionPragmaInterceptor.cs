using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;

namespace WealthLedger.Infrastructure.Persistence;

internal sealed class SqliteConnectionPragmaInterceptor : DbConnectionInterceptor
{
    internal static readonly SqliteConnectionPragmaInterceptor Instance = new();

    private SqliteConnectionPragmaInterceptor()
    {
    }

    public override void ConnectionOpened(
        DbConnection connection,
        ConnectionEndEventData eventData)
    {
        ConfigureConnection(connection);
    }

    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ConfigureConnection(connection);
        return Task.CompletedTask;
    }

    private static void ConfigureConnection(DbConnection connection)
    {
        if (connection is not SqliteConnection)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();
    }
}
