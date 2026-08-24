using Microsoft.EntityFrameworkCore;
using WealthLedger.Infrastructure.Persistence;

namespace WealthLedger.Api.Startup;

internal static class DatabaseInitialization
{
    internal static async Task ApplyDatabaseMigrationsIfEnabledAsync(
        this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>(
                "Database:ApplyMigrationsOnStartup"))
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider
            .GetRequiredService<WealthLedgerDbContext>();

        await context.Database.MigrateAsync();
    }
}
