using Microsoft.Data.Sqlite;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Tests.Persistence;

/// <summary>
/// Proves the migration 010 observation and reconciliation guards without the
/// Application layer.
/// </summary>
public sealed class ReconciliationDirectSqlGuardTests
{
    private const string InsertRecordSql =
        """
        INSERT INTO EvidenceRecord (
            Id, HouseholdId, SourceKindCode, EffectiveDate, InstitutionId,
            AccountId, Label, Reference, Note, SupersedesEvidenceId, CreatedAtUtc)
        VALUES (
            $id, $household, 'STATEMENT', '2026-09-30', $institution,
            $account, 'Synthetic direct statement', NULL, NULL, $supersedes,
            '2026-10-01T09:00:00.0000000Z');
        """;

    private const string InsertObservationSql =
        """
        INSERT INTO EvidenceObservation (
            Id, EvidenceId, PortfolioId, AccountId, AssetId, ObservedQuantityE8,
            ToleranceE8, ObservedPieceCount, RecordedAtUtc)
        VALUES (
            $id, $evidence, $portfolio, $account, $asset, $observed,
            $tolerance, $pieces, '2026-10-01T10:00:00.0000000Z');
        """;

    private const string InsertRunSql =
        """
        INSERT INTO ReconciliationRun (
            Id, HouseholdId, EvidenceId, EvidenceEffectiveDate, RunAtUtc,
            MethodCode, MethodVersion, ItemCount, IdempotencyKey)
        VALUES (
            $id, $household, $evidence, $date, '2026-10-01T11:00:00.0000000Z',
            $method, 1, $count, $key);
        """;

    private const string InsertItemSql =
        """
        INSERT INTO ReconciliationItem (
            Id, RunId, Sequence, ObservationId, PortfolioId, AccountId, AssetId,
            ObservedQuantityE8, LedgerQuantityE8, DeltaE8, ToleranceE8,
            ObservedPieceCount, LedgerPieceCount, PieceDelta, ComparisonStatusCode)
        VALUES (
            $id, $run, $sequence, $observation, $portfolio, $account, $asset,
            $observed, $ledger, $delta, $tolerance,
            $observedPieces, $ledgerPieces, $pieceDelta, $status);
        """;

    private const string InsertResolutionSql =
        """
        INSERT INTO ReconciliationItemResolution (
            ItemId, ResolutionCode, CorrectiveTransactionId, Explanation, ResolvedAtUtc)
        VALUES (
            $item, $code, $transaction, $explanation, '2026-10-01T12:00:00.0000000Z');
        """;

    [Fact]
    public async Task DirectSql_ObservationScopeStaysInsideEvidenceHouseholdAndContext()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        var evidenceId = await InsertRecordAsync(database);
        var accountBound = await InsertRecordAsync(database, account: CoreLedgerTestData.AccountId);
        var institutionBound = await InsertRecordAsync(
            database,
            institution: EvidenceTestData.SecondInstitutionId);

        await AssertRejectedAsync(
            database,
            "WL_M010_OBSERVATION_SCOPE_INVALID",
            InsertObservationSql,
            ObservationParameters(Guid.NewGuid(), evidenceId, account: CoreLedgerTestData.OtherAccountId));
        await AssertRejectedAsync(
            database,
            "WL_M010_OBSERVATION_SCOPE_INVALID",
            InsertObservationSql,
            ObservationParameters(Guid.NewGuid(), evidenceId, portfolio: CoreLedgerTestData.OtherPortfolioId));
        await AssertRejectedAsync(
            database,
            "WL_M010_OBSERVATION_SCOPE_INVALID",
            InsertObservationSql,
            ObservationParameters(
                Guid.NewGuid(),
                accountBound,
                account: CoreLedgerTestData.DestinationAccountId));
        await AssertRejectedAsync(
            database,
            "WL_M010_OBSERVATION_SCOPE_INVALID",
            InsertObservationSql,
            ObservationParameters(Guid.NewGuid(), institutionBound));
        await AssertRejectedAsync(
            database,
            "WL_M010_OBSERVATION_SCOPE_INVALID",
            InsertObservationSql,
            ObservationParameters(Guid.NewGuid(), Guid.NewGuid()));
        await AssertRejectedAsync(
            database,
            "FOREIGN KEY constraint failed",
            InsertObservationSql,
            ObservationParameters(Guid.NewGuid(), evidenceId, asset: Guid.NewGuid()));
        await AssertRejectedAsync(
            database,
            "CK_EvidenceObservation_Tolerance",
            InsertObservationSql,
            ObservationParameters(Guid.NewGuid(), evidenceId, tolerance: -1));

        await database.ExecuteNonQueryAsync(
            InsertObservationSql,
            ObservationParameters(Guid.NewGuid(), accountBound));
        await database.ExecuteNonQueryAsync(
            InsertObservationSql,
            ObservationParameters(Guid.NewGuid(), evidenceId, account: CoreLedgerTestData.DestinationAccountId));
        await AssertRejectedAsync(
            database,
            "UNIQUE constraint failed: EvidenceObservation.EvidenceId, EvidenceObservation.PortfolioId, EvidenceObservation.AccountId, EvidenceObservation.AssetId",
            InsertObservationSql,
            ObservationParameters(
                Guid.NewGuid(),
                evidenceId,
                account: CoreLedgerTestData.DestinationAccountId,
                observed: 2));
    }

    [Fact]
    public async Task DirectSql_PieceCountsAreObservedOnlyForPhysicalGold()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        var evidenceId = await InsertRecordAsync(database);

        await AssertRejectedAsync(
            database,
            "WL_M010_OBSERVATION_PIECES_INVALID",
            InsertObservationSql,
            ObservationParameters(Guid.NewGuid(), evidenceId, pieces: 1));
        await AssertRejectedAsync(
            database,
            "CK_EvidenceObservation_PieceCount",
            InsertObservationSql,
            ObservationParameters(
                Guid.NewGuid(),
                evidenceId,
                asset: CoreLedgerTestData.GoldAssetId,
                pieces: -1));

        Assert.Equal(
            1,
            await database.ExecuteNonQueryAsync(
                InsertObservationSql,
                ObservationParameters(
                    Guid.NewGuid(),
                    evidenceId,
                    asset: CoreLedgerTestData.GoldAssetId,
                    pieces: 0)));
    }

    [Fact]
    public async Task DirectSql_ObservationsAreCappedAndRefusedOnSupersededEvidence()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        var accountIds = await AddAccountsAsync(database, 25);
        var evidenceId = await InsertRecordAsync(database);
        var assetIds = new[]
        {
            CoreLedgerTestData.CashAssetId,
            CoreLedgerTestData.FundAssetId,
            CoreLedgerTestData.OtherFundAssetId,
            CoreLedgerTestData.GoldAssetId
        };

        foreach (var accountId in accountIds)
        {
            foreach (var assetId in assetIds)
            {
                await database.ExecuteNonQueryAsync(
                    InsertObservationSql,
                    ObservationParameters(Guid.NewGuid(), evidenceId, account: accountId, asset: assetId));
            }
        }

        await AssertRejectedAsync(
            database,
            "WL_M010_OBSERVATION_LIMIT",
            InsertObservationSql,
            ObservationParameters(Guid.NewGuid(), evidenceId));

        var supersededId = await InsertRecordAsync(database);
        await InsertRecordAsync(database, supersedes: supersededId);

        await AssertRejectedAsync(
            database,
            "WL_M010_EVIDENCE_SUPERSEDED",
            InsertObservationSql,
            ObservationParameters(Guid.NewGuid(), supersededId));
    }

    [Fact]
    public async Task DirectSql_RunCarriesItsEvidenceHouseholdAndDate()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        var evidenceId = await InsertRecordAsync(database);

        await AssertRejectedAsync(
            database,
            "WL_M010_RECONCILIATION_RUN_INVALID",
            InsertRunSql,
            RunParameters(Guid.NewGuid(), evidenceId, household: CoreLedgerTestData.OtherHouseholdId));
        await AssertRejectedAsync(
            database,
            "WL_M010_RECONCILIATION_RUN_INVALID",
            InsertRunSql,
            RunParameters(Guid.NewGuid(), evidenceId, date: "2026-09-29"));
        await AssertRejectedAsync(
            database,
            "CK_ReconciliationRun_Method",
            InsertRunSql,
            RunParameters(Guid.NewGuid(), evidenceId, method: "FUZZY"));
        await AssertRejectedAsync(
            database,
            "CK_ReconciliationRun_ItemCount",
            InsertRunSql,
            RunParameters(Guid.NewGuid(), evidenceId, count: 0));
        await AssertRejectedAsync(
            database,
            "CK_ReconciliationRun_ItemCount",
            InsertRunSql,
            RunParameters(Guid.NewGuid(), evidenceId, count: 101));
        await AssertRejectedAsync(
            database,
            "CK_ReconciliationRun_IdempotencyKey_Length",
            InsertRunSql,
            RunParameters(Guid.NewGuid(), evidenceId, key: string.Empty));

        await database.ExecuteNonQueryAsync(
            InsertRunSql,
            RunParameters(Guid.NewGuid(), evidenceId, key: "synthetic-run"));
        await AssertRejectedAsync(
            database,
            "UNIQUE constraint failed: ReconciliationRun.HouseholdId, ReconciliationRun.IdempotencyKey",
            InsertRunSql,
            RunParameters(Guid.NewGuid(), evidenceId, key: "synthetic-run"));

        await InsertRecordAsync(database, supersedes: evidenceId);

        await AssertRejectedAsync(
            database,
            "WL_M010_EVIDENCE_SUPERSEDED",
            InsertRunSql,
            RunParameters(Guid.NewGuid(), evidenceId, key: "synthetic-after-supersession"));
    }

    [Fact]
    public async Task DirectSql_ItemRepeatsItsObservationAndFitsItsRun()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        var evidenceId = await InsertRecordAsync(database);
        var otherEvidenceId = await InsertRecordAsync(database);
        var observationId = Guid.NewGuid();
        var foreignObservationId = Guid.NewGuid();
        await database.ExecuteNonQueryAsync(
            InsertObservationSql,
            ObservationParameters(observationId, evidenceId, observed: 500, tolerance: 10));
        await database.ExecuteNonQueryAsync(
            InsertObservationSql,
            ObservationParameters(foreignObservationId, otherEvidenceId, observed: 500, tolerance: 10));
        var runId = Guid.NewGuid();
        await database.ExecuteNonQueryAsync(
            InsertRunSql,
            RunParameters(runId, evidenceId, count: 1));

        await AssertRejectedAsync(
            database,
            "WL_M010_RECONCILIATION_ITEM_INVALID",
            InsertItemSql,
            ItemParameters(runId, foreignObservationId, observed: 500, ledger: 400, tolerance: 10));
        await AssertRejectedAsync(
            database,
            "WL_M010_RECONCILIATION_ITEM_INVALID",
            InsertItemSql,
            ItemParameters(runId, observationId, observed: 501, ledger: 401, tolerance: 10));
        await AssertRejectedAsync(
            database,
            "WL_M010_RECONCILIATION_ITEM_INVALID",
            InsertItemSql,
            ItemParameters(runId, observationId, observed: 500, ledger: 400, tolerance: 200));
        await AssertRejectedAsync(
            database,
            "WL_M010_RECONCILIATION_ITEM_INVALID",
            InsertItemSql,
            ItemParameters(
                runId,
                observationId,
                observed: 500,
                ledger: 400,
                tolerance: 10,
                account: CoreLedgerTestData.DestinationAccountId));
        await AssertRejectedAsync(
            database,
            "WL_M010_RECONCILIATION_ITEM_INVALID",
            InsertItemSql,
            ItemParameters(runId, observationId, observed: 500, ledger: 400, tolerance: 10, observedPieces: 1, ledgerPieces: 1));
        await AssertRejectedAsync(
            database,
            "WL_M010_RECONCILIATION_ITEM_INVALID",
            InsertItemSql,
            ItemParameters(runId, observationId, observed: 500, ledger: 400, tolerance: 10, sequence: 2));
        await AssertRejectedAsync(
            database,
            "CK_ReconciliationItem_Delta",
            InsertItemSql,
            ItemParameters(runId, observationId, observed: 500, ledger: 400, tolerance: 10, delta: 99));
        await AssertRejectedAsync(
            database,
            "CK_ReconciliationItem_ComparisonStatus",
            InsertItemSql,
            ItemParameters(runId, observationId, observed: 500, ledger: 400, tolerance: 10, status: "MATCHED"));
        await AssertRejectedAsync(
            database,
            "CK_ReconciliationItem_ComparisonStatus",
            InsertItemSql,
            ItemParameters(runId, observationId, observed: 500, ledger: 495, tolerance: 10, status: "OUT_OF_TOLERANCE"));
        await AssertRejectedAsync(
            database,
            "CK_ReconciliationItem_ComparisonStatus",
            InsertItemSql,
            ItemParameters(runId, observationId, observed: 500, ledger: 400, tolerance: 10, status: "STALE"));

        await database.ExecuteNonQueryAsync(
            InsertItemSql,
            ItemParameters(runId, observationId, observed: 500, ledger: 400, tolerance: 10));

        // The run recorded one item, so no item can be added to it later.
        await AssertRejectedAsync(
            database,
            "WL_M010_RECONCILIATION_ITEM_INVALID",
            InsertItemSql,
            ItemParameters(runId, observationId, observed: 500, ledger: 400, tolerance: 10));
    }

    [Fact]
    public async Task DirectSql_ItemPiecesAreAllOrNothingAndExact()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        var evidenceId = await InsertRecordAsync(database);
        var observationId = Guid.NewGuid();
        await database.ExecuteNonQueryAsync(
            InsertObservationSql,
            ObservationParameters(
                observationId,
                evidenceId,
                asset: CoreLedgerTestData.GoldAssetId,
                observed: 300,
                pieces: 2));
        var runId = Guid.NewGuid();
        await database.ExecuteNonQueryAsync(
            InsertRunSql,
            RunParameters(runId, evidenceId, count: 1));

        await AssertRejectedAsync(
            database,
            "CK_ReconciliationItem_Pieces",
            InsertItemSql,
            ItemParameters(
                runId,
                observationId,
                observed: 300,
                ledger: 300,
                tolerance: 0,
                asset: CoreLedgerTestData.GoldAssetId,
                observedPieces: 2,
                ledgerPieces: null));
        await AssertRejectedAsync(
            database,
            "CK_ReconciliationItem_Pieces",
            InsertItemSql,
            ItemParameters(
                runId,
                observationId,
                observed: 300,
                ledger: 300,
                tolerance: 0,
                asset: CoreLedgerTestData.GoldAssetId,
                observedPieces: 2,
                ledgerPieces: 3,
                pieceDelta: 0,
                status: "MATCHED"));
        await AssertRejectedAsync(
            database,
            "CK_ReconciliationItem_ComparisonStatus",
            InsertItemSql,
            ItemParameters(
                runId,
                observationId,
                observed: 300,
                ledger: 300,
                tolerance: 0,
                asset: CoreLedgerTestData.GoldAssetId,
                observedPieces: 2,
                ledgerPieces: 3,
                status: "MATCHED"));

        Assert.Equal(
            1,
            await database.ExecuteNonQueryAsync(
                InsertItemSql,
                ItemParameters(
                    runId,
                    observationId,
                    observed: 300,
                    ledger: 300,
                    tolerance: 0,
                    asset: CoreLedgerTestData.GoldAssetId,
                    observedPieces: 2,
                    ledgerPieces: 3)));
    }

    [Fact]
    public async Task DirectSql_ResolutionNeedsAnOutOfToleranceItemAndAPostedSameHouseholdCorrection()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var fixture = await EvidenceTestData.SeedAsync(database.CreateContext);
        var (matchedItemId, mismatchedItemId, secondMismatchedItemId) =
            await InsertRunWithItemsAsync(database);

        await AssertRejectedAsync(
            database,
            "WL_M010_RECONCILIATION_RESOLUTION_INVALID",
            InsertResolutionSql,
            ResolutionParameters(matchedItemId, explanation: "Synthetic reason"));
        await AssertRejectedAsync(
            database,
            "WL_M010_RECONCILIATION_RESOLUTION_INVALID",
            InsertResolutionSql,
            ResolutionParameters(Guid.NewGuid(), explanation: "Synthetic reason"));
        await AssertRejectedAsync(
            database,
            "WL_M010_RECONCILIATION_CORRECTION_INVALID",
            InsertResolutionSql,
            ResolutionParameters(mismatchedItemId, transaction: fixture.DraftId));
        await AssertRejectedAsync(
            database,
            "WL_M010_RECONCILIATION_CORRECTION_INVALID",
            InsertResolutionSql,
            ResolutionParameters(mismatchedItemId, transaction: fixture.OtherHouseholdPostedId));
        await AssertRejectedAsync(
            database,
            "CK_ReconciliationItemResolution_Shape",
            InsertResolutionSql,
            ResolutionParameters(mismatchedItemId, code: "CORRECTED_BY_TRANSACTION"));
        await AssertRejectedAsync(
            database,
            "CK_ReconciliationItemResolution_Shape",
            InsertResolutionSql,
            ResolutionParameters(mismatchedItemId, code: "DOCUMENTED_EXCEPTION"));
        await AssertRejectedAsync(
            database,
            "CK_ReconciliationItemResolution_Shape",
            InsertResolutionSql,
            ResolutionParameters(
                mismatchedItemId,
                code: "DOCUMENTED_EXCEPTION",
                transaction: fixture.FirstPostedId,
                explanation: "Synthetic reason"));
        await AssertRejectedAsync(
            database,
            "CK_ReconciliationItemResolution_Shape",
            InsertResolutionSql,
            ResolutionParameters(mismatchedItemId, explanation: string.Empty));
        await AssertRejectedAsync(
            database,
            "CK_ReconciliationItemResolution_Shape",
            InsertResolutionSql,
            ResolutionParameters(mismatchedItemId, code: "ACCEPTED", explanation: "Synthetic reason"));

        await database.ExecuteNonQueryAsync(
            InsertResolutionSql,
            ResolutionParameters(mismatchedItemId, transaction: fixture.FirstPostedId));
        await database.ExecuteNonQueryAsync(
            InsertResolutionSql,
            ResolutionParameters(secondMismatchedItemId, explanation: "Synthetic reason"));

        await AssertRejectedAsync(
            database,
            "UNIQUE constraint failed: ReconciliationItemResolution.ItemId",
            InsertResolutionSql,
            ResolutionParameters(mismatchedItemId, explanation: "Synthetic second thought"));
    }

    [Fact]
    public async Task DirectSql_ObservationAndReconciliationRowsAreInsertOnly()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var fixture = await EvidenceTestData.SeedAsync(database.CreateContext);
        var (_, mismatchedItemId, _) = await InsertRunWithItemsAsync(database);
        await database.ExecuteNonQueryAsync(
            InsertResolutionSql,
            ResolutionParameters(mismatchedItemId, transaction: fixture.FirstPostedId));

        foreach (var (table, message) in new[]
                 {
                     ("EvidenceObservation", "WL_M010_EVIDENCE_IMMUTABLE"),
                     ("ReconciliationRun", "WL_M010_RECONCILIATION_IMMUTABLE"),
                     ("ReconciliationItem", "WL_M010_RECONCILIATION_IMMUTABLE"),
                     ("ReconciliationItemResolution", "WL_M010_RECONCILIATION_IMMUTABLE")
                 })
        {
            var column = table == "ReconciliationItemResolution" ? "ResolvedAtUtc" : "Id";
            var value = table == "ReconciliationItemResolution"
                ? "'2026-10-02T00:00:00.0000000Z'"
                : $"'{Guid.NewGuid():D}'";

            await AssertRejectedAsync(
                database,
                message,
                $"UPDATE {table} SET {column} = {value};");
            await AssertRejectedAsync(
                database,
                message,
                $"DELETE FROM {table};");
        }
    }

    [Fact]
    public async Task DirectSql_ReferencedRowsCannotBeDeleted()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        var accountIds = await AddAccountsAsync(database, 1);
        var evidenceId = await InsertRecordAsync(database);
        await database.ExecuteNonQueryAsync(
            InsertObservationSql,
            ObservationParameters(Guid.NewGuid(), evidenceId, account: accountIds[0]));

        await AssertRejectedAsync(
            database,
            "FOREIGN KEY constraint failed",
            "DELETE FROM Account WHERE Id = $id;",
            new SqliteParameter("$id", accountIds[0].ToString("D")));
    }

    private static async Task<(Guid Matched, Guid Mismatched, Guid SecondMismatched)>
        InsertRunWithItemsAsync(SqliteTestDatabase database)
    {
        var evidenceId = await InsertRecordAsync(database);
        var matchedObservation = Guid.NewGuid();
        var mismatchedObservation = Guid.NewGuid();
        var secondMismatchedObservation = Guid.NewGuid();
        await database.ExecuteNonQueryAsync(
            InsertObservationSql,
            ObservationParameters(matchedObservation, evidenceId, observed: 100));
        await database.ExecuteNonQueryAsync(
            InsertObservationSql,
            ObservationParameters(
                mismatchedObservation,
                evidenceId,
                asset: CoreLedgerTestData.FundAssetId,
                observed: 500,
                tolerance: 10));
        await database.ExecuteNonQueryAsync(
            InsertObservationSql,
            ObservationParameters(
                secondMismatchedObservation,
                evidenceId,
                account: CoreLedgerTestData.DestinationAccountId,
                observed: 7));
        var runId = Guid.NewGuid();
        await database.ExecuteNonQueryAsync(
            InsertRunSql,
            RunParameters(runId, evidenceId, count: 3));

        var matchedItem = Guid.NewGuid();
        var mismatchedItem = Guid.NewGuid();
        var secondMismatchedItem = Guid.NewGuid();
        await database.ExecuteNonQueryAsync(
            InsertItemSql,
            ItemParameters(runId, matchedObservation, observed: 100, ledger: 100, tolerance: 0, id: matchedItem));
        await database.ExecuteNonQueryAsync(
            InsertItemSql,
            ItemParameters(
                runId,
                mismatchedObservation,
                observed: 500,
                ledger: 400,
                tolerance: 10,
                asset: CoreLedgerTestData.FundAssetId,
                sequence: 2,
                id: mismatchedItem));
        await database.ExecuteNonQueryAsync(
            InsertItemSql,
            ItemParameters(
                runId,
                secondMismatchedObservation,
                observed: 7,
                ledger: 0,
                tolerance: 0,
                account: CoreLedgerTestData.DestinationAccountId,
                sequence: 3,
                id: secondMismatchedItem));

        return (matchedItem, mismatchedItem, secondMismatchedItem);
    }

    private static async Task<Guid> InsertRecordAsync(
        SqliteTestDatabase database,
        Guid? institution = null,
        Guid? account = null,
        Guid? supersedes = null)
    {
        var id = Guid.NewGuid();

        await database.ExecuteNonQueryAsync(
            InsertRecordSql,
            new SqliteParameter("$id", id.ToString("D")),
            new SqliteParameter("$household", CoreLedgerTestData.HouseholdId.ToString("D")),
            new SqliteParameter("$institution", (object?)institution?.ToString("D") ?? DBNull.Value),
            new SqliteParameter("$account", (object?)account?.ToString("D") ?? DBNull.Value),
            new SqliteParameter("$supersedes", (object?)supersedes?.ToString("D") ?? DBNull.Value));

        return id;
    }

    private static async Task<Guid[]> AddAccountsAsync(
        SqliteTestDatabase database,
        int count)
    {
        var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();

        await using var context = database.CreateContext();

        for (var index = 0; index < ids.Length; index++)
        {
            context.Accounts.Add(new AccountRow
            {
                Id = ids[index],
                HouseholdId = CoreLedgerTestData.HouseholdId,
                InstitutionId = CoreLedgerTestData.InstitutionId,
                Code = $"SYN_{index:D3}",
                Name = $"Synthetic Account {index:D3}",
                Type = AccountType.Investment,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            });
        }

        await context.SaveChangesAsync();

        return ids;
    }

    private static async Task AssertRejectedAsync(
        SqliteTestDatabase database,
        string expectedFragment,
        string sql,
        params SqliteParameter[] parameters)
    {
        var exception = await Assert.ThrowsAsync<SqliteException>(
            () => database.ExecuteNonQueryAsync(sql, parameters));

        Assert.Contains(expectedFragment, exception.Message, StringComparison.Ordinal);
    }

    private static SqliteParameter[] ObservationParameters(
        Guid id,
        Guid evidenceId,
        Guid? portfolio = null,
        Guid? account = null,
        Guid? asset = null,
        long observed = 100,
        long tolerance = 0,
        int? pieces = null)
        =>
        [
            new("$id", id.ToString("D")),
            new("$evidence", evidenceId.ToString("D")),
            new("$portfolio", (portfolio ?? CoreLedgerTestData.PortfolioId).ToString("D")),
            new("$account", (account ?? CoreLedgerTestData.AccountId).ToString("D")),
            new("$asset", (asset ?? CoreLedgerTestData.CashAssetId).ToString("D")),
            new("$observed", observed),
            new("$tolerance", tolerance),
            new("$pieces", (object?)pieces ?? DBNull.Value)
        ];

    private static SqliteParameter[] RunParameters(
        Guid id,
        Guid evidenceId,
        Guid? household = null,
        string date = "2026-09-30",
        string method = "EXACT_SCOPE_QUANTITY_AS_OF",
        int count = 1,
        string? key = null)
        =>
        [
            new("$id", id.ToString("D")),
            new("$household", (household ?? CoreLedgerTestData.HouseholdId).ToString("D")),
            new("$evidence", evidenceId.ToString("D")),
            new("$date", date),
            new("$method", method),
            new("$count", count),
            new("$key", key ?? id.ToString("N"))
        ];

    private static SqliteParameter[] ItemParameters(
        Guid runId,
        Guid observationId,
        long observed,
        long ledger,
        long tolerance,
        Guid? account = null,
        Guid? asset = null,
        int sequence = 1,
        long? delta = null,
        int? observedPieces = null,
        int? ledgerPieces = null,
        int? pieceDelta = null,
        string? status = null,
        Guid? id = null)
    {
        var actualDelta = delta ?? observed - ledger;
        var actualPieceDelta = pieceDelta
                               ?? (observedPieces is int o && ledgerPieces is int l
                                   ? (int?)(o - l)
                                   : null);
        var matched = actualDelta >= -tolerance
                      && actualDelta <= tolerance
                      && actualPieceDelta is null or 0;

        return
        [
            new("$id", (id ?? Guid.NewGuid()).ToString("D")),
            new("$run", runId.ToString("D")),
            new("$sequence", sequence),
            new("$observation", observationId.ToString("D")),
            new("$portfolio", CoreLedgerTestData.PortfolioId.ToString("D")),
            new("$account", (account ?? CoreLedgerTestData.AccountId).ToString("D")),
            new("$asset", (asset ?? CoreLedgerTestData.CashAssetId).ToString("D")),
            new("$observed", observed),
            new("$ledger", ledger),
            new("$delta", actualDelta),
            new("$tolerance", tolerance),
            new("$observedPieces", (object?)observedPieces ?? DBNull.Value),
            new("$ledgerPieces", (object?)ledgerPieces ?? DBNull.Value),
            new("$pieceDelta", (object?)actualPieceDelta ?? DBNull.Value),
            new("$status", status ?? (matched ? "MATCHED" : "OUT_OF_TOLERANCE"))
        ];
    }

    private static SqliteParameter[] ResolutionParameters(
        Guid itemId,
        string? code = null,
        Guid? transaction = null,
        string? explanation = null)
        =>
        [
            new("$item", itemId.ToString("D")),
            new(
                "$code",
                code ?? (transaction is null ? "DOCUMENTED_EXCEPTION" : "CORRECTED_BY_TRANSACTION")),
            new("$transaction", (object?)transaction?.ToString("D") ?? DBNull.Value),
            new("$explanation", (object?)explanation ?? DBNull.Value)
        ];
}
