using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Ledger;
using WealthLedger.Infrastructure.Persistence;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class OpeningBalanceEffectiveHistoryReadStoreTests
{
    [Fact]
    public async Task Read_IgnoresDraftHistoryAndDifferentScopes()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var draftTransactionId = Guid.NewGuid();
        var differentScopeTransactionId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);

            context.LedgerTransactions.AddRange(
                CoreLedgerTestData.CreateDraftTransaction(
                    draftTransactionId,
                    TransactionType.OpeningBalance),
                CoreLedgerTestData.CreateDraftTransaction(
                    differentScopeTransactionId,
                    TransactionType.Adjustment));
            context.TransactionEntries.AddRange(
                CoreLedgerTestData.CreateEntry(
                    Guid.NewGuid(),
                    draftTransactionId,
                    0,
                    CoreLedgerTestData.CashAssetId,
                    100,
                    EntryRole.Principal),
                CoreLedgerTestData.CreateEntry(
                    Guid.NewGuid(),
                    differentScopeTransactionId,
                    0,
                    CoreLedgerTestData.CashAssetId,
                    100,
                    EntryRole.Adjustment,
                    accountId: CoreLedgerTestData.DestinationAccountId));

            await context.SaveChangesAsync();
            await CoreLedgerTestData.PostAsync(
                context,
                differentScopeTransactionId);
        }

        await using var readContext = database.CreateContext();
        var result =
            await new EfCoreOpeningBalanceEffectiveHistoryReadStore(
                    readContext)
                .ReadAsync(CreateScope());

        Assert.Null(result.EffectiveOpeningTransactionId);
        Assert.False(result.HasOtherEffectiveHistory);
    }

    [Fact]
    public async Task Read_ReturnsEffectiveOpeningTransactionIdentity()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var transactionId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    transactionId,
                    TransactionType.OpeningBalance));
            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    Guid.NewGuid(),
                    transactionId,
                    0,
                    CoreLedgerTestData.CashAssetId,
                    100,
                    EntryRole.Principal));
            await context.SaveChangesAsync();
            await CoreLedgerTestData.PostAsync(context, transactionId);
        }

        await using var readContext = database.CreateContext();
        var result =
            await new EfCoreOpeningBalanceEffectiveHistoryReadStore(
                    readContext)
                .ReadAsync(CreateScope());

        Assert.Equal(transactionId, result.EffectiveOpeningTransactionId);
        Assert.False(result.HasOtherEffectiveHistory);
    }

    [Fact]
    public async Task Read_ReturnsOtherEffectiveHistory()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var transactionId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    transactionId,
                    TransactionType.Adjustment));
            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    Guid.NewGuid(),
                    transactionId,
                    0,
                    CoreLedgerTestData.CashAssetId,
                    100,
                    EntryRole.Adjustment));
            await context.SaveChangesAsync();
            await CoreLedgerTestData.PostAsync(context, transactionId);
        }

        await using var readContext = database.CreateContext();
        var result =
            await new EfCoreOpeningBalanceEffectiveHistoryReadStore(
                    readContext)
                .ReadAsync(CreateScope());

        Assert.Null(result.EffectiveOpeningTransactionId);
        Assert.True(result.HasOtherEffectiveHistory);
    }

    [Fact]
    public async Task Read_FullyReversedPairDoesNotCountAsEffectiveHistory()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var originalTransactionId = Guid.NewGuid();
        var originalEntryId = Guid.NewGuid();
        var reversalTransactionId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    originalTransactionId,
                    TransactionType.OpeningBalance));
            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    originalEntryId,
                    originalTransactionId,
                    0,
                    CoreLedgerTestData.CashAssetId,
                    100,
                    EntryRole.Principal));
            await context.SaveChangesAsync();
            await CoreLedgerTestData.PostAsync(context, originalTransactionId);

            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    reversalTransactionId,
                    TransactionType.Reversal,
                    reversalOfTransactionId: originalTransactionId));
            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    Guid.NewGuid(),
                    reversalTransactionId,
                    0,
                    CoreLedgerTestData.CashAssetId,
                    -100,
                    EntryRole.Principal));
            await context.SaveChangesAsync();
            await CoreLedgerTestData.PostAsync(context, reversalTransactionId);
        }

        await using var readContext = database.CreateContext();
        var result =
            await new EfCoreOpeningBalanceEffectiveHistoryReadStore(
                    readContext)
                .ReadAsync(CreateScope());

        Assert.Null(result.EffectiveOpeningTransactionId);
        Assert.False(result.HasOtherEffectiveHistory);
    }

    private static OpeningBalanceScope CreateScope()
        => new(
            CoreLedgerTestData.HouseholdId,
            CoreLedgerTestData.PortfolioId,
            CoreLedgerTestData.AccountId,
            CoreLedgerTestData.CashAssetId);
}
