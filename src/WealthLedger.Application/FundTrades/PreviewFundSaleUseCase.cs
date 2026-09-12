using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.FundTrades;

/// <summary>
/// Derives the deterministic FIFO plan and realized-cost projection for a
/// fund sale without writing anything.
/// </summary>
public sealed class PreviewFundSaleUseCase
{
    private readonly IOpeningBalanceReferenceReadStore _referenceStore;
    private readonly IFundLotCustodyReadStore _custodyStore;
    private readonly LotAllocationService _allocationService;
    private readonly TimeProvider _timeProvider;

    public PreviewFundSaleUseCase(
        IOpeningBalanceReferenceReadStore referenceStore,
        IFundLotCustodyReadStore custodyStore,
        LotAllocationService allocationService,
        TimeProvider timeProvider)
    {
        _referenceStore = referenceStore
            ?? throw new ArgumentNullException(nameof(referenceStore));
        _custodyStore = custodyStore
            ?? throw new ArgumentNullException(nameof(custodyStore));
        _allocationService = allocationService
            ?? throw new ArgumentNullException(nameof(allocationService));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<FundSalePreview> ExecuteAsync(
        FundSaleCommand command,
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
                TransactionType.Sell,
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

        var candidates =
            await _custodyStore.ListScopedLotCandidatesAsync(
                scope,
                cancellationToken);

        var plan =
            BuildPlan(
                scope,
                validated.FundQuantity,
                candidates,
                _allocationService);

        var warnings =
            validated.WarningCodes.ToList();

        var candidatesById =
            candidates.ToDictionary(x => x.AssetLotId);

        var planLines =
            plan
                .Select(item =>
                {
                    var candidate =
                        candidatesById[item.AssetLotId];

                    return new FundSalePlanLine(
                        item.AssetLotId,
                        candidate.AcquiredOn,
                        candidate.AvailableQuantity.RawE8,
                        item.Quantity.RawE8,
                        candidate.CostBasis.Status,
                        candidate.CostBasis.Amount?.MinorUnits,
                        candidate.CostBasis.Amount?.Currency.Value);
                })
                .ToList();

        if (planLines.Any(x => x.AcquiredOn is null))
        {
            warnings.Add(
                FundTradeWarningCodes
                    .UnknownAcquisitionDateOrdering);
        }

        var realizedCost =
            ProjectRealizedCost(planLines);

        if (realizedCost.Completeness
            != RealizedCostCompleteness.CompleteKnown)
        {
            warnings.Add(
                FundTradeWarningCodes.IncompleteRealizedCost);
        }

        await AddNegativeCashWarningAsync(
            scope,
            validated,
            warnings,
            cancellationToken);

        var availableRawE8 =
            candidates.Sum(x => x.AvailableQuantity.RawE8);

        var reviewedPlan =
            plan
                .Select(x =>
                    new ReviewedLotAllocation(
                        x.AssetLotId,
                        x.Quantity))
                .ToList();

        return new FundSalePreview(
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
            availableRawE8,
            validated.Economics,
            validated.Costs,
            planLines,
            FundSalePlanFingerprint.Compute(
                scope,
                validated.FundQuantity.RawE8,
                reviewedPlan),
            realizedCost,
            validated.ExternalReference,
            validated.Note,
            warnings);
    }

    /// <summary>
    /// Plans the sale over scoped availability, translating the domain's
    /// insufficiency rule into a stable transport code.
    /// </summary>
    internal static IReadOnlyList<LotAllocationPlanItem> BuildPlan(
        FundTradeScope scope,
        Quantity requestedQuantity,
        IReadOnlyList<ScopedLotCandidate> candidates,
        LotAllocationService allocationService)
    {
        try
        {
            return allocationService.PlanScopedFifo(
                scope.FundAssetId,
                requestedQuantity,
                candidates);
        }
        catch (Domain.Common.DomainRuleViolationException exception)
            when (exception.Message.Contains(
                      "Insufficient",
                      StringComparison.Ordinal))
        {
            throw new FundTradeException(
                FundTradeErrorCategory.Validation,
                FundTradeErrorCodes.InsufficientFundQuantity,
                "The selected portfolio and account do not hold enough of this fund.",
                innerException: exception);
        }
    }

    /// <summary>
    /// Projects realized cost from the planned consumption.
    /// </summary>
    /// <remarks>
    /// The projection assumes each planned lot contributes its whole share,
    /// which is what the cumulative method produces when the sale is the next
    /// effective disposal. After posting, the receipt recomputes the result
    /// from persisted allocations rather than reusing this projection.
    /// </remarks>
    private static RealizedCostProjection ProjectRealizedCost(
        IReadOnlyList<FundSalePlanLine> planLines)
    {
        long knownQuantity = 0;
        long unknownQuantity = 0;

        var amounts =
            new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var line in planLines)
        {
            if (line.CostStatus != CostBasisStatus.Known)
            {
                unknownQuantity = checked(
                    unknownQuantity + line.ConsumedQuantityRawE8);

                continue;
            }

            knownQuantity = checked(
                knownQuantity + line.ConsumedQuantityRawE8);

            /*
             * Available quantity is what remains of the lot, so the share of
             * the remaining cost is proportional to how much of that
             * remainder this sale consumes.
             */
            var share =
                Domain.Common.ExactInteger.DivideRoundHalfToEven(
                    (Int128)line.LotCostMinorUnits!.Value
                    * line.ConsumedQuantityRawE8,
                    line.AvailableQuantityRawE8);

            var currency = line.LotCostCurrencyCode!;

            amounts[currency] =
                checked(
                    amounts.GetValueOrDefault(currency)
                    + (long)share);
        }

        var completeness =
            (knownQuantity, unknownQuantity) switch
            {
                ( > 0, 0) => RealizedCostCompleteness.CompleteKnown,
                ( > 0, > 0) => RealizedCostCompleteness.PartiallyKnown,
                _ => RealizedCostCompleteness.Unknown
            };

        return new RealizedCostProjection(
            completeness,
            knownQuantity,
            unknownQuantity,
            amounts
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x =>
                    new RealizedCostCurrencyAmount(
                        x.Key,
                        x.Value))
                .ToList(),
            RealizedLotCostCalculator.MethodCode);
    }

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
