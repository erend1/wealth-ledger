using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.Setup;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class EfCoreLedgerSetupStateReaderTests
{
    private static readonly DateTime CreatedAtUtc =
        new(
            2026,
            9,
            4,
            8,
            0,
            0,
            DateTimeKind.Utc);

    [Fact]
    public async Task ReadAsync_EmptyDatabase_ReturnsEmpty()
    {
        await using var database =
            await SqliteTestDatabase.CreateAsync();

        await using var context =
            database.CreateContext();

        var reader =
            new EfCoreLedgerSetupStateReader(
                context);

        var result =
            await reader.ReadAsync();

        Assert.True(
            result.Succeeded,
            result.Failure?.Message);

        Assert.NotNull(
            result.Value);

        Assert.Equal(
            CoreLedgerSetupState.Empty,
            result.Value!.State);
    }

    [Fact]
    public async Task ReadAsync_CompleteBootstrapGraph_ReturnsComplete()
    {
        await using var database =
            await SqliteTestDatabase.CreateAsync();

        await using (var context =
                     database.CreateContext())
        {
            await AddCompleteGraphAsync(
                context,
                includeHouseholdMember: true);
        }

        await using (var context =
                     database.CreateContext())
        {
            var reader =
                new EfCoreLedgerSetupStateReader(
                    context);

            var result =
                await reader.ReadAsync();

            Assert.True(
                result.Succeeded,
                result.Failure?.Message);

            Assert.Equal(
                CoreLedgerSetupState.Complete,
                result.Value!.State);
        }
    }

    [Fact]
    public async Task ReadAsync_CompleteGraphWithoutHouseholdMember_ReturnsComplete()
    {
        await using var database =
            await SqliteTestDatabase.CreateAsync();

        await using (var context =
                     database.CreateContext())
        {
            await AddCompleteGraphAsync(
                context,
                includeHouseholdMember: false);
        }

        await using (var context =
                     database.CreateContext())
        {
            var reader =
                new EfCoreLedgerSetupStateReader(
                    context);

            var result =
                await reader.ReadAsync();

            Assert.True(
                result.Succeeded,
                result.Failure?.Message);

            Assert.Equal(
                CoreLedgerSetupState.Complete,
                result.Value!.State);
        }
    }

    [Fact]
    public async Task ReadAsync_CurrencyOnly_ReturnsPartialOrConflicting()
    {
        await using var database =
            await SqliteTestDatabase.CreateAsync();

        await using (var context =
                     database.CreateContext())
        {
            context.Currencies.Add(
                new CurrencyRow
                {
                    Code = "TRY",
                    Name = "Synthetic Currency",
                    MinorUnitDigits = 2
                });

            await context.SaveChangesAsync();
        }

        await using (var context =
                     database.CreateContext())
        {
            var reader =
                new EfCoreLedgerSetupStateReader(
                    context);

            var result =
                await reader.ReadAsync();

            Assert.True(
                result.Succeeded,
                result.Failure?.Message);

            Assert.Equal(
                CoreLedgerSetupState.PartialOrConflicting,
                result.Value!.State);
        }
    }

    [Fact]
    public async Task ReadAsync_MissingPortfolio_ReturnsPartialOrConflicting()
    {
        await using var database =
            await SqliteTestDatabase.CreateAsync();

        await using (var context =
                     database.CreateContext())
        {
            await AddCompleteGraphAsync(
                context,
                includeHouseholdMember: false,
                includePortfolio: false);
        }

        await using (var context =
                     database.CreateContext())
        {
            var reader =
                new EfCoreLedgerSetupStateReader(
                    context);

            var result =
                await reader.ReadAsync();

            Assert.True(
                result.Succeeded,
                result.Failure?.Message);

            Assert.Equal(
                CoreLedgerSetupState.PartialOrConflicting,
                result.Value!.State);
        }
    }

    [Fact]
    public async Task ReadAsync_MissingRequiredFundAsset_ReturnsPartialOrConflicting()
    {
        await using var database =
            await SqliteTestDatabase.CreateAsync();

        await using (var context =
                     database.CreateContext())
        {
            await AddCompleteGraphAsync(
                context,
                includeHouseholdMember: false,
                includeFundAsset: false);
        }

        await using (var context =
                     database.CreateContext())
        {
            var reader =
                new EfCoreLedgerSetupStateReader(
                    context);

            var result =
                await reader.ReadAsync();

            Assert.True(
                result.Succeeded,
                result.Failure?.Message);

            Assert.Equal(
                CoreLedgerSetupState.PartialOrConflicting,
                result.Value!.State);
        }
    }

    [Fact]
    public async Task ReadAsync_MultipleHouseholds_ReturnsPartialOrConflicting()
    {
        await using var database =
            await SqliteTestDatabase.CreateAsync();

        await using (var context =
                     database.CreateContext())
        {
            await AddCompleteGraphAsync(
                context,
                includeHouseholdMember: false);

            context.Households.Add(
                new HouseholdRow
                {
                    Id = Guid.NewGuid(),
                    Name =
                        "Second Synthetic Household",
                    BaseCurrencyCode = "TRY",
                    CreatedAtUtc = CreatedAtUtc
                });

            await context.SaveChangesAsync();
        }

        await using (var context =
                     database.CreateContext())
        {
            var reader =
                new EfCoreLedgerSetupStateReader(
                    context);

            var result =
                await reader.ReadAsync();

            Assert.True(
                result.Succeeded,
                result.Failure?.Message);

            Assert.Equal(
                CoreLedgerSetupState.PartialOrConflicting,
                result.Value!.State);
        }
    }

    [Fact]
    public async Task ReadAsync_AdditionalValidMasterRows_RemainsComplete()
    {
        await using var database =
            await SqliteTestDatabase.CreateAsync();

        await using (var context =
                     database.CreateContext())
        {
            var graph =
                await AddCompleteGraphAsync(
                    context,
                    includeHouseholdMember: false);

            var secondInstitutionId =
                Guid.NewGuid();

            context.Institutions.Add(
                new InstitutionRow
                {
                    Id = secondInstitutionId,
                    Code = "SYNTHETIC_SECOND",
                    Name =
                        "Second Synthetic Institution",
                    Type =
                        InstitutionType.Bank,
                    IsActive = true
                });

            context.Accounts.Add(
                new AccountRow
                {
                    Id = Guid.NewGuid(),
                    HouseholdId =
                        graph.HouseholdId,
                    InstitutionId =
                        secondInstitutionId,
                    Code = "SECONDARY",
                    Name =
                        "Secondary Synthetic Account",
                    Type =
                        AccountType.Investment,
                    IsActive = true,
                    OpenedOn =
                        new DateOnly(
                            2026,
                            9,
                            1)
                });

            context.Assets.Add(
                new AssetRow
                {
                    Id = Guid.NewGuid(),
                    Code = "SECOND_FUND",
                    Name =
                        "Second Synthetic Fund",
                    Type = AssetType.Fund,
                    BaseUnit =
                        AssetUnit.FundUnit,
                    BaseCurrencyCode = "TRY",
                    LotTrackingMode =
                        LotTrackingMode.Required,
                    IsActive = true,
                    CreatedAtUtc =
                        CreatedAtUtc
                });

            await context.SaveChangesAsync();
        }

        await using (var context =
                     database.CreateContext())
        {
            var reader =
                new EfCoreLedgerSetupStateReader(
                    context);

            var result =
                await reader.ReadAsync();

            Assert.True(
                result.Succeeded,
                result.Failure?.Message);

            Assert.Equal(
                CoreLedgerSetupState.Complete,
                result.Value!.State);
        }
    }

    [Fact]
    public async Task ReadAsync_DoesNotTrackOrWriteMasterRows()
    {
        await using var database =
            await SqliteTestDatabase.CreateAsync();

        await using (var seedContext =
                     database.CreateContext())
        {
            await AddCompleteGraphAsync(
                seedContext,
                includeHouseholdMember: false);
        }

        await using var context =
            database.CreateContext();

        var reader =
            new EfCoreLedgerSetupStateReader(
                context);

        var result =
            await reader.ReadAsync();

        Assert.True(
            result.Succeeded,
            result.Failure?.Message);

        Assert.Empty(
            context.ChangeTracker.Entries());

        Assert.False(
            context.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task ReadAsync_WhenCancelled_ReturnsCancelled()
    {
        await using var database =
            await SqliteTestDatabase.CreateAsync();

        await using var context =
            database.CreateContext();

        var reader =
            new EfCoreLedgerSetupStateReader(
                context);

        using var cancellationSource =
            new CancellationTokenSource();

        cancellationSource.Cancel();

        var result =
            await reader.ReadAsync(
                cancellationSource.Token);

        Assert.False(
            result.Succeeded);

        Assert.NotNull(
            result.Failure);

        Assert.Equal(
            Application.LocalData
                .LocalDataFailureCategory.Cancelled,
            result.Failure!.Category);
    }

    private static async Task<SetupGraphIds>
        AddCompleteGraphAsync(
            WealthLedgerDbContext context,
            bool includeHouseholdMember,
            bool includePortfolio = true,
            bool includeFundAsset = true)
    {
        var householdId =
            Guid.NewGuid();

        var institutionId =
            Guid.NewGuid();

        context.Currencies.Add(
            new CurrencyRow
            {
                Code = "TRY",
                Name = "Synthetic Currency",
                MinorUnitDigits = 2
            });

        context.Households.Add(
            new HouseholdRow
            {
                Id = householdId,
                Name = "Synthetic Household",
                BaseCurrencyCode = "TRY",
                CreatedAtUtc = CreatedAtUtc
            });

        if (includeHouseholdMember)
        {
            context.HouseholdMembers.Add(
                new HouseholdMemberRow
                {
                    Id = Guid.NewGuid(),
                    HouseholdId = householdId,
                    DisplayName =
                        "Synthetic Member",
                    IsActive = true,
                    CreatedAtUtc =
                        CreatedAtUtc
                });
        }

        context.Institutions.Add(
            new InstitutionRow
            {
                Id = institutionId,
                Code =
                    "SYNTHETIC_INSTITUTION",
                Name =
                    "Synthetic Institution",
                Type =
                    InstitutionType.Broker,
                IsActive = true
            });

        if (includePortfolio)
        {
            context.Portfolios.Add(
                new PortfolioRow
                {
                    Id = Guid.NewGuid(),
                    HouseholdId =
                        householdId,
                    Code = "CORE",
                    Name =
                        "Core Portfolio",
                    Status =
                        PortfolioStatus.Active,
                    CreatedAtUtc =
                        CreatedAtUtc
                });
        }

        context.Accounts.Add(
            new AccountRow
            {
                Id = Guid.NewGuid(),
                HouseholdId =
                    householdId,
                InstitutionId =
                    institutionId,
                Code = "PRIMARY",
                Name =
                    "Primary Account",
                Type =
                    AccountType.Investment,
                IsActive = true,
                OpenedOn =
                    new DateOnly(
                        2026,
                        1,
                        1)
            });

        context.Assets.Add(
            new AssetRow
            {
                Id = Guid.NewGuid(),
                Code = "SYNTHETIC_CASH",
                Name = "Synthetic Cash",
                Type = AssetType.Cash,
                BaseUnit =
                    AssetUnit.CurrencyUnit,
                BaseCurrencyCode = "TRY",
                LotTrackingMode =
                    LotTrackingMode.None,
                IsActive = true,
                CreatedAtUtc =
                    CreatedAtUtc
            });

        if (includeFundAsset)
        {
            context.Assets.Add(
                new AssetRow
                {
                    Id = Guid.NewGuid(),
                    Code =
                        "SYNTHETIC_FUND",
                    Name =
                        "Synthetic Fund",
                    Type =
                        AssetType.Fund,
                    BaseUnit =
                        AssetUnit.FundUnit,
                    BaseCurrencyCode =
                        "TRY",
                    LotTrackingMode =
                        LotTrackingMode.Required,
                    IsActive = true,
                    CreatedAtUtc =
                        CreatedAtUtc
                });
        }

        await context.SaveChangesAsync();

        return new SetupGraphIds(
            householdId,
            institutionId);
    }

    private sealed record SetupGraphIds(
        Guid HouseholdId,
        Guid InstitutionId);
}