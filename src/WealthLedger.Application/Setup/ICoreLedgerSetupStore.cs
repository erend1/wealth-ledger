using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Households;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.Setup;

public interface ICoreLedgerSetupStore
{
    Task<bool> TryInitializeAsync(
        CoreLedgerSetup setup,
        CancellationToken cancellationToken = default);
}

public sealed record CoreLedgerSetup(
    CoreLedgerCurrencyReference BaseCurrency,
    Household Household,
    HouseholdMember? HouseholdMember,
    Institution Institution,
    Portfolio Portfolio,
    Account Account,
    Asset CashAsset,
    Asset FundAsset,
    DateTimeOffset InitializedAtUtc);

public sealed record CoreLedgerCurrencyReference(
    CurrencyCode Code,
    string Name,
    int MinorUnitDigits);
