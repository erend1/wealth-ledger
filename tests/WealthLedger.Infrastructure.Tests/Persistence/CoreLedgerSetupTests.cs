using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Setup;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class CoreLedgerSetupTests
{
    private static readonly DateTimeOffset InitializedAtUtc = new(
        2026,
        8,
        24,
        12,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task Initialize_PersistsCompleteMasterGraphAfterReopen()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        InitializeCoreLedgerResult result;

        await using (var context = database.CreateContext())
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(CreateCommand());
        }

        await using (var context = database.CreateContext())
        {
            var currency = await context.Currencies
                .AsNoTracking()
                .SingleAsync();
            var household = await context.Households
                .AsNoTracking()
                .SingleAsync();
            var member = await context.HouseholdMembers
                .AsNoTracking()
                .SingleAsync();
            var institution = await context.Institutions
                .AsNoTracking()
                .SingleAsync();
            var portfolio = await context.Portfolios
                .AsNoTracking()
                .SingleAsync();
            var account = await context.Accounts
                .AsNoTracking()
                .SingleAsync();
            var assets = await context.Assets
                .AsNoTracking()
                .OrderBy(x => x.Code)
                .ToListAsync();

            Assert.Equal("TRY", currency.Code);
            Assert.Equal("Synthetic Currency", currency.Name);
            Assert.Equal(2, currency.MinorUnitDigits);
            Assert.Equal(result.HouseholdId, household.Id);
            Assert.Equal("TRY", household.BaseCurrencyCode);
            Assert.Equal(result.HouseholdMemberId, member.Id);
            Assert.Equal(result.HouseholdId, member.HouseholdId);
            Assert.Equal(result.InstitutionId, institution.Id);
            Assert.Equal(InstitutionType.Broker, institution.Type);
            Assert.Equal(result.PortfolioId, portfolio.Id);
            Assert.Equal(PortfolioStatus.Active, portfolio.Status);
            Assert.Equal(result.AccountId, account.Id);
            Assert.Equal(result.InstitutionId, account.InstitutionId);
            Assert.Equal(AccountType.Investment, account.Type);
            Assert.Equal(2, assets.Count);
            Assert.Contains(assets, x =>
                x.Id == result.CashAssetId
                && x.Type == AssetType.Cash
                && x.BaseUnit == AssetUnit.CurrencyUnit
                && x.LotTrackingMode == LotTrackingMode.None);
            Assert.Contains(assets, x =>
                x.Id == result.FundAssetId
                && x.Type == AssetType.Fund
                && x.BaseUnit == AssetUnit.FundUnit
                && x.LotTrackingMode == LotTrackingMode.Required);
        }
    }

    [Fact]
    public async Task Initialize_WhenMasterDataExists_WritesNothing()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        await using (var context = database.CreateContext())
        {
            context.Currencies.Add(new CurrencyRow
            {
                Code = "USD",
                Name = "Existing Synthetic Currency",
                MinorUnitDigits = 2
            });
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            var useCase = CreateUseCase(context);

            await Assert.ThrowsAsync<CoreLedgerAlreadyInitializedException>(
                () => useCase.ExecuteAsync(CreateCommand()));
        }

        await using (var context = database.CreateContext())
        {
            Assert.Equal(1, await context.Currencies.CountAsync());
            Assert.Equal(0, await context.Households.CountAsync());
            Assert.Equal(0, await context.HouseholdMembers.CountAsync());
            Assert.Equal(0, await context.Institutions.CountAsync());
            Assert.Equal(0, await context.Portfolios.CountAsync());
            Assert.Equal(0, await context.Accounts.CountAsync());
            Assert.Equal(0, await context.Assets.CountAsync());
        }
    }

    [Fact]
    public async Task Initialize_WhenFinalInsertFails_RollsBackEverything()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        await database.ExecuteNonQueryAsync(
            """
            CREATE TRIGGER RejectSyntheticFundSetup
            BEFORE INSERT ON Asset
            WHEN NEW.AssetTypeCode = 'FUND'
            BEGIN
                SELECT RAISE(ABORT, 'synthetic setup failure');
            END;
            """);

        await using (var context = database.CreateContext())
        {
            var useCase = CreateUseCase(context);

            await Assert.ThrowsAsync<CoreLedgerPersistenceException>(
                () => useCase.ExecuteAsync(CreateCommand()));
        }

        await using (var context = database.CreateContext())
        {
            Assert.Equal(0, await context.Currencies.CountAsync());
            Assert.Equal(0, await context.Households.CountAsync());
            Assert.Equal(0, await context.HouseholdMembers.CountAsync());
            Assert.Equal(0, await context.Institutions.CountAsync());
            Assert.Equal(0, await context.Portfolios.CountAsync());
            Assert.Equal(0, await context.Accounts.CountAsync());
            Assert.Equal(0, await context.Assets.CountAsync());
        }
    }

    private static InitializeCoreLedgerUseCase CreateUseCase(
        WealthLedgerDbContext context)
        => new(
            new EfCoreLedgerSetupStore(context),
            new FixedTimeProvider(InitializedAtUtc));

    private static InitializeCoreLedgerCommand CreateCommand()
        => new(
            new InitializeCurrencyInput(
                CurrencyCode.TRY,
                "Synthetic Currency",
                MinorUnitDigits: 2),
            HouseholdName: "Synthetic Household",
            HouseholdMemberDisplayName: "Synthetic Member",
            new InitializeInstitutionInput(
                "SYNTHETIC_INSTITUTION",
                "Synthetic Institution",
                InstitutionType.Broker),
            new InitializePortfolioInput(
                "CORE",
                "Core Portfolio"),
            new InitializeAccountInput(
                "PRIMARY",
                "Primary Account",
                AccountType.Investment,
                new DateOnly(2026, 1, 1)),
            new InitializeAssetInput(
                "SYNTHETIC_CASH",
                "Synthetic Cash"),
            new InitializeAssetInput(
                "SYNTHETIC_FUND",
                "Synthetic Fund"));

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
