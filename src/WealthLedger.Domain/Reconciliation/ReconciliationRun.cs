using WealthLedger.Domain.Evidence;

namespace WealthLedger.Domain.Reconciliation
{
    /// <summary>
    /// One deterministic comparison of an evidence record's observations with
    /// ledger-derived quantities as of the evidence effective date.
    /// </summary>
    /// <remarks>
    /// A run is audit history, not a ledger fact: it posts, edits and deletes
    /// nothing. Its items are immutable snapshots. When later effective history
    /// changes a derived quantity, the run becomes stale on re-derivation and
    /// a new run is needed; this run is never rewritten. Only item resolutions
    /// are added later, as separate relationship records.
    /// </remarks>
    public sealed class ReconciliationRun
    {
        public const int CurrentMethodVersion = 1;

        public const int MaximumItemCount = 100;

        private readonly List<ReconciliationItem> _items;

        private ReconciliationRun(
            Guid id,
            Guid householdId,
            Guid evidenceId,
            DateOnly evidenceEffectiveDate,
            DateTimeOffset runAtUtc,
            IReadOnlyList<ReconciliationItemInput> inputs)
        {
            EnsureNonEmpty(id, nameof(id));
            EnsureNonEmpty(householdId, nameof(householdId));
            EnsureNonEmpty(evidenceId, nameof(evidenceId));

            if (inputs.Count is < 1 or > MaximumItemCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(inputs),
                    "A reconciliation run compares between 1 and 100 observations.");
            }

            Id = id;
            HouseholdId = householdId;
            EvidenceId = evidenceId;
            EvidenceEffectiveDate = evidenceEffectiveDate;
            RunAtUtc = runAtUtc.ToUniversalTime();
            Method = ReconciliationMethod.ExactScopeQuantityAsOf;
            MethodVersion = CurrentMethodVersion;

            _items = [];

            foreach (var input in inputs)
            {
                ArgumentNullException.ThrowIfNull(input);
                ArgumentNullException.ThrowIfNull(input.Observation);

                if (input.Observation.EvidenceId != evidenceId)
                {
                    throw new ArgumentException(
                        "Every observation must belong to the run's evidence record.",
                        nameof(inputs));
                }

                if (_items.Any(
                        item => item.Id == input.ItemId
                                || item.ObservationId == input.Observation.Id
                                || (item.PortfolioId == input.Observation.PortfolioId
                                    && item.AccountId == input.Observation.AccountId
                                    && item.AssetId == input.Observation.AssetId)))
                {
                    throw new ArgumentException(
                        "A run compares each item, observation and scope once.",
                        nameof(inputs));
                }

                _items.Add(
                    new ReconciliationItem(
                        input.ItemId,
                        id,
                        _items.Count + 1,
                        input.Observation,
                        input.LedgerQuantityRawE8,
                        input.LedgerPieceCount));
            }
        }

        public Guid Id { get; }

        public Guid HouseholdId { get; }

        public Guid EvidenceId { get; }

        public DateOnly EvidenceEffectiveDate { get; }

        public DateTimeOffset RunAtUtc { get; }

        public ReconciliationMethod Method { get; }

        public int MethodVersion { get; }

        public IReadOnlyList<ReconciliationItem> Items => _items;

        /// <summary>
        /// Compares each observation, in the given order, with its ledger
        /// quantity. Throws <see cref="OverflowException"/> when a delta does
        /// not fit the fixed-point range.
        /// </summary>
        public static ReconciliationRun Create(
            Guid id,
            Guid householdId,
            Guid evidenceId,
            DateOnly evidenceEffectiveDate,
            DateTimeOffset runAtUtc,
            IReadOnlyList<ReconciliationItemInput> inputs)
        {
            ArgumentNullException.ThrowIfNull(inputs);

            return new ReconciliationRun(
                id,
                householdId,
                evidenceId,
                evidenceEffectiveDate,
                runAtUtc,
                inputs);
        }

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

    /// <summary>
    /// One observation and the ledger side it is compared with.
    /// </summary>
    /// <remarks>
    /// The ledger piece count is present exactly when the observation carries
    /// a piece count.
    /// </remarks>
    public sealed record ReconciliationItemInput(
        Guid ItemId,
        EvidenceObservation Observation,
        long LedgerQuantityRawE8,
        int? LedgerPieceCount)
    {
        public override string ToString() => nameof(ReconciliationItemInput);
    }

    /// <summary>
    /// An immutable snapshot of one compared scope inside a run.
    /// </summary>
    public sealed class ReconciliationItem
    {
        internal ReconciliationItem(
            Guid id,
            Guid runId,
            int sequence,
            EvidenceObservation observation,
            long ledgerQuantityRawE8,
            int? ledgerPieceCount)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentException(
                    "Reconciliation item ID cannot be empty.",
                    nameof(id));
            }

            var comparison = ReconciliationComparison.Compare(
                observation.ObservedQuantityRawE8,
                ledgerQuantityRawE8,
                observation.ToleranceRawE8,
                observation.ObservedPieceCount,
                ledgerPieceCount);

            Id = id;
            RunId = runId;
            Sequence = sequence;
            ObservationId = observation.Id;
            PortfolioId = observation.PortfolioId;
            AccountId = observation.AccountId;
            AssetId = observation.AssetId;
            ObservedQuantityRawE8 = observation.ObservedQuantityRawE8;
            LedgerQuantityRawE8 = ledgerQuantityRawE8;
            DeltaRawE8 = comparison.DeltaRawE8;
            ToleranceRawE8 = observation.ToleranceRawE8;
            ObservedPieceCount = observation.ObservedPieceCount;
            LedgerPieceCount = ledgerPieceCount;
            PieceDelta = comparison.PieceDelta;
            ComparisonStatus = comparison.Status;
        }

        public Guid Id { get; }

        public Guid RunId { get; }

        public int Sequence { get; }

        public Guid ObservationId { get; }

        public Guid PortfolioId { get; }

        public Guid AccountId { get; }

        public Guid AssetId { get; }

        public long ObservedQuantityRawE8 { get; }

        public long LedgerQuantityRawE8 { get; }

        public long DeltaRawE8 { get; }

        public long ToleranceRawE8 { get; }

        public int? ObservedPieceCount { get; }

        public int? LedgerPieceCount { get; }

        public int? PieceDelta { get; }

        public ReconciliationComparisonStatus ComparisonStatus { get; }

        public override string ToString() => nameof(ReconciliationItem);
    }
}
