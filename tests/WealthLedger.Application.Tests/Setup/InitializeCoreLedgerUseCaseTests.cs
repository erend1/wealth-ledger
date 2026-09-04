using WealthLedger.Application.Common;
using WealthLedger.Application.LocalData;
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
        var factory = new CapturingSetupSessionFactory();
        var useCase = CreateUseCase(factory);

        var result = await useCase.ExecuteAsync(CreateCommand());

        var setup = Assert.IsType<CoreLedgerSetup>(
            factory.Session.Setup);

        Assert.Equal(
            InitializedAtUtc,
            setup.InitializedAtUtc);

        Assert.Equal(
            "Synthetic Currency",
            setup.BaseCurrency.Name);

        Assert.Equal(
            CurrencyCode.TRY,
            setup.BaseCurrency.Code);

        Assert.Equal(
            2,
            setup.BaseCurrency.MinorUnitDigits);

        Assert.Equal(
            result.HouseholdId,
            setup.Household.Id);

        Assert.Equal(
            result.HouseholdMemberId,
            setup.HouseholdMember?.Id);

        Assert.Equal(
            result.InstitutionId,
            setup.Institution.Id);

        Assert.Equal(
            result.PortfolioId,
            setup.Portfolio.Id);

        Assert.Equal(
            result.AccountId,
            setup.Account.Id);

        Assert.Equal(
            result.CashAssetId,
            setup.CashAsset.Id);

        Assert.Equal(
            result.FundAssetId,
            setup.FundAsset.Id);

        Assert.Equal(
            setup.Household.Id,
            setup.Portfolio.HouseholdId);

        Assert.Equal(
            setup.Household.Id,
            setup.Account.HouseholdId);

        Assert.Equal(
            setup.Institution.Id,
            setup.Account.InstitutionId);

        Assert.Equal(
            AssetType.Cash,
            setup.CashAsset.Type);

        Assert.Equal(
            AssetUnit.CurrencyUnit,
            setup.CashAsset.BaseUnit);

        Assert.Equal(
            LotTrackingMode.None,
            setup.CashAsset.LotTrackingMode);

        Assert.Equal(
            AssetType.Fund,
            setup.FundAsset.Type);

        Assert.Equal(
            AssetUnit.FundUnit,
            setup.FundAsset.BaseUnit);

        Assert.Equal(
            LotTrackingMode.Required,
            setup.FundAsset.LotTrackingMode);

        Assert.Equal(
            CurrencyCode.TRY,
            setup.CashAsset.BaseCurrency);

        Assert.Equal(
            CurrencyCode.TRY,
            setup.FundAsset.BaseCurrency);

        Assert.Equal(
            1,
            factory.OpenCount);

        Assert.True(
            factory.Session.IsDisposed);
    }

    [Fact]
    public async Task Execute_AllowsSetupWithoutHouseholdMember()
    {
        var factory = new CapturingSetupSessionFactory();
        var useCase = CreateUseCase(factory);

        var command = CreateCommand() with
        {
            HouseholdMemberDisplayName = null
        };

        var result = await useCase.ExecuteAsync(command);

        Assert.Null(
            result.HouseholdMemberId);

        Assert.Null(
            factory.Session.Setup?.HouseholdMember);

        Assert.True(
            factory.Session.IsDisposed);
    }

    [Fact]
    public async Task Execute_WhenAlreadyInitialized_ReportsConflict()
    {
        var factory = new CapturingSetupSessionFactory();

        factory.Session.ShouldInitialize = false;

        var useCase = CreateUseCase(factory);

        await Assert.ThrowsAsync<CoreLedgerAlreadyInitializedException>(
            () => useCase.ExecuteAsync(CreateCommand()));

        Assert.Equal(
            1,
            factory.OpenCount);

        Assert.NotNull(
            factory.Session.Setup);

        Assert.True(
            factory.Session.IsDisposed);
    }

    [Fact]
    public async Task Execute_RejectsDuplicateAssetCodesBeforeOpeningSession()
    {
        var factory = new CapturingSetupSessionFactory();
        var useCase = CreateUseCase(factory);

        var command = CreateCommand() with
        {
            FundAsset = new InitializeAssetInput(
                " synthetic_cash ",
                "Synthetic Fund")
        };

        var exception =
            await Assert.ThrowsAsync<ApplicationRuleViolationException>(
                () => useCase.ExecuteAsync(command));

        Assert.Contains(
            "different codes",
            exception.Message);

        Assert.Equal(
            0,
            factory.OpenCount);

        Assert.Null(
            factory.Session.Setup);

        Assert.False(
            factory.Session.IsDisposed);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public async Task Execute_RejectsInvalidMinorUnitDigitsBeforeOpeningSession(
        int minorUnitDigits)
    {
        var factory = new CapturingSetupSessionFactory();
        var useCase = CreateUseCase(factory);

        var command = CreateCommand() with
        {
            BaseCurrency = new InitializeCurrencyInput(
                CurrencyCode.TRY,
                "Synthetic Currency",
                minorUnitDigits)
        };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => useCase.ExecuteAsync(command));

        Assert.Equal(
            0,
            factory.OpenCount);

        Assert.Null(
            factory.Session.Setup);

        Assert.False(
            factory.Session.IsDisposed);
    }

    [Fact]
    public async Task Execute_WhenSetupOwnershipIsBusy_ReportsSanitizedUnavailable()
    {
        const string privateFailureDetail =
            "Synthetic private lock path: C:\\private\\wealthledger.db.lock";

        var factory = new CapturingSetupSessionFactory
        {
            FailureCategory =
                LocalDataFailureCategory.OwnershipBusy,

            FailureMessage =
                privateFailureDetail
        };

        var useCase = CreateUseCase(factory);

        var exception =
            await Assert.ThrowsAsync<CoreLedgerSetupUnavailableException>(
                () => useCase.ExecuteAsync(CreateCommand()));

        Assert.Equal(
            LocalDataFailureCategory.OwnershipBusy,
            exception.Category);

        Assert.DoesNotContain(
            privateFailureDetail,
            exception.Message);

        Assert.DoesNotContain(
            "wealthledger.db.lock",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            1,
            factory.OpenCount);

        Assert.Null(
            factory.Session.Setup);

        Assert.False(
            factory.Session.IsDisposed);
    }

    [Fact]
    public async Task Execute_WhenSessionOpeningFails_DoesNotAttemptSetup()
    {
        var factory = new CapturingSetupSessionFactory
        {
            FailureCategory =
                LocalDataFailureCategory.DatabaseNotReady,

            FailureMessage =
                "Synthetic infrastructure diagnostic."
        };

        var useCase = CreateUseCase(factory);

        var exception =
            await Assert.ThrowsAsync<CoreLedgerSetupUnavailableException>(
                () => useCase.ExecuteAsync(CreateCommand()));

        Assert.Equal(
            LocalDataFailureCategory.DatabaseNotReady,
            exception.Category);

        Assert.Equal(
            1,
            factory.OpenCount);

        Assert.Null(
            factory.Session.Setup);

        Assert.False(
            factory.Session.IsDisposed);
    }

    [Fact]
    public async Task Execute_DisposesSession_WhenInitializationThrows()
    {
        var factory = new CapturingSetupSessionFactory();

        factory.Session.ExceptionToThrow =
            new InvalidOperationException(
                "Synthetic setup failure.");

        var useCase = CreateUseCase(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => useCase.ExecuteAsync(CreateCommand()));

        Assert.Equal(
            1,
            factory.OpenCount);

        Assert.True(
            factory.Session.IsDisposed);
    }

    [Fact]
    public async Task Execute_PropagatesCancellationBeforeOpeningSession()
    {
        var factory = new CapturingSetupSessionFactory();
        var useCase = CreateUseCase(factory);

        using var cancellationSource =
            new CancellationTokenSource();

        await cancellationSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => useCase.ExecuteAsync(
                CreateCommand(),
                cancellationSource.Token));

        Assert.Equal(
            1,
            factory.OpenCount);

        Assert.Null(
            factory.Session.Setup);

        Assert.False(
            factory.Session.IsDisposed);
    }

    private static InitializeCoreLedgerUseCase CreateUseCase(
        CapturingSetupSessionFactory factory)
        => new(
            factory,
            new FixedTimeProvider(
                InitializedAtUtc));

    private static InitializeCoreLedgerCommand CreateCommand()
        => new(
            new InitializeCurrencyInput(
                CurrencyCode.TRY,
                "  Synthetic Currency  ",
                MinorUnitDigits: 2),

            HouseholdName:
                "Synthetic Household",

            HouseholdMemberDisplayName:
                "Synthetic Member",

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
                new DateOnly(
                    2026,
                    1,
                    1)),

            new InitializeAssetInput(
                " synthetic_cash ",
                "Synthetic Cash"),

            new InitializeAssetInput(
                " synthetic_fund ",
                "Synthetic Fund"));

    private sealed class CapturingSetupSessionFactory
        : ICoreLedgerSetupSessionFactory
    {
        internal CapturingSetupSession Session { get; } =
            new();

        internal int OpenCount { get; private set; }

        internal LocalDataFailureCategory? FailureCategory
        {
            get;
            init;
        }

        internal string FailureMessage { get; init; } =
            "Synthetic local-data failure.";

        public Task<
            LocalDataOperationResult<ICoreLedgerSetupSession>> OpenAsync(
            CancellationToken cancellationToken = default)
        {
            OpenCount++;

            cancellationToken.ThrowIfCancellationRequested();

            if (FailureCategory is { } category)
            {
                return Task.FromResult(
                    LocalDataOperationResult<
                        ICoreLedgerSetupSession>.Failed(
                        category,
                        FailureMessage));
            }

            return Task.FromResult(
                LocalDataOperationResult<
                    ICoreLedgerSetupSession>.Success(
                    Session));
        }
    }

    private sealed class CapturingSetupSession
        : ICoreLedgerSetupSession
    {
        internal bool ShouldInitialize { get; set; } =
            true;

        internal CoreLedgerSetup? Setup
        {
            get;
            private set;
        }

        internal Exception? ExceptionToThrow
        {
            get;
            set;
        }

        internal bool IsDisposed
        {
            get;
            private set;
        }

        public Task<bool> TryInitializeAsync(
            CoreLedgerSetup setup,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Setup = setup;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(
                ShouldInitialize);
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        internal FixedTimeProvider(
            DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
            => _utcNow;
    }
}