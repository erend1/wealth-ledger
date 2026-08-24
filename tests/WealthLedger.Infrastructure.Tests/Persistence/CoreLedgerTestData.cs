using Microsoft.EntityFrameworkCore;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Tests.Persistence;

internal static class CoreLedgerTestData
{
    internal static readonly Guid HouseholdId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    internal static readonly Guid OtherHouseholdId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    internal static readonly Guid HouseholdMemberId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    internal static readonly Guid InstitutionId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    internal static readonly Guid PortfolioId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    internal static readonly Guid OtherPortfolioId = Guid.Parse("40000000-0000-0000-0000-000000000002");
    internal static readonly Guid AccountId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    internal static readonly Guid DestinationAccountId = Guid.Parse("50000000-0000-0000-0000-000000000002");
    internal static readonly Guid OtherAccountId = Guid.Parse("50000000-0000-0000-0000-000000000003");
    internal static readonly Guid CashAssetId = Guid.Parse("60000000-0000-0000-0000-000000000001");
    internal static readonly Guid FundAssetId = Guid.Parse("60000000-0000-0000-0000-000000000002");
    internal static readonly Guid OtherFundAssetId = Guid.Parse("60000000-0000-0000-0000-000000000003");
    internal static readonly Guid GoldAssetId = Guid.Parse("60000000-0000-0000-0000-000000000004");

    internal static readonly DateTime CreatedAtUtc = new(
        2026,
        8,
        24,
        8,
        0,
        0,
        DateTimeKind.Utc);

    internal static readonly DateOnly ExecutionDate = new(2026, 8, 24);

    internal static async Task SeedMasterDataAsync(WealthLedgerDbContext context)
    {
        context.Currencies.AddRange(
            new CurrencyRow
            {
                Code = "TRY",
                Name = "Test Currency",
                MinorUnitDigits = 2
            },
            new CurrencyRow
            {
                Code = "USD",
                Name = "Test Currency Two",
                MinorUnitDigits = 2
            });

        context.Households.AddRange(
            new HouseholdRow
            {
                Id = HouseholdId,
                Name = "Test Household",
                BaseCurrencyCode = "TRY",
                CreatedAtUtc = CreatedAtUtc
            },
            new HouseholdRow
            {
                Id = OtherHouseholdId,
                Name = "Other Test Household",
                BaseCurrencyCode = "TRY",
                CreatedAtUtc = CreatedAtUtc
            });

        context.HouseholdMembers.Add(new HouseholdMemberRow
        {
            Id = HouseholdMemberId,
            HouseholdId = HouseholdId,
            DisplayName = "Test Member",
            IsActive = true,
            CreatedAtUtc = CreatedAtUtc
        });

        context.Institutions.Add(new InstitutionRow
        {
            Id = InstitutionId,
            Code = "TEST_INSTITUTION",
            Name = "Test Institution",
            Type = InstitutionType.Broker,
            IsActive = true
        });

        context.Portfolios.AddRange(
            new PortfolioRow
            {
                Id = PortfolioId,
                HouseholdId = HouseholdId,
                Code = "CORE",
                Name = "Core Portfolio",
                Status = PortfolioStatus.Active,
                CreatedAtUtc = CreatedAtUtc
            },
            new PortfolioRow
            {
                Id = OtherPortfolioId,
                HouseholdId = OtherHouseholdId,
                Code = "OTHER",
                Name = "Other Portfolio",
                Status = PortfolioStatus.Active,
                CreatedAtUtc = CreatedAtUtc
            });

        context.Accounts.AddRange(
            new AccountRow
            {
                Id = AccountId,
                HouseholdId = HouseholdId,
                InstitutionId = InstitutionId,
                Code = "PRIMARY",
                Name = "Primary Account",
                Type = AccountType.Investment,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            },
            new AccountRow
            {
                Id = DestinationAccountId,
                HouseholdId = HouseholdId,
                InstitutionId = InstitutionId,
                Code = "SECONDARY",
                Name = "Secondary Account",
                Type = AccountType.Investment,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            },
            new AccountRow
            {
                Id = OtherAccountId,
                HouseholdId = OtherHouseholdId,
                Code = "OTHER",
                Name = "Other Account",
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
                CreatedAtUtc = CreatedAtUtc
            },
            new AssetRow
            {
                Id = FundAssetId,
                Code = "FUND_A",
                Name = "Test Fund A",
                Type = AssetType.Fund,
                BaseUnit = AssetUnit.FundUnit,
                BaseCurrencyCode = "TRY",
                LotTrackingMode = LotTrackingMode.Required,
                IsActive = true,
                CreatedAtUtc = CreatedAtUtc
            },
            new AssetRow
            {
                Id = OtherFundAssetId,
                Code = "FUND_B",
                Name = "Test Fund B",
                Type = AssetType.Fund,
                BaseUnit = AssetUnit.FundUnit,
                BaseCurrencyCode = "TRY",
                LotTrackingMode = LotTrackingMode.Required,
                IsActive = true,
                CreatedAtUtc = CreatedAtUtc
            },
            new AssetRow
            {
                Id = GoldAssetId,
                Code = "GOLD_TEST",
                Name = "Test Physical Gold",
                Type = AssetType.PhysicalGold,
                BaseUnit = AssetUnit.GrossGram,
                BaseCurrencyCode = "TRY",
                LotTrackingMode = LotTrackingMode.Required,
                IsActive = true,
                CreatedAtUtc = CreatedAtUtc
            });

        await context.SaveChangesAsync();
    }

    internal static LedgerTransactionRow CreateDraftTransaction(
        Guid id,
        TransactionType type,
        Guid? householdId = null,
        DateOnly? executionDate = null,
        Guid? reversalOfTransactionId = null)
        => new()
        {
            Id = id,
            HouseholdId = householdId ?? HouseholdId,
            Type = type,
            Status = TransactionStatus.Draft,
            ExecutionDate = executionDate ?? ExecutionDate,
            ReversalOfTransactionId = reversalOfTransactionId,
            CreatedAtUtc = CreatedAtUtc
        };

    internal static TransactionEntryRow CreateEntry(
        Guid id,
        Guid transactionId,
        int sequence,
        Guid assetId,
        long quantityDeltaE8,
        EntryRole role,
        Guid? portfolioId = null,
        Guid? accountId = null,
        long? unitPriceE8 = null,
        string? priceCurrencyCode = null)
        => new()
        {
            Id = id,
            TransactionId = transactionId,
            EntrySequence = sequence,
            PortfolioId = portfolioId ?? PortfolioId,
            AccountId = accountId ?? AccountId,
            AssetId = assetId,
            QuantityDeltaE8 = quantityDeltaE8,
            Role = role,
            UnitPriceE8 = unitPriceE8,
            PriceCurrencyCode = priceCurrencyCode,
            CreatedAtUtc = CreatedAtUtc
        };

    internal static async Task PostAsync(
        WealthLedgerDbContext context,
        Guid transactionId,
        DateTime? postedAtUtc = null)
    {
        var transaction = await context.LedgerTransactions.SingleAsync(x => x.Id == transactionId);
        transaction.Status = TransactionStatus.Posted;
        transaction.PostedAtUtc = postedAtUtc ?? CreatedAtUtc.AddMinutes(1);
        await context.SaveChangesAsync();
    }
}
