using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Positions;
using WealthLedger.Domain.Ledger;
using WealthLedger.Infrastructure.Persistence;

namespace WealthLedger.Infrastructure.LocalData;

internal sealed class SqliteDatabaseVerifier
{
    private const string CoreLedgerMigration =
        "20260824074930_001_CoreLedger";
    private const string CommandReceiptMigration =
        "20260827072019_002_CommandReceipt";
    internal const string WorkspaceIdentityMigration =
        "20260903075104_005_WorkspaceIdentity";

    private static readonly IReadOnlyDictionary<string, string[]>
        RepresentativeTablesByIntroducingMigration =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [CoreLedgerMigration] =
                [
                    "LedgerTransaction",
                    "TransactionEntry",
                    "AssetLot",
                    "LotEntryAllocation"
                ],
                [CommandReceiptMigration] = ["CommandReceipt"],
                [WorkspaceIdentityMigration] = ["WorkspaceIdentity"]
            };

    internal async Task<LocalDataOperationResult<SqliteDatabaseVerification>>
        VerifyAsync(
            string databasePath,
            CancellationToken cancellationToken = default)
    {
        if (!File.Exists(databasePath))
        {
            return LocalDataOperationResult<SqliteDatabaseVerification>.Failed(
                LocalDataFailureCategory.NotFound,
                "The database file does not exist.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var connection =
                SqliteLocalDataConnectionFactory.CreateConnection(
                    databasePath,
                    SqliteOpenMode.ReadOnly);
            await connection.OpenAsync(cancellationToken);

            if (!await IntegrityCheckPassesAsync(
                    connection,
                    cancellationToken))
            {
                return LocalDataOperationResult<SqliteDatabaseVerification>.Failed(
                    LocalDataFailureCategory.IntegrityFailure,
                    "SQLite integrity validation failed.");
            }

            if (!await ForeignKeyCheckPassesAsync(
                    connection,
                    cancellationToken))
            {
                return LocalDataOperationResult<SqliteDatabaseVerification>.Failed(
                    LocalDataFailureCategory.IntegrityFailure,
                    "SQLite foreign-key validation failed.");
            }

            var appliedMigrations = await ReadAppliedMigrationsAsync(
                connection,
                cancellationToken);

            await using var context =
                SqliteLocalDataConnectionFactory.CreateContext(
                    databasePath,
                    SqliteOpenMode.ReadOnly);
            var knownMigrations = context.Database
                .GetMigrations()
                .ToArray();
            var compatibility = DetermineCompatibility(
                appliedMigrations,
                knownMigrations);
            var pendingMigrations = compatibility
                is LocalDatabaseCompatibility.Compatible
                or LocalDatabaseCompatibility.MigrationRequired
                    ? knownMigrations.Skip(appliedMigrations.Count).ToArray()
                    : [];

            if (compatibility == LocalDatabaseCompatibility.Uninitialized)
            {
                return LocalDataOperationResult<SqliteDatabaseVerification>.Failed(
                    LocalDataFailureCategory.DatabaseNotReady,
                    "The database has not been initialized by WealthLedger.");
            }

            if (compatibility == LocalDatabaseCompatibility.Incompatible)
            {
                return LocalDataOperationResult<SqliteDatabaseVerification>.Success(
                    new SqliteDatabaseVerification(
                        Path.GetFullPath(databasePath),
                        appliedMigrations,
                        pendingMigrations,
                        compatibility,
                        LocalDataIntegrityStatus.Passed,
                        RepresentativeFingerprint: string.Empty,
                        WorkspaceId: null));
            }

            await RunBoundedTableChecksAsync(
                connection,
                appliedMigrations,
                cancellationToken);
            var workspaceId = await ReadWorkspaceIdentityAsync(
                connection,
                appliedMigrations,
                cancellationToken);

            if (workspaceId is null
                && appliedMigrations.Contains(
                    WorkspaceIdentityMigration,
                    StringComparer.Ordinal))
            {
                return LocalDataOperationResult<SqliteDatabaseVerification>.Failed(
                    LocalDataFailureCategory.IntegrityFailure,
                    "The database is missing its required workspace identity.");
            }

            var representativeFingerprint =
                await RunRepresentativeApplicationQueriesAsync(
                    context,
                    cancellationToken);

            return LocalDataOperationResult<SqliteDatabaseVerification>.Success(
                new SqliteDatabaseVerification(
                    Path.GetFullPath(databasePath),
                    appliedMigrations,
                    pendingMigrations,
                    compatibility,
                    LocalDataIntegrityStatus.Passed,
                    representativeFingerprint,
                    workspaceId));
        }
        catch (OperationCanceledException)
        {
            return LocalDataOperationResult<SqliteDatabaseVerification>.Failed(
                LocalDataFailureCategory.Cancelled,
                "Database verification was cancelled.");
        }
        catch (SqliteException exception)
            when (exception.SqliteErrorCode is 11 or 26)
        {
            return LocalDataOperationResult<SqliteDatabaseVerification>.Failed(
                LocalDataFailureCategory.IntegrityFailure,
                "The database is corrupt or is not a supported SQLite file.");
        }
        catch (Exception exception)
            when (exception is SqliteException
                  or InvalidOperationException
                  or FormatException)
        {
            return LocalDataOperationResult<SqliteDatabaseVerification>.Failed(
                LocalDataFailureCategory.DatabaseNotReady,
                "The database schema is incomplete or incompatible.");
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or System.Security.SecurityException)
        {
            return LocalDataOperationResult<SqliteDatabaseVerification>.Failed(
                LocalDataFailureCategory.IoFailure,
                "The database could not be read for verification.");
        }
    }

    private static async Task<bool> IntegrityCheckPassesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        var sawResult = false;

        while (await reader.ReadAsync(cancellationToken))
        {
            sawResult = true;

            if (!string.Equals(
                    reader.GetString(0),
                    "ok",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return sawResult;
    }

    private static async Task<bool> ForeignKeyCheckPassesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);

        return !await reader.ReadAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> ReadAppliedMigrationsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;";
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        var migrations = new List<string>();

        while (await reader.ReadAsync(cancellationToken))
        {
            migrations.Add(reader.GetString(0));
        }

        return migrations;
    }

    private static LocalDatabaseCompatibility DetermineCompatibility(
        IReadOnlyList<string> appliedMigrations,
        IReadOnlyList<string> knownMigrations)
    {
        if (appliedMigrations.Count == 0)
        {
            return LocalDatabaseCompatibility.Uninitialized;
        }

        if (appliedMigrations.Count > knownMigrations.Count)
        {
            return LocalDatabaseCompatibility.Incompatible;
        }

        for (var index = 0; index < appliedMigrations.Count; index++)
        {
            if (!string.Equals(
                    appliedMigrations[index],
                    knownMigrations[index],
                    StringComparison.Ordinal))
            {
                return LocalDatabaseCompatibility.Incompatible;
            }
        }

        return appliedMigrations.Count == knownMigrations.Count
            ? LocalDatabaseCompatibility.Compatible
            : LocalDatabaseCompatibility.MigrationRequired;
    }

    private static async Task RunBoundedTableChecksAsync(
        SqliteConnection connection,
        IReadOnlyList<string> appliedMigrations,
        CancellationToken cancellationToken)
    {
        foreach (var migration in appliedMigrations)
        {
            if (!RepresentativeTablesByIntroducingMigration.TryGetValue(
                    migration,
                    out var representativeTables))
            {
                continue;
            }

            foreach (var table in representativeTables)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"SELECT 1 FROM \"{table}\" LIMIT 1;";
                _ = await command.ExecuteScalarAsync(cancellationToken);
            }
        }
    }

    /*
     * The workspace identity is operational lineage, not a ledger fact, so it
     * is read with a bounded direct query in the same way as the migration
     * history rather than through the EF model. A database that predates the
     * introducing migration simply has no identity; that is an explicit
     * unknown, not a failure.
     */
    private static async Task<string?> ReadWorkspaceIdentityAsync(
        SqliteConnection connection,
        IReadOnlyList<string> appliedMigrations,
        CancellationToken cancellationToken)
    {
        if (!appliedMigrations.Contains(
                WorkspaceIdentityMigration,
                StringComparer.Ordinal))
        {
            return null;
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT WorkspaceId FROM WorkspaceIdentity WHERE Id = 1;";
        var value = await command.ExecuteScalarAsync(cancellationToken);

        return value is string text && IsWellFormedWorkspaceId(text)
            ? text
            : null;
    }

    internal static bool IsWellFormedWorkspaceId(string? value)
        => value is not null
           && value.Length == 36
           && value.Equals(value.ToLowerInvariant(), StringComparison.Ordinal)
           && Guid.TryParseExact(value, "D", out var parsed)
           && parsed != Guid.Empty;

    private static async Task<string> RunRepresentativeApplicationQueriesAsync(
        WealthLedgerDbContext context,
        CancellationToken cancellationToken)
    {
        var representativeScope = await (
                from transaction in context.LedgerTransactions.AsNoTracking()
                join entry in context.TransactionEntries.AsNoTracking()
                    on transaction.Id equals entry.TransactionId
                where transaction.Status == TransactionStatus.Posted
                orderby transaction.CreatedAtUtc, transaction.Id, entry.EntrySequence
                select new
                {
                    TransactionId = transaction.Id,
                    transaction.HouseholdId,
                    entry.PortfolioId,
                    entry.AccountId,
                    entry.AssetId
                })
            .FirstOrDefaultAsync(cancellationToken);

        if (representativeScope is null)
        {
            return Convert.ToHexString(
                SHA256.HashData(Array.Empty<byte>()));
        }

        var readStore = new EfCoreLedgerTransactionReadStore(context);
        var detail = await readStore.FindByIdAsync(
            representativeScope.TransactionId,
            cancellationToken);

        if (detail is null)
        {
            throw new InvalidOperationException(
                "Representative transaction readback failed.");
        }

        var positionUseCase = new GetPositionUseCase(
            new EfCorePostedEntrySource(context),
            new EfCoreNavigationScopeReadStore(context));
        var position = await positionUseCase.ExecuteAsync(
            new GetPositionQuery(
                representativeScope.HouseholdId,
                representativeScope.PortfolioId,
                representativeScope.AccountId,
                representativeScope.AssetId),
            cancellationToken);
        var canonical = string.Join(
            '|',
            detail.TransactionId.ToString("D"),
            detail.Type,
            detail.Status,
            detail.Entries.Count,
            detail.LotAllocations.Count,
            position.Quantity.RawE8,
            position.SourceEntryCount);

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

internal sealed record SqliteDatabaseVerification(
    string DatabasePath,
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyList<string> PendingMigrations,
    LocalDatabaseCompatibility Compatibility,
    LocalDataIntegrityStatus IntegrityStatus,
    string RepresentativeFingerprint,
    string? WorkspaceId)
{
    internal string? LatestMigration => AppliedMigrations.LastOrDefault();
}
