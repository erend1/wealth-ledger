using WealthLedger.Application.Positions;
using WealthLedger.Application.Navigation;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.Tests.Positions;

public sealed class GetPositionUseCaseTests
{
    private static readonly Guid HouseholdId = Guid.NewGuid();
    private static readonly Guid PortfolioId = Guid.NewGuid();
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly Guid AssetId = Guid.NewGuid();

    [Fact]
    public async Task Execute_DerivesPositionFromOrderedEntryFacts()
    {
        var source = new StubPostedEntrySource(
        [
            CreateFact(
                Guid.Parse("20000000-0000-0000-0000-000000000001"),
                day: 2,
                quantityRawE8: -400),
            CreateFact(
                Guid.Parse("10000000-0000-0000-0000-000000000001"),
                day: 1,
                quantityRawE8: 1_000)
        ]);
        var useCase = new GetPositionUseCase(
            source,
            new StubNavigationScopeReadStore(positionScopeExists: true));

        var result = await useCase.ExecuteAsync(
            new GetPositionQuery(
                HouseholdId,
                PortfolioId,
                AccountId,
                AssetId));

        Assert.Equal(600, result.Quantity.RawE8);
        Assert.Equal(2, result.SourceEntryCount);
    }

    [Fact]
    public async Task Execute_WhenHistoricalPositionOverflows_Throws()
    {
        var source = new StubPostedEntrySource(
        [
            CreateFact(Guid.NewGuid(), day: 1, quantityRawE8: long.MaxValue),
            CreateFact(Guid.NewGuid(), day: 2, quantityRawE8: 1)
        ]);
        var useCase = new GetPositionUseCase(
            source,
            new StubNavigationScopeReadStore(positionScopeExists: true));

        await Assert.ThrowsAsync<OverflowException>(
            () => useCase.ExecuteAsync(
                new GetPositionQuery(
                    HouseholdId,
                    PortfolioId,
                    AccountId,
                AssetId)));
    }

    [Fact]
    public async Task Execute_PositionScopeValidWithNoHistory_ReturnsZero()
    {
        var source = new StubPostedEntrySource([]);
        var useCase = new GetPositionUseCase(
            source,
            new StubNavigationScopeReadStore(positionScopeExists: true));

        var result = await useCase.ExecuteAsync(
            new GetPositionQuery(
                HouseholdId,
                PortfolioId,
                AccountId,
                AssetId));

        Assert.Equal(0, result.Quantity.RawE8);
        Assert.Equal(0, result.SourceEntryCount);
        Assert.Equal(1, source.CallCount);
    }

    [Fact]
    public async Task Execute_PositionScopeUnknown_ThrowsBeforeEntryRead()
    {
        var source = new StubPostedEntrySource([]);
        var useCase = new GetPositionUseCase(
            source,
            new StubNavigationScopeReadStore(positionScopeExists: false));

        var exception = await Assert.ThrowsAsync<PositionScopeNotFoundException>(
            () => useCase.ExecuteAsync(
                new GetPositionQuery(
                    HouseholdId,
                    PortfolioId,
                    AccountId,
                    AssetId)));

        Assert.Equal(
            "The requested position scope does not exist.",
            exception.Message);
        Assert.Equal(0, source.CallCount);
    }

    private static PostedEntryFact CreateFact(
        Guid transactionId,
        int day,
        long quantityRawE8)
        => new(
            transactionId,
            new DateOnly(2026, 8, day),
            new DateTimeOffset(2026, 8, day, 8, 0, 0, TimeSpan.Zero),
            EntrySequence: 0,
            QuantityDelta.FromRaw(quantityRawE8));

    private sealed class StubPostedEntrySource : IPostedEntrySource
    {
        private readonly IReadOnlyList<PostedEntryFact> _entries;

        internal StubPostedEntrySource(IReadOnlyList<PostedEntryFact> entries)
        {
            _entries = entries;
        }

        internal int CallCount { get; private set; }

        public Task<IReadOnlyList<PostedEntryFact>> ListPositionEntriesAsync(
            Guid householdId,
            Guid portfolioId,
            Guid accountId,
            Guid assetId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(_entries);
        }
    }

    private sealed class StubNavigationScopeReadStore
        : INavigationScopeReadStore
    {
        private readonly bool _positionScopeExists;

        internal StubNavigationScopeReadStore(bool positionScopeExists)
        {
            _positionScopeExists = positionScopeExists;
        }

        public Task<bool> HouseholdExistsAsync(
            Guid householdId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }

        public Task<bool> PositionScopeExistsAsync(
            Guid householdId,
            Guid portfolioId,
            Guid accountId,
            Guid assetId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_positionScopeExists);
        }
    }
}
