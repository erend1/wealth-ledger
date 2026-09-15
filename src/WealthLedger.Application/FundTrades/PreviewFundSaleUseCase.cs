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
    private readonly IFundRealizedCostReadStore _realizedCostStore;
    private readonly LotAllocationService _allocationService;
    private readonly TimeProvider _timeProvider;

    public PreviewFundSaleUseCase(
        IOpeningBalanceReferenceReadStore referenceStore,
        IFundLotCustodyReadStore custodyStore,
        IFundRealizedCostReadStore realizedCostStore,
        LotAllocationService allocationService,
        TimeProvider timeProvider)
    {
        _referenceStore = referenceStore
            ?? throw new ArgumentNullException(nameof(referenceStore));
        _custodyStore = custodyStore
            ?? throw new ArgumentNullException(nameof(custodyStore));
        _realizedCostStore = realizedCostStore
            ?? throw new ArgumentNullException(nameof(realizedCostStore));
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
            await ProjectRealizedCostAsync(
                scope.HouseholdId,
                planLines,
                cancellationToken);

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
    /// Projects realized cost for the planned consumption.
    /// </summary>
    /// <remarks>
    /// This loads each planned lot's existing effective disposal sequence and
    /// applies the accepted cumulative rule, so the figure shown in review is
    /// the figure the receipt will show.
    ///
    /// A proportional share computed on its own, whether against the original
    /// or the remaining quantity, agrees only for the first sale from a lot
    /// and drifts once the lot has been partly sold.
    /// </remarks>
    private async Task<RealizedCostProjection> ProjectRealizedCostAsync(
        Guid householdId,
        IReadOnlyList<FundSalePlanLine> planLines,
        CancellationToken cancellationToken)
    {
        var histories =
            await _realizedCostStore.ListLotHistoryAsync(
                householdId,
                planLines.Select(x => x.AssetLotId).ToArray(),
                cancellationToken);

        var historyByLot =
            histories.ToDictionary(x => x.AssetLotId);

        var planned = new List<PlannedLotDisposal>();

        foreach (var line in planLines)
        {
            if (!historyByLot.TryGetValue(
                    line.AssetLotId,
                    out var history))
            {
                throw FundTradeException.Invalid(
                    FundTradeErrorCodes.UnsupportedPersistedShape,
                    "A planned lot is missing its persisted acquisition history.");
            }

            planned.Add(
                new PlannedLotDisposal(
                    history,
                    Quantity.FromRaw(line.ConsumedQuantityRawE8)));
        }

        RealizedSaleCost projected;

        try
        {
            projected =
                RealizedLotCostCalculator.Project(planned);
        }
        catch (Domain.Common.DomainRuleViolationException exception)
        {
            throw new FundTradeException(
                FundTradeErrorCategory.Conflict,
                FundTradeErrorCodes.UnsupportedPersistedShape,
                "The persisted lot history cannot support a realized-cost projection.",
                innerException: exception);
        }

        return new RealizedCostProjection(
            projected.Completeness,
            projected.KnownQuantity.RawE8,
            projected.UnknownQuantity.RawE8,
            projected.KnownAmountsByCurrency
                .Select(x =>
                    new RealizedCostCurrencyAmount(
                        x.Currency.Value,
                        x.MinorUnits))
                .ToList(),
            projected.MethodCode);
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
