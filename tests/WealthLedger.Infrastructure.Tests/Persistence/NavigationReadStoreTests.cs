using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WealthLedger.Application.Navigation;
using WealthLedger.Application.Positions;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class NavigationReadStoreTests
{
    private static readonly Guid InactiveInstitutionId =
        Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid ClosedPortfolioId =
        Guid.Parse("40000000-0000-0000-0000-000000000010");
    private static readonly Guid ArchivedPortfolioId =
        Guid.Parse("40000000-0000-0000-0000-000000000011");
    private static readonly Guid NullInstitutionAccountId =
        Guid.Parse("50000000-0000-0000-0000-000000000010");
    private static readonly Guid InactiveInstitutionAccountId =
        Guid.Parse("50000000-0000-0000-0000-000000000011");
    private static readonly Guid InactiveAccountId =
        Guid.Parse("50000000-0000-0000-0000-000000000012");
    private static readonly Guid InactiveAssetId =
        Guid.Parse("60000000-0000-0000-0000-000000000010");

    [Fact]
    public async Task Navigation_MasterPagesAreScopedFilteredDeterministicAndRestartSafe()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            await SeedAdditionalMasterDataAsync(context);
        }

        string householdCursor;

        await using (var firstContext = database.CreateContext())
        {
            var firstPage = await new ListHouseholdsUseCase(
                    new EfCoreMasterNavigationReadStore(firstContext))
                .ExecuteAsync(new ListHouseholdsQuery(PageSize: 1));

            Assert.Single(firstPage.Items);
            Assert.Equal(
                CoreLedgerTestData.HouseholdId,
                firstPage.Items[0].HouseholdId);
            Assert.Equal("TRY", firstPage.Items[0].BaseCurrency.Code);
            Assert.NotNull(firstPage.NextCursor);
            householdCursor = firstPage.NextCursor!;
        }

        await using (var restartedContext = database.CreateContext())
        {
            var store = new EfCoreMasterNavigationReadStore(restartedContext);
            var scope = new EfCoreNavigationScopeReadStore(restartedContext);
            var secondPage = await new ListHouseholdsUseCase(store)
                .ExecuteAsync(
                    new ListHouseholdsQuery(
                        PageSize: 1,
                        Cursor: householdCursor));
            var membersDefault = await new ListHouseholdMembersUseCase(
                    store,
                    scope)
                .ExecuteAsync(
                    new ListHouseholdMembersQuery(
                        CoreLedgerTestData.HouseholdId));
            var membersAll = await new ListHouseholdMembersUseCase(store, scope)
                .ExecuteAsync(
                    new ListHouseholdMembersQuery(
                        CoreLedgerTestData.HouseholdId,
                        IncludeInactive: true));
            var portfoliosDefault = await new ListPortfoliosUseCase(store, scope)
                .ExecuteAsync(
                    new ListPortfoliosQuery(CoreLedgerTestData.HouseholdId));
            var portfoliosAll = await new ListPortfoliosUseCase(store, scope)
                .ExecuteAsync(
                    new ListPortfoliosQuery(
                        CoreLedgerTestData.HouseholdId,
                        IncludeInactive: true));
            var accountsDefault = await new ListAccountsUseCase(store, scope)
                .ExecuteAsync(
                    new ListAccountsQuery(CoreLedgerTestData.HouseholdId));
            var accountsAll = await new ListAccountsUseCase(store, scope)
                .ExecuteAsync(
                    new ListAccountsQuery(
                        CoreLedgerTestData.HouseholdId,
                        IncludeInactive: true));
            var institutionsDefault = await new ListInstitutionsUseCase(store)
                .ExecuteAsync(new ListInstitutionsQuery());
            var institutionsAll = await new ListInstitutionsUseCase(store)
                .ExecuteAsync(
                    new ListInstitutionsQuery(IncludeInactive: true));
            var assetsDefault = await new ListAssetsUseCase(store)
                .ExecuteAsync(new ListAssetsQuery());
            var assetsAll = await new ListAssetsUseCase(store)
                .ExecuteAsync(new ListAssetsQuery(IncludeInactive: true));
            var currencies = await new ListCurrenciesUseCase(store)
                .ExecuteAsync(new ListCurrenciesQuery());
            var knownEmpty = await new ListHouseholdMembersUseCase(store, scope)
                .ExecuteAsync(
                    new ListHouseholdMembersQuery(
                        CoreLedgerTestData.OtherHouseholdId));

            Assert.Single(secondPage.Items);
            Assert.Equal(
                CoreLedgerTestData.OtherHouseholdId,
                secondPage.Items[0].HouseholdId);
            Assert.Null(secondPage.NextCursor);
            Assert.Single(membersDefault.Items);
            Assert.Equal(2, membersAll.Items.Count);
            Assert.All(
                membersAll.Items,
                item => Assert.Equal(
                    CoreLedgerTestData.HouseholdId,
                    item.HouseholdId));
            Assert.Single(portfoliosDefault.Items);
            Assert.Equal(3, portfoliosAll.Items.Count);
            Assert.Contains(
                portfoliosAll.Items,
                item => item.Status == PortfolioStatus.Archived
                        && item.ClosedAtUtc is not null);
            Assert.Equal(4, accountsDefault.Items.Count);
            Assert.Equal(5, accountsAll.Items.Count);
            Assert.Contains(
                accountsDefault.Items,
                item => item.AccountId == NullInstitutionAccountId
                        && item.Institution is null);
            Assert.Contains(
                accountsDefault.Items,
                item => item.AccountId == InactiveInstitutionAccountId
                        && item.Institution is { IsActive: false });
            Assert.All(
                accountsAll.Items,
                item => Assert.Equal(
                    CoreLedgerTestData.HouseholdId,
                    item.HouseholdId));
            Assert.Single(institutionsDefault.Items);
            Assert.Equal(2, institutionsAll.Items.Count);
            Assert.Equal(4, assetsDefault.Items.Count);
            Assert.Equal(5, assetsAll.Items.Count);
            Assert.Equal(["TRY", "USD"], currencies.Items.Select(x => x.Code));
            Assert.Empty(knownEmpty.Items);
            await Assert.ThrowsAsync<HouseholdNotFoundException>(
                () => new ListAccountsUseCase(store, scope).ExecuteAsync(
                    new ListAccountsQuery(Guid.NewGuid())));
        }
    }

    [Fact]
    public async Task Navigation_EveryMasterCursorTraversesAfterRestartAndNewRow()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            await SeedAdditionalMasterDataAsync(context);
        }

        NavigationPage<AssetNavigationItem> firstAssetPage;

        await using (var context = database.CreateContext())
        {
            firstAssetPage = await new ListAssetsUseCase(
                    new EfCoreMasterNavigationReadStore(context))
                .ExecuteAsync(
                    new ListAssetsQuery(
                        PageSize: 1,
                        IncludeInactive: true));
        }

        Assert.Single(firstAssetPage.Items);
        Assert.NotNull(firstAssetPage.NextCursor);
        var insertedAssetId =
            Guid.Parse("60000000-0000-0000-0000-000000000099");

        await using (var context = database.CreateContext())
        {
            context.Assets.Add(
                new AssetRow
                {
                    Id = insertedAssetId,
                    Code = "ZZZ_CURSOR_ASSET",
                    Name = "Synthetic Cursor Asset",
                    Type = AssetType.Other,
                    BaseUnit = AssetUnit.Other,
                    BaseCurrencyCode = null,
                    LotTrackingMode = LotTrackingMode.None,
                    IsActive = true,
                    CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
                });
            await context.SaveChangesAsync();
        }

        var households = await TraverseAsync(
            database,
            (context, cursor) => new ListHouseholdsUseCase(
                    new EfCoreMasterNavigationReadStore(context))
                .ExecuteAsync(
                    new ListHouseholdsQuery(
                        PageSize: 1,
                        Cursor: cursor)));
        var members = await TraverseAsync(
            database,
            (context, cursor) => new ListHouseholdMembersUseCase(
                    new EfCoreMasterNavigationReadStore(context),
                    new EfCoreNavigationScopeReadStore(context))
                .ExecuteAsync(
                    new ListHouseholdMembersQuery(
                        CoreLedgerTestData.HouseholdId,
                        PageSize: 1,
                        Cursor: cursor,
                        IncludeInactive: true)));
        var institutions = await TraverseAsync(
            database,
            (context, cursor) => new ListInstitutionsUseCase(
                    new EfCoreMasterNavigationReadStore(context))
                .ExecuteAsync(
                    new ListInstitutionsQuery(
                        PageSize: 1,
                        Cursor: cursor,
                        IncludeInactive: true)));
        var portfolios = await TraverseAsync(
            database,
            (context, cursor) => new ListPortfoliosUseCase(
                    new EfCoreMasterNavigationReadStore(context),
                    new EfCoreNavigationScopeReadStore(context))
                .ExecuteAsync(
                    new ListPortfoliosQuery(
                        CoreLedgerTestData.HouseholdId,
                        PageSize: 1,
                        Cursor: cursor,
                        IncludeInactive: true)));
        var accounts = await TraverseAsync(
            database,
            (context, cursor) => new ListAccountsUseCase(
                    new EfCoreMasterNavigationReadStore(context),
                    new EfCoreNavigationScopeReadStore(context))
                .ExecuteAsync(
                    new ListAccountsQuery(
                        CoreLedgerTestData.HouseholdId,
                        PageSize: 1,
                        Cursor: cursor,
                        IncludeInactive: true)));
        var currencies = await TraverseAsync(
            database,
            (context, cursor) => new ListCurrenciesUseCase(
                    new EfCoreMasterNavigationReadStore(context))
                .ExecuteAsync(
                    new ListCurrenciesQuery(
                        PageSize: 1,
                        Cursor: cursor)));
        var assets = await TraverseAsync(
            database,
            (context, cursor) => new ListAssetsUseCase(
                    new EfCoreMasterNavigationReadStore(context))
                .ExecuteAsync(
                    new ListAssetsQuery(
                        PageSize: 1,
                        Cursor: cursor,
                        IncludeInactive: true)));
        var assetContinuation = await TraverseAsync(
            database,
            (context, cursor) => new ListAssetsUseCase(
                    new EfCoreMasterNavigationReadStore(context))
                .ExecuteAsync(
                    new ListAssetsQuery(
                        PageSize: 1,
                        Cursor: cursor,
                        IncludeInactive: true)),
            firstAssetPage.NextCursor);

        Assert.Equal(2, households.Count);
        Assert.Equal(2, members.Count);
        Assert.Equal(2, institutions.Count);
        Assert.Equal(3, portfolios.Count);
        Assert.Equal(5, accounts.Count);
        Assert.Equal(2, currencies.Count);
        Assert.Equal(6, assets.Count);
        Assert.Equal(
            households
                .OrderBy(item => item.CreatedAtUtc)
                .ThenBy(item => item.HouseholdId)
                .Select(item => item.HouseholdId),
            households.Select(item => item.HouseholdId));
        Assert.Equal(
            members
                .OrderBy(item => item.CreatedAtUtc)
                .ThenBy(item => item.HouseholdMemberId)
                .Select(item => item.HouseholdMemberId),
            members.Select(item => item.HouseholdMemberId));
        Assert.Equal(
            institutions.OrderBy(item => item.Code).Select(item => item.Code),
            institutions.Select(item => item.Code));
        Assert.Equal(
            portfolios.OrderBy(item => item.Code).Select(item => item.Code),
            portfolios.Select(item => item.Code));
        Assert.Equal(
            accounts.OrderBy(item => item.Code).Select(item => item.Code),
            accounts.Select(item => item.Code));
        Assert.Equal(
            currencies.OrderBy(item => item.Code).Select(item => item.Code),
            currencies.Select(item => item.Code));
        Assert.Equal(
            assets.OrderBy(item => item.Code).Select(item => item.Code),
            assets.Select(item => item.Code));
        Assert.Equal(
            assets.Count,
            assets.Select(item => item.AssetId).Distinct().Count());
        Assert.DoesNotContain(
            assetContinuation,
            item => item.AssetId == firstAssetPage.Items[0].AssetId);
        Assert.Contains(
            assetContinuation,
            item => item.AssetId == insertedAssetId);
    }

    [Fact]
    public async Task Navigation_RecentLedgerIsPostedOrderedBatchedAndUsesCurrentContext()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var identities = new LedgerIdentities();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            await SeedAdditionalMasterDataAsync(context);
            await SeedLedgerNavigationHistoryAsync(context, identities);

            var account = await context.Accounts.SingleAsync(
                row => row.Id == CoreLedgerTestData.AccountId);
            var institution = await context.Institutions.SingleAsync(
                row => row.Id == CoreLedgerTestData.InstitutionId);
            var portfolio = await context.Portfolios.SingleAsync(
                row => row.Id == CoreLedgerTestData.PortfolioId);
            var asset = await context.Assets.SingleAsync(
                row => row.Id == CoreLedgerTestData.CashAssetId);
            account.Name = "Current Renamed Account";
            account.IsActive = false;
            institution.Name = "Current Renamed Institution";
            institution.IsActive = false;
            portfolio.Name = "Current Renamed Portfolio";
            portfolio.Status = PortfolioStatus.Closed;
            portfolio.ClosedAtUtc = CoreLedgerTestData.CreatedAtUtc.AddDays(10);
            asset.Name = "Current Renamed Asset";
            asset.IsActive = false;
            await context.SaveChangesAsync();
        }

        var interceptor = new CommandCountingInterceptor();
        await using var readContext = CreateContext(database, interceptor);
        var useCase = new ListRecentLedgerTransactionsUseCase(
            new EfCoreLedgerNavigationReadStore(readContext),
            new EfCoreNavigationScopeReadStore(readContext));
        var first = await useCase.ExecuteAsync(
            new ListRecentLedgerTransactionsQuery(
                CoreLedgerTestData.HouseholdId,
                PageSize: 2));

        Assert.Equal(3, interceptor.CommandCount);
        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        Assert.Equal(identities.ReversalId, first.Items[0].TransactionId);
        Assert.Equal(identities.NullInstitutionTransactionId, first.Items[1].TransactionId);
        Assert.Equal(first.Items[0].PostedAtUtc, first.Items[1].PostedAtUtc);
        Assert.True(
            first.Items[0].TransactionId.CompareTo(
                first.Items[1].TransactionId) > 0);
        Assert.All(
            first.Items,
            item => Assert.Equal(TransactionStatus.Posted, item.Status));
        Assert.Equal(
            CoreLedgerTestData.ExecutionDate,
            first.Items[0].ExecutionDate);
        var reversalEffect = Assert.Single(first.Items[0].EntryEffects);
        Assert.Equal(identities.ReversalEntryId, reversalEffect.EntryId);
        Assert.Equal(-500, reversalEffect.QuantityDeltaRawE8);
        Assert.Equal("Current Renamed Portfolio", reversalEffect.PortfolioName);
        Assert.Equal(PortfolioStatus.Closed, reversalEffect.PortfolioStatus);
        Assert.Equal("Current Renamed Account", reversalEffect.AccountName);
        Assert.False(reversalEffect.AccountIsActive);
        Assert.Equal("Current Renamed Institution", reversalEffect.InstitutionName);
        Assert.False(reversalEffect.InstitutionIsActive);
        Assert.Equal("Current Renamed Asset", reversalEffect.AssetName);
        Assert.False(reversalEffect.AssetIsActive);
        var nullInstitutionEffect = Assert.Single(first.Items[1].EntryEffects);
        Assert.Null(nullInstitutionEffect.InstitutionId);
        Assert.Null(nullInstitutionEffect.InstitutionCode);
        Assert.Null(nullInstitutionEffect.InstitutionName);
        Assert.Null(nullInstitutionEffect.InstitutionType);
        Assert.Null(nullInstitutionEffect.InstitutionIsActive);

        await using (var writeContext = database.CreateContext())
        {
            writeContext.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    identities.NewlyPostedId,
                    TransactionType.Adjustment));
            writeContext.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    identities.NewlyPostedEntryId,
                    identities.NewlyPostedId,
                    sequence: 0,
                    CoreLedgerTestData.CashAssetId,
                    quantityDeltaE8: 321,
                    EntryRole.Adjustment));
            await writeContext.SaveChangesAsync();
            await CoreLedgerTestData.PostAsync(
                writeContext,
                identities.NewlyPostedId,
                new DateTime(2026, 9, 2, 14, 0, 0, DateTimeKind.Utc));
        }

        var oneItemInterceptor = new CommandCountingInterceptor();
        await using var freshContext = CreateContext(
            database,
            oneItemInterceptor);
        var refreshedFirst = await new ListRecentLedgerTransactionsUseCase(
                new EfCoreLedgerNavigationReadStore(freshContext),
                new EfCoreNavigationScopeReadStore(freshContext))
            .ExecuteAsync(
                new ListRecentLedgerTransactionsQuery(
                    CoreLedgerTestData.HouseholdId,
                    PageSize: 1));

        Assert.Equal(3, oneItemInterceptor.CommandCount);
        Assert.Equal(
            identities.NewlyPostedId,
            Assert.Single(refreshedFirst.Items).TransactionId);

        await using var restartedContext = database.CreateContext();
        var continuation = await new ListRecentLedgerTransactionsUseCase(
                new EfCoreLedgerNavigationReadStore(restartedContext),
                new EfCoreNavigationScopeReadStore(restartedContext))
            .ExecuteAsync(
                new ListRecentLedgerTransactionsQuery(
                    CoreLedgerTestData.HouseholdId,
                    PageSize: 2,
                    Cursor: first.NextCursor));

        Assert.Equal(2, continuation.Items.Count);
        var multiEntry = Assert.Single(
            continuation.Items,
            item => item.TransactionId == identities.MultiEntryId);
        Assert.Equal(
            [identities.MultiEntryFirstId, identities.MultiEntrySecondId],
            multiEntry.EntryEffects.Select(item => item.EntryId));
        Assert.Equal(
            [0, 1],
            multiEntry.EntryEffects.Select(item => item.EntrySequence));
        Assert.Equal(
            [111L, 222L],
            multiEntry.EntryEffects.Select(item => item.QuantityDeltaRawE8));
        var original = Assert.Single(
            continuation.Items,
            item => item.TransactionId == identities.OriginalId);
        Assert.Equal(identities.OriginalId, original.TransactionId);
        Assert.Equal(identities.ReversalId, original.ReversedByTransactionId);
        Assert.Null(continuation.NextCursor);
        Assert.DoesNotContain(
            continuation.Items,
            item => item.TransactionId == identities.NewlyPostedId);
        Assert.DoesNotContain(
            first.Items.Concat(continuation.Items),
            item => item.TransactionId == identities.DraftId
                    || item.TransactionId == identities.OrderedId
                    || item.TransactionId == identities.CancelledId
                    || item.TransactionId == identities.OtherHouseholdPostedId);
        Assert.Equal(
            4,
            first.Items.Concat(continuation.Items).Select(x => x.TransactionId)
                .Distinct()
                .Count());
    }

    [Fact]
    public async Task PositionScope_ValidEmptyNetZeroUnknownAndCrossHouseholdDiffer()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var identities = new LedgerIdentities();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            await SeedAdditionalMasterDataAsync(context);
            await SeedLedgerNavigationHistoryAsync(context, identities);
        }

        await using var readContext = database.CreateContext();
        var scope = new EfCoreNavigationScopeReadStore(readContext);
        var useCase = new GetPositionUseCase(
            new EfCorePostedEntrySource(readContext),
            scope);
        var netZero = await useCase.ExecuteAsync(
            new GetPositionQuery(
                CoreLedgerTestData.HouseholdId,
                CoreLedgerTestData.PortfolioId,
                CoreLedgerTestData.AccountId,
                CoreLedgerTestData.CashAssetId));
        var validEmpty = await useCase.ExecuteAsync(
            new GetPositionQuery(
                CoreLedgerTestData.HouseholdId,
                CoreLedgerTestData.PortfolioId,
                CoreLedgerTestData.AccountId,
                CoreLedgerTestData.OtherFundAssetId));
        var validInactiveHistoryScope = await useCase.ExecuteAsync(
            new GetPositionQuery(
                CoreLedgerTestData.HouseholdId,
                ClosedPortfolioId,
                InactiveAccountId,
                InactiveAssetId));

        Assert.Equal(0, netZero.Quantity.RawE8);
        Assert.Equal(2, netZero.SourceEntryCount);
        Assert.Equal(0, validEmpty.Quantity.RawE8);
        Assert.Equal(0, validEmpty.SourceEntryCount);
        Assert.Equal(0, validInactiveHistoryScope.Quantity.RawE8);
        Assert.Equal(0, validInactiveHistoryScope.SourceEntryCount);
        await Assert.ThrowsAsync<PositionScopeNotFoundException>(
            () => useCase.ExecuteAsync(
                new GetPositionQuery(
                    CoreLedgerTestData.HouseholdId,
                    CoreLedgerTestData.OtherPortfolioId,
                    CoreLedgerTestData.AccountId,
                    CoreLedgerTestData.CashAssetId)));
        await Assert.ThrowsAsync<PositionScopeNotFoundException>(
            () => useCase.ExecuteAsync(
                new GetPositionQuery(
                    CoreLedgerTestData.HouseholdId,
                    CoreLedgerTestData.PortfolioId,
                    CoreLedgerTestData.AccountId,
                    Guid.NewGuid())));
    }

    [Fact]
    public async Task Navigation_CancellationReachesTheSqliteQuery()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(seedContext);
        }

        await using var context = database.CreateContext();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ListAssetsUseCase(
                    new EfCoreMasterNavigationReadStore(context))
                .ExecuteAsync(
                    new ListAssetsQuery(),
                    cancellation.Token));
    }

    private static async Task SeedAdditionalMasterDataAsync(
        WealthLedgerDbContext context)
    {
        context.HouseholdMembers.Add(
            new HouseholdMemberRow
            {
                Id = Guid.Parse("20000000-0000-0000-0000-000000000002"),
                HouseholdId = CoreLedgerTestData.HouseholdId,
                DisplayName = "Inactive Synthetic Member",
                IsActive = false,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
        context.Institutions.Add(
            new InstitutionRow
            {
                Id = InactiveInstitutionId,
                Code = "INACTIVE_INSTITUTION",
                Name = "Inactive Synthetic Institution",
                Type = InstitutionType.Bank,
                IsActive = false
            });
        context.Portfolios.AddRange(
            new PortfolioRow
            {
                Id = ClosedPortfolioId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                Code = "CLOSED_GOAL",
                Name = "Closed Synthetic Goal",
                Status = PortfolioStatus.Closed,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc,
                ClosedAtUtc = CoreLedgerTestData.CreatedAtUtc.AddDays(1)
            },
            new PortfolioRow
            {
                Id = ArchivedPortfolioId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                Code = "ARCHIVED_GOAL",
                Name = "Archived Synthetic Goal",
                Status = PortfolioStatus.Archived,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc,
                ClosedAtUtc = CoreLedgerTestData.CreatedAtUtc.AddDays(2)
            });
        context.Accounts.AddRange(
            new AccountRow
            {
                Id = NullInstitutionAccountId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                InstitutionId = null,
                Code = "NO_INSTITUTION",
                Name = "No Institution Account",
                Type = AccountType.Cash,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            },
            new AccountRow
            {
                Id = InactiveInstitutionAccountId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                InstitutionId = InactiveInstitutionId,
                Code = "INACTIVE_CUSTODIAN",
                Name = "Inactive Custodian Account",
                Type = AccountType.Investment,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            },
            new AccountRow
            {
                Id = InactiveAccountId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                InstitutionId = null,
                Code = "CLOSED_ACCOUNT",
                Name = "Closed Synthetic Account",
                Type = AccountType.Cash,
                IsActive = false,
                OpenedOn = new DateOnly(2026, 1, 1),
                ClosedOn = new DateOnly(2026, 8, 1)
            });
        context.Assets.Add(
            new AssetRow
            {
                Id = InactiveAssetId,
                Code = "INACTIVE_ASSET",
                Name = "Inactive Synthetic Asset",
                Type = AssetType.Equity,
                BaseUnit = AssetUnit.Share,
                BaseCurrencyCode = "TRY",
                LotTrackingMode = LotTrackingMode.Optional,
                IsActive = false,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
        await context.SaveChangesAsync();
    }

    private static async Task SeedLedgerNavigationHistoryAsync(
        WealthLedgerDbContext context,
        LedgerIdentities identities)
    {
        context.LedgerTransactions.Add(
            CoreLedgerTestData.CreateDraftTransaction(
                identities.OriginalId,
                TransactionType.Contribution));
        context.TransactionEntries.Add(
            CoreLedgerTestData.CreateEntry(
                identities.OriginalEntryId,
                identities.OriginalId,
                sequence: 0,
                CoreLedgerTestData.CashAssetId,
                quantityDeltaE8: 500,
                EntryRole.Principal));
        await context.SaveChangesAsync();
        await CoreLedgerTestData.PostAsync(
            context,
            identities.OriginalId,
            new DateTime(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc));

        var multiEntryTransaction = CoreLedgerTestData.CreateDraftTransaction(
            identities.MultiEntryId,
            TransactionType.Adjustment,
            executionDate: new DateOnly(2026, 9, 1));
        multiEntryTransaction.ExternalReference = "SYNTHETIC-MULTI-ENTRY";
        multiEntryTransaction.Note = "Private synthetic navigation note.";
        context.LedgerTransactions.Add(multiEntryTransaction);
        context.TransactionEntries.AddRange(
            CoreLedgerTestData.CreateEntry(
                identities.MultiEntrySecondId,
                identities.MultiEntryId,
                sequence: 1,
                InactiveAssetId,
                quantityDeltaE8: 222,
                EntryRole.Adjustment,
                accountId: NullInstitutionAccountId),
            CoreLedgerTestData.CreateEntry(
                identities.MultiEntryFirstId,
                identities.MultiEntryId,
                sequence: 0,
                InactiveAssetId,
                quantityDeltaE8: 111,
                EntryRole.Adjustment,
                accountId: NullInstitutionAccountId));
        await context.SaveChangesAsync();
        await CoreLedgerTestData.PostAsync(
            context,
            identities.MultiEntryId,
            new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc));

        context.LedgerTransactions.Add(
            CoreLedgerTestData.CreateDraftTransaction(
                identities.NullInstitutionTransactionId,
                TransactionType.Adjustment,
                executionDate: new DateOnly(2026, 9, 2)));
        context.TransactionEntries.Add(
            CoreLedgerTestData.CreateEntry(
                identities.NullInstitutionEntryId,
                identities.NullInstitutionTransactionId,
                sequence: 0,
                InactiveAssetId,
                quantityDeltaE8: 900,
                EntryRole.Adjustment,
                accountId: NullInstitutionAccountId));
        await context.SaveChangesAsync();
        await CoreLedgerTestData.PostAsync(
            context,
            identities.NullInstitutionTransactionId,
            new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc));

        context.LedgerTransactions.Add(
            CoreLedgerTestData.CreateDraftTransaction(
                identities.ReversalId,
                TransactionType.Reversal,
                executionDate: CoreLedgerTestData.ExecutionDate,
                reversalOfTransactionId: identities.OriginalId));
        context.TransactionEntries.Add(
            CoreLedgerTestData.CreateEntry(
                identities.ReversalEntryId,
                identities.ReversalId,
                sequence: 0,
                CoreLedgerTestData.CashAssetId,
                quantityDeltaE8: -500,
                EntryRole.Principal));
        await context.SaveChangesAsync();
        await CoreLedgerTestData.PostAsync(
            context,
            identities.ReversalId,
            new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc));

        context.LedgerTransactions.AddRange(
            CoreLedgerTestData.CreateDraftTransaction(
                identities.DraftId,
                TransactionType.Adjustment),
            new LedgerTransactionRow
            {
                Id = identities.OrderedId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                Type = TransactionType.Buy,
                Status = TransactionStatus.Ordered,
                OrderDate = CoreLedgerTestData.ExecutionDate.AddDays(-1),
                ExecutionDate = CoreLedgerTestData.ExecutionDate,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            },
            new LedgerTransactionRow
            {
                Id = identities.CancelledId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                Type = TransactionType.Adjustment,
                Status = TransactionStatus.Cancelled,
                ExecutionDate = CoreLedgerTestData.ExecutionDate,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
        context.TransactionEntries.AddRange(
            CoreLedgerTestData.CreateEntry(
                identities.DraftEntryId,
                identities.DraftId,
                sequence: 0,
                CoreLedgerTestData.CashAssetId,
                quantityDeltaE8: 700,
                EntryRole.Adjustment),
            CoreLedgerTestData.CreateEntry(
                identities.OrderedEntryId,
                identities.OrderedId,
                sequence: 0,
                CoreLedgerTestData.CashAssetId,
                quantityDeltaE8: 750,
                EntryRole.Adjustment),
            CoreLedgerTestData.CreateEntry(
                identities.CancelledEntryId,
                identities.CancelledId,
                sequence: 0,
                CoreLedgerTestData.CashAssetId,
                quantityDeltaE8: 800,
                EntryRole.Adjustment));

        context.LedgerTransactions.Add(
            CoreLedgerTestData.CreateDraftTransaction(
                identities.OtherHouseholdPostedId,
                TransactionType.Adjustment,
                householdId: CoreLedgerTestData.OtherHouseholdId));
        context.TransactionEntries.Add(
            CoreLedgerTestData.CreateEntry(
                identities.OtherHouseholdPostedEntryId,
                identities.OtherHouseholdPostedId,
                sequence: 0,
                CoreLedgerTestData.CashAssetId,
                quantityDeltaE8: 1_000,
                EntryRole.Adjustment,
                portfolioId: CoreLedgerTestData.OtherPortfolioId,
                accountId: CoreLedgerTestData.OtherAccountId));
        await context.SaveChangesAsync();
        await CoreLedgerTestData.PostAsync(
            context,
            identities.OtherHouseholdPostedId,
            new DateTime(2026, 9, 2, 13, 0, 0, DateTimeKind.Utc));
    }

    private static WealthLedgerDbContext CreateContext(
        SqliteTestDatabase database,
        DbCommandInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<WealthLedgerDbContext>()
            .UseSqlite(database.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        return new WealthLedgerDbContext(options);
    }

    private static async Task<IReadOnlyList<T>> TraverseAsync<T>(
        SqliteTestDatabase database,
        Func<WealthLedgerDbContext, string?, Task<NavigationPage<T>>> readPage,
        string? initialCursor = null)
    {
        var items = new List<T>();
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        var cursor = initialCursor;

        do
        {
            await using var context = database.CreateContext();
            var page = await readPage(context, cursor);
            Assert.InRange(page.Items.Count, 0, 1);
            items.AddRange(page.Items);
            cursor = page.NextCursor;

            if (cursor is not null)
            {
                Assert.True(seenCursors.Add(cursor));
            }
        }
        while (cursor is not null);

        return items;
    }

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

    private sealed class LedgerIdentities
    {
        internal Guid OriginalId { get; } =
            Guid.Parse("70000000-0000-0000-0000-000000000001");
        internal Guid OriginalEntryId { get; } =
            Guid.Parse("71000000-0000-0000-0000-000000000001");
        internal Guid MultiEntryId { get; } =
            Guid.Parse("70000000-0000-0000-0000-000000000008");
        internal Guid MultiEntryFirstId { get; } =
            Guid.Parse("71000000-0000-0000-0000-000000000008");
        internal Guid MultiEntrySecondId { get; } =
            Guid.Parse("71000000-0000-0000-0000-000000000009");
        internal Guid NullInstitutionTransactionId { get; } =
            Guid.Parse("70000000-0000-0000-0000-000000000002");
        internal Guid NullInstitutionEntryId { get; } =
            Guid.Parse("71000000-0000-0000-0000-000000000002");
        internal Guid ReversalId { get; } =
            Guid.Parse("70000000-0000-0000-0000-000000000003");
        internal Guid ReversalEntryId { get; } =
            Guid.Parse("71000000-0000-0000-0000-000000000003");
        internal Guid DraftId { get; } =
            Guid.Parse("70000000-0000-0000-0000-000000000004");
        internal Guid DraftEntryId { get; } =
            Guid.Parse("71000000-0000-0000-0000-000000000004");
        internal Guid OrderedId { get; } =
            Guid.Parse("70000000-0000-0000-0000-000000000010");
        internal Guid OrderedEntryId { get; } =
            Guid.Parse("71000000-0000-0000-0000-000000000010");
        internal Guid CancelledId { get; } =
            Guid.Parse("70000000-0000-0000-0000-000000000005");
        internal Guid CancelledEntryId { get; } =
            Guid.Parse("71000000-0000-0000-0000-000000000005");
        internal Guid OtherHouseholdPostedId { get; } =
            Guid.Parse("70000000-0000-0000-0000-000000000006");
        internal Guid OtherHouseholdPostedEntryId { get; } =
            Guid.Parse("71000000-0000-0000-0000-000000000006");
        internal Guid NewlyPostedId { get; } =
            Guid.Parse("70000000-0000-0000-0000-000000000007");
        internal Guid NewlyPostedEntryId { get; } =
            Guid.Parse("71000000-0000-0000-0000-000000000007");
    }
}
