using WealthLedger.Domain.Evidence;

namespace WealthLedger.Domain.Reconciliation
{
    /// <summary>
    /// The explicit resolution of one out-of-tolerance reconciliation item.
    /// </summary>
    /// <remarks>
    /// A resolution is a relationship record of its run: it is added once and
    /// never edited or removed. It does not change the item's delta or its
    /// OutOfTolerance comparison; a corrected difference becomes Matched only
    /// in a new run. A documented exception carries a bounded human
    /// explanation and stays visibly out of tolerance.
    /// </remarks>
    public sealed class ReconciliationItemResolution
    {
        public const int MaximumExplanationLength = EvidenceText.MaximumNoteLength;

        private ReconciliationItemResolution(
            Guid itemId,
            ReconciliationResolutionKind kind,
            Guid? correctiveTransactionId,
            string? explanation,
            DateTimeOffset resolvedAtUtc)
        {
            if (itemId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Reconciliation item ID cannot be empty.",
                    nameof(itemId));
            }

            ItemId = itemId;
            Kind = kind;
            CorrectiveTransactionId = correctiveTransactionId;
            Explanation = explanation;
            ResolvedAtUtc = resolvedAtUtc.ToUniversalTime();
        }

        public Guid ItemId { get; }

        public ReconciliationResolutionKind Kind { get; }

        public Guid? CorrectiveTransactionId { get; }

        public string? Explanation { get; }

        public DateTimeOffset ResolvedAtUtc { get; }

        public static ReconciliationItemResolution CorrectedByTransaction(
            Guid itemId,
            Guid correctiveTransactionId,
            DateTimeOffset resolvedAtUtc)
        {
            if (correctiveTransactionId == Guid.Empty)
            {
                throw new ArgumentException(
                    "A corrective transaction ID cannot be empty.",
                    nameof(correctiveTransactionId));
            }

            return new ReconciliationItemResolution(
                itemId,
                ReconciliationResolutionKind.CorrectedByTransaction,
                correctiveTransactionId,
                explanation: null,
                resolvedAtUtc);
        }

        public static ReconciliationItemResolution DocumentedException(
            Guid itemId,
            string explanation,
            DateTimeOffset resolvedAtUtc)
        {
            // The explanation follows the evidence note rules: multi-line,
            // bounded, and free of other control and bidirectional characters.
            if (!EvidenceText.TryNormalizeNote(explanation, out var normalized)
                || normalized is null)
            {
                throw new ArgumentException(
                    "A documented exception requires an explanation of up to 2000 characters without control characters.",
                    nameof(explanation));
            }

            return new ReconciliationItemResolution(
                itemId,
                ReconciliationResolutionKind.DocumentedException,
                correctiveTransactionId: null,
                normalized,
                resolvedAtUtc);
        }

        /// <summary>
        /// Whether the other resolution resolves the same item in the same
        /// way. Its recording time may differ.
        /// </summary>
        public bool HasSameFacts(ReconciliationItemResolution other)
        {
            ArgumentNullException.ThrowIfNull(other);

            return ItemId == other.ItemId
                   && Kind == other.Kind
                   && CorrectiveTransactionId == other.CorrectiveTransactionId
                   && string.Equals(Explanation, other.Explanation, StringComparison.Ordinal);
        }

        // The explanation is human text; never print it.
        public override string ToString() => nameof(ReconciliationItemResolution);
    }
}
