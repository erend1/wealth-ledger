using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.Tests.OpeningBalances;

public sealed class OpeningBalanceReferenceUseCaseTests
{
    private static readonly Guid HouseholdId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");

    private static readonly Guid InstitutionId =
        Guid.Parse("20000000-0000-0000-0000-000000000001");

    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateCurrency_NormalizesAndCreatesReferenceOnly()
    {
        var store = CreateStore();
        var useCase =
            new CreateOpeningBalanceCurrencyUseCase(store);

        var result = await useCase.ExecuteAsync(
            new CreateOpeningBalanceCurrencyCommand(
                new CurrencyCode(" usd "),
                "  Synthetic US Dollar  ",
                2));

        Assert.True(result.WasCreated);
        Assert.Equal("USD", result.Reference.Code.Value);
        Assert.Equal("Synthetic US Dollar", result.Reference.Name);
        Assert.Equal(2, result.Reference.MinorUnitDigits);
        Assert.Equal(result.Reference, store.CurrencyCandidate);
        Assert.Equal(0, store.LedgerWriteCount);
    }

    [Fact]
    public async Task CreateCurrency_WhenEquivalent_ReturnsPersistedReference()
    {
        var store = CreateStore();
        store.CurrencyWriteStatus =
            OpeningBalanceReferenceWriteStatus.Equivalent;
        store.ExistingCurrency =
            new OpeningBalanceCurrencyReference(
                CurrencyCode.USD,
                "Synthetic US Dollar",
                2);
        var useCase =
            new CreateOpeningBalanceCurrencyUseCase(store);

        var result = await useCase.ExecuteAsync(
            new CreateOpeningBalanceCurrencyCommand(
                CurrencyCode.USD,
                "Synthetic US Dollar",
                2));

        Assert.False(result.WasCreated);
        Assert.Same(store.ExistingCurrency, result.Reference);
        Assert.Equal(0, store.LedgerWriteCount);
    }

    [Fact]
    public async Task CreateInstitution_WhenCodeConflicts_ReturnsStableConflict()
    {
        var store = CreateStore();
        store.InstitutionWriteStatus =
            OpeningBalanceReferenceWriteStatus.Conflict;
        var useCase =
            new CreateOpeningBalanceInstitutionUseCase(store);

        var exception =
            await Assert.ThrowsAsync<OpeningBalanceException>(
                () => useCase.ExecuteAsync(
                    new CreateOpeningBalanceInstitutionCommand(
                        "synthetic_broker",
                        "Synthetic Broker",
                        InstitutionType.Broker)));

        Assert.Equal(
            OpeningBalanceErrorCategory.Conflict,
            exception.Category);
        Assert.Equal(
            OpeningBalanceErrorCodes.ReferenceConflict,
            exception.ErrorCode);
        Assert.Equal(0, store.LedgerWriteCount);
    }

    [Fact]
    public async Task CreateAccount_RequiresInstitutionForInvestmentAccount()
    {
        var store = CreateStore();
        var useCase =
            new CreateOpeningBalanceAccountUseCase(store, store);

        var exception =
            await Assert.ThrowsAsync<OpeningBalanceException>(
                () => useCase.ExecuteAsync(
                    new CreateOpeningBalanceAccountCommand(
                        HouseholdId,
                        InstitutionId: null,
                        "synthetic_investment",
                        "Synthetic Investment Account",
                        AccountType.Investment)));

        Assert.Equal(
            OpeningBalanceErrorCodes.ReferenceShapeInvalid,
            exception.ErrorCode);
        Assert.Null(store.AccountCandidate);
    }

    [Fact]
    public async Task CreateAccount_RejectsInactiveInstitution()
    {
        var store = CreateStore();
        store.Institution = store.Institution! with
        {
            IsActive = false
        };
        var useCase =
            new CreateOpeningBalanceAccountUseCase(store, store);

        var exception =
            await Assert.ThrowsAsync<OpeningBalanceException>(
                () => useCase.ExecuteAsync(
                    new CreateOpeningBalanceAccountCommand(
                        HouseholdId,
                        InstitutionId,
                        "synthetic_investment",
                        "Synthetic Investment Account",
                        AccountType.Investment)));

        Assert.Equal(
            OpeningBalanceErrorCodes.ReferenceInactive,
            exception.ErrorCode);
        Assert.Null(store.AccountCandidate);
    }

    [Fact]
    public async Task CreateAccount_AllowsInstitutionlessPhysicalVault()
    {
        var store = CreateStore();
        var useCase =
            new CreateOpeningBalanceAccountUseCase(store, store);

        var result = await useCase.ExecuteAsync(
            new CreateOpeningBalanceAccountCommand(
                HouseholdId,
                InstitutionId: null,
                " synthetic_vault ",
                " Synthetic Physical Vault ",
                AccountType.PhysicalVault,
                new DateOnly(2026, 1, 1)));

        Assert.True(result.WasCreated);
        Assert.Equal("SYNTHETIC_VAULT", result.Reference.Code);
        Assert.Null(result.Reference.InstitutionId);
        Assert.Equal(AccountType.PhysicalVault, result.Reference.Type);
        Assert.Equal(0, store.LedgerWriteCount);
    }

    [Fact]
    public async Task CreateAccount_WhenHouseholdIsMissing_DoesNotWrite()
    {
        var store = CreateStore();
        store.Household = null;
        var useCase =
            new CreateOpeningBalanceAccountUseCase(store, store);

        var exception =
            await Assert.ThrowsAsync<OpeningBalanceException>(
                () => useCase.ExecuteAsync(
                    new CreateOpeningBalanceAccountCommand(
                        HouseholdId,
                        InstitutionId: null,
                        "synthetic_cash",
                        "Synthetic Cash Account",
                        AccountType.Cash)));

        Assert.Equal(
            OpeningBalanceErrorCategory.NotFound,
            exception.Category);
        Assert.Null(store.AccountCandidate);
    }

    [Fact]
    public async Task CreateAsset_DerivesEveryAcceptedUnitAndPreservesLotMode()
    {
        var cases = new[]
        {
            (AssetType.Cash, LotTrackingMode.None, AssetUnit.CurrencyUnit),
            (AssetType.Currency, LotTrackingMode.None, AssetUnit.CurrencyUnit),
            (AssetType.Fund, LotTrackingMode.Optional, AssetUnit.FundUnit),
            (AssetType.Fund, LotTrackingMode.Required, AssetUnit.FundUnit),
            (AssetType.Equity, LotTrackingMode.Optional, AssetUnit.Share),
            (AssetType.Equity, LotTrackingMode.Required, AssetUnit.Share),
            (AssetType.PhysicalGold, LotTrackingMode.Required, AssetUnit.GrossGram)
        };

        foreach (var testCase in cases)
        {
            var store = CreateStore();
            var useCase =
                new CreateOpeningBalanceAssetUseCase(
                    store,
                    store,
                    new FixedTimeProvider(CreatedAtUtc));

            var result = await useCase.ExecuteAsync(
                new CreateOpeningBalanceAssetCommand(
                    $"SYNTHETIC_{testCase.Item1}",
                    $"Synthetic {testCase.Item1}",
                    testCase.Item1,
                    CurrencyCode.TRY,
                    testCase.Item2));

            Assert.True(result.WasCreated);
            Assert.Equal(testCase.Item3, result.Reference.BaseUnit);
            Assert.Equal(testCase.Item2, result.Reference.LotTrackingMode);
            Assert.Equal(CreatedAtUtc, result.Reference.CreatedAtUtc);
            Assert.Equal(0, store.LedgerWriteCount);
        }
    }

    [Fact]
    public async Task CreateAsset_RejectsIncompatibleLotModeBeforeWriting()
    {
        var store = CreateStore();
        var useCase =
            new CreateOpeningBalanceAssetUseCase(
                store,
                store,
                new FixedTimeProvider(CreatedAtUtc));

        var exception =
            await Assert.ThrowsAsync<OpeningBalanceException>(
                () => useCase.ExecuteAsync(
                    new CreateOpeningBalanceAssetCommand(
                        "SYNTHETIC_EQUITY",
                        "Synthetic Equity",
                        AssetType.Equity,
                        CurrencyCode.TRY,
                        LotTrackingMode.None)));

        Assert.Equal(
            OpeningBalanceErrorCodes.ReferenceShapeInvalid,
            exception.ErrorCode);
        Assert.Null(store.AssetCandidate);
    }

    [Fact]
    public async Task CreateAsset_RejectsUnsupportedAssetTypeBeforeWriting()
    {
        var store = CreateStore();
        var useCase =
            new CreateOpeningBalanceAssetUseCase(
                store,
                store,
                new FixedTimeProvider(CreatedAtUtc));

        var exception =
            await Assert.ThrowsAsync<OpeningBalanceException>(
                () => useCase.ExecuteAsync(
                    new CreateOpeningBalanceAssetCommand(
                        "SYNTHETIC_PROPERTY",
                        "Synthetic Property",
                        AssetType.RealEstate,
                        CurrencyCode.TRY,
                        LotTrackingMode.None)));

        Assert.Equal(
            OpeningBalanceErrorCodes.AssetNotSupported,
            exception.ErrorCode);
        Assert.Null(store.AssetCandidate);
    }

    [Fact]
    public async Task CreateAsset_WhenCurrencyIsMissing_DoesNotWrite()
    {
        var store = CreateStore();
        store.Currency = null;
        var useCase =
            new CreateOpeningBalanceAssetUseCase(
                store,
                store,
                new FixedTimeProvider(CreatedAtUtc));

        var exception =
            await Assert.ThrowsAsync<OpeningBalanceException>(
                () => useCase.ExecuteAsync(
                    new CreateOpeningBalanceAssetCommand(
                        "SYNTHETIC_FUND",
                        "Synthetic Fund",
                        AssetType.Fund,
                        CurrencyCode.TRY,
                        LotTrackingMode.Required)));

        Assert.Equal(
            OpeningBalanceErrorCategory.NotFound,
            exception.Category);
        Assert.Null(store.AssetCandidate);
    }

    private static ReferenceStoreFake CreateStore()
        => new()
        {
            Household =
                new OpeningBalanceHouseholdReference(
                    HouseholdId,
                    CurrencyCode.TRY),
            Currency =
                new OpeningBalanceCurrencyReference(
                    CurrencyCode.TRY,
                    "Synthetic Turkish Lira",
                    2),
            Institution =
                new OpeningBalanceInstitutionReference(
                    InstitutionId,
                    "SYNTHETIC_BROKER",
                    "Synthetic Broker",
                    InstitutionType.Broker,
                    IsActive: true)
        };

    private sealed class ReferenceStoreFake
        : IOpeningBalanceReferenceReadStore,
          IOpeningBalanceReferenceWriteStore
    {
        internal OpeningBalanceHouseholdReference? Household { get; set; }

        internal OpeningBalanceCurrencyReference? Currency { get; set; }

        internal OpeningBalanceInstitutionReference? Institution { get; set; }

        internal OpeningBalancePortfolioReference? Portfolio { get; set; }

        internal OpeningBalanceAccountReference? Account { get; set; }

        internal OpeningBalanceAssetReference? Asset { get; set; }

        internal OpeningBalanceCurrencyReference? ExistingCurrency
        {
            get;
            set;
        }

        internal OpeningBalanceReferenceWriteStatus CurrencyWriteStatus
        {
            get;
            set;
        } = OpeningBalanceReferenceWriteStatus.Created;

        internal OpeningBalanceReferenceWriteStatus InstitutionWriteStatus
        {
            get;
            set;
        } = OpeningBalanceReferenceWriteStatus.Created;

        internal OpeningBalanceReferenceWriteStatus AccountWriteStatus
        {
            get;
            set;
        } = OpeningBalanceReferenceWriteStatus.Created;

        internal OpeningBalanceReferenceWriteStatus AssetWriteStatus
        {
            get;
            set;
        } = OpeningBalanceReferenceWriteStatus.Created;

        internal OpeningBalanceCurrencyReference? CurrencyCandidate
        {
            get;
            private set;
        }

        internal Institution? InstitutionCandidate { get; private set; }

        internal Account? AccountCandidate { get; private set; }

        internal Asset? AssetCandidate { get; private set; }

        internal int LedgerWriteCount => 0;

        public Task<OpeningBalanceHouseholdReference?> FindHouseholdAsync(
            Guid householdId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                Household?.HouseholdId == householdId
                    ? Household
                    : null);

        public Task<OpeningBalanceCurrencyReference?> FindCurrencyAsync(
            CurrencyCode code,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                Currency?.Code == code
                    ? Currency
                    : null);

        public Task<OpeningBalanceInstitutionReference?> FindInstitutionAsync(
            Guid institutionId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                Institution?.InstitutionId == institutionId
                    ? Institution
                    : null);

        public Task<OpeningBalancePortfolioReference?> FindPortfolioAsync(
            Guid portfolioId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                Portfolio?.PortfolioId == portfolioId
                    ? Portfolio
                    : null);

        public Task<OpeningBalanceAccountReference?> FindAccountAsync(
            Guid accountId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                Account?.AccountId == accountId
                    ? Account
                    : null);

        public Task<OpeningBalanceAssetReference?> FindAssetAsync(
            Guid assetId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                Asset?.AssetId == assetId
                    ? Asset
                    : null);

        public Task<OpeningBalanceReferenceWriteResult<
            OpeningBalanceCurrencyReference>> TryCreateCurrencyAsync(
                OpeningBalanceCurrencyReference candidate,
                CancellationToken cancellationToken = default)
        {
            CurrencyCandidate = candidate;

            return Task.FromResult(
                Complete(
                    CurrencyWriteStatus,
                    ExistingCurrency ?? candidate));
        }

        public Task<OpeningBalanceReferenceWriteResult<
            OpeningBalanceInstitutionReference>> TryCreateInstitutionAsync(
                Institution candidate,
                CancellationToken cancellationToken = default)
        {
            InstitutionCandidate = candidate;
            var reference =
                new OpeningBalanceInstitutionReference(
                    candidate.Id,
                    candidate.Code,
                    candidate.Name,
                    candidate.Type,
                    candidate.IsActive);

            return Task.FromResult(
                Complete(
                    InstitutionWriteStatus,
                    reference));
        }

        public Task<OpeningBalanceReferenceWriteResult<
            OpeningBalanceAccountReference>> TryCreateAccountAsync(
                Account candidate,
                CancellationToken cancellationToken = default)
        {
            AccountCandidate = candidate;
            var reference =
                new OpeningBalanceAccountReference(
                    candidate.Id,
                    candidate.HouseholdId,
                    candidate.InstitutionId,
                    candidate.Code,
                    candidate.Name,
                    candidate.Type,
                    candidate.IsActive,
                    candidate.OpenedOn,
                    candidate.ClosedOn);

            return Task.FromResult(
                Complete(
                    AccountWriteStatus,
                    reference));
        }

        public Task<OpeningBalanceReferenceWriteResult<
            OpeningBalanceAssetReference>> TryCreateAssetAsync(
                Asset candidate,
                DateTimeOffset createdAtUtc,
                CancellationToken cancellationToken = default)
        {
            AssetCandidate = candidate;
            var reference =
                new OpeningBalanceAssetReference(
                    candidate.Id,
                    candidate.Code,
                    candidate.Name,
                    candidate.Type,
                    candidate.BaseUnit,
                    candidate.BaseCurrency,
                    candidate.LotTrackingMode,
                    candidate.IsActive,
                    createdAtUtc);

            return Task.FromResult(
                Complete(
                    AssetWriteStatus,
                    reference));
        }

        private static OpeningBalanceReferenceWriteResult<T> Complete<T>(
            OpeningBalanceReferenceWriteStatus status,
            T reference)
            where T : class
            => status switch
            {
                OpeningBalanceReferenceWriteStatus.Created
                    => OpeningBalanceReferenceWriteResult<T>.Created(reference),
                OpeningBalanceReferenceWriteStatus.Equivalent
                    => OpeningBalanceReferenceWriteResult<T>.Equivalent(reference),
                OpeningBalanceReferenceWriteStatus.Conflict
                    => OpeningBalanceReferenceWriteResult<T>.Conflict(),
                _ => throw new InvalidOperationException()
            };
    }

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
