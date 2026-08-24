using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Api.Tests;

internal static class ApiTestData
{
    internal static readonly Guid HouseholdId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");

    internal static readonly Guid HouseholdMemberId =
        Guid.Parse("20000000-0000-0000-0000-000000000001");

    internal static readonly Guid InstitutionId =
        Guid.Parse("30000000-0000-0000-0000-000000000001");

    internal static readonly Guid PortfolioId =
        Guid.Parse("40000000-0000-0000-0000-000000000001");

    internal static readonly Guid AccountId =
        Guid.Parse("50000000-0000-0000-0000-000000000001");

    internal static readonly Guid CashAssetId =
        Guid.Parse("60000000-0000-0000-0000-000000000001");

    internal static readonly Guid FundAssetId =
        Guid.Parse("60000000-0000-0000-0000-000000000002");

    internal static readonly DateOnly ExecutionDate = new(2026, 8, 24);

    internal static async Task SeedAsync(WealthLedgerDbContext context)
    {
        var createdAtUtc = new DateTime(
            2026,
            8,
            24,
            8,
            0,
            0,
            DateTimeKind.Utc);

        context.Currencies.Add(new CurrencyRow
        {
            Code = "TRY",
            Name = "Test Currency",
            MinorUnitDigits = 2
        });

        context.Households.Add(new HouseholdRow
        {
            Id = HouseholdId,
            Name = "Test Household",
            BaseCurrencyCode = "TRY",
            CreatedAtUtc = createdAtUtc
        });

        context.HouseholdMembers.Add(new HouseholdMemberRow
        {
            Id = HouseholdMemberId,
            HouseholdId = HouseholdId,
            DisplayName = "Test Member",
            IsActive = true,
            CreatedAtUtc = createdAtUtc
        });

        context.Institutions.Add(new InstitutionRow
        {
            Id = InstitutionId,
            Code = "TEST_INSTITUTION",
            Name = "Test Institution",
            Type = InstitutionType.Broker,
            IsActive = true
        });

        context.Portfolios.Add(new PortfolioRow
        {
            Id = PortfolioId,
            HouseholdId = HouseholdId,
            Code = "CORE",
            Name = "Core Portfolio",
            Status = PortfolioStatus.Active,
            CreatedAtUtc = createdAtUtc
        });

        context.Accounts.Add(new AccountRow
        {
            Id = AccountId,
            HouseholdId = HouseholdId,
            InstitutionId = InstitutionId,
            Code = "PRIMARY",
            Name = "Primary Account",
            Type = AccountType.Investment,
            IsActive = true,
            OpenedOn = new DateOnly(2026, 1, 1)
        });

        context.Assets.AddRange(
            new AssetRow
            {
                Id = CashAssetId,
                Code = "TRY_CASH",
                Name = "Test Cash Asset",
                Type = AssetType.Cash,
                BaseUnit = AssetUnit.CurrencyUnit,
                BaseCurrencyCode = "TRY",
                LotTrackingMode = LotTrackingMode.None,
                IsActive = true,
                CreatedAtUtc = createdAtUtc
            },
            new AssetRow
            {
                Id = FundAssetId,
                Code = "FUND_A",
                Name = "Test Fund",
                Type = AssetType.Fund,
                BaseUnit = AssetUnit.FundUnit,
                BaseCurrencyCode = "TRY",
                LotTrackingMode = LotTrackingMode.Required,
                IsActive = true,
                CreatedAtUtc = createdAtUtc
            });

        await context.SaveChangesAsync();
    }
}
