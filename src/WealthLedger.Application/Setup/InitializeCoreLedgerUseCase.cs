using WealthLedger.Application.Common;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Households;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.Setup;

public sealed record InitializeCurrencyInput(
    CurrencyCode Code,
    string Name,
    int MinorUnitDigits);

public sealed record InitializeInstitutionInput(
    string Code,
    string Name,
    InstitutionType Type);

public sealed record InitializePortfolioInput(
    string Code,
    string Name);

public sealed record InitializeAccountInput(
    string Code,
    string Name,
    AccountType Type,
    DateOnly? OpenedOn = null);

public sealed record InitializeAssetInput(
    string Code,
    string Name);

public sealed record InitializeCoreLedgerCommand(
    InitializeCurrencyInput BaseCurrency,
    string HouseholdName,
    string? HouseholdMemberDisplayName,
    InitializeInstitutionInput Institution,
    InitializePortfolioInput Portfolio,
    InitializeAccountInput Account,
    InitializeAssetInput CashAsset,
    InitializeAssetInput FundAsset);

public sealed record InitializeCoreLedgerResult(
    Guid HouseholdId,
    Guid? HouseholdMemberId,
    Guid InstitutionId,
    Guid PortfolioId,
    Guid AccountId,
    Guid CashAssetId,
    Guid FundAssetId);

public sealed class InitializeCoreLedgerUseCase
{
    private readonly ICoreLedgerSetupSessionFactory _sessionFactory;
    private readonly TimeProvider _timeProvider;

    public InitializeCoreLedgerUseCase(
        ICoreLedgerSetupSessionFactory sessionFactory,
        TimeProvider timeProvider)
    {
        _sessionFactory = sessionFactory
            ?? throw new ArgumentNullException(nameof(sessionFactory));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<InitializeCoreLedgerResult> ExecuteAsync(
        InitializeCoreLedgerCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.BaseCurrency);
        ArgumentNullException.ThrowIfNull(command.BaseCurrency.Code);
        ArgumentNullException.ThrowIfNull(command.Institution);
        ArgumentNullException.ThrowIfNull(command.Portfolio);
        ArgumentNullException.ThrowIfNull(command.Account);
        ArgumentNullException.ThrowIfNull(command.CashAsset);
        ArgumentNullException.ThrowIfNull(command.FundAsset);

        var initializedAtUtc = _timeProvider.GetUtcNow();
        var currency = new CoreLedgerCurrencyReference(
            command.BaseCurrency.Code,
            NormalizeCurrencyName(command.BaseCurrency.Name),
            ValidateMinorUnitDigits(
                command.BaseCurrency.MinorUnitDigits));

        var household = Household.Create(
            Guid.NewGuid(),
            command.HouseholdName,
            currency.Code,
            initializedAtUtc);

        var householdMember = command.HouseholdMemberDisplayName is null
            ? null
            : HouseholdMember.Create(
                Guid.NewGuid(),
                household.Id,
                command.HouseholdMemberDisplayName,
                initializedAtUtc);

        var institution = Institution.Create(
            Guid.NewGuid(),
            command.Institution.Code,
            command.Institution.Name,
            command.Institution.Type);

        var portfolio = Portfolio.Create(
            Guid.NewGuid(),
            household.Id,
            command.Portfolio.Code,
            command.Portfolio.Name,
            initializedAtUtc);

        var account = Account.Create(
            Guid.NewGuid(),
            household.Id,
            institution.Id,
            command.Account.Code,
            command.Account.Name,
            command.Account.Type,
            command.Account.OpenedOn);

        var cashAsset = Asset.Create(
            Guid.NewGuid(),
            command.CashAsset.Code,
            command.CashAsset.Name,
            AssetType.Cash,
            AssetUnit.CurrencyUnit,
            currency.Code,
            LotTrackingMode.None);

        var fundAsset = Asset.Create(
            Guid.NewGuid(),
            command.FundAsset.Code,
            command.FundAsset.Name,
            AssetType.Fund,
            AssetUnit.FundUnit,
            currency.Code,
            LotTrackingMode.Required);

        if (cashAsset.Code == fundAsset.Code)
        {
            throw new ApplicationRuleViolationException(
                "The initial cash and fund assets must use different codes.");
        }

        var setup = new CoreLedgerSetup(
            currency,
            household,
            householdMember,
            institution,
            portfolio,
            account,
            cashAsset,
            fundAsset,
            initializedAtUtc);

        var sessionResult =
            await _sessionFactory.OpenAsync(cancellationToken);

        if (!sessionResult.Succeeded)
        {
            throw new CoreLedgerSetupUnavailableException(
                sessionResult.Failure!.Category);
        }

        await using var session = sessionResult.Value!;

        if (!await session.TryInitializeAsync(
                setup,
                cancellationToken))
        {
            throw new CoreLedgerAlreadyInitializedException();
        }

        return new InitializeCoreLedgerResult(
            household.Id,
            householdMember?.Id,
            institution.Id,
            portfolio.Id,
            account.Id,
            cashAsset.Id,
            fundAsset.Id);
    }

    private static string NormalizeCurrencyName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalized = value.Trim();

        if (normalized.Length > 128)
        {
            throw new ArgumentException(
                "Currency name cannot exceed 128 characters.",
                nameof(value));
        }

        return normalized;
    }

    private static int ValidateMinorUnitDigits(int value)
    {
        if (value is < 0 or > 8)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Currency minor-unit digits must be between 0 and 8.");
        }

        return value;
    }
}
