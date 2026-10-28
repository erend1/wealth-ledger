namespace WealthLedger.Domain.Evidence
{
    /// <summary>
    /// One externally observed quantity for one exact
    /// Portfolio/Account/Asset scope, on its evidence record's effective date.
    /// </summary>
    /// <remarks>
    /// An observation is a relationship record of its evidence record: it is
    /// added, never edited or removed, and a wrong observation is corrected by
    /// superseding the evidence record. The quantity tolerance belongs to the
    /// observation, so a later comparison cannot loosen it. A whole-piece
    /// count is observed only for physical gold, and its tolerance is always
    /// zero. A scope that was not observed has no observation: it is never
    /// fabricated as zero.
    /// </remarks>
    public sealed class EvidenceObservation
    {
        public const int MaximumPerEvidenceRecord = 100;

        private EvidenceObservation(
            Guid id,
            Guid evidenceId,
            Guid portfolioId,
            Guid accountId,
            Guid assetId,
            long observedQuantityRawE8,
            long toleranceRawE8,
            int? observedPieceCount,
            DateTimeOffset recordedAtUtc)
        {
            EnsureNonEmpty(id, nameof(id));
            EnsureNonEmpty(evidenceId, nameof(evidenceId));
            EnsureNonEmpty(portfolioId, nameof(portfolioId));
            EnsureNonEmpty(accountId, nameof(accountId));
            EnsureNonEmpty(assetId, nameof(assetId));

            if (toleranceRawE8 < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(toleranceRawE8),
                    "An observation tolerance cannot be negative.");
            }

            if (observedPieceCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(observedPieceCount),
                    "An observed piece count cannot be negative.");
            }

            Id = id;
            EvidenceId = evidenceId;
            PortfolioId = portfolioId;
            AccountId = accountId;
            AssetId = assetId;
            ObservedQuantityRawE8 = observedQuantityRawE8;
            ToleranceRawE8 = toleranceRawE8;
            ObservedPieceCount = observedPieceCount;
            RecordedAtUtc = recordedAtUtc.ToUniversalTime();
        }

        public Guid Id { get; }

        public Guid EvidenceId { get; }

        public Guid PortfolioId { get; }

        public Guid AccountId { get; }

        public Guid AssetId { get; }

        public long ObservedQuantityRawE8 { get; }

        public long ToleranceRawE8 { get; }

        public int? ObservedPieceCount { get; }

        public DateTimeOffset RecordedAtUtc { get; }

        public static EvidenceObservation Create(
            Guid id,
            Guid evidenceId,
            Guid portfolioId,
            Guid accountId,
            Guid assetId,
            long observedQuantityRawE8,
            long toleranceRawE8,
            int? observedPieceCount,
            DateTimeOffset recordedAtUtc)
            => new(
                id,
                evidenceId,
                portfolioId,
                accountId,
                assetId,
                observedQuantityRawE8,
                toleranceRawE8,
                observedPieceCount,
                recordedAtUtc);

        public bool HasSameScope(EvidenceObservation other)
        {
            ArgumentNullException.ThrowIfNull(other);

            return EvidenceId == other.EvidenceId
                   && PortfolioId == other.PortfolioId
                   && AccountId == other.AccountId
                   && AssetId == other.AssetId;
        }

        /// <summary>
        /// Whether the other observation states the same facts for the same
        /// scope. Its identity and recording time may differ.
        /// </summary>
        public bool HasSameFacts(EvidenceObservation other)
            => HasSameScope(other)
               && ObservedQuantityRawE8 == other.ObservedQuantityRawE8
               && ToleranceRawE8 == other.ToleranceRawE8
               && ObservedPieceCount == other.ObservedPieceCount;

        // Quantities are household financial facts; never print them.
        public override string ToString() => nameof(EvidenceObservation);

        private static void EnsureNonEmpty(Guid value, string parameterName)
        {
            if (value == Guid.Empty)
            {
                throw new ArgumentException(
                    $"{parameterName} cannot be empty.",
                    parameterName);
            }
        }
    }
}
