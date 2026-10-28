using WealthLedger.Application.Evidence;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Evidence;

namespace WealthLedger.Application.Tests.Evidence;

public sealed class EvidenceObservationUseCaseTests
{
    private static readonly Guid HouseholdId = Id("a1");
    private static readonly Guid OtherHouseholdId = Id("a2");
    private static readonly Guid InstitutionId = Id("b1");
    private static readonly Guid OtherInstitutionId = Id("b2");
    private static readonly Guid PortfolioId = Id("c1");
    private static readonly Guid OtherHouseholdPortfolioId = Id("c2");
    private static readonly Guid AccountId = Id("d1");
    private static readonly Guid SecondAccountId = Id("d2");
    private static readonly Guid AccountWithoutInstitutionId = Id("d3");
    private static readonly Guid OtherHouseholdAccountId = Id("d4");
    private static readonly Guid CashAssetId = Id("e1");
    private static readonly Guid GoldAssetId = Id("e2");
    private static readonly Guid EvidenceId = Id("f1");
    private static readonly Guid SupersededEvidenceId = Id("f2");
    private static readonly Guid OtherHouseholdEvidenceId = Id("f3");
    private static readonly Guid AccountBoundEvidenceId = Id("f4");
    private static readonly Guid InstitutionBoundEvidenceId = Id("f5");

    private static readonly DateOnly EffectiveDate = new(2026, 9, 30);

    private static readonly DateTimeOffset Now =
        new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Record_AddsExactObservationsForTheEvidenceRecord()
    {
        var store = new FakeEvidenceStore();

        var result = await CreateUseCase(store).ExecuteAsync(
            Command(
                EvidenceId,
                new EvidenceObservationInput(PortfolioId, AccountId, CashAssetId, -1_234_567_890_123, 5),
                new EvidenceObservationInput(PortfolioId, AccountId, GoldAssetId, 2_500_000_000, 0, 2)));

        Assert.Equal(EvidenceId, result.EvidenceId);
        Assert.Equal(2, result.AddedCount);
        Assert.Equal(0, result.AlreadyRecordedCount);
        Assert.Equal(2, store.Observations.Count);

        var cash = store.Observations[0];
        Assert.Equal(EvidenceId, cash.EvidenceId);
        Assert.Equal(PortfolioId, cash.PortfolioId);
        Assert.Equal(AccountId, cash.AccountId);
        Assert.Equal(CashAssetId, cash.AssetId);
        Assert.Equal(-1_234_567_890_123, cash.ObservedQuantityRawE8);
        Assert.Equal(5, cash.ToleranceRawE8);
        Assert.Null(cash.ObservedPieceCount);
        Assert.Equal(Now, cash.RecordedAtUtc);
        Assert.Equal(2, store.Observations[1].ObservedPieceCount);
    }

    [Fact]
    public async Task Record_RepeatingStoredFactsAddsOnlyTheNewScopes()
    {
        var store = new FakeEvidenceStore();
        var useCase = CreateUseCase(store);
        var cash = new EvidenceObservationInput(PortfolioId, AccountId, CashAssetId, 100);

        await useCase.ExecuteAsync(Command(EvidenceId, cash));
        var replay = await useCase.ExecuteAsync(Command(EvidenceId, cash));
        var partial = await useCase.ExecuteAsync(
            Command(
                EvidenceId,
                cash,
                new EvidenceObservationInput(PortfolioId, SecondAccountId, CashAssetId, 200)));

        Assert.Equal((0, 1), (replay.AddedCount, replay.AlreadyRecordedCount));
        Assert.Equal((1, 1), (partial.AddedCount, partial.AlreadyRecordedCount));
        Assert.Equal(2, store.Observations.Count);
        Assert.Equal(2, store.AddCount);
    }

    [Theory]
    [InlineData(101, 0, null)]
    [InlineData(100, 1, null)]
    [InlineData(100, 0, 1)]
    public async Task Record_DifferentFactsForAStoredScopeAreAConflict(
        long observed,
        long tolerance,
        int? pieces)
    {
        var store = new FakeEvidenceStore();
        var useCase = CreateUseCase(store);
        await useCase.ExecuteAsync(
            Command(EvidenceId, new EvidenceObservationInput(PortfolioId, AccountId, GoldAssetId, 100)));

        var exception = await Assert.ThrowsAsync<EvidenceException>(
            () => useCase.ExecuteAsync(
                Command(
                    EvidenceId,
                    new EvidenceObservationInput(PortfolioId, SecondAccountId, CashAssetId, 5),
                    new EvidenceObservationInput(PortfolioId, AccountId, GoldAssetId, observed, tolerance, pieces))));

        Assert.Equal(EvidenceErrorCategory.Conflict, exception.Category);
        Assert.Equal(EvidenceErrorCodes.ObservationConflict, exception.ErrorCode);
        Assert.Single(store.Observations);
        Assert.Equal(1, store.AddCount);
    }

    [Theory]
    [InlineData("other-household-portfolio")]
    [InlineData("other-household-account")]
    [InlineData("unknown-portfolio")]
    [InlineData("unknown-account")]
    [InlineData("unknown-asset")]
    public async Task Record_UnknownOrCrossHouseholdScopeIsNotFound(string scope)
    {
        var input = scope switch
        {
            "other-household-portfolio" => new EvidenceObservationInput(OtherHouseholdPortfolioId, AccountId, CashAssetId, 1),
            "other-household-account" => new EvidenceObservationInput(PortfolioId, OtherHouseholdAccountId, CashAssetId, 1),
            "unknown-portfolio" => new EvidenceObservationInput(Guid.NewGuid(), AccountId, CashAssetId, 1),
            "unknown-account" => new EvidenceObservationInput(PortfolioId, Guid.NewGuid(), CashAssetId, 1),
            _ => new EvidenceObservationInput(PortfolioId, AccountId, Guid.NewGuid(), 1)
        };

        var exception = await FailsAsync(Command(EvidenceId, input));

        Assert.Equal(EvidenceErrorCategory.NotFound, exception.Category);
        Assert.Equal(EvidenceErrorCodes.ObservationScopeNotFound, exception.ErrorCode);
    }

    [Theory]
    [InlineData("account-bound")]
    [InlineData("institution-bound")]
    [InlineData("institution-bound-account-without-institution")]
    public async Task Record_ScopeOutsideTheEvidenceContextIsAMismatch(string context)
    {
        var command = context switch
        {
            "account-bound" => Command(
                AccountBoundEvidenceId,
                new EvidenceObservationInput(PortfolioId, SecondAccountId, CashAssetId, 1)),
            "institution-bound" => Command(
                InstitutionBoundEvidenceId,
                new EvidenceObservationInput(PortfolioId, SecondAccountId, CashAssetId, 1)),
            _ => Command(
                InstitutionBoundEvidenceId,
                new EvidenceObservationInput(PortfolioId, AccountWithoutInstitutionId, CashAssetId, 1))
        };

        var exception = await FailsAsync(command);

        Assert.Equal(EvidenceErrorCategory.Validation, exception.Category);
        Assert.Equal(EvidenceErrorCodes.ObservationScopeMismatch, exception.ErrorCode);
    }

    [Fact]
    public async Task Record_ScopeInsideTheEvidenceContextIsAccepted()
    {
        var store = new FakeEvidenceStore();

        await CreateUseCase(store).ExecuteAsync(
            Command(
                AccountBoundEvidenceId,
                new EvidenceObservationInput(PortfolioId, AccountId, CashAssetId, 1)));
        await CreateUseCase(store).ExecuteAsync(
            Command(
                InstitutionBoundEvidenceId,
                new EvidenceObservationInput(PortfolioId, AccountId, CashAssetId, 1)));

        Assert.Equal(2, store.Observations.Count);
    }

    [Fact]
    public async Task Record_PieceCountIsAcceptedOnlyForPhysicalGold()
    {
        var exception = await FailsAsync(
            Command(EvidenceId, new EvidenceObservationInput(PortfolioId, AccountId, CashAssetId, 1, 0, 1)));

        Assert.Equal(EvidenceErrorCodes.ObservationInvalid, exception.ErrorCode);
    }

    [Theory]
    [InlineData("null-list")]
    [InlineData("empty-list")]
    [InlineData("null-item")]
    [InlineData("empty-portfolio")]
    [InlineData("empty-account")]
    [InlineData("empty-asset")]
    [InlineData("negative-tolerance")]
    [InlineData("negative-pieces")]
    [InlineData("duplicate-scope")]
    public async Task Record_RejectsInvalidInputBeforeAnyStoreAccess(string shape)
    {
        IReadOnlyList<EvidenceObservationInput> observations = shape switch
        {
            "null-list" => null!,
            "empty-list" => [],
            "null-item" => [null!],
            "empty-portfolio" => [new(Guid.Empty, AccountId, CashAssetId, 1)],
            "empty-account" => [new(PortfolioId, Guid.Empty, CashAssetId, 1)],
            "empty-asset" => [new(PortfolioId, AccountId, Guid.Empty, 1)],
            "negative-tolerance" => [new(PortfolioId, AccountId, CashAssetId, 1, -1)],
            "negative-pieces" => [new(PortfolioId, AccountId, GoldAssetId, 1, 0, -1)],
            _ =>
            [
                new(PortfolioId, AccountId, CashAssetId, 1),
                new(PortfolioId, AccountId, CashAssetId, 2)
            ]
        };
        var store = new FakeEvidenceStore();

        var exception = await Assert.ThrowsAsync<EvidenceException>(
            () => CreateUseCase(store).ExecuteAsync(
                new RecordEvidenceObservationsCommand(HouseholdId, EvidenceId, observations)));

        Assert.Equal(EvidenceErrorCategory.Validation, exception.Category);
        Assert.Equal(EvidenceErrorCodes.ObservationInvalid, exception.ErrorCode);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Record_BoundsOneCommandAndTheWholeRecordToOneHundred()
    {
        var store = new FakeEvidenceStore();
        var tooMany = Enumerable.Range(0, 101)
            .Select(_ => new EvidenceObservationInput(PortfolioId, Guid.NewGuid(), CashAssetId, 1))
            .ToArray();

        var oversized = await Assert.ThrowsAsync<EvidenceException>(
            () => CreateUseCase(store).ExecuteAsync(
                new RecordEvidenceObservationsCommand(HouseholdId, EvidenceId, tooMany)));

        Assert.Equal(EvidenceErrorCodes.ObservationLimitExceeded, oversized.ErrorCode);
        Assert.Equal(0, store.CallCount);

        for (var index = 0; index < 99; index++)
        {
            store.Observations.Add(
                EvidenceObservation.Create(
                    Guid.NewGuid(),
                    EvidenceId,
                    PortfolioId,
                    Guid.NewGuid(),
                    CashAssetId,
                    1,
                    0,
                    null,
                    Now));
        }

        var full = await Assert.ThrowsAsync<EvidenceException>(
            () => CreateUseCase(store).ExecuteAsync(
                Command(
                    EvidenceId,
                    new EvidenceObservationInput(PortfolioId, AccountId, CashAssetId, 1),
                    new EvidenceObservationInput(PortfolioId, SecondAccountId, CashAssetId, 1))));

        Assert.Equal(EvidenceErrorCodes.ObservationLimitExceeded, full.ErrorCode);
        Assert.Equal(0, store.AddCount);

        var last = await CreateUseCase(store).ExecuteAsync(
            Command(EvidenceId, new EvidenceObservationInput(PortfolioId, AccountId, CashAssetId, 1)));

        Assert.Equal(1, last.AddedCount);
        Assert.Equal(100, store.Observations.Count);
    }

    [Theory]
    [InlineData("unknown", EvidenceErrorCategory.NotFound, EvidenceErrorCodes.NotFound)]
    [InlineData("other-household", EvidenceErrorCategory.NotFound, EvidenceErrorCodes.NotFound)]
    [InlineData("superseded", EvidenceErrorCategory.Conflict, EvidenceErrorCodes.Superseded)]
    public async Task Record_RequiresTheLatestSameHouseholdEvidenceRecord(
        string evidence,
        EvidenceErrorCategory category,
        string errorCode)
    {
        var evidenceId = evidence switch
        {
            "unknown" => Guid.NewGuid(),
            "other-household" => OtherHouseholdEvidenceId,
            _ => SupersededEvidenceId
        };

        var exception = await FailsAsync(
            Command(evidenceId, new EvidenceObservationInput(PortfolioId, AccountId, CashAssetId, 1)));

        Assert.Equal(category, exception.Category);
        Assert.Equal(errorCode, exception.ErrorCode);
    }

    [Fact]
    public async Task Record_FailuresAndCommandsNeverEchoObservedQuantities()
    {
        var store = new FakeEvidenceStore();
        var input = new EvidenceObservationInput(PortfolioId, AccountId, CashAssetId, 987_654_321_012, 4_242_424);
        await CreateUseCase(store).ExecuteAsync(Command(EvidenceId, input));

        var conflict = await Assert.ThrowsAsync<EvidenceException>(
            () => CreateUseCase(store).ExecuteAsync(
                Command(
                    EvidenceId,
                    new EvidenceObservationInput(PortfolioId, AccountId, CashAssetId, 987_654_321_013, 4_242_424))));

        foreach (var text in new[]
                 {
                     conflict.Message,
                     input.ToString(),
                     Command(EvidenceId, input).ToString(),
                     store.Observations[0].ToString()
                 })
        {
            Assert.DoesNotContain("987654321", text, StringComparison.Ordinal);
            Assert.DoesNotContain("4242424", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ListObservations_ReturnsOnlySameHouseholdRecords()
    {
        var list = new EvidenceObservationList(
            EvidenceId,
            HouseholdId,
            EffectiveDate,
            null,
            []);
        var readStore = new FakeEvidenceReadStore(list);
        var useCase = new ListEvidenceObservationsUseCase(readStore);

        Assert.Same(list, await useCase.ExecuteAsync(new ListEvidenceObservationsQuery(HouseholdId, EvidenceId)));

        var missing = await Assert.ThrowsAsync<EvidenceException>(
            () => useCase.ExecuteAsync(new ListEvidenceObservationsQuery(OtherHouseholdId, EvidenceId)));

        Assert.Equal(EvidenceErrorCodes.NotFound, missing.ErrorCode);
        Assert.Equal(nameof(EvidenceObservationList), list.ToString());
        await Assert.ThrowsAsync<ArgumentException>(
            () => useCase.ExecuteAsync(new ListEvidenceObservationsQuery(Guid.Empty, EvidenceId)));
    }

    private static async Task<EvidenceException> FailsAsync(
        RecordEvidenceObservationsCommand command)
    {
        var store = new FakeEvidenceStore();

        var exception = await Assert.ThrowsAsync<EvidenceException>(
            () => CreateUseCase(store).ExecuteAsync(command));

        Assert.Equal(0, store.AddCount);
        Assert.Empty(store.Observations);
        return exception;
    }

    private static RecordEvidenceObservationsUseCase CreateUseCase(FakeEvidenceStore store)
        => new(store, new FixedTimeProvider());

    private static RecordEvidenceObservationsCommand Command(
        Guid evidenceId,
        params EvidenceObservationInput[] observations)
        => new(HouseholdId, evidenceId, observations);

    private static Guid Id(string suffix)
        => Guid.Parse($"e4000000-0000-0000-0000-0000000000{suffix}");

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeEvidenceStore : IEvidenceStore
    {
        public List<EvidenceObservation> Observations { get; } = [];

        public int AddCount { get; private set; }

        public int CallCount { get; private set; }

        public Task<EvidenceRecordReference?> FindEvidenceAsync(
            Guid evidenceId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            EvidenceRecordReference? evidence = evidenceId switch
            {
                _ when evidenceId == EvidenceId =>
                    new(EvidenceId, HouseholdId, null, EffectiveDate, null, null),
                _ when evidenceId == SupersededEvidenceId =>
                    new(SupersededEvidenceId, HouseholdId, EvidenceId, EffectiveDate, null, null),
                _ when evidenceId == OtherHouseholdEvidenceId =>
                    new(OtherHouseholdEvidenceId, OtherHouseholdId, null, EffectiveDate, null, null),
                _ when evidenceId == AccountBoundEvidenceId =>
                    new(AccountBoundEvidenceId, HouseholdId, null, EffectiveDate, InstitutionId, AccountId),
                _ when evidenceId == InstitutionBoundEvidenceId =>
                    new(InstitutionBoundEvidenceId, HouseholdId, null, EffectiveDate, InstitutionId, null),
                _ => null
            };
            return Task.FromResult(evidence);
        }

        public Task<EvidenceObservationReferences> FindObservationReferencesAsync(
            IReadOnlyCollection<Guid> portfolioIds,
            IReadOnlyCollection<Guid> accountIds,
            IReadOnlyCollection<Guid> assetIds,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            EvidencePortfolioReference[] portfolios =
            [
                new(PortfolioId, HouseholdId),
                new(OtherHouseholdPortfolioId, OtherHouseholdId)
            ];
            EvidenceAccountReference[] accounts =
            [
                new(AccountId, HouseholdId, InstitutionId),
                new(SecondAccountId, HouseholdId, OtherInstitutionId),
                new(AccountWithoutInstitutionId, HouseholdId, null),
                new(OtherHouseholdAccountId, OtherHouseholdId, InstitutionId)
            ];
            EvidenceAssetReference[] assets =
            [
                new(CashAssetId, AssetType.Cash),
                new(GoldAssetId, AssetType.PhysicalGold)
            ];

            return Task.FromResult(
                new EvidenceObservationReferences(
                    portfolios.Where(x => portfolioIds.Contains(x.PortfolioId)).ToArray(),
                    accounts.Where(x => accountIds.Contains(x.AccountId)).ToArray(),
                    assets.Where(x => assetIds.Contains(x.AssetId)).ToArray()));
        }

        public Task<IReadOnlyList<EvidenceObservation>> ListObservationsAsync(
            Guid evidenceId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            IReadOnlyList<EvidenceObservation> stored = Observations
                .Where(x => x.EvidenceId == evidenceId)
                .ToArray();
            return Task.FromResult(stored);
        }

        public Task<int> AddObservationsAsync(
            Guid evidenceId,
            IReadOnlyCollection<EvidenceObservation> observations,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            AddCount++;
            var added = 0;

            foreach (var observation in observations)
            {
                if (!Observations.Any(observation.HasSameScope))
                {
                    Observations.Add(observation);
                    added++;
                }
            }

            return Task.FromResult(added);
        }

        public Task<bool> HouseholdExistsAsync(Guid householdId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> InstitutionExistsAsync(Guid institutionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EvidenceAccountReference?> FindAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<EvidenceTransactionReference>> FindTransactionsAsync(
            IReadOnlyCollection<Guid> transactionIds,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EvidenceCommandReceipt?> FindReceiptAsync(
            EvidenceCommandScope scope,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EvidenceCommitResult> TryCommitAsync(
            EvidenceCommandReceipt receipt,
            EvidenceRecord record,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> AddTransactionLinksAsync(
            IReadOnlyCollection<EvidenceTransactionLink> links,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeEvidenceReadStore(EvidenceObservationList list)
        : IEvidenceReadStore
    {
        public Task<EvidenceObservationList?> ListObservationsAsync(
            Guid householdId,
            Guid evidenceId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                householdId == list.HouseholdId && evidenceId == list.EvidenceId
                    ? list
                    : null);

        public Task<bool> HouseholdExistsAsync(Guid householdId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<EvidenceSummary>> ListAsync(
            Guid householdId,
            int take,
            EvidenceCursorKey? after,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EvidenceDetail?> GetAsync(
            Guid householdId,
            Guid evidenceId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StoredEvidenceAttachment?> ReadAttachmentAsync(
            Guid householdId,
            Guid evidenceId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
