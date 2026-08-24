using WealthLedger.Application.Common;
using WealthLedger.Application.Setup;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.Tests.Setup;

public sealed class InitializeCoreLedgerUseCaseTests
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
    public async Task Execute_CreatesNormalizedAtomicSetupGraph()
    {
        var store = new CapturingSetupStore();
        var useCase = CreateUseCase(store);

        var result = await useCase.ExecuteAsync(CreateCommand());

        var setup = Assert.IsType<CoreLedgerSetup>(store.Setup);
        Assert.Equal(InitializedAtUtc, setup.InitializedAtUtc);
        Assert.Equal("Synthetic Currency", setup.BaseCurrency.Name);
        Assert.Equal(CurrencyCode.TRY, setup.BaseCurrency.Code);
        Assert.Equal(2, setup.BaseCurrency.MinorUnitDigits);
        Assert.Equal(result.HouseholdId, setup.Household.Id);
        Assert.Equal(result.HouseholdMemberId, setup.HouseholdMember?.Id);
        Assert.Equal(result.InstitutionId, setup.Institution.Id);
        Assert.Equal(result.PortfolioId, setup.Portfolio.Id);
        Assert.Equal(result.AccountId, setup.Account.Id);
        Assert.Equal(result.CashAssetId, setup.CashAsset.Id);
        Assert.Equal(result.FundAssetId, setup.FundAsset.Id);
        Assert.Equal(setup.Household.Id, setup.Portfolio.HouseholdId);
        Assert.Equal(setup.Household.Id, setup.Account.HouseholdId);
        Assert.Equal(setup.Institution.Id, setup.Account.InstitutionId);
        Assert.Equal(AssetType.Cash, setup.CashAsset.Type);
        Assert.Equal(AssetUnit.CurrencyUnit, setup.CashAsset.BaseUnit);
        Assert.Equal(LotTrackingMode.None, setup.CashAsset.LotTrackingMode);
        Assert.Equal(AssetType.Fund, setup.FundAsset.Type);
        Assert.Equal(AssetUnit.FundUnit, setup.FundAsset.BaseUnit);
        Assert.Equal(LotTrackingMode.Required, setup.FundAsset.LotTrackingMode);
        Assert.Equal(CurrencyCode.TRY, setup.CashAsset.BaseCurrency);
        Assert.Equal(CurrencyCode.TRY, setup.FundAsset.BaseCurrency);
    }

    [Fact]
    public async Task Execute_AllowsSetupWithoutHouseholdMember()
    {
        var store = new CapturingSetupStore();
        var useCase = CreateUseCase(store);
        var command = CreateCommand() with
        {
            HouseholdMemberDisplayName = null
        };

        var result = await useCase.ExecuteAsync(command);

        Assert.Null(result.HouseholdMemberId);
        Assert.Null(store.Setup?.HouseholdMember);
    }

    [Fact]
    public async Task Execute_WhenAlreadyInitialized_ReportsConflict()
    {
        var store = new CapturingSetupStore
        {
            ShouldInitialize = false
        };
        var useCase = CreateUseCase(store);

        await Assert.ThrowsAsync<CoreLedgerAlreadyInitializedException>(
            () => useCase.ExecuteAsync(CreateCommand()));
    }

    [Fact]
    public async Task Execute_RejectsDuplicateAssetCodesBeforeWriting()
    {
        var store = new CapturingSetupStore();
        var useCase = CreateUseCase(store);
        var command = CreateCommand() with
        {
            FundAsset = new InitializeAssetInput(
                " synthetic_cash ",
                "Synthetic Fund")
        };

        var exception = await Assert.ThrowsAsync<ApplicationRuleViolationException>(
            () => useCase.ExecuteAsync(command));

        Assert.Contains("different codes", exception.Message);
        Assert.Null(store.Setup);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public async Task Execute_RejectsInvalidMinorUnitDigitsBeforeWriting(
        int minorUnitDigits)
    {
        var store = new CapturingSetupStore();
        var useCase = CreateUseCase(store);
        var command = CreateCommand() with
        {
            BaseCurrency = new InitializeCurrencyInput(
                CurrencyCode.TRY,
                "Synthetic Currency",
                minorUnitDigits)
        };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => useCase.ExecuteAsync(command));

        Assert.Null(store.Setup);
    }

    private static InitializeCoreLedgerUseCase CreateUseCase(
        CapturingSetupStore store)
        => new(
            store,
            new FixedTimeProvider(InitializedAtUtc));

    private static InitializeCoreLedgerCommand CreateCommand()
        => new(
            new InitializeCurrencyInput(
                CurrencyCode.TRY,
                "  Synthetic Currency  ",
                MinorUnitDigits: 2),
            HouseholdName: "Synthetic Household",
            HouseholdMemberDisplayName: "Synthetic Member",
            new InitializeInstitutionInput(
                " synthetic_institution ",
                "Synthetic Institution",
                InstitutionType.Broker),
            new InitializePortfolioInput(
                " core ",
                "Core Portfolio"),
            new InitializeAccountInput(
                " primary ",
                "Primary Account",
                AccountType.Investment,
                new DateOnly(2026, 1, 1)),
            new InitializeAssetInput(
                " synthetic_cash ",
                "Synthetic Cash"),
            new InitializeAssetInput(
                " synthetic_fund ",
                "Synthetic Fund"));

    private sealed class CapturingSetupStore : ICoreLedgerSetupStore
    {
        internal bool ShouldInitialize { get; init; } = true;

        internal CoreLedgerSetup? Setup { get; private set; }

        public Task<bool> TryInitializeAsync(
            CoreLedgerSetup setup,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Setup = setup;
            return Task.FromResult(ShouldInitialize);
        }
    }

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
