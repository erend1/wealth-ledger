using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Navigation;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class LedgerTransactionExplanationReadStoreTests
{
    [Fact]
    public async Task Explanation_CurrentLabelsUseTheSameBoundedQueriesForOneOrManyEntries()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var singleTransactionId = Guid.NewGuid();
        var manyTransactionId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            await SeedAdjustmentAsync(
                context,
                singleTransactionId,
                entryCount: 1);
            await SeedAdjustmentAsync(
                context,
                manyTransactionId,
                entryCount: 6);

            var portfolio = await context.Portfolios.SingleAsync(
                row => row.Id == CoreLedgerTestData.PortfolioId);
            var account = await context.Accounts.SingleAsync(
                row => row.Id == CoreLedgerTestData.AccountId);
            var institution = await context.Institutions.SingleAsync(
                row => row.Id == CoreLedgerTestData.InstitutionId);
            var asset = await context.Assets.SingleAsync(
                row => row.Id == CoreLedgerTestData.CashAssetId);

            portfolio.Status = PortfolioStatus.Archived;
            portfolio.ClosedAtUtc = CoreLedgerTestData.CreatedAtUtc.AddDays(1);
            account.IsActive = false;
            institution.IsActive = false;
            asset.IsActive = false;
            await context.SaveChangesAsync();
        }

        var single = await ReadAsync(database, singleTransactionId);
        var many = await ReadAsync(database, manyTransactionId);

        Assert.Equal(9, single.CommandCount);
        Assert.Equal(single.CommandCount, many.CommandCount);
        Assert.Single(single.Explanation.Transaction.Entries);
        Assert.Equal(6, many.Explanation.Transaction.Entries.Count);
        Assert.All(
            many.Explanation.CurrentContext.Entries,
            entry =>
            {
                Assert.Equal(
                    PortfolioStatus.Archived,
                    entry.Portfolio.Status);
                Assert.False(entry.Account.IsActive);
                Assert.False(entry.Account.Institution!.IsActive);
                Assert.False(entry.Asset.IsActive);
            });
    }

    private static async Task SeedAdjustmentAsync(
        WealthLedgerDbContext context,
        Guid transactionId,
        int entryCount)
    {
        context.LedgerTransactions.Add(
            CoreLedgerTestData.CreateDraftTransaction(
                transactionId,
                TransactionType.Adjustment));

        for (var sequence = 0; sequence < entryCount; sequence++)
        {
            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    Guid.NewGuid(),
                    transactionId,
                    sequence,
                    CoreLedgerTestData.CashAssetId,
                    quantityDeltaE8: sequence + 1,
                    EntryRole.Adjustment));
        }

        await context.SaveChangesAsync();
        await CoreLedgerTestData.PostAsync(context, transactionId);
    }

    private static async Task<ReadResult> ReadAsync(
        SqliteTestDatabase database,
        Guid transactionId)
    {
        var interceptor = new CommandCountingInterceptor();
        var options = new DbContextOptionsBuilder<WealthLedgerDbContext>()
            .UseSqlite(database.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        await using var context = new WealthLedgerDbContext(options);
        var useCase = new GetLedgerTransactionExplanationUseCase(
            new GetLedgerTransactionUseCase(
                new EfCoreLedgerTransactionReadStore(context)),
            new EfCoreLedgerTransactionCurrentContextReadStore(context));
        var explanation = await useCase.ExecuteAsync(
            new GetLedgerTransactionExplanationQuery(
                CoreLedgerTestData.HouseholdId,
                transactionId));

        return new ReadResult(
            Assert.IsType<LedgerTransactionExplanation>(explanation),
            interceptor.CommandCount);
    }

    private sealed record ReadResult(
        LedgerTransactionExplanation Explanation,
        int CommandCount);

    private sealed class CommandCountingInterceptor : DbCommandInterceptor
    {
        internal int CommandCount { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            CommandCount++;
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>>
            ReaderExecutingAsync(
                DbCommand command,
                CommandEventData eventData,
                InterceptionResult<DbDataReader> result,
                CancellationToken cancellationToken = default)
        {
            CommandCount++;
            return ValueTask.FromResult(result);
        }
    }
}
