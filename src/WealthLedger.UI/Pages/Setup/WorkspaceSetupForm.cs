using System.Globalization;
using WealthLedger.Application.Setup;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Setup;

public sealed class WorkspaceSetupForm
{
    public string? BaseCurrencyCode { get; set; } = "TRY";

    public string? BaseCurrencyName { get; set; } = "Türk lirası";

    public string? MinorUnitDigits { get; set; } = "2";

    public string? HouseholdName { get; set; }

    public string? HouseholdMemberDisplayName { get; set; }

    public string? InstitutionCode { get; set; } = "ANA_KURUM";

    public string? InstitutionName { get; set; }

    public string? InstitutionTypeCode { get; set; } = "BROKER";

    public string? PortfolioCode { get; set; } = "ANA";

    public string? PortfolioName { get; set; }

    public string? AccountCode { get; set; } = "ANA_HESAP";

    public string? AccountName { get; set; }

    public string? AccountTypeCode { get; set; } = "INVESTMENT";

    public string? AccountOpenedOn { get; set; }

    public string? CashAssetCode { get; set; } = "TRY_NAKIT";

    public string? CashAssetName { get; set; } = "Türk lirası nakit";

    public string? FundAssetCode { get; set; } = "ILK_FON";

    public string? FundAssetName { get; set; }
}

public sealed record WorkspaceSetupFieldError(
    string FieldName,
    string ResourceKey);

public sealed record WorkspaceSetupFormMappingResult(
    InitializeCoreLedgerCommand? Command,
    IReadOnlyList<WorkspaceSetupFieldError> Errors)
{
    public bool Succeeded => Command is not null && Errors.Count == 0;
}

public static class WorkspaceSetupFormMapper
{
    public static WorkspaceSetupFormMappingResult Map(
        WorkspaceSetupForm form)
    {
        ArgumentNullException.ThrowIfNull(form);

        var errors = new List<WorkspaceSetupFieldError>();

        var currencyCode = RequiredCurrencyCode(
            form.BaseCurrencyCode,
            nameof(form.BaseCurrencyCode),
            errors);
        var currencyName = RequiredText(
            form.BaseCurrencyName,
            maximumLength: 128,
            nameof(form.BaseCurrencyName),
            errors);
        var minorUnitDigits = ParseMinorUnitDigits(
            form.MinorUnitDigits,
            errors);
        var householdName = RequiredText(
            form.HouseholdName,
            maximumLength: 256,
            nameof(form.HouseholdName),
            errors);
        var householdMember = OptionalText(
            form.HouseholdMemberDisplayName,
            maximumLength: 128,
            nameof(form.HouseholdMemberDisplayName),
            errors);
        var institutionCode = RequiredStableCode(
            form.InstitutionCode,
            nameof(form.InstitutionCode),
            errors);
        var institutionName = RequiredText(
            form.InstitutionName,
            maximumLength: 256,
            nameof(form.InstitutionName),
            errors);
        var institutionType = ParseInstitutionType(
            form.InstitutionTypeCode,
            errors);
        var portfolioCode = RequiredStableCode(
            form.PortfolioCode,
            nameof(form.PortfolioCode),
            errors);
        var portfolioName = RequiredText(
            form.PortfolioName,
            maximumLength: 256,
            nameof(form.PortfolioName),
            errors);
        var accountCode = RequiredStableCode(
            form.AccountCode,
            nameof(form.AccountCode),
            errors);
        var accountName = RequiredText(
            form.AccountName,
            maximumLength: 256,
            nameof(form.AccountName),
            errors);
        var accountType = ParseAccountType(
            form.AccountTypeCode,
            errors);
        var accountOpenedOn = ParseOptionalDate(
            form.AccountOpenedOn,
            errors);
        var cashAssetCode = RequiredStableCode(
            form.CashAssetCode,
            nameof(form.CashAssetCode),
            errors);
        var cashAssetName = RequiredText(
            form.CashAssetName,
            maximumLength: 256,
            nameof(form.CashAssetName),
            errors);
        var fundAssetCode = RequiredStableCode(
            form.FundAssetCode,
            nameof(form.FundAssetCode),
            errors);
        var fundAssetName = RequiredText(
            form.FundAssetName,
            maximumLength: 256,
            nameof(form.FundAssetName),
            errors);

        if (cashAssetCode is not null
            && fundAssetCode is not null
            && string.Equals(
                cashAssetCode,
                fundAssetCode,
                StringComparison.Ordinal))
        {
            errors.Add(
                new WorkspaceSetupFieldError(
                    nameof(form.FundAssetCode),
                    "Workspace_Validation_AssetCodesDistinct"));
        }

        if (errors.Count > 0)
        {
            return new WorkspaceSetupFormMappingResult(
                Command: null,
                errors);
        }

        var command = new InitializeCoreLedgerCommand(
            new InitializeCurrencyInput(
                new CurrencyCode(currencyCode!),
                currencyName!,
                minorUnitDigits!.Value),
            householdName!,
            householdMember,
            new InitializeInstitutionInput(
                institutionCode!,
                institutionName!,
                institutionType!.Value),
            new InitializePortfolioInput(
                portfolioCode!,
                portfolioName!),
            new InitializeAccountInput(
                accountCode!,
                accountName!,
                accountType!.Value,
                accountOpenedOn),
            new InitializeAssetInput(
                cashAssetCode!,
                cashAssetName!),
            new InitializeAssetInput(
                fundAssetCode!,
                fundAssetName!));

        return new WorkspaceSetupFormMappingResult(
            command,
            errors);
    }

    private static string? RequiredCurrencyCode(
        string? value,
        string fieldName,
        ICollection<WorkspaceSetupFieldError> errors)
    {
        var normalized = value?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            AddRequired(fieldName, errors);
            return null;
        }

        if (normalized.Length != 3
            || normalized.Any(character =>
                character is < 'A' or > 'Z'))
        {
            errors.Add(
                new WorkspaceSetupFieldError(
                    fieldName,
                    "Workspace_Validation_CurrencyCode"));
            return null;
        }

        return normalized;
    }

    private static string? RequiredStableCode(
        string? value,
        string fieldName,
        ICollection<WorkspaceSetupFieldError> errors)
    {
        var normalized = value?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            AddRequired(fieldName, errors);
            return null;
        }

        if (normalized.Length > 64
            || !normalized.All(IsStableCodeCharacter))
        {
            errors.Add(
                new WorkspaceSetupFieldError(
                    fieldName,
                    "Workspace_Validation_StableCode"));
            return null;
        }

        return normalized;
    }

    private static string? RequiredText(
        string? value,
        int maximumLength,
        string fieldName,
        ICollection<WorkspaceSetupFieldError> errors)
    {
        var normalized = value?.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            AddRequired(fieldName, errors);
            return null;
        }

        if (normalized.Length > maximumLength)
        {
            errors.Add(
                new WorkspaceSetupFieldError(
                    fieldName,
                    "Workspace_Validation_TooLong"));
            return null;
        }

        return normalized;
    }

    private static string? OptionalText(
        string? value,
        int maximumLength,
        string fieldName,
        ICollection<WorkspaceSetupFieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();

        if (normalized.Length > maximumLength)
        {
            errors.Add(
                new WorkspaceSetupFieldError(
                    fieldName,
                    "Workspace_Validation_TooLong"));
            return null;
        }

        return normalized;
    }

    private static int? ParseMinorUnitDigits(
        string? value,
        ICollection<WorkspaceSetupFieldError> errors)
    {
        if (!int.TryParse(
                value?.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed)
            || parsed is < 0 or > 8)
        {
            errors.Add(
                new WorkspaceSetupFieldError(
                    nameof(WorkspaceSetupForm.MinorUnitDigits),
                    "Workspace_Validation_MinorUnitDigits"));
            return null;
        }

        return parsed;
    }

    private static InstitutionType? ParseInstitutionType(
        string? value,
        ICollection<WorkspaceSetupFieldError> errors)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        var parsed = Enum
            .GetValues<InstitutionType>()
            .Cast<InstitutionType?>()
            .SingleOrDefault(
                candidate => string.Equals(
                    StableCodes.ToCode(candidate!.Value),
                    normalized,
                    StringComparison.Ordinal));

        if (parsed is null)
        {
            errors.Add(
                new WorkspaceSetupFieldError(
                    nameof(WorkspaceSetupForm.InstitutionTypeCode),
                    "Workspace_Validation_InstitutionType"));
        }

        return parsed;
    }

    private static AccountType? ParseAccountType(
        string? value,
        ICollection<WorkspaceSetupFieldError> errors)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        var parsed = Enum
            .GetValues<AccountType>()
            .Cast<AccountType?>()
            .SingleOrDefault(
                candidate => string.Equals(
                    StableCodes.ToCode(candidate!.Value),
                    normalized,
                    StringComparison.Ordinal));

        if (parsed is null)
        {
            errors.Add(
                new WorkspaceSetupFieldError(
                    nameof(WorkspaceSetupForm.AccountTypeCode),
                    "Workspace_Validation_AccountType"));
        }

        return parsed;
    }

    private static DateOnly? ParseOptionalDate(
        string? value,
        ICollection<WorkspaceSetupFieldError> errors)
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

        errors.Add(
            new WorkspaceSetupFieldError(
                nameof(WorkspaceSetupForm.AccountOpenedOn),
                "Workspace_Validation_Date"));

        return null;
    }

    private static void AddRequired(
        string fieldName,
        ICollection<WorkspaceSetupFieldError> errors)
        => errors.Add(
            new WorkspaceSetupFieldError(
                fieldName,
                "Workspace_Validation_Required"));

    private static bool IsStableCodeCharacter(char value)
        => value is >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '_'
            or '-';
}
