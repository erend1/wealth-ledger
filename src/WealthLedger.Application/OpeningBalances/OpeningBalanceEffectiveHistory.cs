namespace WealthLedger.Application.OpeningBalances;

public sealed record OpeningBalanceScope(
    Guid HouseholdId,
    Guid PortfolioId,
    Guid AccountId,
    Guid AssetId);

public sealed record OpeningBalanceEffectiveHistory(
    Guid? EffectiveOpeningTransactionId,
    bool HasOtherEffectiveHistory);

public interface IOpeningBalanceEffectiveHistoryReadStore
{
    Task<OpeningBalanceEffectiveHistory> ReadAsync(
        OpeningBalanceScope scope,
        CancellationToken cancellationToken = default);
}
