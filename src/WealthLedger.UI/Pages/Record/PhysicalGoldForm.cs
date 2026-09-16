using System.Globalization;
using WealthLedger.Application.Navigation;
using WealthLedger.Application.PhysicalGold;
using WealthLedger.Domain.Common;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.UI.Pages.Record;

/// <summary>
/// Browser text carried through a reviewed physical-gold workflow.
/// </summary>
public sealed class PhysicalGoldForm
{
    public string? PortfolioId { get; set; }

    public string? GoldAccountId { get; set; }

    public string? CashAccountId { get; set; }

    public string? GoldAssetId { get; set; }

    public string? CashAssetId { get; set; }

    public string? CounterpartyInstitutionId { get; set; }

    public string? SourcePortfolioId { get; set; }

    public string? SourceGoldAccountId { get; set; }

    public string? DestinationPortfolioId { get; set; }

    public string? DestinationGoldAccountId { get; set; }

    public string? CashPortfolioId { get; set; }

    public string? ExecutionDate { get; set; }

    public string? OrderDate { get; set; }

    public string? SettlementDate { get; set; }

    public string? GrossWeight { get; set; }

    public string? FinenessChoice { get; set; }

    public string? CustomFinenessPermille { get; set; }

    public string? PieceCount { get; set; }

    public string? UnitPrice { get; set; }

    public string? CashConsideration { get; set; }

    public string? Hallmark { get; set; }

    public string? CertificateReference { get; set; }

    public string? LotNote { get; set; }

    public string? ExternalReference { get; set; }

    public string? Note { get; set; }

    public string? IdempotencyKey { get; set; }

    public string? ReviewedPlanFingerprint { get; set; }

    public List<PhysicalGoldSelectedLotForm> SelectedLots { get; set; } = [];

    public List<PhysicalGoldCostForm> Costs { get; set; } = [];
}

public sealed class PhysicalGoldSelectedLotForm
{
    public string? AssetLotId { get; set; }

    public string? GrossWeight { get; set; }

    public string? PieceCount { get; set; }
}

public sealed class PhysicalGoldCostForm
{
    public string? TypeCode { get; set; }

    public string? TreatmentCode { get; set; }

    public string? Amount { get; set; }

    public string? Note { get; set; }
}

public sealed record PhysicalGoldFormError(
    string FieldName,
    string ResourceKey);

public sealed record PhysicalGoldFormMappingResult(
    PhysicalGoldPurchaseCommand? Purchase,
    PhysicalGoldSaleCommand? Sale,
    PhysicalGoldTransferCommand? Transfer,
    IReadOnlyList<PhysicalGoldFormError> Errors)
{
    public bool Succeeded
        => Errors.Count == 0
           && (Purchase is not null || Sale is not null || Transfer is not null);
}

/// <summary>
/// Parses localized browser text into exact application commands.
/// </summary>
internal static class PhysicalGoldFormMapper
{
    private const string CustomFinenessCode = "CUSTOM";

    internal static PhysicalGoldFormMappingResult MapPurchase(
        PhysicalGoldForm form,
        Guid householdId,
        PhysicalGoldChoices choices)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(choices);
        var errors = new List<PhysicalGoldFormError>();
        var portfolioId = ParseRequiredSelection(
            form.PortfolioId,
            nameof(form.PortfolioId),
            choices.Portfolios.Items.Select(x => x.PortfolioId),
            errors);
        var goldAccountId = ParseRequiredSelection(
            form.GoldAccountId,
            nameof(form.GoldAccountId),
            choices.GoldAccounts.Items.Select(x => x.AccountId),
            errors);
        var cashAccountId = ParseRequiredSelection(
            form.CashAccountId,
            nameof(form.CashAccountId),
            choices.CashAccounts.Items.Select(x => x.AccountId),
            errors);
        var goldAssetId = ParseRequiredSelection(
            form.GoldAssetId,
            nameof(form.GoldAssetId),
            choices.GoldAssets.Items.Select(x => x.AssetId),
            errors);
        var cashAssetId = ParseRequiredSelection(
            form.CashAssetId,
            nameof(form.CashAssetId),
            choices.CashAssets.Items.Select(x => x.AssetId),
            errors);
        var counterpartyId = ParseOptionalSelection(
            form.CounterpartyInstitutionId,
            nameof(form.CounterpartyInstitutionId),
            choices.Counterparties.Items.Select(x => x.InstitutionId),
            errors);
        var currency = ResolveCurrency(goldAssetId, choices, errors);
        var grossWeight = ParsePositiveQuantity(
            form.GrossWeight,
            nameof(form.GrossWeight),
            errors);
        var fineness = ParseFineness(form, errors);
        var pieceCount = ParsePositiveInt(
            form.PieceCount,
            nameof(form.PieceCount),
            errors);
        var consideration = ParsePositiveMoney(
            form.CashConsideration,
            currency,
            nameof(form.CashConsideration),
            errors);
        var unitPrice = ParseOptionalUnitPrice(
            form.UnitPrice,
            currency,
            nameof(form.UnitPrice),
            errors);
        var executionDate = ParseRequiredDate(
            form.ExecutionDate,
            nameof(form.ExecutionDate),
            errors);
        var orderDate = ParseOptionalDate(
            form.OrderDate,
            nameof(form.OrderDate),
            errors);
        var settlementDate = ParseOptionalDate(
            form.SettlementDate,
            nameof(form.SettlementDate),
            errors);
        var costs = MapCosts(form.Costs, currency, errors);

        if (errors.Count > 0)
        {
            return Failed(errors);
        }

        return new PhysicalGoldFormMappingResult(
            new PhysicalGoldPurchaseCommand(
                householdId,
                portfolioId!.Value,
                goldAccountId!.Value,
                cashAccountId!.Value,
                goldAssetId!.Value,
                cashAssetId!.Value,
                grossWeight!.Value,
                fineness!,
                pieceCount!.Value,
                consideration!,
                executionDate!.Value,
                unitPrice,
                counterpartyId,
                orderDate,
                settlementDate,
                costs,
                form.Hallmark,
                form.CertificateReference,
                form.LotNote,
                form.ExternalReference,
                form.Note),
            null,
            null,
            []);
    }

    internal static PhysicalGoldFormMappingResult MapSale(
        PhysicalGoldForm form,
        Guid householdId,
        PhysicalGoldChoices choices,
        IReadOnlyList<PhysicalGoldCustodyPosition> custody)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(custody);
        var errors = new List<PhysicalGoldFormError>();
        var portfolioId = ParseRequiredSelection(
            form.PortfolioId,
            nameof(form.PortfolioId),
            choices.Portfolios.Items.Select(x => x.PortfolioId),
            errors);
        var goldAccountId = ParseRequiredSelection(
            form.GoldAccountId,
            nameof(form.GoldAccountId),
            choices.GoldAccounts.Items.Select(x => x.AccountId),
            errors);
        var cashAccountId = ParseRequiredSelection(
            form.CashAccountId,
            nameof(form.CashAccountId),
            choices.CashAccounts.Items.Select(x => x.AccountId),
            errors);
        var goldAssetId = ParseRequiredSelection(
            form.GoldAssetId,
            nameof(form.GoldAssetId),
            choices.GoldAssets.Items.Select(x => x.AssetId),
            errors);
        var cashAssetId = ParseRequiredSelection(
            form.CashAssetId,
            nameof(form.CashAssetId),
            choices.CashAssets.Items.Select(x => x.AssetId),
            errors);
        var counterpartyId = ParseOptionalSelection(
            form.CounterpartyInstitutionId,
            nameof(form.CounterpartyInstitutionId),
            choices.Counterparties.Items.Select(x => x.InstitutionId),
            errors);
        var currency = ResolveCurrency(goldAssetId, choices, errors);
        var selection = MapSelectedLots(
            form,
            custody,
            portfolioId,
            goldAccountId,
            goldAssetId,
            errors);
        var consideration = ParsePositiveMoney(
            form.CashConsideration,
            currency,
            nameof(form.CashConsideration),
            errors);
        var unitPrice = ParseOptionalUnitPrice(
            form.UnitPrice,
            currency,
            nameof(form.UnitPrice),
            errors);
        var executionDate = ParseRequiredDate(
            form.ExecutionDate,
            nameof(form.ExecutionDate),
            errors);
        var orderDate = ParseOptionalDate(
            form.OrderDate,
            nameof(form.OrderDate),
            errors);
        var settlementDate = ParseOptionalDate(
            form.SettlementDate,
            nameof(form.SettlementDate),
            errors);
        var costs = MapCosts(form.Costs, currency, errors);

        if (errors.Count > 0 || selection is null)
        {
            return Failed(errors);
        }

        return new PhysicalGoldFormMappingResult(
            null,
            new PhysicalGoldSaleCommand(
                householdId,
                portfolioId!.Value,
                goldAccountId!.Value,
                cashAccountId!.Value,
                goldAssetId!.Value,
                cashAssetId!.Value,
                selection.Value.GrossWeight,
                selection.Value.PieceCount,
                selection.Value.Lots,
                consideration!,
                executionDate!.Value,
                unitPrice,
                counterpartyId,
                orderDate,
                settlementDate,
                costs,
                form.ExternalReference,
                form.Note,
                form.ReviewedPlanFingerprint),
            null,
            []);
    }

    internal static PhysicalGoldFormMappingResult MapTransfer(
        PhysicalGoldForm form,
        Guid householdId,
        PhysicalGoldChoices choices,
        IReadOnlyList<PhysicalGoldCustodyPosition> custody)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(custody);
        var errors = new List<PhysicalGoldFormError>();
        var sourcePortfolioId = ParseRequiredSelection(
            form.SourcePortfolioId,
            nameof(form.SourcePortfolioId),
            choices.Portfolios.Items.Select(x => x.PortfolioId),
            errors);
        var sourceAccountId = ParseRequiredSelection(
            form.SourceGoldAccountId,
            nameof(form.SourceGoldAccountId),
            choices.GoldAccounts.Items.Select(x => x.AccountId),
            errors);
        var destinationPortfolioId = ParseRequiredSelection(
            form.DestinationPortfolioId,
            nameof(form.DestinationPortfolioId),
            choices.Portfolios.Items.Select(x => x.PortfolioId),
            errors);
        var destinationAccountId = ParseRequiredSelection(
            form.DestinationGoldAccountId,
            nameof(form.DestinationGoldAccountId),
            choices.GoldAccounts.Items.Select(x => x.AccountId),
            errors);
        var goldAssetId = ParseRequiredSelection(
            form.GoldAssetId,
            nameof(form.GoldAssetId),
            choices.GoldAssets.Items.Select(x => x.AssetId),
            errors);
        var currency = ResolveCurrency(goldAssetId, choices, errors);
        var selection = MapSelectedLots(
            form,
            custody,
            sourcePortfolioId,
            sourceAccountId,
            goldAssetId,
            errors);
        var executionDate = ParseRequiredDate(
            form.ExecutionDate,
            nameof(form.ExecutionDate),
            errors);
        var costs = MapCosts(form.Costs, currency, errors);
        var needsCash = costs?.Any(x =>
            x.Treatment == CostTreatment.AdditionalCashOutflow) == true;
        Guid? cashPortfolioId = null;
        Guid? cashAccountId = null;
        Guid? cashAssetId = null;
        if (needsCash)
        {
            cashPortfolioId = ParseRequiredSelection(
                form.CashPortfolioId,
                nameof(form.CashPortfolioId),
                choices.Portfolios.Items.Select(x => x.PortfolioId),
                errors);
            cashAccountId = ParseRequiredSelection(
                form.CashAccountId,
                nameof(form.CashAccountId),
                choices.CashAccounts.Items.Select(x => x.AccountId),
                errors);
            cashAssetId = ParseRequiredSelection(
                form.CashAssetId,
                nameof(form.CashAssetId),
                choices.CashAssets.Items.Select(x => x.AssetId),
                errors);
        }

        if (errors.Count > 0 || selection is null)
        {
            return Failed(errors);
        }

        return new PhysicalGoldFormMappingResult(
            null,
            null,
            new PhysicalGoldTransferCommand(
                householdId,
                sourcePortfolioId!.Value,
                sourceAccountId!.Value,
                destinationPortfolioId!.Value,
                destinationAccountId!.Value,
                goldAssetId!.Value,
                selection.Value.GrossWeight,
                selection.Value.PieceCount,
                selection.Value.Lots,
                executionDate!.Value,
                cashPortfolioId,
                cashAccountId,
                cashAssetId,
                costs,
                form.ExternalReference,
                form.Note,
                form.ReviewedPlanFingerprint),
            []);
    }

    internal static bool TryParseIdempotencyKey(
        string? value,
        out string key)
    {
        key = value?.Trim() ?? string.Empty;
        return Guid.TryParseExact(key, "D", out var parsed)
               && parsed != Guid.Empty;
    }

    private static PhysicalGoldFormMappingResult Failed(
        IReadOnlyList<PhysicalGoldFormError> errors)
        => new(null, null, null, errors);

    private static (
        Quantity GrossWeight,
        int PieceCount,
        IReadOnlyList<PhysicalGoldSelectedLot> Lots)? MapSelectedLots(
            PhysicalGoldForm form,
            IReadOnlyList<PhysicalGoldCustodyPosition> custody,
            Guid? portfolioId,
            Guid? accountId,
            Guid? assetId,
            ICollection<PhysicalGoldFormError> errors)
    {
        var available = custody
            .Where(x => x.PortfolioId == portfolioId
                        && x.AccountId == accountId
                        && x.GoldAssetId == assetId
                        && x.GrossWeightRawE8 > 0
                        && x.PieceCount > 0)
            .ToDictionary(x => x.AssetLotId);
        var selected = new List<PhysicalGoldSelectedLot>();
        long grossRawE8 = 0;
        var pieces = 0;

        for (var index = 0; index < form.SelectedLots.Count; index++)
        {
            var line = form.SelectedLots[index];
            var field = $"SelectedLots[{index}]";
            var hasGross = !string.IsNullOrWhiteSpace(line.GrossWeight);
            var hasPieces = !string.IsNullOrWhiteSpace(line.PieceCount);
            if (!hasGross && !hasPieces)
            {
                continue;
            }

            var requireCurrentChoice = string.IsNullOrWhiteSpace(
                form.ReviewedPlanFingerprint);
            if (!Guid.TryParseExact(line.AssetLotId, "D", out var lotId)
                || requireCurrentChoice && !available.ContainsKey(lotId))
            {
                errors.Add(new(
                    $"{field}.GrossWeight",
                    "Gold_Validation_LotUnavailable"));
                continue;
            }

            var quantity = ParsePositiveQuantity(
                line.GrossWeight,
                $"{field}.GrossWeight",
                errors);
            var pieceCount = ParsePositiveInt(
                line.PieceCount,
                $"{field}.PieceCount",
                errors);
            if (quantity is null || pieceCount is null)
            {
                continue;
            }

            try
            {
                grossRawE8 = checked(grossRawE8 + quantity.Value.RawE8);
                pieces = checked(pieces + pieceCount.Value);
                selected.Add(new PhysicalGoldSelectedLot(
                    lotId,
                    quantity.Value,
                    pieceCount.Value));
            }
            catch (OverflowException)
            {
                errors.Add(new(
                    $"{field}.GrossWeight",
                    "Gold_Validation_Quantity"));
            }
        }

        if (selected.Count == 0)
        {
            errors.Add(new(
                nameof(form.SelectedLots),
                "Gold_Validation_SelectLot"));
            return null;
        }

        try
        {
            return (Quantity.FromRaw(grossRawE8), pieces, selected);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                  or OverflowException
                  or DomainRuleViolationException)
        {
            errors.Add(new(
                nameof(form.SelectedLots),
                "Gold_Validation_Quantity"));
            return null;
        }
    }

    private static IReadOnlyList<PhysicalGoldCostInput>? MapCosts(
        IReadOnlyList<PhysicalGoldCostForm> lines,
        CurrencyNavigationItem? currency,
        ICollection<PhysicalGoldFormError> errors)
    {
        var result = new List<PhysicalGoldCostInput>();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
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
                type = PhysicalGoldCodes.ParseCostType(line.TypeCode);
            }
            catch (PhysicalGoldException)
            {
                errors.Add(new(
                    $"{field}.TypeCode",
                    "Gold_Validation_CostType"));
                continue;
            }

            try
            {
                treatment = PhysicalGoldCodes.ParseTreatment(
                    line.TreatmentCode);
            }
            catch (PhysicalGoldException)
            {
                errors.Add(new(
                    $"{field}.TreatmentCode",
                    "Gold_Validation_CostTreatment"));
                continue;
            }

            var amount = ParsePositiveMoney(
                line.Amount,
                currency,
                $"{field}.Amount",
                errors);
            if (amount is not null)
            {
                result.Add(new PhysicalGoldCostInput(
                    type,
                    treatment,
                    amount,
                    line.Note));
            }
        }

        return result.Count == 0 ? null : result;
    }

    private static Fineness? ParseFineness(
        PhysicalGoldForm form,
        ICollection<PhysicalGoldFormError> errors)
    {
        var source = string.Equals(
            form.FinenessChoice,
            CustomFinenessCode,
            StringComparison.Ordinal)
            ? form.CustomFinenessPermille
            : form.FinenessChoice;
        if (!TryParseDecimal(source, out var permille)
            || permille <= 0
            || permille > 1_000)
        {
            errors.Add(new(
                nameof(form.FinenessChoice),
                "Gold_Validation_Fineness"));
            return null;
        }

        try
        {
            var scaled = checked(permille * 1_000m);
            if (decimal.Truncate(scaled) != scaled)
            {
                throw new ArgumentException("Fineness is more precise than ppm.");
            }

            return new Fineness(checked((int)scaled));
        }
        catch (Exception exception)
            when (exception is ArgumentException
                  or OverflowException
                  or DomainRuleViolationException)
        {
            errors.Add(new(
                nameof(form.FinenessChoice),
                "Gold_Validation_Fineness"));
            return null;
        }
    }

    private static CurrencyNavigationItem? ResolveCurrency(
        Guid? goldAssetId,
        PhysicalGoldChoices choices,
        ICollection<PhysicalGoldFormError> errors)
    {
        var asset = goldAssetId is null
            ? null
            : choices.GoldAssets.Items.SingleOrDefault(
                x => x.AssetId == goldAssetId.Value);
        var currency = asset?.BaseCurrencyCode is null
            ? null
            : choices.Currencies.Items.SingleOrDefault(
                x => x.Code == asset.BaseCurrencyCode);
        if (asset is not null && currency is null)
        {
            errors.Add(new(
                nameof(PhysicalGoldForm.GoldAssetId),
                "Gold_Validation_ChoiceUnavailable"));
        }

        return currency;
    }

    private static Guid? ParseRequiredSelection(
        string? value,
        string fieldName,
        IEnumerable<Guid> allowed,
        ICollection<PhysicalGoldFormError> errors)
    {
        if (!Guid.TryParseExact(value, "D", out var parsed)
            || !allowed.Contains(parsed))
        {
            errors.Add(new(fieldName, "Gold_Validation_ChoiceUnavailable"));
            return null;
        }

        return parsed;
    }

    private static Guid? ParseOptionalSelection(
        string? value,
        string fieldName,
        IEnumerable<Guid> allowed,
        ICollection<PhysicalGoldFormError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return ParseRequiredSelection(value, fieldName, allowed, errors);
    }

    private static DateOnly? ParseRequiredDate(
        string? value,
        string fieldName,
        ICollection<PhysicalGoldFormError> errors)
    {
        if (!DateOnly.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            errors.Add(new(fieldName, "Gold_Validation_Date"));
            return null;
        }

        return parsed;
    }

    private static DateOnly? ParseOptionalDate(
        string? value,
        string fieldName,
        ICollection<PhysicalGoldFormError> errors)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : ParseRequiredDate(value, fieldName, errors);

    private static Quantity? ParsePositiveQuantity(
        string? value,
        string fieldName,
        ICollection<PhysicalGoldFormError> errors)
    {
        if (!TryParseDecimal(value, out var parsed) || parsed <= 0)
        {
            errors.Add(new(fieldName, "Gold_Validation_Quantity"));
            return null;
        }

        try
        {
            return Quantity.FromDecimal(parsed);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                  or OverflowException
                  or DomainRuleViolationException)
        {
            errors.Add(new(fieldName, "Gold_Validation_Quantity"));
            return null;
        }
    }

    private static int? ParsePositiveInt(
        string? value,
        string fieldName,
        ICollection<PhysicalGoldFormError> errors)
    {
        if (!int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed)
            || parsed <= 0)
        {
            errors.Add(new(fieldName, "Gold_Validation_Pieces"));
            return null;
        }

        return parsed;
    }

    private static UnitPrice? ParseOptionalUnitPrice(
        string? value,
        CurrencyNavigationItem? currency,
        string fieldName,
        ICollection<PhysicalGoldFormError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (currency is null
            || !TryParseDecimal(value, out var parsed)
            || parsed <= 0)
        {
            errors.Add(new(fieldName, "Gold_Validation_Price"));
            return null;
        }

        try
        {
            return UnitPrice.FromDecimal(
                parsed,
                new CurrencyCode(currency.Code));
        }
        catch (Exception exception)
            when (exception is ArgumentException
                  or OverflowException
                  or DomainRuleViolationException)
        {
            errors.Add(new(fieldName, "Gold_Validation_Price"));
            return null;
        }
    }

    private static Money? ParsePositiveMoney(
        string? value,
        CurrencyNavigationItem? currency,
        string fieldName,
        ICollection<PhysicalGoldFormError> errors)
    {
        if (currency is null
            || !TryParseDecimal(value, out var parsed)
            || parsed <= 0)
        {
            errors.Add(new(fieldName, "Gold_Validation_Amount"));
            return null;
        }

        try
        {
            var scaled = checked(parsed * PowerOfTen(currency.MinorUnitDigits));
            if (decimal.Truncate(scaled) != scaled)
            {
                throw new ArgumentException("Money exceeds currency precision.");
            }

            return Money.FromMinorUnits(
                checked((long)scaled),
                new CurrencyCode(currency.Code));
        }
        catch (Exception exception)
            when (exception is ArgumentException or OverflowException)
        {
            errors.Add(new(fieldName, "Gold_Validation_Amount"));
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

        return decimal.TryParse(
            normalized.Replace(',', '.'),
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
