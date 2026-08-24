using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.CoreLedger;

public interface ILedgerReferenceData
{
    Task<LedgerLocationReference?> FindLocationAsync(
        Guid portfolioId,
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task<Asset?> FindAssetAsync(
        Guid assetId,
        CancellationToken cancellationToken = default);

    Task<CurrencyReference?> FindCurrencyAsync(
        CurrencyCode currency,
        CancellationToken cancellationToken = default);

    Task<HouseholdMemberReference?> FindHouseholdMemberAsync(
        Guid householdMemberId,
        CancellationToken cancellationToken = default);
}

public sealed record LedgerLocationReference(
    Guid PortfolioId,
    Guid PortfolioHouseholdId,
    PortfolioStatus PortfolioStatus,
    Guid AccountId,
    Guid AccountHouseholdId,
    bool AccountIsActive);

public sealed record CurrencyReference(
    CurrencyCode Code,
    int MinorUnitDigits);

public sealed record HouseholdMemberReference(
    Guid Id,
    Guid HouseholdId,
    bool IsActive);
