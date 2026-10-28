using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.Evidence;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Evidence;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class EvidenceObservationPersistenceTests
{
    [Fact]
    public async Task Observations_RoundTripAfterRestartWithExactFactsAndLabels()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        var evidenceId = await RecordEvidenceAsync(database, "observation-round-trip");

        await using (var context = database.CreateContext())
        {
            var result = await EvidenceTestData.ObservationUseCase(context, EvidenceTestData.Now.AddHours(3))
                .ExecuteAsync(new RecordEvidenceObservationsCommand(
                    CoreLedgerTestData.HouseholdId,
                    evidenceId,
                    [
                        new EvidenceObservationInput(
                            CoreLedgerTestData.PortfolioId,
                            CoreLedgerTestData.AccountId,
                            CoreLedgerTestData.CashAssetId,
                            -9_223_372_036_854_775_807,
                            9_223_372_036_854_775_807),
                        new EvidenceObservationInput(
                            CoreLedgerTestData.PortfolioId,
                            CoreLedgerTestData.DestinationAccountId,
                            CoreLedgerTestData.GoldAssetId,
                            1_250_000_000,
                            0,
                            3)
                    ]));

            Assert.Equal(2, result.AddedCount);
        }

        await using (var restarted = database.CreateContext())
        {
            var list = await EvidenceTestData.ListObservationsUseCase(restarted)
                .ExecuteAsync(new ListEvidenceObservationsQuery(
                    CoreLedgerTestData.HouseholdId,
                    evidenceId));

            Assert.Equal(EvidenceTestData.EffectiveDate, list.EffectiveDate);
            Assert.Null(list.SupersededByEvidenceId);
            Assert.Equal(2, list.Items.Count);

            var cash = list.Items.Single(x => x.Scope.AssetId == CoreLedgerTestData.CashAssetId);
            Assert.Equal(-9_223_372_036_854_775_807, cash.ObservedQuantityRawE8);
            Assert.Equal(9_223_372_036_854_775_807, cash.ToleranceRawE8);
            Assert.Null(cash.ObservedPieceCount);
            Assert.Equal("CORE", cash.Scope.PortfolioCode);
            Assert.Equal("PRIMARY", cash.Scope.AccountCode);
            Assert.Equal(CoreLedgerTestData.InstitutionId, cash.Scope.InstitutionId);
            Assert.Equal(AssetType.Cash, cash.Scope.AssetType);
            Assert.Equal(EvidenceTestData.Now.AddHours(3), cash.RecordedAtUtc);

            var gold = list.Items.Single(x => x.Scope.AssetId == CoreLedgerTestData.GoldAssetId);
            Assert.Equal(1_250_000_000, gold.ObservedQuantityRawE8);
            Assert.Equal(3, gold.ObservedPieceCount);
            Assert.Equal(AssetType.PhysicalGold, gold.Scope.AssetType);
        }

        Assert.Equal(
            "-9223372036854775807|integer|2026-10-01T12:00:00.0000000Z",
            Convert.ToString(await database.ExecuteScalarAsync(
                """
                SELECT ObservedQuantityE8 || '|' || typeof(ToleranceE8) || '|' || RecordedAtUtc
                FROM EvidenceObservation
                WHERE AssetId = $asset;
                """,
                new SqliteParameter("$asset", CoreLedgerTestData.CashAssetId.ToString("D")))));
    }

    [Fact]
    public async Task Observations_RepeatAddsNothingAndAContradictionChangesNothing()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        var evidenceId = await RecordEvidenceAsync(database, "observation-repeat");
        var cash = CashInput(100);

        await AddAsync(database, evidenceId, cash);
        var replay = await AddAsync(database, evidenceId, cash, CashInput(200, CoreLedgerTestData.DestinationAccountId));

        await using (var context = database.CreateContext())
        {
            var conflict = await Assert.ThrowsAsync<EvidenceException>(
                () => EvidenceTestData.ObservationUseCase(context).ExecuteAsync(
                    new RecordEvidenceObservationsCommand(
                        CoreLedgerTestData.HouseholdId,
                        evidenceId,
                        [CashInput(300, CoreLedgerTestData.DestinationAccountId, CoreLedgerTestData.FundAssetId), CashInput(101)])));

            Assert.Equal(EvidenceErrorCodes.ObservationConflict, conflict.ErrorCode);
        }

        Assert.Equal((1, 1), (replay.AddedCount, replay.AlreadyRecordedCount));
        Assert.Equal(2L, await CountObservationsAsync(database));
    }

    [Fact]
    public async Task ConcurrentContradictoryObservations_KeepExactlyOne()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        var evidenceId = await RecordEvidenceAsync(database, "observation-race");
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        using var start = new SemaphoreSlim(0, 2);

        async Task<EvidenceException?> AddAsync(WealthLedgerDbContext context, long observed)
        {
            await start.WaitAsync();

            try
            {
                await EvidenceTestData.ObservationUseCase(context).ExecuteAsync(
                    new RecordEvidenceObservationsCommand(
                        CoreLedgerTestData.HouseholdId,
                        evidenceId,
                        [CashInput(observed)]));
                return null;
            }
            catch (EvidenceException exception)
            {
                return exception;
            }
        }

        var first = Task.Run(() => AddAsync(firstContext, 100));
        var second = Task.Run(() => AddAsync(secondContext, 200));
        start.Release(2);
        var results = await Task.WhenAll(first, second);

        var failure = Assert.Single(results, result => result is not null);
        Assert.Equal(EvidenceErrorCodes.ObservationConflict, failure!.ErrorCode);
        Assert.Equal(1L, await CountObservationsAsync(database));
    }

    [Fact]
    public async Task Observations_RefuseSupersededEvidenceAndForeignScopesAgainstRealMasters()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        var original = await RecordEvidenceAsync(database, "observation-original");
        var accountBound = await RecordEvidenceAsync(
            database,
            "observation-account-bound",
            accountId: CoreLedgerTestData.AccountId);

        await using (var context = database.CreateContext())
        {
            await EvidenceTestData.RecordUseCase(context).ExecuteAsync(
                "observation-successor",
                EvidenceTestData.Command(supersedesEvidenceId: original));
        }

        var superseded = await AddFailsAsync(database, original, CashInput(1));
        var foreignAccount = await AddFailsAsync(
            database,
            accountBound,
            CashInput(1, CoreLedgerTestData.OtherAccountId));
        var outsideContext = await AddFailsAsync(
            database,
            accountBound,
            CashInput(1, CoreLedgerTestData.DestinationAccountId));
        var cashPieces = await AddFailsAsync(
            database,
            accountBound,
            CashInput(1) with { ObservedPieceCount = 1 });

        Assert.Equal(EvidenceErrorCodes.Superseded, superseded.ErrorCode);
        Assert.Equal(EvidenceErrorCodes.ObservationScopeNotFound, foreignAccount.ErrorCode);
        Assert.Equal(EvidenceErrorCodes.ObservationScopeMismatch, outsideContext.ErrorCode);
        Assert.Equal(EvidenceErrorCodes.ObservationInvalid, cashPieces.ErrorCode);
        Assert.Equal(0L, await CountObservationsAsync(database));
    }

    [Fact]
    public async Task Store_MapsGuardsThatFireAfterValidationToStableErrors()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        var evidenceId = await RecordEvidenceAsync(database, "observation-guards");

        // Observations built here skip the use case's validation, as a
        // concurrent or careless writer would.
        var foreignAccount = await StoreAddFailsAsync(
            database,
            Observation(evidenceId, CoreLedgerTestData.OtherAccountId, CoreLedgerTestData.CashAssetId));
        var cashPieces = await StoreAddFailsAsync(
            database,
            Observation(evidenceId, CoreLedgerTestData.AccountId, CoreLedgerTestData.CashAssetId, pieces: 1));

        Assert.Equal(EvidenceErrorCodes.ObservationScopeMismatch, foreignAccount.ErrorCode);
        Assert.Equal(EvidenceErrorCodes.ObservationInvalid, cashPieces.ErrorCode);

        var accountIds = new List<Guid>();

        await using (var context = database.CreateContext())
        {
            for (var index = 0; index < 25; index++)
            {
                var accountId = Guid.NewGuid();
                accountIds.Add(accountId);
                context.Accounts.Add(new AccountRow
                {
                    Id = accountId,
                    HouseholdId = CoreLedgerTestData.HouseholdId,
                    InstitutionId = CoreLedgerTestData.InstitutionId,
                    Code = $"SYN_LIMIT_{index:D2}",
                    Name = $"Synthetic Limit Account {index:D2}",
                    Type = AccountType.Investment,
                    IsActive = true,
                    OpenedOn = new DateOnly(2026, 1, 1)
                });
            }

            await context.SaveChangesAsync();
        }

        var full = accountIds
            .SelectMany(accountId => new[]
            {
                CoreLedgerTestData.CashAssetId,
                CoreLedgerTestData.FundAssetId,
                CoreLedgerTestData.OtherFundAssetId,
                CoreLedgerTestData.GoldAssetId
            }.Select(assetId => Observation(evidenceId, accountId, assetId)))
            .ToArray();

        await using (var context = database.CreateContext())
        {
            Assert.Equal(100, await new EfCoreEvidenceStore(context).AddObservationsAsync(evidenceId, full));
        }

        var overLimit = await StoreAddFailsAsync(
            database,
            Observation(evidenceId, CoreLedgerTestData.AccountId, CoreLedgerTestData.CashAssetId));

        Assert.Equal(EvidenceErrorCodes.ObservationLimitExceeded, overLimit.ErrorCode);
        Assert.Equal(100L, await CountObservationsAsync(database));
    }

    [Fact]
    public async Task Observations_LeaveLedgerRowsUntouched()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        var ledgerBefore = await ReconciliationTestData.SnapshotLedgerAsync(database);
        var evidenceId = await RecordEvidenceAsync(database, "observation-ledger");

        await AddAsync(database, evidenceId, CashInput(10_000), CashInput(1, CoreLedgerTestData.DestinationAccountId));

        Assert.Equal(ledgerBefore, await ReconciliationTestData.SnapshotLedgerAsync(database));
    }

    private static EvidenceObservationInput CashInput(
        long observed,
        Guid? accountId = null,
        Guid? assetId = null)
        => new(
            CoreLedgerTestData.PortfolioId,
            accountId ?? CoreLedgerTestData.AccountId,
            assetId ?? CoreLedgerTestData.CashAssetId,
            observed);

    private static async Task<Guid> RecordEvidenceAsync(
        SqliteTestDatabase database,
        string idempotencyKey,
        Guid? accountId = null)
    {
        await using var context = database.CreateContext();

        return (await EvidenceTestData.RecordUseCase(context).ExecuteAsync(
            idempotencyKey,
            EvidenceTestData.Command(accountId: accountId))).EvidenceId;
    }

    private static async Task<RecordEvidenceObservationsResult> AddAsync(
        SqliteTestDatabase database,
        Guid evidenceId,
        params EvidenceObservationInput[] observations)
    {
        await using var context = database.CreateContext();

        return await EvidenceTestData.ObservationUseCase(context).ExecuteAsync(
            new RecordEvidenceObservationsCommand(
                CoreLedgerTestData.HouseholdId,
                evidenceId,
                observations));
    }

    private static async Task<EvidenceException> AddFailsAsync(
        SqliteTestDatabase database,
        Guid evidenceId,
        EvidenceObservationInput observation)
    {
        await using var context = database.CreateContext();

        return await Assert.ThrowsAsync<EvidenceException>(
            () => EvidenceTestData.ObservationUseCase(context).ExecuteAsync(
                new RecordEvidenceObservationsCommand(
                    CoreLedgerTestData.HouseholdId,
                    evidenceId,
                    [observation])));
    }

    private static EvidenceObservation Observation(
        Guid evidenceId,
        Guid accountId,
        Guid assetId,
        int? pieces = null)
        => EvidenceObservation.Create(
            Guid.NewGuid(),
            evidenceId,
            CoreLedgerTestData.PortfolioId,
            accountId,
            assetId,
            1,
            0,
            pieces,
            EvidenceTestData.Now);

    private static async Task<EvidenceException> StoreAddFailsAsync(
        SqliteTestDatabase database,
        EvidenceObservation observation)
    {
        await using var context = database.CreateContext();

        return await Assert.ThrowsAsync<EvidenceException>(
            () => new EfCoreEvidenceStore(context).AddObservationsAsync(
                observation.EvidenceId,
                [observation]));
    }

    private static async Task<long> CountObservationsAsync(SqliteTestDatabase database)
        => Convert.ToInt64(await database.ExecuteScalarAsync(
            "SELECT COUNT(*) FROM EvidenceObservation;"));
}
