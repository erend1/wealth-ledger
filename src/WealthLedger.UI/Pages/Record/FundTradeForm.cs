using System.Globalization;
using WealthLedger.Application.FundTrades;
using WealthLedger.Application.Navigation;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.UI.Pages.Record;

/// <summary>
/// The entered source facts of a fund trade, before any parsing.
/// </summary>
/// <remarks>
/// Everything arrives as text because the browser sends text. Parsing happens
/// once, in the mapper, so a malformed value produces one field-anchored
/// message instead of a generic failure.
///
/// The form is not authoritative state. It is posted on every step, and the
/// reviewed plan travels with it so a refresh cannot silently change what the
/// user approved.
/// </remarks>
public sealed class FundTradeForm
{
    public string? PortfolioId { get; set; }

    public string? FundAccountId { get; set; }

    public string? CashAccountId { get; set; }

    public string? FundAssetId { get; set; }

    public string? CashAssetId { get; set; }

    public string? ExecutionDate { get; set; }

    public string? OrderDate { get; set; }

    public string? SettlementDate { get; set; }

    public string? Quantity { get; set; }

    public string? UnitPrice { get; set; }

    public string? CashConsideration { get; set; }

    public string? ExternalReference { get; set; }

    public string? Note { get; set; }

    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// The plan the user was shown, carried back with the post.
    /// </summary>
    public string? ReviewedPlanFingerprint { get; set; }

    public List<ReviewedPlanLineForm> ReviewedPlan { get; set; } = [];

    public List<FundTradeCostForm> Costs { get; set; } = [];
}

public sealed class FundTradeCostForm
{
    public string? TypeCode { get; set; }

    public string? TreatmentCode { get; set; }

    public string? Amount { get; set; }

    public string? Note { get; set; }
}

public sealed class ReviewedPlanLineForm
{
    public string? AssetLotId { get; set; }

    public string? QuantityRawE8 { get; set; }
}

public sealed record FundTradeFormError(
    string FieldName,
    string ResourceKey);

public sealed record FundTradeFormMappingResult(
    FundPurchaseCommand? Purchase,
    FundSaleCommand? Sale,
    IReadOnlyList<FundTradeFormError> Errors)
{
    public bool Succeeded
        => Errors.Count == 0
           && (Purchase is not null || Sale is not null);
}

/// <summary>
/// Turns entered text into a validated fund-trade command.
/// </summary>
internal static class FundTradeFormMapper
{
    internal static FundTradeFormMappingResult Map(
        FundTradeForm form,
        TransactionType tradeType,
        Guid householdId,
        FundTradeChoices choices)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(choices);

        var errors = new List<FundTradeFormError>();

        var portfolioId =
            ParseSelection(
                form.PortfolioId,
                nameof(form.PortfolioId),
                choices.Portfolios.Items.Select(x => x.PortfolioId),
                errors);

        var fundAccountId =
            ParseSelection(
                form.FundAccountId,
                nameof(form.FundAccountId),
                choices.FundAccounts.Items.Select(x => x.AccountId),
                errors);

        var cashAccountId =
            ParseSelection(
                form.CashAccountId,
                nameof(form.CashAccountId),
                choices.CashAccounts.Items.Select(x => x.AccountId),
                errors);

        var fundAssetId =
            ParseSelection(
                form.FundAssetId,
                nameof(form.FundAssetId),
                choices.FundAssets.Items.Select(x => x.AssetId),
                errors);

        var cashAssetId =
            ParseSelection(
                form.CashAssetId,
                nameof(form.CashAssetId),
                choices.CashAssets.Items.Select(x => x.AssetId),
                errors);

        var executionDate =
            ParseRequiredDate(
                form.ExecutionDate,
                nameof(form.ExecutionDate),
                errors);

        var orderDate =
            ParseOptionalDate(
                form.OrderDate,
                nameof(form.OrderDate),
                errors);

        var settlementDate =
            ParseOptionalDate(
                form.SettlementDate,
                nameof(form.SettlementDate),
                errors);

        var currency =
            ResolveCurrency(
                fundAssetId,
                choices,
                errors);

        var quantity =
            ParsePositiveQuantity(
                form.Quantity,
                nameof(form.Quantity),
                errors);

        var unitPrice =
            ParsePositiveUnitPrice(
                form.UnitPrice,
                currency,
                nameof(form.UnitPrice),
                errors);

        var consideration =
            ParsePositiveMoney(
                form.CashConsideration,
                currency,
                nameof(form.CashConsideration),
                errors);

        var costs =
            MapCosts(form, currency, errors);

        if (errors.Count > 0
            || portfolioId is null
            || fundAccountId is null
            || cashAccountId is null
            || fundAssetId is null
            || cashAssetId is null
            || executionDate is null
            || quantity is null
            || unitPrice is null
            || consideration is null)
        {
            return new FundTradeFormMappingResult(null, null, errors);
        }

        if (tradeType == TransactionType.Buy)
        {
            return new FundTradeFormMappingResult(
                new FundPurchaseCommand(
                    householdId,
                    portfolioId.Value,
                    fundAccountId.Value,
                    cashAccountId.Value,
                    fundAssetId.Value,
                    cashAssetId.Value,
                    quantity.Value,
                    unitPrice,
                    consideration,
                    executionDate.Value,
                    orderDate,
                    settlementDate,
                    costs,
                    form.ExternalReference,
                    form.Note),
                null,
                errors);
        }

        return new FundTradeFormMappingResult(
            null,
            new FundSaleCommand(
                householdId,
                portfolioId.Value,
                fundAccountId.Value,
                cashAccountId.Value,
                fundAssetId.Value,
                cashAssetId.Value,
                quantity.Value,
                unitPrice,
                consideration,
                executionDate.Value,
                orderDate,
                settlementDate,
                costs,
                form.ExternalReference,
                form.Note,
                MapReviewedPlan(form),
                form.ReviewedPlanFingerprint),
            errors);
    }

    internal static bool TryParseIdempotencyKey(
        string? value,
        out string key)
    {
        key = value?.Trim() ?? string.Empty;

        return key.Length is >= 1 and <= 256
               && !key.Any(char.IsControl);
    }

    private static IReadOnlyList<ReviewedLotAllocation>? MapReviewedPlan(
        FundTradeForm form)
    {
        if (form.ReviewedPlan.Count == 0)
        {
            return null;
        }

        var plan = new List<ReviewedLotAllocation>();

        foreach (var line in form.ReviewedPlan)
        {
            if (!Guid.TryParseExact(line.AssetLotId, "D", out var lotId)
                || !long.TryParse(
                    line.QuantityRawE8,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var raw)
                || raw <= 0)
            {
                /*
                 * A plan line that cannot be read is not silently dropped:
                 * returning nothing makes the application refuse the post and
                 * send the user back to review.
                 */
                return null;
            }

            plan.Add(
                new ReviewedLotAllocation(
                    lotId,
                    Quantity.FromRaw(raw)));
        }

        return plan;
    }

    private static IReadOnlyList<FundTradeCostInput>? MapCosts(
        FundTradeForm form,
        CurrencyNavigationItem? currency,
        ICollection<FundTradeFormError> errors)
    {
        if (form.Costs.Count == 0 || currency is null)
        {
            return null;
        }

        var costs = new List<FundTradeCostInput>();

        for (var index = 0; index < form.Costs.Count; index++)
        {
            var line = form.Costs[index];

            // A wholly blank row is an unused slot, not an error.
            if (string.IsNullOrWhiteSpace(line.TypeCode)
                && string.IsNullOrWhiteSpace(line.Amount))
            {
                continue;
            }

            var field = $"Costs[{index}]";

            CostType type;
            CostTreatment treatment;

            try
            {
                type = FundTradeCodes.ParseCostType(line.TypeCode ?? string.Empty);
                treatment = FundTradeCodes.ParseTreatment(
                    line.TreatmentCode ?? string.Empty);
            }
            catch (FundTradeException)
            {
                errors.Add(
                    new($"{field}.TypeCode", "Fund_Validation_CostType"));

                continue;
            }

            var amount =
                ParsePositiveMoney(
                    line.Amount,
                    currency,
                    $"{field}.Amount",
                    errors);

            if (amount is null)
            {
                continue;
            }

            costs.Add(
                new FundTradeCostInput(
                    type,
                    treatment,
                    amount,
                    line.Note));
        }

        return costs.Count == 0 ? null : costs;
    }

    private static CurrencyNavigationItem? ResolveCurrency(
        Guid? fundAssetId,
        FundTradeChoices choices,
        ICollection<FundTradeFormError> errors)
    {
        var asset =
            fundAssetId is null
                ? null
                : choices.FundAssets.Items.SingleOrDefault(
                    x => x.AssetId == fundAssetId.Value);

        if (asset?.BaseCurrencyCode is null)
        {
            return null;
        }

        var currency =
            choices.Currencies.Items.SingleOrDefault(
                x => x.Code == asset.BaseCurrencyCode);

        if (currency is null)
        {
            errors.Add(
                new(
                    nameof(FundTradeForm.FundAssetId),
                    "Fund_Validation_ChoiceUnavailable"));
        }

        return currency;
    }

    private static Guid? ParseSelection(
        string? value,
        string fieldName,
        IEnumerable<Guid> allowed,
        ICollection<FundTradeFormError> errors)
    {
        if (!Guid.TryParseExact(value, "D", out var parsed)
            || !allowed.Contains(parsed))
        {
            errors.Add(
                new(fieldName, "Fund_Validation_ChoiceUnavailable"));

            return null;
        }

        return parsed;
    }

    private static DateOnly? ParseRequiredDate(
        string? value,
        string fieldName,
        ICollection<FundTradeFormError> errors)
    {
        if (!DateOnly.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            errors.Add(new(fieldName, "Fund_Validation_Date"));

            return null;
        }

        return parsed;
    }

    private static DateOnly? ParseOptionalDate(
        string? value,
        string fieldName,
        ICollection<FundTradeFormError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return ParseRequiredDate(value, fieldName, errors);
    }

    private static Quantity? ParsePositiveQuantity(
        string? value,
        string fieldName,
        ICollection<FundTradeFormError> errors)
    {
        if (!TryParseDecimal(value, out var parsed) || parsed <= 0)
        {
            errors.Add(new(fieldName, "Fund_Validation_Quantity"));

            return null;
        }

        try
        {
            return Quantity.FromDecimal(parsed);
        }
        catch (Exception exception)
            when (exception is ArgumentException or OverflowException)
        {
            errors.Add(new(fieldName, "Fund_Validation_Quantity"));

            return null;
        }
    }

    private static UnitPrice? ParsePositiveUnitPrice(
        string? value,
        CurrencyNavigationItem? currency,
        string fieldName,
        ICollection<FundTradeFormError> errors)
    {
        if (currency is null)
        {
            return null;
        }

        if (!TryParseDecimal(value, out var parsed) || parsed <= 0)
        {
            errors.Add(new(fieldName, "Fund_Validation_Price"));

            return null;
        }

        try
        {
            return UnitPrice.FromDecimal(
                parsed,
                new CurrencyCode(currency.Code));
        }
        catch (Exception exception)
            when (exception is ArgumentException or OverflowException)
        {
            errors.Add(new(fieldName, "Fund_Validation_Price"));

            return null;
        }
    }

    private static Money? ParsePositiveMoney(
        string? value,
        CurrencyNavigationItem? currency,
        string fieldName,
        ICollection<FundTradeFormError> errors)
    {
        if (currency is null)
        {
            return null;
        }

        if (!TryParseDecimal(value, out var parsed) || parsed <= 0)
        {
            errors.Add(new(fieldName, "Fund_Validation_Amount"));

            return null;
        }

        try
        {
            var scaled =
                checked(parsed * PowerOfTen(currency.MinorUnitDigits));

            /*
             * More precision than the currency has is a data error rather
             * than something to round away silently.
             */
            if (decimal.Truncate(scaled) != scaled)
            {
                errors.Add(new(fieldName, "Fund_Validation_Amount"));

                return null;
            }

            return Money.FromMinorUnits(
                checked((long)scaled),
                new CurrencyCode(currency.Code));
        }
        catch (Exception exception)
            when (exception is ArgumentException or OverflowException)
        {
            errors.Add(new(fieldName, "Fund_Validation_Amount"));

            return null;
        }
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

        // Turkish keyboards produce a comma; both separators are accepted.
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
