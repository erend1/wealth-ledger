using WealthLedger.Application.Positions;
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
        var useCase = new GetPositionUseCase(source);

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
        var useCase = new GetPositionUseCase(source);

        await Assert.ThrowsAsync<OverflowException>(
            () => useCase.ExecuteAsync(
                new GetPositionQuery(
                    HouseholdId,
                    PortfolioId,
                    AccountId,
                    AssetId)));
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

        public Task<IReadOnlyList<PostedEntryFact>> ListPositionEntriesAsync(
            Guid householdId,
            Guid portfolioId,
            Guid accountId,
            Guid assetId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_entries);
        }
    }
}
