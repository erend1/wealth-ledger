namespace WealthLedger.Api.Contracts;

public sealed record InitializeCurrencyRequest(
    string Code,
    string Name,
    int MinorUnitDigits);

public sealed record InitializeInstitutionRequest(
    string Code,
    string Name,
    string TypeCode);

public sealed record InitializePortfolioRequest(
    string Code,
    string Name);

public sealed record InitializeAccountRequest(
    string Code,
    string Name,
    string TypeCode,
    DateOnly? OpenedOn = null);

public sealed record InitializeAssetRequest(
    string Code,
    string Name);

public sealed record InitializeCoreLedgerRequest(
    InitializeCurrencyRequest BaseCurrency,
    string HouseholdName,
    string? HouseholdMemberDisplayName,
    InitializeInstitutionRequest Institution,
    InitializePortfolioRequest Portfolio,
    InitializeAccountRequest Account,
    InitializeAssetRequest CashAsset,
    InitializeAssetRequest FundAsset);

public sealed record InitializeCoreLedgerResponse(
    Guid HouseholdId,
    Guid? HouseholdMemberId,
    Guid InstitutionId,
    Guid PortfolioId,
    Guid AccountId,
    Guid CashAssetId,
    Guid FundAssetId);
