using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;

namespace WealthLedger.Application.FundTrades;

/// <summary>
/// Everything a persisted fund trade can prove about itself.
/// </summary>
/// <remarks>
/// Every value here is read or derived from persisted facts, never carried
/// over from the preview that preceded the post. That is what makes the
/// receipt survive a restart.
/// </remarks>
public sealed record FundTradeVerification(
    FundTradePersistedFacts Facts,
    FundTradeEconomics Economics,
    int MinorUnitDigits,
    long CurrentFundPositionRawE8,
    RealizedCostResult? RealizedCost,
    IReadOnlyList<string> WarningCodes);

/// <summary>
/// The derived realized cost of a sale, with its completeness and method.
/// </summary>
public sealed record RealizedCostResult(
    RealizedCostCompleteness Completeness,
    long KnownQuantityRawE8,
    long UnknownQuantityRawE8,
    IReadOnlyList<RealizedCostCurrencyAmount> KnownAmounts,
    IReadOnlyList<RealizedCostLotLine> Lines,
    string MethodCode,
    DateTimeOffset DerivedAtUtc,
    bool SourceSaleIsEffective);

public sealed record RealizedCostLotLine(
    Guid AssetLotId,
    long QuantityRawE8,
    CostBasisStatus CostStatus,
    long? KnownCostMinorUnits,
    string? KnownCostCurrencyCode);

/// <summary>
/// Reads a posted fund trade back and rebuilds its explanation.
/// </summary>
public sealed class GetFundTradeVerificationUseCase
{
    private readonly IFundTradeVerificationReadStore _verificationStore;
    private readonly IFundRealizedCostReadStore _realizedCostStore;
    private readonly OpeningBalances.IOpeningBalanceReferenceReadStore
        _referenceStore;
    private readonly TimeProvider _timeProvider;

    public GetFundTradeVerificationUseCase(
        IFundTradeVerificationReadStore verificationStore,
        IFundRealizedCostReadStore realizedCostStore,
        OpeningBalances.IOpeningBalanceReferenceReadStore referenceStore,
        TimeProvider timeProvider)
    {
        _verificationStore = verificationStore
            ?? throw new ArgumentNullException(nameof(verificationStore));
        _realizedCostStore = realizedCostStore
            ?? throw new ArgumentNullException(nameof(realizedCostStore));
        _referenceStore = referenceStore
            ?? throw new ArgumentNullException(nameof(referenceStore));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<FundTradeVerification> ExecuteAsync(
        Guid householdId,
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        if (householdId == Guid.Empty
            || transactionId == Guid.Empty)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.ReferenceShapeInvalid,
                "A verification request requires a household and a transaction.");
        }

        var facts =
            await _verificationStore.FindPostedFundTradeAsync(
                householdId,
                transactionId,
                cancellationToken)
            ?? throw new FundTradeException(
                FundTradeErrorCategory.NotFound,
                FundTradeErrorCodes.NotFound,
                "The requested fund trade does not exist in this household.");

        if (facts.Type is not TransactionType.Buy
            and not TransactionType.Sell)
        {
            throw new FundTradeException(
                FundTradeErrorCategory.NotFound,
                FundTradeErrorCodes.NotFound,
                "The requested transaction is not a fund trade.");
        }

        var currency =
            await _referenceStore.FindCurrencyAsync(
                facts.CashConsideration.Currency,
                cancellationToken)
            ?? throw FundTradeException.Invalid(
                FundTradeErrorCodes.UnsupportedPersistedShape,
                "The persisted trade currency is no longer available.");

        /*
         * The receipt recomputes its explanatory figures from the persisted
         * entries and costs rather than trusting anything the preview
         * produced, so a restart cannot change what the receipt says.
         */
        var economics =
            FundTradeEconomicsCalculator.Compute(
                facts.Type,
                facts.FundQuantity,
                facts.ExecutedUnitPrice,
                facts.CashConsideration,
                facts.Costs,
                currency.MinorUnitDigits);

        var currentPosition =
            await _verificationStore.DeriveFundPositionRawE8Async(
                facts.Scope,
                cancellationToken);

        var warnings = new List<string>();

        if (economics.Discrepancy
            == DiscrepancyClassification.Material)
        {
            warnings.Add(
                FundTradeWarningCodes.ExplainedDiscrepancy);
        }
        else if (economics.Discrepancy
                 == DiscrepancyClassification.RoundingConsistent)
        {
            warnings.Add(
                FundTradeWarningCodes.RoundingConsistent);
        }

        RealizedCostResult? realizedCost = null;

        if (facts.Type == TransactionType.Sell)
        {
            realizedCost =
                await DeriveRealizedCostAsync(
                    facts,
                    cancellationToken);

            if (realizedCost.Completeness
                != RealizedCostCompleteness.CompleteKnown)
            {
                warnings.Add(
                    FundTradeWarningCodes.IncompleteRealizedCost);
            }
        }

        if (facts.Allocations.Any(x => x.AcquiredOn is null))
        {
            warnings.Add(
                FundTradeWarningCodes
                    .UnknownAcquisitionDateOrdering);
        }

        return new FundTradeVerification(
            facts,
            economics,
            currency.MinorUnitDigits,
            currentPosition,
            realizedCost,
            warnings);
    }

    private async Task<RealizedCostResult> DeriveRealizedCostAsync(
        FundTradePersistedFacts facts,
        CancellationToken cancellationToken)
    {
        var derivedAtUtc = _timeProvider.GetUtcNow();

        /*
         * A reversed sale keeps its audit history but leaves the effective
         * sequence, so it no longer carries realized cost. Saying so is more
         * honest than recomputing a number that no longer applies.
         */
        if (facts.ReversedByTransactionId is not null)
        {
            return new RealizedCostResult(
                RealizedCostCompleteness.Unknown,
                KnownQuantityRawE8: 0,
                UnknownQuantityRawE8: 0,
                KnownAmounts: [],
                Lines: [],
                RealizedLotCostCalculator.MethodCode,
                derivedAtUtc,
                SourceSaleIsEffective: false);
        }

        var history =
            await _realizedCostStore.ListEffectiveLotHistoryAsync(
                facts.HouseholdId,
                facts.TransactionId,
                cancellationToken);

        RealizedSaleCost derived;

        try
        {
            derived =
                RealizedLotCostCalculator.Compute(
                    facts.TransactionId,
                    history);
        }
        catch (Domain.Common.DomainRuleViolationException exception)
        {
            throw new FundTradeException(
                FundTradeErrorCategory.Conflict,
                FundTradeErrorCodes.UnsupportedPersistedShape,
                "The persisted lot history cannot support a realized-cost derivation.",
                facts.TransactionId,
                exception);
        }

        return new RealizedCostResult(
            derived.Completeness,
            derived.KnownQuantity.RawE8,
            derived.UnknownQuantity.RawE8,
            derived.KnownAmountsByCurrency
                .Select(x =>
                    new RealizedCostCurrencyAmount(
                        x.Currency.Value,
                        x.MinorUnits))
                .ToList(),
            derived.Lines
                .Select(x =>
                    new RealizedCostLotLine(
                        x.AssetLotId,
                        x.Quantity.RawE8,
                        x.CostStatus,
                        x.KnownCost?.MinorUnits,
                        x.KnownCost?.Currency.Value))
                .ToList(),
            derived.MethodCode,
            derivedAtUtc,
            SourceSaleIsEffective: true);
    }
}
