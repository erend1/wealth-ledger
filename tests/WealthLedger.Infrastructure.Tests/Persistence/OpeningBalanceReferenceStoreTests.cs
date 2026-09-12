using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;
using WealthLedger.Infrastructure.Persistence;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class OpeningBalanceReferenceStoreTests
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateUseCases_PersistReferencesWithoutLedgerHistory()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        await using (var seedContext = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(seedContext);
        }

        OpeningBalanceReferenceCreationResult<
            OpeningBalanceCurrencyReference> currencyResult;
        OpeningBalanceReferenceCreationResult<
            OpeningBalanceInstitutionReference> institutionResult;
        OpeningBalanceReferenceCreationResult<
            OpeningBalanceAccountReference> accountResult;
        OpeningBalanceReferenceCreationResult<
            OpeningBalanceAssetReference> assetResult;

        await using (var writeContext = database.CreateContext())
        {
            var store =
                new EfCoreOpeningBalanceReferenceStore(writeContext);

            currencyResult =
                await new CreateOpeningBalanceCurrencyUseCase(store)
                    .ExecuteAsync(
                        new CreateOpeningBalanceCurrencyCommand(
                            new CurrencyCode("jpy"),
                            "Synthetic Japanese Yen",
                            0));

            institutionResult =
                await new CreateOpeningBalanceInstitutionUseCase(store)
                    .ExecuteAsync(
                        new CreateOpeningBalanceInstitutionCommand(
                            "synthetic_jeweler",
                            "Synthetic Jeweler",
                            InstitutionType.Jeweler));

            accountResult =
                await new CreateOpeningBalanceAccountUseCase(store, store)
                    .ExecuteAsync(
                        new CreateOpeningBalanceAccountCommand(
                            CoreLedgerTestData.HouseholdId,
                            InstitutionId: null,
                            "synthetic_vault",
                            "Synthetic Physical Vault",
                            AccountType.PhysicalVault,
                            new DateOnly(2026, 1, 1)));

            assetResult =
                await new CreateOpeningBalanceAssetUseCase(
                        store,
                        store,
                        new FixedTimeProvider(CreatedAtUtc))
                    .ExecuteAsync(
                        new CreateOpeningBalanceAssetCommand(
                            "synthetic_equity",
                            "Synthetic Equity",
                            AssetType.Equity,
                            new CurrencyCode("jpy"),
                            LotTrackingMode.Optional));
        }

        await using var verificationContext = database.CreateContext();

        Assert.True(currencyResult.WasCreated);
        Assert.True(institutionResult.WasCreated);
        Assert.True(accountResult.WasCreated);
        Assert.True(assetResult.WasCreated);

        Assert.Equal(
            1,
            await verificationContext.Currencies.CountAsync(
                row => row.Code == "JPY"));
        Assert.Equal(
            institutionResult.Reference.InstitutionId,
            await verificationContext.Institutions
                .Where(row => row.Code == "SYNTHETIC_JEWELER")
                .Select(row => row.Id)
                .SingleAsync());
        Assert.Equal(
            accountResult.Reference.AccountId,
            await verificationContext.Accounts
                .Where(row => row.Code == "SYNTHETIC_VAULT")
                .Select(row => row.Id)
                .SingleAsync());

        var asset = await verificationContext.Assets
            .SingleAsync(row => row.Code == "SYNTHETIC_EQUITY");

        Assert.Equal(assetResult.Reference.AssetId, asset.Id);
        Assert.Equal(AssetType.Equity, asset.Type);
        Assert.Equal(AssetUnit.Share, asset.BaseUnit);
        Assert.Equal("JPY", asset.BaseCurrencyCode);
        Assert.Equal(LotTrackingMode.Optional, asset.LotTrackingMode);
        Assert.Equal(CreatedAtUtc.UtcDateTime, asset.CreatedAtUtc);
        Assert.Equal(0, await verificationContext.LedgerTransactions.CountAsync());
        Assert.Equal(0, await verificationContext.TransactionEntries.CountAsync());
        Assert.Equal(0, await verificationContext.AssetLots.CountAsync());
    }

    [Fact]
    public async Task EquivalentRetry_AfterReopenReturnsPersistedIdentities()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        await using (var seedContext = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(seedContext);
        }

        Guid institutionId;
        Guid accountId;
        Guid assetId;

        await using (var firstContext = database.CreateContext())
        {
            var store =
                new EfCoreOpeningBalanceReferenceStore(firstContext);

            institutionId =
                (await new CreateOpeningBalanceInstitutionUseCase(store)
                    .ExecuteAsync(CreateInstitutionCommand()))
                .Reference.InstitutionId;

            accountId =
                (await new CreateOpeningBalanceAccountUseCase(store, store)
                    .ExecuteAsync(CreateAccountCommand(institutionId)))
                .Reference.AccountId;

            assetId =
                (await new CreateOpeningBalanceAssetUseCase(
                        store,
                        store,
                        new FixedTimeProvider(CreatedAtUtc))
                    .ExecuteAsync(CreateAssetCommand()))
                .Reference.AssetId;
        }

        await using (var retryContext = database.CreateContext())
        {
            var store =
                new EfCoreOpeningBalanceReferenceStore(retryContext);

            var institution =
                await new CreateOpeningBalanceInstitutionUseCase(store)
                    .ExecuteAsync(CreateInstitutionCommand());
            var account =
                await new CreateOpeningBalanceAccountUseCase(store, store)
                    .ExecuteAsync(CreateAccountCommand(institutionId));
            var asset =
                await new CreateOpeningBalanceAssetUseCase(
                        store,
                        store,
                        new FixedTimeProvider(CreatedAtUtc.AddDays(1)))
                    .ExecuteAsync(CreateAssetCommand());

            Assert.False(institution.WasCreated);
            Assert.False(account.WasCreated);
            Assert.False(asset.WasCreated);
            Assert.Equal(institutionId, institution.Reference.InstitutionId);
            Assert.Equal(accountId, account.Reference.AccountId);
            Assert.Equal(assetId, asset.Reference.AssetId);
            Assert.Equal(CreatedAtUtc, asset.Reference.CreatedAtUtc);
        }
    }

    [Fact]
    public async Task SameCodeWithDifferentFacts_ReturnsConflictWithoutMutation()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        await using (var seedContext = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(seedContext);
        }

        await using (var firstContext = database.CreateContext())
        {
            var store =
                new EfCoreOpeningBalanceReferenceStore(firstContext);

            await new CreateOpeningBalanceCurrencyUseCase(store)
                .ExecuteAsync(
                    new CreateOpeningBalanceCurrencyCommand(
                        new CurrencyCode("eur"),
                        "Synthetic Euro",
                        2));
        }

        await using (var conflictContext = database.CreateContext())
        {
            var store =
                new EfCoreOpeningBalanceReferenceStore(conflictContext);

            var exception =
                await Assert.ThrowsAsync<OpeningBalanceException>(
                    () => new CreateOpeningBalanceCurrencyUseCase(store)
                        .ExecuteAsync(
                            new CreateOpeningBalanceCurrencyCommand(
                                new CurrencyCode("eur"),
                                "Different Synthetic Euro",
                                2)));

            Assert.Equal(
                OpeningBalanceErrorCodes.ReferenceConflict,
                exception.ErrorCode);
        }

        await using var verificationContext = database.CreateContext();
        var currency = await verificationContext.Currencies
            .SingleAsync(row => row.Code == "EUR");

        Assert.Equal("Synthetic Euro", currency.Name);
        Assert.Equal(2, currency.MinorUnitDigits);
    }

    [Fact]
    public async Task InactiveSameCode_ReturnsConflictInsteadOfReactivation()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        await using (var seedContext = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(seedContext);
            var institution = await seedContext.Institutions
                .SingleAsync(row => row.Id == CoreLedgerTestData.InstitutionId);
            institution.IsActive = false;
            await seedContext.SaveChangesAsync();
        }

        await using (var writeContext = database.CreateContext())
        {
            var store =
                new EfCoreOpeningBalanceReferenceStore(writeContext);

            var exception =
                await Assert.ThrowsAsync<OpeningBalanceException>(
                    () => new CreateOpeningBalanceInstitutionUseCase(store)
                        .ExecuteAsync(
                            new CreateOpeningBalanceInstitutionCommand(
                                "TEST_INSTITUTION",
                                "Test Institution",
                                InstitutionType.Broker)));

            Assert.Equal(
                OpeningBalanceErrorCodes.ReferenceConflict,
                exception.ErrorCode);
        }

        await using var verificationContext = database.CreateContext();
        Assert.False(
            await verificationContext.Institutions
                .Where(row => row.Id == CoreLedgerTestData.InstitutionId)
                .Select(row => row.IsActive)
                .SingleAsync());
    }

    [Fact]
    public async Task AccountCode_IsNaturallyIdempotentWithinHouseholdOnly()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        await using (var seedContext = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(seedContext);
        }

        await using (var writeContext = database.CreateContext())
        {
            var store =
                new EfCoreOpeningBalanceReferenceStore(writeContext);

            var result =
                await new CreateOpeningBalanceAccountUseCase(store, store)
                    .ExecuteAsync(
                        new CreateOpeningBalanceAccountCommand(
                            CoreLedgerTestData.OtherHouseholdId,
                            InstitutionId: null,
                            "PRIMARY",
                            "Synthetic Other Household Cash",
                            AccountType.Cash));

            Assert.True(result.WasCreated);
        }

        await using var verificationContext = database.CreateContext();

        Assert.Equal(
            2,
            await verificationContext.Accounts.CountAsync(
                row => row.Code == "PRIMARY"));
    }

    private static CreateOpeningBalanceInstitutionCommand
        CreateInstitutionCommand()
        => new(
            "SYNTHETIC_ASSET_MANAGER",
            "Synthetic Asset Manager",
            InstitutionType.AssetManager);

    private static CreateOpeningBalanceAccountCommand CreateAccountCommand(
        Guid institutionId)
        => new(
            CoreLedgerTestData.HouseholdId,
            institutionId,
            "SYNTHETIC_PENSION",
            "Synthetic Pension Account",
            AccountType.Pension,
            new DateOnly(2026, 1, 1));

    private static CreateOpeningBalanceAssetCommand CreateAssetCommand()
        => new(
            "SYNTHETIC_FUND",
            "Synthetic Investment Fund",
            AssetType.Fund,
            CurrencyCode.TRY,
            LotTrackingMode.Required);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        internal FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
            => _utcNow;
    }
}
