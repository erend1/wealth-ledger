using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Ledger;

namespace WealthLedger.Application.FundTrades;

/// <summary>
/// Derives the complete economic effect of a fund purchase without writing
/// anything.
/// </summary>
/// <remarks>
/// Preview creates no transaction, lot, allocation or receipt, and needs no
/// idempotency key. It exists so the user reviews exact numbers, not so the
/// system reserves anything.
/// </remarks>
public sealed class PreviewFundPurchaseUseCase
{
    private readonly IOpeningBalanceReferenceReadStore _referenceStore;
    private readonly IFundLotCustodyReadStore _custodyStore;
    private readonly TimeProvider _timeProvider;

    public PreviewFundPurchaseUseCase(
        IOpeningBalanceReferenceReadStore referenceStore,
        IFundLotCustodyReadStore custodyStore,
        TimeProvider timeProvider)
    {
        _referenceStore = referenceStore
            ?? throw new ArgumentNullException(nameof(referenceStore));
        _custodyStore = custodyStore
            ?? throw new ArgumentNullException(nameof(custodyStore));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<FundPurchasePreview> ExecuteAsync(
        FundPurchaseCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var scope =
            new FundTradeScope(
                command.HouseholdId,
                command.PortfolioId,
                command.FundAccountId,
                command.CashAccountId,
                command.FundAssetId,
                command.CashAssetId);

        var validated =
            await FundTradeEvaluator.EvaluateAsync(
                TransactionType.Buy,
                scope,
                command.FundQuantity,
                command.ExecutedUnitPrice,
                command.CashConsideration,
                command.ExecutionDate,
                command.OrderDate,
                command.SettlementDate,
                command.Costs,
                command.ExternalReference,
                command.Note,
                _timeProvider.GetUtcNow(),
                _timeProvider.LocalTimeZone,
                _referenceStore,
                cancellationToken);

        var warnings =
            validated.WarningCodes.ToList();

        await AddNegativeCashWarningAsync(
            scope,
            validated,
            warnings,
            cancellationToken);

        return new FundPurchasePreview(
            scope,
            validated.Portfolio.Name,
            validated.FundAccount.Name,
            validated.CashAccount.Name,
            validated.FundAsset.Code,
            validated.FundAsset.Name,
            validated.Currency.Code.Value,
            validated.Currency.MinorUnitDigits,
            validated.ExecutionDate,
            validated.OrderDate,
            validated.SettlementDate,
            validated.FundQuantity.RawE8,
            validated.ExecutedUnitPrice.RawE8,
            validated.Economics,
            validated.Costs,
            validated.ExternalReference,
            validated.Note,
            warnings);
    }

    /// <summary>
    /// Warns when the purchase would drive the derived cash position below
    /// zero.
    /// </summary>
    /// <remarks>
    /// This is only guidance during review. The rule that a negative position
    /// requires an explanatory note is enforced at post time, inside the write
    /// transaction, so a direct caller cannot avoid it and a concurrent write
    /// cannot race past it.
    /// </remarks>
    private async Task AddNegativeCashWarningAsync(
        FundTradeScope scope,
        ValidatedFundTrade validated,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var currentRawE8 =
            await _custodyStore.DeriveCashPositionRawE8Async(
                scope,
                cancellationToken);

        var effectRawE8 =
            FundTradeCashConversion.ToQuantityRawE8(
                validated.Economics.NetCashEffect,
                validated.Currency.MinorUnitDigits);

        if (checked(currentRawE8 + effectRawE8) < 0)
        {
            warnings.Add(
                FundTradeWarningCodes.NegativeProjectedCash);
        }
    }
}

/// <summary>
/// Converts money into the E8 quantity representation cash entries use.
/// </summary>
internal static class FundTradeCashConversion
{
    internal static long ToQuantityRawE8(
        Domain.ValueObjects.Money amount,
        int minorUnitDigits)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (minorUnitDigits is < 0 or > 8)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.PrecisionOverflow,
                "Currency minor-unit digits must be between zero and eight.");
        }

        long multiplier = 1;

        for (var digit = minorUnitDigits; digit < 8; digit++)
        {
            multiplier = checked(multiplier * 10);
        }

        try
        {
            return checked(amount.MinorUnits * multiplier);
        }
        catch (OverflowException exception)
        {
            throw new FundTradeException(
                FundTradeErrorCategory.Validation,
                FundTradeErrorCodes.PrecisionOverflow,
                "The amount is too large to represent as a quantity.",
                innerException: exception);
        }
    }
}
