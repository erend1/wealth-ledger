using WealthLedger.Domain.Common;

namespace WealthLedger.Domain.Ledger
{
    /// <summary>
    /// The bounded cost vocabulary for supported physical-gold activities.
    /// </summary>
    public static class PhysicalGoldActivityCostRules
    {
        public const int MaxComponentsPerTransaction = 16;

        public const int MaxComponentNoteLength = 1_000;

        public static bool IsSupportedCostType(
            TransactionType activityType,
            CostType costType)
            => activityType switch
            {
                TransactionType.Buy =>
                    costType is CostType.MakingCharge
                        or CostType.Commission
                        or CostType.OtherTax
                        or CostType.Other,

                TransactionType.Sell =>
                    costType is CostType.Commission
                        or CostType.OtherTax
                        or CostType.Other,

                TransactionType.Transfer =>
                    costType is CostType.Commission
                        or CostType.Insurance
                        or CostType.OtherTax
                        or CostType.Other,

                _ => throw new DomainRuleViolationException(
                    $"Transaction type '{activityType}' is not a supported physical-gold activity.")
            };

        public static bool IsSupportedTreatment(
            TransactionType activityType,
            CostTreatment treatment)
            => activityType switch
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

                TransactionType.Transfer =>
                    treatment is CostTreatment.AdditionalCashOutflow
                        or CostTreatment.InformationalOnly,

                _ => throw new DomainRuleViolationException(
                    $"Transaction type '{activityType}' is not a supported physical-gold activity.")
            };

        public static EntryRole ResolveSupportingEntryRole(CostType type)
            => type switch
            {
                CostType.OtherTax => EntryRole.Tax,

                CostType.MakingCharge
                    or CostType.Commission
                    or CostType.Insurance
                    or CostType.Other => EntryRole.Fee,

                _ => throw new DomainRuleViolationException(
                    $"Cost type '{type}' is not supported for a physical-gold activity.")
            };

        public static bool CreatesAdditionalCashEntry(
            CostTreatment treatment)
            => treatment == CostTreatment.AdditionalCashOutflow;
    }
}
