using WealthLedger.Domain.Common;

namespace WealthLedger.Domain.Ledger
{
    /// <summary>
    /// The bounded cost vocabulary supported by fund purchases and sales.
    /// </summary>
    /// <remarks>
    /// The general <see cref="CostType"/> vocabulary also carries physical
    /// gold and property terms. A fund trade cannot carry a making charge, a
    /// title deed fee or a notary fee, so the supported subset is stated here
    /// once and reused by the application, the transport boundary and the
    /// database guards.
    /// </remarks>
    public static class FundTradeCostRules
    {
        /// <summary>
        /// The most cost components one fund trade may carry.
        /// </summary>
        /// <remarks>
        /// A bound exists so one transaction cannot grow an unbounded child
        /// collection. Sixteen is far above any realistic broker note while
        /// still keeping review, canonical ordering and receipt rendering
        /// predictable.
        /// </remarks>
        public const int MaxComponentsPerTransaction = 16;

        /// <summary>
        /// The longest normalized note one cost component may carry.
        /// </summary>
        public const int MaxComponentNoteLength = 1_000;

        public static bool IsSupportedCostType(CostType type)
            => type is CostType.Commission
                or CostType.Brokerage
                or CostType.WithholdingTax
                or CostType.OtherTax
                or CostType.Other;

        /// <summary>
        /// Maps a supported cost type to the supporting entry role that
        /// carries its aggregated cash effect.
        /// </summary>
        public static EntryRole ResolveSupportingEntryRole(CostType type)
        {
            return type switch
            {
                CostType.Commission
                    or CostType.Brokerage
                    or CostType.Other =>
                    EntryRole.Fee,

                CostType.WithholdingTax
                    or CostType.OtherTax =>
                    EntryRole.Tax,

                _ =>
                    throw new DomainRuleViolationException(
                        $"Cost type '{type}' is not supported for a fund trade.")
            };
        }

        /// <summary>
        /// States whether a treatment is meaningful for the given trade type.
        /// </summary>
        /// <remarks>
        /// A purchase has no proceeds, so nothing can be withheld from them.
        /// </remarks>
        public static bool IsSupportedTreatment(
            TransactionType tradeType,
            CostTreatment treatment)
        {
            return tradeType switch
            {
                TransactionType.Buy =>
                    treatment is CostTreatment.AdditionalCashOutflow
                        or CostTreatment.IncludedInConsideration
                        or CostTreatment.InformationalOnly,

                TransactionType.Sell =>
                    treatment is CostTreatment.AdditionalCashOutflow
                        or CostTreatment.WithheldFromProceeds
                        or CostTreatment.IncludedInConsideration
                        or CostTreatment.InformationalOnly,

                _ =>
                    throw new DomainRuleViolationException(
                        $"Transaction type '{tradeType}' is not a fund trade.")
            };
        }

        /// <summary>
        /// States whether a treatment produces its own negative cash entry.
        /// </summary>
        /// <remarks>
        /// Only an additional outflow moves cash a second time. A cost already
        /// inside the consideration, or withheld from proceeds, is already
        /// reflected in the consideration entry, and an informational cost
        /// never moves cash at all. Counting any of those again would double
        /// count the same money.
        /// </remarks>
        public static bool CreatesAdditionalCashEntry(
            CostTreatment treatment)
            => treatment == CostTreatment.AdditionalCashOutflow;
    }
}
