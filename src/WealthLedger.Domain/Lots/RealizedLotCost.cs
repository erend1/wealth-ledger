using WealthLedger.Domain.Common;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Lots
{
    /// <summary>
    /// How much of a disposed quantity has supported acquisition cost.
    /// </summary>
    /// <remarks>
    /// Absence of evidence is not a zero-cost acquisition, so a partially
    /// known result must never be presented as a complete one.
    /// </remarks>
    public enum RealizedCostCompleteness
    {
        CompleteKnown,
        PartiallyKnown,
        Unknown
    }

    /// <summary>
    /// One effective disposal of one lot, in the order it became effective.
    /// </summary>
    /// <remarks>
    /// Only a posted fund sale that has no posted reversal is effective. A
    /// reversed sale and its reversal remain visible audit history but leave
    /// the current apportionment sequence entirely.
    /// </remarks>
    public sealed record EffectiveLotDisposal(
        Guid TransactionId,
        DateTimeOffset PostedAtUtc,
        int EntrySequence,
        Guid AllocationId,
        Quantity Quantity);

    /// <summary>
    /// One consumed lot with everything needed to apportion its cost.
    /// </summary>
    public sealed record RealizedCostLotHistory(
        Guid AssetLotId,
        Quantity OriginalQuantity,
        CostBasis CostBasis,
        IReadOnlyList<EffectiveLotDisposal> EffectiveDisposals);

    /// <summary>
    /// The cost apportioned to one sale from one lot.
    /// </summary>
    public sealed record RealizedLotCostLine(
        Guid AssetLotId,
        Quantity Quantity,
        CostBasisStatus CostStatus,
        Money? KnownCost);

    /// <summary>
    /// The complete derived realized-cost result for one sale.
    /// </summary>
    /// <remarks>
    /// Known amounts stay grouped by their original cost-basis currency.
    /// Different currencies are never added or converted, because no accepted
    /// rate observation, date or source policy exists yet.
    /// </remarks>
    public sealed record RealizedSaleCost(
        RealizedCostCompleteness Completeness,
        Quantity KnownQuantity,
        Quantity UnknownQuantity,
        IReadOnlyList<Money> KnownAmountsByCurrency,
        IReadOnlyList<RealizedLotCostLine> Lines)
    {
        /// <summary>
        /// The stable code identifying how this result was derived.
        /// </summary>
        public string MethodCode
            => RealizedLotCostCalculator.MethodCode;
    }

    /// <summary>
    /// Derives realized acquisition cost from recorded lineage alone.
    /// </summary>
    /// <remarks>
    /// See <c>docs/decisions/ADR-009-deterministic-realized-lot-cost.md</c>.
    ///
    /// The result is a rebuildable query result, never an entered amount and
    /// never a stored authority. It is also not a gain, a return, a tax
    /// figure, or a claim about any jurisdiction's required lot method.
    /// </remarks>
    public static class RealizedLotCostCalculator
    {
        /// <summary>
        /// Stable identifier for the accepted derivation method.
        /// </summary>
        public const string MethodCode =
            "ADR009_CUMULATIVE_ROUND_HALF_TO_EVEN_V1";

        /// <summary>
        /// Apportions realized cost to one sale across every lot it consumed.
        /// </summary>
        public static RealizedSaleCost Compute(
            Guid saleTransactionId,
            IReadOnlyCollection<RealizedCostLotHistory> lots)
        {
            if (saleTransactionId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Sale transaction ID cannot be empty.",
                    nameof(saleTransactionId));
            }

            ArgumentNullException.ThrowIfNull(lots);

            if (lots.Count == 0)
            {
                throw new DomainRuleViolationException(
                    "A realized-cost derivation requires at least one consumed lot.");
            }

            var lines = new List<RealizedLotCostLine>();

            foreach (var lot in lots
                         .OrderBy(x => x.AssetLotId))
            {
                var line =
                    ComputeLine(
                        saleTransactionId,
                        lot);

                if (line is not null)
                {
                    lines.Add(line);
                }
            }

            if (lines.Count == 0)
            {
                throw new DomainRuleViolationException(
                    "The sale does not appear in the effective disposal history of any supplied lot.");
            }

            return Summarize(lines);
        }

        private static RealizedLotCostLine? ComputeLine(
            Guid saleTransactionId,
            RealizedCostLotHistory lot)
        {
            ArgumentNullException.ThrowIfNull(lot);
            ArgumentNullException.ThrowIfNull(lot.CostBasis);
            ArgumentNullException.ThrowIfNull(
                lot.EffectiveDisposals);

            var originalQuantity =
                lot.OriginalQuantity.RawE8;

            if (originalQuantity <= 0)
            {
                throw new DomainRuleViolationException(
                    "A lot with realized cost must have a positive original quantity.");
            }

            /*
             * A fund lot whose cost is NotApplicable is an unsupported
             * persisted shape rather than a zero-cost lot, so it fails closed
             * instead of silently contributing nothing.
             */
            if (lot.CostBasis.Status
                == CostBasisStatus.NotApplicable)
            {
                throw new DomainRuleViolationException(
                    "A fund lot cannot carry a not-applicable cost basis.");
            }

            long cumulative = 0;
            long saleQuantity = 0;

            Int128 previousAssigned = 0;
            Int128 assigned = 0;

            var isKnown =
                lot.CostBasis.Status
                    == CostBasisStatus.Known;

            var totalCost =
                isKnown
                    ? lot.CostBasis.Amount!.MinorUnits
                    : 0L;

            foreach (var disposal in lot.EffectiveDisposals)
            {
                ArgumentNullException.ThrowIfNull(disposal);

                if (disposal.Quantity.RawE8 <= 0)
                {
                    throw new DomainRuleViolationException(
                        "An effective disposal quantity must be positive.");
                }

                cumulative = checked(
                    cumulative
                    + disposal.Quantity.RawE8);

                /*
                 * Disposing more than the lot ever held is corrupt persisted
                 * history. Clamping it would hide the corruption and invent a
                 * plausible cost, so it fails closed instead.
                 */
                if (cumulative > originalQuantity)
                {
                    throw new DomainRuleViolationException(
                        "Effective disposals exceed the original lot quantity.");
                }

                if (!isKnown)
                {
                    if (disposal.TransactionId == saleTransactionId)
                    {
                        saleQuantity = checked(
                            saleQuantity
                            + disposal.Quantity.RawE8);
                    }

                    continue;
                }

                /*
                 * Cumulative apportionment: round the running proportional
                 * total, then take the difference. Rounding each sale on its
                 * own would let the assigned amounts fail to add up to the
                 * original cost when the lot finally closes.
                 */
                var runningAssigned =
                    ExactInteger.DivideRoundHalfToEven(
                        (Int128)totalCost * cumulative,
                        originalQuantity);

                if (disposal.TransactionId == saleTransactionId)
                {
                    saleQuantity = checked(
                        saleQuantity
                        + disposal.Quantity.RawE8);

                    assigned +=
                        runningAssigned - previousAssigned;
                }

                previousAssigned = runningAssigned;
            }

            if (saleQuantity == 0)
            {
                return null;
            }

            if (!isKnown)
            {
                return new RealizedLotCostLine(
                    lot.AssetLotId,
                    Quantity.FromRaw(saleQuantity),
                    CostBasisStatus.Unknown,
                    KnownCost: null);
            }

            var assignedMinorUnits =
                ExactInteger.ToInt64(
                    assigned,
                    "The apportioned realized cost does not fit the supported money range.");

            return new RealizedLotCostLine(
                lot.AssetLotId,
                Quantity.FromRaw(saleQuantity),
                CostBasisStatus.Known,
                Money.FromMinorUnits(
                    assignedMinorUnits,
                    lot.CostBasis.Amount!.Currency));
        }

        private static RealizedSaleCost Summarize(
            IReadOnlyList<RealizedLotCostLine> lines)
        {
            long knownQuantity = 0;
            long unknownQuantity = 0;

            var amountsByCurrency =
                new Dictionary<CurrencyCode, long>();

            foreach (var line in lines)
            {
                if (line.CostStatus == CostBasisStatus.Known)
                {
                    knownQuantity = checked(
                        knownQuantity
                        + line.Quantity.RawE8);

                    var cost = line.KnownCost!;

                    amountsByCurrency[cost.Currency] =
                        checked(
                            amountsByCurrency.GetValueOrDefault(
                                cost.Currency)
                            + cost.MinorUnits);

                    continue;
                }

                unknownQuantity = checked(
                    unknownQuantity
                    + line.Quantity.RawE8);
            }

            var completeness =
                (knownQuantity, unknownQuantity) switch
                {
                    ( > 0, 0) =>
                        RealizedCostCompleteness.CompleteKnown,

                    ( > 0, > 0) =>
                        RealizedCostCompleteness.PartiallyKnown,

                    _ =>
                        RealizedCostCompleteness.Unknown
                };

            var knownAmounts =
                amountsByCurrency
                    .OrderBy(x => x.Key.Value, StringComparer.Ordinal)
                    .Select(x =>
                        Money.FromMinorUnits(
                            x.Value,
                            x.Key))
                    .ToList();

            return new RealizedSaleCost(
                completeness,
                Quantity.FromRaw(knownQuantity),
                Quantity.FromRaw(unknownQuantity),
                knownAmounts,
                lines);
        }
    }
}
