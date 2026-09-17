using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.PhysicalGold;

public sealed class GetPhysicalGoldActivityVerificationUseCase
{
    private readonly IPhysicalGoldVerificationReadStore _verificationStore;
    private readonly IPhysicalGoldRealizedCostReadStore _realizedCostStore;
    private readonly IOpeningBalanceReferenceReadStore _referenceStore;
    private readonly TimeProvider _timeProvider;

    public GetPhysicalGoldActivityVerificationUseCase(
        IPhysicalGoldVerificationReadStore verificationStore,
        IPhysicalGoldRealizedCostReadStore realizedCostStore,
        IOpeningBalanceReferenceReadStore referenceStore,
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

    public async Task<PhysicalGoldActivityVerification> ExecuteAsync(
        Guid householdId,
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        if (householdId == Guid.Empty || transactionId == Guid.Empty)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.ReferenceShapeInvalid,
                "A physical-gold receipt requires a household and transaction.");
        }

        var facts = await _verificationStore.FindPostedActivityAsync(
            householdId, transactionId, cancellationToken)
            ?? throw new PhysicalGoldException(
                PhysicalGoldErrorCategory.NotFound,
                PhysicalGoldErrorCodes.NotFound,
                "The requested physical-gold activity does not exist in this household.");

        if (facts.Type is not TransactionType.Buy
            and not TransactionType.Sell
            and not TransactionType.Transfer)
        {
            throw new PhysicalGoldException(
                PhysicalGoldErrorCategory.NotFound,
                PhysicalGoldErrorCodes.NotFound,
                "The requested transaction is not a supported physical-gold activity.");
        }

        CurrencyCode currencyCode;
        try
        {
            currencyCode = new CurrencyCode(facts.CurrencyCode);
        }
        catch (ArgumentException exception)
        {
            throw new PhysicalGoldException(
                PhysicalGoldErrorCategory.Conflict,
                PhysicalGoldErrorCodes.UnsupportedPersistedShape,
                "The persisted physical-gold currency is invalid.",
                facts.TransactionId,
                exception);
        }

        var currency = await _referenceStore.FindCurrencyAsync(
            currencyCode, cancellationToken)
            ?? throw PhysicalGoldException.Conflict(
                PhysicalGoldErrorCodes.UnsupportedPersistedShape,
                "The persisted physical-gold currency is no longer available.");

        var economics = facts.Type == TransactionType.Transfer
            ? PhysicalGoldEconomicsCalculator.ComputeTransfer(
                currencyCode, facts.Costs)
            : PhysicalGoldEconomicsCalculator.ComputeTrade(
                facts.Type,
                facts.GrossWeight,
                facts.ExecutedUnitPrice,
                facts.CashConsideration
                    ?? throw PhysicalGoldException.Conflict(
                        PhysicalGoldErrorCodes.UnsupportedPersistedShape,
                        "A persisted physical-gold trade lacks consideration."),
                facts.Costs,
                currency.MinorUnitDigits);
        var warnings = new List<string>();
        if (facts.ExecutedUnitPrice is null
            && facts.Type is TransactionType.Buy or TransactionType.Sell)
        {
            warnings.Add(PhysicalGoldWarningCodes.ExecutedPriceUnavailable);
        }

        if (economics.Discrepancy
            == PhysicalGoldDiscrepancyClassification.Material)
        {
            warnings.Add(PhysicalGoldWarningCodes.ExplainedDiscrepancy);
        }
        else if (economics.Discrepancy
                 == PhysicalGoldDiscrepancyClassification.RoundingConsistent)
        {
            warnings.Add(PhysicalGoldWarningCodes.RoundingConsistent);
        }

        PhysicalGoldRealizedCostResult? realized = null;
        if (facts.Type == TransactionType.Sell)
        {
            realized = await DeriveRealizedCostAsync(
                facts, cancellationToken);
            if (realized.Completeness != RealizedCostCompleteness.CompleteKnown)
            {
                warnings.Add(PhysicalGoldWarningCodes.IncompleteRealizedCost);
            }
        }

        var custody = await _verificationStore.ListCustodyAsync(
            householdId, transactionId, cancellationToken);
        return new PhysicalGoldActivityVerification(
            facts,
            economics,
            currency.MinorUnitDigits,
            custody,
            realized,
            warnings);
    }

    private async Task<PhysicalGoldRealizedCostResult>
        DeriveRealizedCostAsync(
            PhysicalGoldActivityPersistedFacts facts,
            CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (facts.ReversedByTransactionId is not null)
        {
            return new PhysicalGoldRealizedCostResult(
                RealizedCostCompleteness.Unknown,
                0,
                0,
                [],
                [],
                RealizedLotCostCalculator.MethodCode,
                now,
                SourceSaleIsEffective: false);
        }

        var history = await _realizedCostStore.ListEffectiveLotHistoryAsync(
            facts.HouseholdId,
            facts.TransactionId,
            cancellationToken);
        RealizedSaleCost result;
        try
        {
            result = RealizedLotCostCalculator.Compute(
                facts.TransactionId, history);
        }
        catch (Domain.Common.DomainRuleViolationException exception)
        {
            throw new PhysicalGoldException(
                PhysicalGoldErrorCategory.Conflict,
                PhysicalGoldErrorCodes.UnsupportedPersistedShape,
                "Persisted physical-gold history cannot support realized-cost derivation.",
                facts.TransactionId,
                exception);
        }

        return new PhysicalGoldRealizedCostResult(
            result.Completeness,
            result.KnownQuantity.RawE8,
            result.UnknownQuantity.RawE8,
            result.KnownAmountsByCurrency
                .Select(x => new PhysicalGoldRealizedCostAmount(
                    x.Currency.Value, x.MinorUnits))
                .ToArray(),
            result.Lines.Select(x => new PhysicalGoldRealizedCostLotLine(
                x.AssetLotId,
                x.Quantity.RawE8,
                x.CostStatus,
                x.KnownCost?.MinorUnits,
                x.KnownCost?.Currency.Value)).ToArray(),
            result.MethodCode,
            now,
            SourceSaleIsEffective: true);
    }
}

public sealed class GetPhysicalGoldCustodyInventoryUseCase
{
    private readonly IPhysicalGoldVerificationReadStore _store;

    public GetPhysicalGoldCustodyInventoryUseCase(
        IPhysicalGoldVerificationReadStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<PhysicalGoldCustodyInventory> ExecuteAsync(
        Guid householdId,
        CancellationToken cancellationToken = default)
    {
        if (householdId == Guid.Empty)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.ReferenceShapeInvalid,
                "A custody query requires a household.");
        }

        return new PhysicalGoldCustodyInventory(
            householdId,
            await _store.ListCustodyAsync(
                householdId, cancellationToken: cancellationToken));
    }
}
