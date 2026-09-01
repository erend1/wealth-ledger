using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Tests.Persistence
{
    public sealed class ReversalDependencyTriggerTests
    {
        [Fact]
        public async Task
            DraftDependentReversal_DoesNotUnblockAcquisitionReversal()
        {
            await using var database =
                await SqliteTestDatabase.CreateAsync();

            var scenario =
                await SeedScenarioAsync(
                    database);

            await SeedDependentReversalAsync(
                database,
                scenario,
                TransactionStatus.Draft);

            var acquisitionReversalId =
                await SeedAcquisitionReversalDraftAsync(
                    database,
                    scenario);

            await using var context =
                database.CreateContext();

            var exception =
                await Assert.ThrowsAsync<
                    DbUpdateException>(
                    () =>
                        CoreLedgerTestData.PostAsync(
                            context,
                            acquisitionReversalId));

            Assert.Contains(
                "later posted lot allocations",
                exception.InnerException?.Message,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task
            CancelledDependentReversal_DoesNotUnblockAcquisitionReversal()
        {
            await using var database =
                await SqliteTestDatabase.CreateAsync();

            var scenario =
                await SeedScenarioAsync(
                    database);

            await SeedDependentReversalAsync(
                database,
                scenario,
                TransactionStatus.Cancelled);

            var acquisitionReversalId =
                await SeedAcquisitionReversalDraftAsync(
                    database,
                    scenario);

            await using var context =
                database.CreateContext();

            var exception =
                await Assert.ThrowsAsync<
                    DbUpdateException>(
                    () =>
                        CoreLedgerTestData.PostAsync(
                            context,
                            acquisitionReversalId));

            Assert.Contains(
                "later posted lot allocations",
                exception.InnerException?.Message,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task
            PostedDependentReversal_UnblocksAcquisitionReversal()
        {
            await using var database =
                await SqliteTestDatabase.CreateAsync();

            var scenario =
                await SeedScenarioAsync(
                    database);

            await SeedDependentReversalAsync(
                database,
                scenario,
                TransactionStatus.Posted);

            var acquisitionReversalId =
                await SeedAcquisitionReversalDraftAsync(
                    database,
                    scenario);

            await using (var context =
                         database.CreateContext())
            {
                await CoreLedgerTestData.PostAsync(
                    context,
                    acquisitionReversalId);
            }

            await using var verificationContext =
                database.CreateContext();

            var reversal =
                await verificationContext
                    .LedgerTransactions
                    .AsNoTracking()
                    .SingleAsync(
                        x =>
                            x.Id
                            == acquisitionReversalId);

            Assert.Equal(
                TransactionStatus.Posted,
                reversal.Status);

            var effectiveQuantity =
                await GetPostedLotQuantityAsync(
                    verificationContext,
                    scenario.LotId);

            Assert.Equal(
                0,
                effectiveQuantity);
        }

        [Fact]
        public async Task
            UnrelatedPositiveNetting_DoesNotUnblockAcquisitionReversal()
        {
            await using var database =
                await SqliteTestDatabase.CreateAsync();

            var scenario =
                await SeedScenarioAsync(
                    database);

            await SeedUnrelatedNegativeAdjustmentAsync(
                database,
                scenario.LotId);

            await using (var verificationContext =
                         database.CreateContext())
            {
                Assert.Equal(
                    100,
                    await GetPostedLotQuantityAsync(
                        verificationContext,
                        scenario.LotId));
            }

            var acquisitionReversalId =
                await SeedAcquisitionReversalDraftAsync(
                    database,
                    scenario);

            await using var context =
                database.CreateContext();

            var exception =
                await Assert.ThrowsAsync<
                    DbUpdateException>(
                    () =>
                        CoreLedgerTestData.PostAsync(
                            context,
                            acquisitionReversalId));

            Assert.Contains(
                "later posted lot allocations",
                exception.InnerException?.Message,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task
            DirectSql_CannotBypassOutstandingDependencyProtection()
        {
            await using var database =
                await SqliteTestDatabase.CreateAsync();

            var scenario =
                await SeedScenarioAsync(
                    database);

            var acquisitionReversalId =
                await SeedAcquisitionReversalDraftAsync(
                    database,
                    scenario);

            var exception =
                await Assert.ThrowsAsync<
                    SqliteException>(
                    () =>
                        database.ExecuteNonQueryAsync(
                            """
                        UPDATE LedgerTransaction
                        SET StatusCode = 'POSTED',
                            PostedAtUtc = $postedAtUtc
                        WHERE Id = $id;
                        """,

                            new SqliteParameter(
                                "$postedAtUtc",
                                CoreLedgerTestData
                                    .CreatedAtUtc
                                    .AddMinutes(10)),

                            new SqliteParameter(
                                "$id",
                                acquisitionReversalId
                                    .ToString("D"))));

            Assert.Contains(
                "later posted lot allocations",
                exception.Message,
                StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<Scenario>
            SeedScenarioAsync(
                SqliteTestDatabase database)
        {
            await using var context =
                database.CreateContext();

            await CoreLedgerTestData.SeedMasterDataAsync(
                context);

            var acquisitionTransactionId =
                Guid.NewGuid();

            var acquisitionEntryId =
                Guid.NewGuid();

            var lotId =
                Guid.NewGuid();

            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    acquisitionTransactionId,
                    TransactionType.OpeningBalance));

            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    acquisitionEntryId,
                    acquisitionTransactionId,
                    0,
                    CoreLedgerTestData.FundAssetId,
                    100,
                    EntryRole.Principal));

            context.AssetLots.Add(
                new AssetLotRow
                {
                    Id =
                        lotId,

                    AssetId =
                        CoreLedgerTestData.FundAssetId,

                    OpeningTransactionEntryId =
                        acquisitionEntryId,

                    CostBasisStatus =
                        CostBasisStatus.Unknown,

                    CreatedAtUtc =
                        CoreLedgerTestData.CreatedAtUtc
                });

            context.LotEntryAllocations.Add(
                new LotEntryAllocationRow
                {
                    Id =
                        Guid.NewGuid(),

                    AssetLotId =
                        lotId,

                    TransactionEntryId =
                        acquisitionEntryId,

                    QuantityDeltaE8 =
                        100,

                    CreatedAtUtc =
                        CoreLedgerTestData.CreatedAtUtc
                });

            await context.SaveChangesAsync();

            await CoreLedgerTestData.PostAsync(
                context,
                acquisitionTransactionId);

            var dependentTransactionId =
                Guid.NewGuid();

            var dependentEntryId =
                Guid.NewGuid();

            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    dependentTransactionId,
                    TransactionType.Adjustment));

            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    dependentEntryId,
                    dependentTransactionId,
                    0,
                    CoreLedgerTestData.FundAssetId,
                    40,
                    EntryRole.Adjustment));

            context.LotEntryAllocations.Add(
                new LotEntryAllocationRow
                {
                    Id =
                        Guid.NewGuid(),

                    AssetLotId =
                        lotId,

                    TransactionEntryId =
                        dependentEntryId,

                    QuantityDeltaE8 =
                        40,

                    CreatedAtUtc =
                        CoreLedgerTestData.CreatedAtUtc
                });

            await context.SaveChangesAsync();

            await CoreLedgerTestData.PostAsync(
                context,
                dependentTransactionId,
                CoreLedgerTestData
                    .CreatedAtUtc
                    .AddMinutes(2));

            return new Scenario(
                acquisitionTransactionId,
                acquisitionEntryId,
                lotId,
                dependentTransactionId,
                dependentEntryId);
        }

        private static async Task<Guid>
            SeedDependentReversalAsync(
                SqliteTestDatabase database,
                Scenario scenario,
                TransactionStatus requestedStatus)
        {
            await using var context =
                database.CreateContext();

            var transactionId =
                Guid.NewGuid();

            var entryId =
                Guid.NewGuid();

            var transaction =
                CoreLedgerTestData.CreateDraftTransaction(
                    transactionId,
                    TransactionType.Reversal,
                    reversalOfTransactionId:
                        scenario.DependentTransactionId);

            if (requestedStatus
                == TransactionStatus.Cancelled)
            {
                transaction.Status =
                    TransactionStatus.Cancelled;
            }

            context.LedgerTransactions.Add(
                transaction);

            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    entryId,
                    transactionId,
                    0,
                    CoreLedgerTestData.FundAssetId,
                    -40,
                    EntryRole.Adjustment));

            context.LotEntryAllocations.Add(
                new LotEntryAllocationRow
                {
                    Id =
                        Guid.NewGuid(),

                    AssetLotId =
                        scenario.LotId,

                    TransactionEntryId =
                        entryId,

                    QuantityDeltaE8 =
                        -40,

                    CreatedAtUtc =
                        CoreLedgerTestData.CreatedAtUtc
                });

            await context.SaveChangesAsync();

            if (requestedStatus
                == TransactionStatus.Posted)
            {
                await CoreLedgerTestData.PostAsync(
                    context,
                    transactionId,
                    CoreLedgerTestData
                        .CreatedAtUtc
                        .AddMinutes(3));
            }

            return transactionId;
        }

        private static async Task<Guid>
            SeedAcquisitionReversalDraftAsync(
                SqliteTestDatabase database,
                Scenario scenario)
        {
            await using var context =
                database.CreateContext();

            var transactionId =
                Guid.NewGuid();

            var entryId =
                Guid.NewGuid();

            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    transactionId,
                    TransactionType.Reversal,
                    reversalOfTransactionId:
                        scenario
                            .AcquisitionTransactionId));

            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    entryId,
                    transactionId,
                    0,
                    CoreLedgerTestData.FundAssetId,
                    -100,
                    EntryRole.Principal));

            context.LotEntryAllocations.Add(
                new LotEntryAllocationRow
                {
                    Id =
                        Guid.NewGuid(),

                    AssetLotId =
                        scenario.LotId,

                    TransactionEntryId =
                        entryId,

                    QuantityDeltaE8 =
                        -100,

                    CreatedAtUtc =
                        CoreLedgerTestData.CreatedAtUtc
                });

            await context.SaveChangesAsync();

            return transactionId;
        }

        private static async Task
            SeedUnrelatedNegativeAdjustmentAsync(
                SqliteTestDatabase database,
                Guid lotId)
        {
            await using var context =
                database.CreateContext();

            var transactionId =
                Guid.NewGuid();

            var entryId =
                Guid.NewGuid();

            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    transactionId,
                    TransactionType.Adjustment));

            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    entryId,
                    transactionId,
                    0,
                    CoreLedgerTestData.FundAssetId,
                    -40,
                    EntryRole.Adjustment));

            context.LotEntryAllocations.Add(
                new LotEntryAllocationRow
                {
                    Id =
                        Guid.NewGuid(),

                    AssetLotId =
                        lotId,

                    TransactionEntryId =
                        entryId,

                    QuantityDeltaE8 =
                        -40,

                    CreatedAtUtc =
                        CoreLedgerTestData.CreatedAtUtc
                });

            await context.SaveChangesAsync();

            await CoreLedgerTestData.PostAsync(
                context,
                transactionId,
                CoreLedgerTestData
                    .CreatedAtUtc
                    .AddMinutes(4));
        }

        private static async Task<long>
            GetPostedLotQuantityAsync(
                WealthLedgerDbContext context,
                Guid lotId)
        {
            return await (
                from allocation
                    in context.LotEntryAllocations
                        .AsNoTracking()
                join entry
                    in context.TransactionEntries
                        .AsNoTracking()
                    on allocation.TransactionEntryId
                    equals entry.Id
                join transaction
                    in context.LedgerTransactions
                        .AsNoTracking()
                    on entry.TransactionId
                    equals transaction.Id
                where
                    allocation.AssetLotId
                    == lotId
                    && transaction.Status
                    == TransactionStatus.Posted
                select allocation.QuantityDeltaE8
            ).SumAsync();
        }

        private sealed record Scenario(
            Guid AcquisitionTransactionId,
            Guid AcquisitionEntryId,
            Guid LotId,
            Guid DependentTransactionId,
            Guid DependentEntryId);
    }
}
