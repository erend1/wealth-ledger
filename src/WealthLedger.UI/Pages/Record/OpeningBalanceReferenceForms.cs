using System.Globalization;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Record;

public sealed class OpeningBalanceCurrencyForm
{
    public string? Code { get; set; }

    public string? Name { get; set; }

    public string? MinorUnitDigits { get; set; } = "2";
}

public sealed class OpeningBalanceInstitutionForm
{
    public string? Code { get; set; }

    public string? Name { get; set; }

    public string? TypeCode { get; set; }
}

public sealed class OpeningBalanceAccountForm
{
    public string? InstitutionId { get; set; }

    public string? Code { get; set; }

    public string? Name { get; set; }

    public string? TypeCode { get; set; }

    public string? OpenedOn { get; set; }
}

public sealed class OpeningBalanceAssetForm
{
    public string? Code { get; set; }

    public string? Name { get; set; }

    public string? TypeCode { get; set; }

    public string? BaseCurrencyCode { get; set; }

    public string? LotTrackingModeCode { get; set; }
}

public sealed record OpeningBalanceReferenceFormResult<T>(
    T? Command,
    IReadOnlyList<OpeningBalanceFormError> Errors)
    where T : class
{
    public bool Succeeded => Command is not null && Errors.Count == 0;
}

public static class OpeningBalanceReferenceFormMapper
{
    public static OpeningBalanceReferenceFormResult<
        CreateOpeningBalanceCurrencyCommand> MapCurrency(
        OpeningBalanceCurrencyForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var errors = new List<OpeningBalanceFormError>();
        var code = CurrencyCode(
            form.Code,
            "CurrencyInput.Code",
            errors);
        var name = RequiredText(
            form.Name,
            128,
            "CurrencyInput.Name",
            errors);

        if (!int.TryParse(
                form.MinorUnitDigits?.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var digits)
            || digits is < 0 or > 8)
        {
            errors.Add(new(
                "CurrencyInput.MinorUnitDigits",
                "Opening_Validation_MinorUnitDigits"));
        }

        return errors.Count == 0
            ? new(
                new CreateOpeningBalanceCurrencyCommand(
                    new CurrencyCode(code!),
                    name!,
                    digits),
                errors)
            : new(null, errors);
    }

    public static OpeningBalanceReferenceFormResult<
        CreateOpeningBalanceInstitutionCommand> MapInstitution(
        OpeningBalanceInstitutionForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var errors = new List<OpeningBalanceFormError>();
        var code = StableCode(
            form.Code,
            "InstitutionInput.Code",
            errors);
        var name = RequiredText(
            form.Name,
            256,
            "InstitutionInput.Name",
            errors);
        var type = ParseCode(
            form.TypeCode,
            Enum.GetValues<InstitutionType>(),
            StableCodes.ToCode,
            "InstitutionInput.TypeCode",
            errors);

        return errors.Count == 0
            ? new(
                new CreateOpeningBalanceInstitutionCommand(
                    code!,
                    name!,
                    type!.Value),
                errors)
            : new(null, errors);
    }

    public static OpeningBalanceReferenceFormResult<
        CreateOpeningBalanceAccountCommand> MapAccount(
        OpeningBalanceAccountForm form,
        Guid householdId)
    {
        ArgumentNullException.ThrowIfNull(form);
        var errors = new List<OpeningBalanceFormError>();
        var code = StableCode(
            form.Code,
            "AccountInput.Code",
            errors);
        var name = RequiredText(
            form.Name,
            256,
            "AccountInput.Name",
            errors);
        var type = ParseCode(
            form.TypeCode,
            new[]
            {
                AccountType.Cash,
                AccountType.Investment,
                AccountType.PhysicalVault,
                AccountType.Pension
            },
            StableCodes.ToCode,
            "AccountInput.TypeCode",
            errors);
        var institutionId = OptionalGuid(
            form.InstitutionId,
            "AccountInput.InstitutionId",
            errors);
        var openedOn = OptionalDate(
            form.OpenedOn,
            "AccountInput.OpenedOn",
            errors);

        if (type is AccountType.Investment or AccountType.Pension
            && institutionId is null)
        {
            errors.Add(new(
                "AccountInput.InstitutionId",
                "Opening_Validation_InstitutionRequired"));
        }

        return errors.Count == 0
            ? new(
                new CreateOpeningBalanceAccountCommand(
                    householdId,
                    institutionId,
                    code!,
                    name!,
                    type!.Value,
                    openedOn),
                errors)
            : new(null, errors);
    }

    public static OpeningBalanceReferenceFormResult<
        CreateOpeningBalanceAssetCommand> MapAsset(
        OpeningBalanceAssetForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var errors = new List<OpeningBalanceFormError>();
        var code = StableCode(
            form.Code,
            "AssetInput.Code",
            errors);
        var name = RequiredText(
            form.Name,
            256,
            "AssetInput.Name",
            errors);
        var type = ParseCode(
            form.TypeCode,
            new[]
            {
                AssetType.Cash,
                AssetType.Currency,
                AssetType.Fund,
                AssetType.Equity,
                AssetType.PhysicalGold
            },
            StableCodes.ToCode,
            "AssetInput.TypeCode",
            errors);
        var currencyCode = CurrencyCode(
            form.BaseCurrencyCode,
            "AssetInput.BaseCurrencyCode",
            errors);
        var lotTrackingMode = ParseCode(
            form.LotTrackingModeCode,
            Enum.GetValues<LotTrackingMode>(),
            StableCodes.ToCode,
            "AssetInput.LotTrackingModeCode",
            errors);

        if (type is not null
            && lotTrackingMode is not null
            && !IsCompatible(type.Value, lotTrackingMode.Value))
        {
            errors.Add(new(
                "AssetInput.LotTrackingModeCode",
                "Opening_Validation_AssetLotMode"));
        }

        return errors.Count == 0
            ? new(
                new CreateOpeningBalanceAssetCommand(
                    code!,
                    name!,
                    type!.Value,
                    new CurrencyCode(currencyCode!),
                    lotTrackingMode!.Value),
                errors)
            : new(null, errors);
    }

    private static bool IsCompatible(
        AssetType type,
        LotTrackingMode mode)
        => type switch
        {
            AssetType.Cash or AssetType.Currency =>
                mode == LotTrackingMode.None,
            AssetType.Fund or AssetType.Equity =>
                mode is LotTrackingMode.Optional or LotTrackingMode.Required,
            AssetType.PhysicalGold => mode == LotTrackingMode.Required,
            _ => false
        };

    private static T? ParseCode<T>(
        string? value,
        IReadOnlyList<T> accepted,
        Func<T, string> toCode,
        string fieldName,
        ICollection<OpeningBalanceFormError> errors)
        where T : struct
    {
        var normalized = value?.Trim().ToUpperInvariant();
        var matches = accepted
            .Where(item => string.Equals(
                toCode(item),
                normalized,
                StringComparison.Ordinal))
            .ToArray();

        if (matches.Length != 1)
        {
            errors.Add(new(fieldName, "Opening_Validation_ChoiceRequired"));
            return null;
        }

        return matches[0];
    }

    private static Guid? OptionalGuid(
        string? value,
        string fieldName,
        ICollection<OpeningBalanceFormError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Guid.TryParseExact(value.Trim(), "D", out var parsed)
            && parsed != Guid.Empty)
        {
            return parsed;
        }

        errors.Add(new(fieldName, "Opening_Validation_ChoiceUnavailable"));
        return null;
    }

    private static DateOnly? OptionalDate(
        string? value,
        string fieldName,
        ICollection<OpeningBalanceFormError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParseExact(
                value.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return parsed;
        }

        errors.Add(new(fieldName, "Opening_Validation_Date"));
        return null;
    }

    private static string? CurrencyCode(
        string? value,
        string fieldName,
        ICollection<OpeningBalanceFormError> errors)
    {
        var normalized = value?.Trim().ToUpperInvariant();

        if (normalized is null
            || normalized.Length != 3
            || normalized.Any(character => character is < 'A' or > 'Z'))
        {
            errors.Add(new(fieldName, "Opening_Validation_CurrencyCode"));
            return null;
        }

        return normalized;
    }

    private static string? StableCode(
        string? value,
        string fieldName,
        ICollection<OpeningBalanceFormError> errors)
    {
        var normalized = value?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Length > 64
            || !normalized.All(character => character is >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '_'
                or '-'))
        {
            errors.Add(new(fieldName, "Opening_Validation_StableCode"));
            return null;
        }

        return normalized;
    }

    private static string? RequiredText(
        string? value,
        int maximumLength,
        string fieldName,
        ICollection<OpeningBalanceFormError> errors)
    {
        var normalized = value?.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            errors.Add(new(fieldName, "Opening_Validation_Required"));
            return null;
        }

        if (normalized.Length > maximumLength
            || normalized.Any(char.IsControl))
        {
            errors.Add(new(fieldName, "Opening_Validation_Text"));
            return null;
        }

        return normalized;
    }
}
