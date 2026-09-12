using System.Globalization;
using WealthLedger.Application.Navigation;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.UI.Pages.Record;

public sealed class OpeningBalanceForm
{
    public string? PortfolioId { get; set; }

    public string? AccountId { get; set; }

    public string? AssetId { get; set; }

    public string? AsOfDate { get; set; }

    public string? Quantity { get; set; }

    public string? ExternalReference { get; set; }

    public string? Note { get; set; }

    public string? IdempotencyKey { get; set; }

    public List<OpeningBalanceLotForm> Lots { get; set; } = [];
}

public sealed class OpeningBalanceLotForm
{
    public string? Quantity { get; set; }

    public string? AcquiredOn { get; set; }

    public string? CostBasisStatusCode { get; set; }

    public string? CostAmount { get; set; }

    public string? CostCurrencyCode { get; set; }

    public string? FinenessChoiceCode { get; set; }

    public string? PreciseFinenessPerMille { get; set; }

    public string? PieceCount { get; set; }

    public string? Hallmark { get; set; }

    public string? CertificateReference { get; set; }

    public string? Note { get; set; }
}

public sealed record OpeningBalanceFormError(
    string FieldName,
    string ResourceKey);

public sealed record OpeningBalanceFormMappingResult(
    RecordOpeningBalanceCommand? Command,
    AssetNavigationItem? Asset,
    IReadOnlyList<OpeningBalanceFormError> Errors)
{
    public bool Succeeded => Command is not null && Errors.Count == 0;
}

public sealed record FinenessChoice(
    string Code,
    string ResourceKey,
    int? PartsPerMillion)
{
    public const string PreciseCode = "PRECISE";

    public static IReadOnlyList<FinenessChoice> All { get; } =
    [
        new("24K_999_9", "Opening_Fineness_24K", 999_900),
        new("22K_916", "Opening_Fineness_22K", 916_000),
        new("18K_750", "Opening_Fineness_18K", 750_000),
        new("14K_585", "Opening_Fineness_14K", 585_000),
        new("8K_333", "Opening_Fineness_8K", 333_000),
        new(PreciseCode, "Opening_Fineness_Precise", null)
    ];
}

public static class OpeningBalanceFormMapper
{
    private const int MaximumReferenceLength = 256;
    private const int MaximumTransactionNoteLength = 2_000;

    public static OpeningBalanceFormMappingResult Map(
        OpeningBalanceForm form,
        Guid householdId,
        OpeningBalanceChoices choices)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(choices);

        var errors = new List<OpeningBalanceFormError>();

        if (householdId == Guid.Empty
            || choices.Household.HouseholdId != householdId)
        {
            errors.Add(new(
                string.Empty,
                "Opening_Validation_ContextUnavailable"));
        }

        var portfolio = ParseChoice(
            form.PortfolioId,
            nameof(form.PortfolioId),
            choices.Portfolios.Items,
            item => item.PortfolioId,
            errors);
        var account = ParseChoice(
            form.AccountId,
            nameof(form.AccountId),
            choices.Accounts.Items,
            item => item.AccountId,
            errors);
        var asset = ParseChoice(
            form.AssetId,
            nameof(form.AssetId),
            choices.Assets.Items,
            item => item.AssetId,
            errors);
        var asOfDate = ParseRequiredDate(
            form.AsOfDate,
            nameof(form.AsOfDate),
            errors);
        var quantity = ParsePositiveQuantity(
            form.Quantity,
            nameof(form.Quantity),
            errors);
        var externalReference = OptionalText(
            form.ExternalReference,
            MaximumReferenceLength,
            nameof(form.ExternalReference),
            errors);
        var note = RequiredText(
            form.Note,
            MaximumTransactionNoteLength,
            nameof(form.Note),
            errors);

        if (asset is not null
            && quantity is not null
            && asset.Type is AssetType.Cash or AssetType.Currency)
        {
            ValidateCurrencyPrecision(
                quantity.Value,
                asset,
                choices.Currencies.Items,
                errors);
        }

        var lots = MapLots(
            form,
            asset,
            asOfDate,
            choices.Currencies.Items,
            errors);

        if (asset is not null
            && asset.Type is not AssetType.Cash and not AssetType.Currency
            && quantity is not null
            && lots is not null)
        {
            try
            {
                var lotTotal = lots.Aggregate(
                    0L,
                    (total, lot) => checked(total + lot.Quantity.RawE8));

                if (lotTotal != quantity.Value.RawE8)
                {
                    errors.Add(new(
                        nameof(form.Lots),
                        "Opening_Validation_LotTotalMismatch"));
                }
            }
            catch (OverflowException)
            {
                errors.Add(new(
                    nameof(form.Lots),
                    "Opening_Validation_LotTotalMismatch"));
            }
        }

        if (errors.Count > 0
            || portfolio is null
            || account is null
            || asset is null
            || asOfDate is null
            || quantity is null
            || note is null
            || lots is null)
        {
            return new OpeningBalanceFormMappingResult(
                Command: null,
                asset,
                errors);
        }

        return new OpeningBalanceFormMappingResult(
            new RecordOpeningBalanceCommand(
                householdId,
                portfolio.PortfolioId,
                account.AccountId,
                asset.AssetId,
                asOfDate.Value,
                quantity.Value,
                externalReference,
                note,
                lots),
            asset,
            errors);
    }

    public static bool TryParseIdempotencyKey(string? value, out string key)
    {
        key = value?.Trim() ?? string.Empty;

        return key.Length is >= 1 and <= 256
               && !key.Any(char.IsControl);
    }

    private static IReadOnlyList<OpeningBalanceLotCommand>? MapLots(
        OpeningBalanceForm form,
        AssetNavigationItem? asset,
        DateOnly? asOfDate,
        IReadOnlyList<CurrencyNavigationItem> currencies,
        ICollection<OpeningBalanceFormError> errors)
    {
        if (asset is null)
        {
            return null;
        }

        if (asset.Type is AssetType.Cash or AssetType.Currency)
        {
            if (form.Lots.Count > 0)
            {
                errors.Add(new(
                    nameof(form.Lots),
                    "Opening_Validation_LotsForbidden"));
            }

            return [];
        }

        if (form.Lots.Count == 0)
        {
            errors.Add(new(
                nameof(form.Lots),
                "Opening_Validation_LotsRequired"));
            return null;
        }

        var result = new List<OpeningBalanceLotCommand>(form.Lots.Count);

        for (var index = 0; index < form.Lots.Count; index++)
        {
            var input = form.Lots[index];
            var prefix = $"{nameof(form.Lots)}[{index}]";
            var quantity = ParsePositiveQuantity(
                input.Quantity,
                $"{prefix}.{nameof(input.Quantity)}",
                errors);
            var acquiredOn = ParseOptionalDate(
                input.AcquiredOn,
                $"{prefix}.{nameof(input.AcquiredOn)}",
                errors);

            if (acquiredOn is not null
                && asOfDate is not null
                && acquiredOn > asOfDate)
            {
                errors.Add(new(
                    $"{prefix}.{nameof(input.AcquiredOn)}",
                    "Opening_Validation_AcquiredAfterAsOf"));
            }

            var costBasis = ParseCostBasis(
                input,
                prefix,
                currencies,
                errors);
            var goldDetail = asset.Type == AssetType.PhysicalGold
                ? ParseGoldDetail(input, prefix, errors)
                : RejectAndDiscardGoldFields(input, prefix, errors);

            if (quantity is not null
                && costBasis is not null
                && (asset.Type != AssetType.PhysicalGold
                    || goldDetail is not null))
            {
                result.Add(new OpeningBalanceLotCommand(
                    quantity.Value,
                    acquiredOn,
                    costBasis,
                    goldDetail));
            }
        }

        return result.Count == form.Lots.Count
            ? result
            : null;
    }

    private static CostBasis? ParseCostBasis(
        OpeningBalanceLotForm input,
        string prefix,
        IReadOnlyList<CurrencyNavigationItem> currencies,
        ICollection<OpeningBalanceFormError> errors)
    {
        var status = input.CostBasisStatusCode?.Trim().ToUpperInvariant();

        if (status == "UNKNOWN")
        {
            if (!string.IsNullOrWhiteSpace(input.CostAmount)
                || !string.IsNullOrWhiteSpace(input.CostCurrencyCode))
            {
                errors.Add(new(
                    $"{prefix}.{nameof(input.CostAmount)}",
                    "Opening_Validation_UnknownCostHasAmount"));
                return null;
            }

            return CostBasis.Unknown();
        }

        if (status != "KNOWN")
        {
            errors.Add(new(
                $"{prefix}.{nameof(input.CostBasisStatusCode)}",
                "Opening_Validation_CostStatusRequired"));
            return null;
        }

        var currencyCode = input.CostCurrencyCode?.Trim().ToUpperInvariant();
        var currency = currencies.SingleOrDefault(
            item => string.Equals(
                item.Code,
                currencyCode,
                StringComparison.Ordinal));

        if (currency is null)
        {
            errors.Add(new(
                $"{prefix}.{nameof(input.CostCurrencyCode)}",
                "Opening_Validation_CostCurrency"));
            return null;
        }

        if (!TryParseNonNegativeMoney(
                input.CostAmount,
                currency,
                out var money))
        {
            errors.Add(new(
                $"{prefix}.{nameof(input.CostAmount)}",
                "Opening_Validation_CostAmount"));
            return null;
        }

        return CostBasis.Known(money!);
    }

    private static PhysicalGoldLotDetail? ParseGoldDetail(
        OpeningBalanceLotForm input,
        string prefix,
        ICollection<OpeningBalanceFormError> errors)
    {
        var choiceCode = input.FinenessChoiceCode?.Trim().ToUpperInvariant();
        var choice = FinenessChoice.All.SingleOrDefault(
            candidate => string.Equals(
                candidate.Code,
                choiceCode,
                StringComparison.Ordinal));
        Fineness? fineness = null;

        if (choice is null)
        {
            errors.Add(new(
                $"{prefix}.{nameof(input.FinenessChoiceCode)}",
                "Opening_Validation_FinenessChoice"));
        }
        else if (choice.PartsPerMillion is int ppm)
        {
            if (!string.IsNullOrWhiteSpace(input.PreciseFinenessPerMille))
            {
                errors.Add(new(
                    $"{prefix}.{nameof(input.PreciseFinenessPerMille)}",
                    "Opening_Validation_PreciseFinenessPresetConflict"));
            }
            else
            {
                fineness = new Fineness(ppm);
            }
        }
        else if (!TryParseDecimal(
                     input.PreciseFinenessPerMille,
                     out var perMille))
        {
            errors.Add(new(
                $"{prefix}.{nameof(input.PreciseFinenessPerMille)}",
                "Opening_Validation_PreciseFineness"));
        }
        else
        {
            try
            {
                fineness = Fineness.FromPerMille(perMille);
            }
            catch (Exception exception)
                when (exception is ArgumentException or OverflowException)
            {
                errors.Add(new(
                    $"{prefix}.{nameof(input.PreciseFinenessPerMille)}",
                    "Opening_Validation_PreciseFineness"));
            }
        }

        if (!int.TryParse(
                input.PieceCount?.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var pieceCount)
            || pieceCount <= 0)
        {
            errors.Add(new(
                $"{prefix}.{nameof(input.PieceCount)}",
                "Opening_Validation_PieceCount"));
        }

        var hallmark = OptionalText(
            input.Hallmark,
            128,
            $"{prefix}.{nameof(input.Hallmark)}",
            errors);
        var certificate = OptionalText(
            input.CertificateReference,
            256,
            $"{prefix}.{nameof(input.CertificateReference)}",
            errors);
        var note = OptionalText(
            input.Note,
            1_000,
            $"{prefix}.{nameof(input.Note)}",
            errors);

        if (fineness is null || pieceCount <= 0)
        {
            return null;
        }

        try
        {
            return new PhysicalGoldLotDetail(
                fineness,
                pieceCount,
                hallmark,
                certificate,
                note);
        }
        catch (ArgumentException)
        {
            errors.Add(new(
                prefix,
                "Opening_Validation_GoldDetail"));
            return null;
        }
    }

    private static PhysicalGoldLotDetail? RejectAndDiscardGoldFields(
        OpeningBalanceLotForm input,
        string prefix,
        ICollection<OpeningBalanceFormError> errors)
    {
        if (!string.IsNullOrWhiteSpace(input.FinenessChoiceCode)
            || !string.IsNullOrWhiteSpace(input.PreciseFinenessPerMille)
            || !string.IsNullOrWhiteSpace(input.PieceCount)
            || !string.IsNullOrWhiteSpace(input.Hallmark)
            || !string.IsNullOrWhiteSpace(input.CertificateReference)
            || !string.IsNullOrWhiteSpace(input.Note))
        {
            errors.Add(new(
                prefix,
                "Opening_Validation_GoldFieldsForbidden"));
        }

        return null;
    }

    private static void ValidateCurrencyPrecision(
        Quantity quantity,
        AssetNavigationItem asset,
        IReadOnlyList<CurrencyNavigationItem> currencies,
        ICollection<OpeningBalanceFormError> errors)
    {
        var currency = currencies.SingleOrDefault(
            item => string.Equals(
                item.Code,
                asset.BaseCurrencyCode,
                StringComparison.Ordinal));

        if (currency is null
            || currency.MinorUnitDigits is < 0 or > 8)
        {
            errors.Add(new(
                nameof(OpeningBalanceForm.Quantity),
                "Opening_Validation_CurrencyMetadata"));
            return;
        }

        var rawPerMinorUnit = PowerOfTen(8 - currency.MinorUnitDigits);

        if (quantity.RawE8 % rawPerMinorUnit != 0)
        {
            errors.Add(new(
                nameof(OpeningBalanceForm.Quantity),
                "Opening_Validation_CurrencyPrecision"));
        }
    }

    private static T? ParseChoice<T>(
        string? value,
        string fieldName,
        IReadOnlyList<T> choices,
        Func<T, Guid> identity,
        ICollection<OpeningBalanceFormError> errors)
        where T : class
    {
        if (!Guid.TryParseExact(value?.Trim(), "D", out var parsed)
            || parsed == Guid.Empty)
        {
            errors.Add(new(fieldName, "Opening_Validation_ChoiceRequired"));
            return null;
        }

        var selected = choices.SingleOrDefault(item => identity(item) == parsed);

        if (selected is null)
        {
            errors.Add(new(fieldName, "Opening_Validation_ChoiceUnavailable"));
        }

        return selected;
    }

    private static Quantity? ParsePositiveQuantity(
        string? value,
        string fieldName,
        ICollection<OpeningBalanceFormError> errors)
    {
        if (!TryParseDecimal(value, out var parsed) || parsed <= 0)
        {
            errors.Add(new(fieldName, "Opening_Validation_Quantity"));
            return null;
        }

        try
        {
            return Quantity.FromDecimal(parsed);
        }
        catch (Exception exception)
            when (exception is ArgumentException or OverflowException)
        {
            errors.Add(new(fieldName, "Opening_Validation_Quantity"));
            return null;
        }
    }

    private static bool TryParseNonNegativeMoney(
        string? value,
        CurrencyNavigationItem currency,
        out Money? money)
    {
        money = null;

        if (!TryParseDecimal(value, out var parsed) || parsed < 0)
        {
            return false;
        }

        try
        {
            var scaled = checked(parsed * PowerOfTen(currency.MinorUnitDigits));

            if (decimal.Truncate(scaled) != scaled)
            {
                return false;
            }

            money = Money.FromMinorUnits(
                checked((long)scaled),
                new CurrencyCode(currency.Code));
            return true;
        }
        catch (Exception exception)
            when (exception is ArgumentException or OverflowException)
        {
            return false;
        }
    }

    private static DateOnly? ParseRequiredDate(
        string? value,
        string fieldName,
        ICollection<OpeningBalanceFormError> errors)
    {
        var parsed = ParseDate(value);

        if (parsed is null)
        {
            errors.Add(new(fieldName, "Opening_Validation_Date"));
        }

        return parsed;
    }

    private static DateOnly? ParseOptionalDate(
        string? value,
        string fieldName,
        ICollection<OpeningBalanceFormError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parsed = ParseDate(value);

        if (parsed is null)
        {
            errors.Add(new(fieldName, "Opening_Validation_Date"));
        }

        return parsed;
    }

    private static DateOnly? ParseDate(string? value)
        => DateOnly.TryParseExact(
            value?.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;

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

    private static string? OptionalText(
        string? value,
        int maximumLength,
        string fieldName,
        ICollection<OpeningBalanceFormError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();

        if (normalized.Length > maximumLength
            || normalized.Any(char.IsControl))
        {
            errors.Add(new(fieldName, "Opening_Validation_Text"));
            return null;
        }

        return normalized;
    }

    private static bool TryParseDecimal(string? value, out decimal parsed)
    {
        parsed = 0;
        var normalized = value?.Trim();

        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Contains('e', StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(' ')
            || normalized.Contains('.') && normalized.Contains(','))
        {
            return false;
        }

        normalized = normalized.Replace(',', '.');

        return decimal.TryParse(
            normalized,
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out parsed);
    }

    private static long PowerOfTen(int exponent)
    {
        if (exponent is < 0 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(exponent));
        }

        long result = 1;

        for (var index = 0; index < exponent; index++)
        {
            result = checked(result * 10);
        }

        return result;
    }
}
