namespace WealthLedger.Domain.Evidence
{
    /// <summary>
    /// Retained external support for ledger facts, such as a statement,
    /// receipt or physical count.
    /// </summary>
    /// <remarks>
    /// Evidence is adjunct audit data, never a ledger fact: creating or
    /// linking it posts, edits and deletes nothing. The record and its
    /// attachment are immutable once created. A mistaken record is corrected
    /// by a new record that supersedes it, and the original stays readable.
    /// Only transaction links can be added later, as separate relationship
    /// records.
    /// </remarks>
    public sealed class EvidenceRecord
    {
        private readonly List<EvidenceTransactionLink> _transactionLinks;

        private EvidenceRecord(
            Guid id,
            Guid householdId,
            EvidenceSourceKind sourceKind,
            DateOnly effectiveDate,
            string label,
            DateTimeOffset createdAtUtc,
            Guid? institutionId,
            Guid? accountId,
            string? reference,
            string? note,
            Guid? supersedesEvidenceId,
            EvidenceAttachment? attachment,
            IEnumerable<Guid> linkedTransactionIds)
        {
            EnsureNonEmpty(id, nameof(id));
            EnsureNonEmpty(householdId, nameof(householdId));
            EnsureNonEmpty(institutionId, nameof(institutionId));
            EnsureNonEmpty(accountId, nameof(accountId));
            EnsureNonEmpty(supersedesEvidenceId, nameof(supersedesEvidenceId));

            if (!Enum.IsDefined(sourceKind))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sourceKind),
                    sourceKind,
                    "Unsupported evidence source kind.");
            }

            if (supersedesEvidenceId == id)
            {
                throw new ArgumentException(
                    "An evidence record cannot supersede itself.",
                    nameof(supersedesEvidenceId));
            }

            if (!EvidenceText.TryNormalizeLabel(label, out var normalizedLabel))
            {
                throw new ArgumentException(
                    "Evidence label is required, single-line, and limited to 256 characters.",
                    nameof(label));
            }

            if (!EvidenceText.TryNormalizeReference(reference, out var normalizedReference))
            {
                throw new ArgumentException(
                    "Evidence reference is single-line and limited to 256 characters.",
                    nameof(reference));
            }

            if (!EvidenceText.TryNormalizeNote(note, out var normalizedNote))
            {
                throw new ArgumentException(
                    "Evidence note is limited to 2000 characters without control characters.",
                    nameof(note));
            }

            Id = id;
            HouseholdId = householdId;
            SourceKind = sourceKind;
            EffectiveDate = effectiveDate;
            Label = normalizedLabel;
            CreatedAtUtc = createdAtUtc.ToUniversalTime();
            InstitutionId = institutionId;
            AccountId = accountId;
            Reference = normalizedReference;
            Note = normalizedNote;
            SupersedesEvidenceId = supersedesEvidenceId;
            Attachment = attachment;

            _transactionLinks = [];

            foreach (var transactionId in linkedTransactionIds)
            {
                if (_transactionLinks.Any(x => x.TransactionId == transactionId))
                {
                    throw new ArgumentException(
                        "An evidence record cannot link the same transaction twice.",
                        nameof(linkedTransactionIds));
                }

                _transactionLinks.Add(
                    EvidenceTransactionLink.Create(
                        id,
                        transactionId,
                        CreatedAtUtc));
            }
        }

        public Guid Id { get; }

        public Guid HouseholdId { get; }

        public EvidenceSourceKind SourceKind { get; }

        public DateOnly EffectiveDate { get; }

        public string Label { get; }

        public DateTimeOffset CreatedAtUtc { get; }

        public Guid? InstitutionId { get; }

        public Guid? AccountId { get; }

        public string? Reference { get; }

        public string? Note { get; }

        public Guid? SupersedesEvidenceId { get; }

        public EvidenceAttachment? Attachment { get; }

        public IReadOnlyList<EvidenceTransactionLink> TransactionLinks
            => _transactionLinks;

        public static EvidenceRecord Create(
            Guid id,
            Guid householdId,
            EvidenceSourceKind sourceKind,
            DateOnly effectiveDate,
            string label,
            DateTimeOffset createdAtUtc,
            Guid? institutionId = null,
            Guid? accountId = null,
            string? reference = null,
            string? note = null,
            Guid? supersedesEvidenceId = null,
            EvidenceAttachment? attachment = null,
            IEnumerable<Guid>? linkedTransactionIds = null)
        {
            return new EvidenceRecord(
                id,
                householdId,
                sourceKind,
                effectiveDate,
                label,
                createdAtUtc,
                institutionId,
                accountId,
                reference,
                note,
                supersedesEvidenceId,
                attachment,
                linkedTransactionIds ?? []);
        }

        private static void EnsureNonEmpty(Guid? value, string parameterName)
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
    /// A relationship record between evidence and one existing transaction.
    /// </summary>
    /// <remarks>
    /// A link is added, never edited or removed. It changes neither the
    /// evidence record nor the linked transaction's posted facts.
    /// </remarks>
    public sealed class EvidenceTransactionLink
    {
        private EvidenceTransactionLink(
            Guid evidenceId,
            Guid transactionId,
            DateTimeOffset linkedAtUtc)
        {
            if (evidenceId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Evidence ID cannot be empty.",
                    nameof(evidenceId));
            }

            if (transactionId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Transaction ID cannot be empty.",
                    nameof(transactionId));
            }

            EvidenceId = evidenceId;
            TransactionId = transactionId;
            LinkedAtUtc = linkedAtUtc.ToUniversalTime();
        }

        public Guid EvidenceId { get; }

        public Guid TransactionId { get; }

        public DateTimeOffset LinkedAtUtc { get; }

        public static EvidenceTransactionLink Create(
            Guid evidenceId,
            Guid transactionId,
            DateTimeOffset linkedAtUtc)
            => new(evidenceId, transactionId, linkedAtUtc);
    }

    /// <summary>
    /// Normalization rules for the bounded human text of an evidence record.
    /// </summary>
    public static class EvidenceText
    {
        public const int MaximumLabelLength = 256;

        public const int MaximumReferenceLength = 256;

        public const int MaximumNoteLength = 2_000;

        public static bool TryNormalizeLabel(
            string? value,
            out string normalized)
        {
            normalized = string.Empty;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim();

            if (trimmed.Length > MaximumLabelLength
                || trimmed.Any(char.IsControl)
                || !IsSafeText(trimmed))
            {
                return false;
            }

            normalized = trimmed;
            return true;
        }

        public static bool TryNormalizeReference(
            string? value,
            out string? normalized)
        {
            normalized = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            var trimmed = value.Trim();

            if (trimmed.Length > MaximumReferenceLength
                || trimmed.Any(char.IsControl)
                || !IsSafeText(trimmed))
            {
                return false;
            }

            normalized = trimmed;
            return true;
        }

        public static bool TryNormalizeNote(
            string? value,
            out string? normalized)
        {
            normalized = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            var trimmed = value.Trim();

            if (trimmed.Length > MaximumNoteLength
                || trimmed.Any(
                    character => char.IsControl(character)
                        && character is not ('\n' or '\r' or '\t'))
                || !IsSafeText(trimmed))
            {
                return false;
            }

            normalized = trimmed;
            return true;
        }

        /// <summary>
        /// Refuses unpaired surrogates, which cannot be stored or fingerprinted
        /// faithfully, and bidirectional embedding, override and isolate
        /// controls, which can make displayed text read differently from what
        /// was stored.
        /// </summary>
        private static bool IsSafeText(string value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];

                if (char.IsSurrogate(character))
                {
                    if (!char.IsSurrogatePair(value, index))
                    {
                        return false;
                    }

                    index++;
                    continue;
                }

                if (character is '؜' or '‎' or '‏'
                    or >= '‪' and <= '‮'
                    or >= '⁦' and <= '⁩')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
