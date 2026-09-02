using System.Text;
using Microsoft.Data.Sqlite;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.LocalData;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.ValueObjects;
using WealthLedger.Infrastructure.LocalData;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Tests.Persistence;

namespace WealthLedger.Infrastructure.Tests.LocalData;

public sealed class SqliteDatabaseVerifierTests
{
    [Fact]
    public async Task Verify_CurrentDatabase_RunsIntegrityMigrationAndReadChecks()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedPostedContributionAsync(database);
        var verifier = new SqliteDatabaseVerifier();

        var first = await verifier.VerifyAsync(database.DatabasePath);
        var restarted = await verifier.VerifyAsync(database.DatabasePath);

        Assert.True(first.Succeeded);
        Assert.Equal(
            LocalDatabaseCompatibility.Compatible,
            first.Value!.Compatibility);
        Assert.Equal(LocalDataIntegrityStatus.Passed, first.Value.IntegrityStatus);
        Assert.Equal(4, first.Value.AppliedMigrations.Count);
        Assert.Empty(first.Value.PendingMigrations);
        Assert.NotEmpty(first.Value.RepresentativeFingerprint);
        Assert.Equal(
            first.Value.RepresentativeFingerprint,
            restarted.Value!.RepresentativeFingerprint);
    }

    [Fact]
    public async Task Verify_OldSupportedSchema_ReportsMigrationRequired()
    {
        await using var database = await SqliteTestDatabase.CreateAsync(
            "20260827072019_002_CommandReceipt");
        var verifier = new SqliteDatabaseVerifier();

        var result = await verifier.VerifyAsync(database.DatabasePath);

        Assert.True(result.Succeeded);
        Assert.Equal(
            LocalDatabaseCompatibility.MigrationRequired,
            result.Value!.Compatibility);
        Assert.Equal(
            [
                "20260831113310_003_ReversalDependencySemantics",
                "20260902112549_004_LedgerNavigationQueries"
            ],
            result.Value.PendingMigrations);
    }

    [Fact]
    public async Task Verify_FutureMigrationHistory_IsStructurallyIncompatible()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await database.ExecuteNonQueryAsync(
            """
            INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
            VALUES ('99999999999999_999_FutureSchema', '99.0.0');
            """);
        var verifier = new SqliteDatabaseVerifier();

        var result = await verifier.VerifyAsync(database.DatabasePath);

        Assert.True(result.Succeeded);
        Assert.Equal(
            LocalDatabaseCompatibility.Incompatible,
            result.Value!.Compatibility);
        Assert.Empty(result.Value.RepresentativeFingerprint);
    }

    [Fact]
    public async Task Verify_ForeignKeyViolation_FailsIntegrityWithoutDetails()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await database.ExecuteNonQueryAsync(
            """
            PRAGMA foreign_keys = OFF;
            INSERT INTO Household (Id, Name, BaseCurrencyCode, CreatedAtUtc)
            VALUES (
                '70000000-0000-0000-0000-000000000099',
                'Synthetic Household',
                'ZZZ',
                '2026-09-01T08:00:00.0000000Z');
            """);
        var verifier = new SqliteDatabaseVerifier();

        var result = await verifier.VerifyAsync(database.DatabasePath);

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.IntegrityFailure,
            result.Failure!.Category);
        Assert.DoesNotContain("Household", result.Failure.Message);
        Assert.DoesNotContain("ZZZ", result.Failure.Message);
    }

    [Fact]
    public async Task Verify_TruncatedFile_ReturnsSanitizedIntegrityFailure()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "WealthLedger.Infrastructure.Tests",
            nameof(SqliteDatabaseVerifierTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "truncated.db");
        await File.WriteAllBytesAsync(
            databasePath,
            Encoding.UTF8.GetBytes("not a sqlite database"));

        try
        {
            var result = await new SqliteDatabaseVerifier().VerifyAsync(
                databasePath);

            Assert.False(result.Succeeded);
            Assert.Equal(
                LocalDataFailureCategory.IntegrityFailure,
                result.Failure!.Category);
            Assert.DoesNotContain(databasePath, result.Failure.Message);
            Assert.DoesNotContain("SQLite Error", result.Failure.Message);
        }
        finally
        {
            File.Delete(databasePath);
            Directory.Delete(directory);
        }
    }

    private static async Task SeedPostedContributionAsync(
        SqliteTestDatabase database)
    {
        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
        }

        await using var writeContext = database.CreateContext();
        var useCase = new RecordContributionUseCase(
            new EfCoreLedgerReferenceData(writeContext),
            new EfCoreLedgerPostingStore(writeContext),
            new FixedTimeProvider(
                new DateTimeOffset(
                    2026,
                    9,
                    1,
                    8,
                    0,
                    0,
                    TimeSpan.Zero)));

        _ = await useCase.ExecuteAsync(
            "synthetic-verifier-contribution",
            new RecordContributionCommand(
                CoreLedgerTestData.HouseholdId,
                CoreLedgerTestData.PortfolioId,
                CoreLedgerTestData.AccountId,
                CoreLedgerTestData.CashAssetId,
                Money.FromMinorUnits(12_345, CurrencyCode.TRY),
                CashFlowCategory.Other,
                CoreLedgerTestData.ExecutionDate));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        internal FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
