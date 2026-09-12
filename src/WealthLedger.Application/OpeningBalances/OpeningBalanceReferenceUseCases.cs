using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.OpeningBalances;

public sealed record CreateOpeningBalanceCurrencyCommand(
    CurrencyCode Code,
    string Name,
    int MinorUnitDigits);

public sealed record CreateOpeningBalanceInstitutionCommand(
    string Code,
    string Name,
    InstitutionType Type);

public sealed record CreateOpeningBalanceAccountCommand(
    Guid HouseholdId,
    Guid? InstitutionId,
    string Code,
    string Name,
    AccountType Type,
    DateOnly? OpenedOn = null);

public sealed record CreateOpeningBalanceAssetCommand(
    string Code,
    string Name,
    AssetType Type,
    CurrencyCode BaseCurrency,
    LotTrackingMode LotTrackingMode);

public sealed class CreateOpeningBalanceCurrencyUseCase
{
    private readonly IOpeningBalanceReferenceWriteStore _writeStore;

    public CreateOpeningBalanceCurrencyUseCase(
        IOpeningBalanceReferenceWriteStore writeStore)
    {
        _writeStore = writeStore
            ?? throw new ArgumentNullException(nameof(writeStore));
    }

    public async Task<OpeningBalanceReferenceCreationResult<
        OpeningBalanceCurrencyReference>> ExecuteAsync(
            CreateOpeningBalanceCurrencyCommand command,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Code);

        var candidate =
            new OpeningBalanceCurrencyReference(
                command.Code,
                OpeningBalanceReferenceValidation.NormalizeCurrencyName(
                    command.Name),
                OpeningBalanceReferenceValidation.ValidateMinorUnitDigits(
                    command.MinorUnitDigits));

        var writeResult =
            await _writeStore.TryCreateCurrencyAsync(
                candidate,
                cancellationToken);

        return OpeningBalanceReferenceValidation.Resolve(writeResult);
    }
}

public sealed class CreateOpeningBalanceInstitutionUseCase
{
    private readonly IOpeningBalanceReferenceWriteStore _writeStore;

    public CreateOpeningBalanceInstitutionUseCase(
        IOpeningBalanceReferenceWriteStore writeStore)
    {
        _writeStore = writeStore
            ?? throw new ArgumentNullException(nameof(writeStore));
    }

    public async Task<OpeningBalanceReferenceCreationResult<
        OpeningBalanceInstitutionReference>> ExecuteAsync(
            CreateOpeningBalanceInstitutionCommand command,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!Enum.IsDefined(command.Type))
        {
            throw OpeningBalanceReferenceValidation.InvalidShape(
                "The institution type is not supported by the opening-balance workflow.");
        }

        var candidate =
            Institution.Create(
                Guid.NewGuid(),
                command.Code,
                command.Name,
                command.Type);

        var writeResult =
            await _writeStore.TryCreateInstitutionAsync(
                candidate,
                cancellationToken);

        return OpeningBalanceReferenceValidation.Resolve(writeResult);
    }
}

public sealed class CreateOpeningBalanceAccountUseCase
{
    private readonly IOpeningBalanceReferenceReadStore _readStore;
    private readonly IOpeningBalanceReferenceWriteStore _writeStore;

    public CreateOpeningBalanceAccountUseCase(
        IOpeningBalanceReferenceReadStore readStore,
        IOpeningBalanceReferenceWriteStore writeStore)
    {
        _readStore = readStore
            ?? throw new ArgumentNullException(nameof(readStore));
        _writeStore = writeStore
            ?? throw new ArgumentNullException(nameof(writeStore));
    }

    public async Task<OpeningBalanceReferenceCreationResult<
        OpeningBalanceAccountReference>> ExecuteAsync(
            CreateOpeningBalanceAccountCommand command,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureNonEmpty(command.HouseholdId, nameof(command.HouseholdId));

        if (command.InstitutionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Institution ID cannot be empty.",
                nameof(command));
        }

        ValidateAccountTypeAndInstitution(
            command.Type,
            command.InstitutionId);

        var household =
            await _readStore.FindHouseholdAsync(
                command.HouseholdId,
                cancellationToken);

        if (household is null)
        {
            throw OpeningBalanceReferenceValidation.NotFound(
                "The requested household does not exist.");
        }

        if (command.InstitutionId is Guid institutionId)
        {
            var institution =
                await _readStore.FindInstitutionAsync(
                    institutionId,
                    cancellationToken);

            if (institution is null)
            {
                throw OpeningBalanceReferenceValidation.NotFound(
                    "The requested institution does not exist.");
            }

            if (!institution.IsActive)
            {
                throw OpeningBalanceReferenceValidation.Inactive(
                    "The requested institution is inactive.");
            }
        }

        var candidate =
            Account.Create(
                Guid.NewGuid(),
                household.HouseholdId,
                command.InstitutionId,
                command.Code,
                command.Name,
                command.Type,
                command.OpenedOn);

        var writeResult =
            await _writeStore.TryCreateAccountAsync(
                candidate,
                cancellationToken);

        return OpeningBalanceReferenceValidation.Resolve(writeResult);
    }

    private static void ValidateAccountTypeAndInstitution(
        AccountType accountType,
        Guid? institutionId)
    {
        if (accountType is not AccountType.Cash
            and not AccountType.Investment
            and not AccountType.PhysicalVault
            and not AccountType.Pension)
        {
            throw OpeningBalanceReferenceValidation.InvalidShape(
                "The account type is not supported by the opening-balance workflow.");
        }

        if (accountType is AccountType.Investment or AccountType.Pension
            && institutionId is null)
        {
            throw OpeningBalanceReferenceValidation.InvalidShape(
                "Investment and pension accounts require an institution.");
        }
    }

    private static void EnsureNonEmpty(
        Guid value,
        string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                $"{parameterName} cannot be empty.",
                parameterName);
        }
    }
}

public sealed class CreateOpeningBalanceAssetUseCase
{
    private readonly IOpeningBalanceReferenceReadStore _readStore;
    private readonly IOpeningBalanceReferenceWriteStore _writeStore;
    private readonly TimeProvider _timeProvider;

    public CreateOpeningBalanceAssetUseCase(
        IOpeningBalanceReferenceReadStore readStore,
        IOpeningBalanceReferenceWriteStore writeStore,
        TimeProvider timeProvider)
    {
        _readStore = readStore
            ?? throw new ArgumentNullException(nameof(readStore));
        _writeStore = writeStore
            ?? throw new ArgumentNullException(nameof(writeStore));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<OpeningBalanceReferenceCreationResult<
        OpeningBalanceAssetReference>> ExecuteAsync(
            CreateOpeningBalanceAssetCommand command,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.BaseCurrency);

        var baseUnit = ValidateAndDeriveBaseUnit(
            command.Type,
            command.LotTrackingMode);

        var currency =
            await _readStore.FindCurrencyAsync(
                command.BaseCurrency,
                cancellationToken);

        if (currency is null)
        {
            throw OpeningBalanceReferenceValidation.NotFound(
                "The requested asset base currency does not exist.");
        }

        var candidate =
            Asset.Create(
                Guid.NewGuid(),
                command.Code,
                command.Name,
                command.Type,
                baseUnit,
                currency.Code,
                command.LotTrackingMode);

        var writeResult =
            await _writeStore.TryCreateAssetAsync(
                candidate,
                _timeProvider.GetUtcNow(),
                cancellationToken);

        return OpeningBalanceReferenceValidation.Resolve(writeResult);
    }

    private static AssetUnit ValidateAndDeriveBaseUnit(
        AssetType assetType,
        LotTrackingMode lotTrackingMode)
    {
        return assetType switch
        {
            AssetType.Cash or AssetType.Currency
                when lotTrackingMode == LotTrackingMode.None
                => AssetUnit.CurrencyUnit,

            AssetType.Fund
                when lotTrackingMode is LotTrackingMode.Optional
                    or LotTrackingMode.Required
                => AssetUnit.FundUnit,

            AssetType.Equity
                when lotTrackingMode is LotTrackingMode.Optional
                    or LotTrackingMode.Required
                => AssetUnit.Share,

            AssetType.PhysicalGold
                when lotTrackingMode == LotTrackingMode.Required
                => AssetUnit.GrossGram,

            AssetType.Cash
                or AssetType.Currency
                or AssetType.Fund
                or AssetType.Equity
                or AssetType.PhysicalGold
                => throw OpeningBalanceReferenceValidation.InvalidShape(
                    "The lot-tracking mode is incompatible with the asset type."),

            _ => throw new OpeningBalanceException(
                OpeningBalanceErrorCategory.Validation,
                OpeningBalanceErrorCodes.AssetNotSupported,
                "The asset type is not supported by the opening-balance workflow.")
        };
    }
}

internal static class OpeningBalanceReferenceValidation
{
    internal static string NormalizeCurrencyName(string value)
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

    internal static int ValidateMinorUnitDigits(int value)
    {
        if (value is < 0 or > 8)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Currency minor-unit digits must be between 0 and 8.");
        }

        return value;
    }

    internal static OpeningBalanceReferenceCreationResult<T> Resolve<T>(
        OpeningBalanceReferenceWriteResult<T> result)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Status == OpeningBalanceReferenceWriteStatus.Conflict)
        {
            throw new OpeningBalanceException(
                OpeningBalanceErrorCategory.Conflict,
                OpeningBalanceErrorCodes.ReferenceConflict,
                "A reference with the same code already exists with different or inactive facts.");
        }

        return new OpeningBalanceReferenceCreationResult<T>(
            result.Status == OpeningBalanceReferenceWriteStatus.Created,
            result.Reference
                ?? throw new InvalidOperationException(
                    "The reference write store returned no reference."));
    }

    internal static OpeningBalanceException NotFound(string message)
        => new(
            OpeningBalanceErrorCategory.NotFound,
            OpeningBalanceErrorCodes.ReferenceNotFound,
            message);

    internal static OpeningBalanceException Inactive(string message)
        => new(
            OpeningBalanceErrorCategory.Validation,
            OpeningBalanceErrorCodes.ReferenceInactive,
            message);

    internal static OpeningBalanceException InvalidShape(string message)
        => new(
            OpeningBalanceErrorCategory.Validation,
            OpeningBalanceErrorCodes.ReferenceShapeInvalid,
            message);
}
