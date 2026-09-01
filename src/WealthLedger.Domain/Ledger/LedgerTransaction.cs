using WealthLedger.Domain.Common;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Ledger
{
    public sealed class LedgerTransaction
    {
        private readonly List<TransactionEntry> _entries = [];
        private readonly List<TransactionCostComponent> _costs = [];

        public Guid Id { get; }

        public Guid HouseholdId { get; }

        public TransactionType Type { get; }

        public TransactionStatus Status { get; private set; }

        public DateOnly? OrderDate { get; private set; }

        public DateOnly? ExecutionDate { get; private set; }

        public DateOnly? SettlementDate { get; private set; }

        public string? ExternalReference { get; private set; }

        public string? Note { get; private set; }

        public Guid? ReversalOfTransactionId { get; }

        public DateTimeOffset CreatedAtUtc { get; }

        public DateTimeOffset? PostedAtUtc { get; private set; }

        public CashFlowDetail? CashFlowDetail { get; private set; }

        public IReadOnlyCollection<TransactionEntry> Entries => _entries;

        public IReadOnlyCollection<TransactionCostComponent> Costs => _costs;

        private LedgerTransaction(
            Guid id,
            Guid householdId,
            TransactionType type,
            DateTimeOffset createdAtUtc,
            DateOnly? orderDate,
            DateOnly? executionDate,
            DateOnly? settlementDate,
            Guid? reversalOfTransactionId,
            string? externalReference,
            string? note)
        {
            EnsureNonEmpty(id, nameof(id));
            EnsureNonEmpty(householdId, nameof(householdId));

            if (reversalOfTransactionId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Reversal transaction ID cannot be empty.",
                    nameof(reversalOfTransactionId));
            }

            if (reversalOfTransactionId == id)
            {
                throw new ArgumentException(
                    "A transaction cannot reverse itself.",
                    nameof(reversalOfTransactionId));
            }

            ValidateDateOrder(
                orderDate,
                executionDate,
                settlementDate);

            Id = id;
            HouseholdId = householdId;
            Type = type;
            Status = TransactionStatus.Draft;

            OrderDate = orderDate;
            ExecutionDate = executionDate;
            SettlementDate = settlementDate;

            ReversalOfTransactionId = reversalOfTransactionId;

            ExternalReference = NormalizeExternalReference(
                externalReference);

            Note = NormalizeNote(note);

            CreatedAtUtc = createdAtUtc.ToUniversalTime();
        }

        public static LedgerTransaction CreateDraft(
            Guid id,
            Guid householdId,
            TransactionType type,
            DateTimeOffset createdAtUtc,
            DateOnly? orderDate = null,
            DateOnly? executionDate = null,
            DateOnly? settlementDate = null,
            string? externalReference = null,
            string? note = null)
        {
            if (type == TransactionType.Reversal)
            {
                throw new DomainRuleViolationException(
                    "Reversal transactions must be created from an existing posted transaction.");
            }

            return new LedgerTransaction(
                id,
                householdId,
                type,
                createdAtUtc,
                orderDate,
                executionDate,
                settlementDate,
                reversalOfTransactionId: null,
                externalReference,
                note);
        }

        public static LedgerTransaction ReconstitutePosted(
            Guid id,
            Guid householdId,
            TransactionType type,
            DateTimeOffset createdAtUtc,
            DateTimeOffset postedAtUtc,
            DateOnly? orderDate,
            DateOnly? executionDate,
            DateOnly? settlementDate,
            Guid? reversalOfTransactionId,
            string? externalReference,
            string? note,
            IReadOnlyCollection<LedgerTransactionEntrySnapshot> entries,
            IReadOnlyCollection<LedgerTransactionCostSnapshot> costs,
            LedgerCashFlowSnapshot? cashFlowDetail)
        {
            ArgumentNullException.ThrowIfNull(entries);
            ArgumentNullException.ThrowIfNull(costs);

            var normalizedPostedAt =
                postedAtUtc.ToUniversalTime();

            var transaction =
                new LedgerTransaction(
                    id,
                    householdId,
                    type,
                    createdAtUtc,
                    orderDate,
                    executionDate,
                    settlementDate,
                    reversalOfTransactionId,
                    externalReference,
                    note);

            if (normalizedPostedAt < transaction.CreatedAtUtc)
            {
                throw new DomainRuleViolationException(
                    "Posted time cannot be earlier than creation time.");
            }

            if (type == TransactionType.Reversal)
            {
                if (reversalOfTransactionId is null)
                {
                    throw new DomainRuleViolationException(
                        "A reversal must reference the original transaction.");
                }
            }
            else if (reversalOfTransactionId is not null)
            {
                throw new DomainRuleViolationException(
                    "Only a reversal transaction may reference an original transaction.");
            }

            EnsureCanonicalPersistedText(
                externalReference,
                transaction.ExternalReference,
                "external reference");

            EnsureCanonicalPersistedText(
                note,
                transaction.Note,
                "note");

            var orderedEntries =
                entries
                    .OrderBy(x => x.Sequence)
                    .ToArray();

            for (var sequence = 0;
                 sequence < orderedEntries.Length;
                 sequence++)
            {
                var snapshot =
                    orderedEntries[sequence];

                if (snapshot.Sequence != sequence)
                {
                    throw new DomainRuleViolationException(
                        "Persisted transaction entry sequences must be contiguous and start at zero.");
                }

                if (transaction._entries.Any(
                        x => x.Id == snapshot.Id))
                {
                    throw new DomainRuleViolationException(
                        "Persisted transaction entries cannot contain duplicate IDs.");
                }

                transaction._entries.Add(
                    new TransactionEntry(
                        snapshot.Id,
                        snapshot.Sequence,
                        snapshot.PortfolioId,
                        snapshot.AccountId,
                        snapshot.AssetId,
                        snapshot.QuantityDelta,
                        snapshot.Role,
                        snapshot.UnitPrice));
            }

            foreach (var snapshot in costs)
            {
                if (transaction._costs.Any(
                        x => x.Id == snapshot.Id))
                {
                    throw new DomainRuleViolationException(
                        "Persisted transaction costs cannot contain duplicate IDs.");
                }

                var cost =
                    new TransactionCostComponent(
                        snapshot.Id,
                        snapshot.Type,
                        snapshot.Treatment,
                        snapshot.Amount,
                        snapshot.Note);

                EnsureCanonicalPersistedText(
                    snapshot.Note,
                    cost.Note,
                    "transaction cost note");

                transaction._costs.Add(cost);
            }

            if (cashFlowDetail is not null)
            {
                transaction.CashFlowDetail =
                    new CashFlowDetail(
                        cashFlowDetail.Category,
                        cashFlowDetail.HouseholdMemberId);
            }

            transaction.ValidateForPosting();

            transaction.Status =
                TransactionStatus.Posted;

            transaction.PostedAtUtc =
                normalizedPostedAt;

            return transaction;
        }

        private static void EnsureCanonicalPersistedText(
            string? supplied,
            string? normalized,
            string fieldName)
        {
            if (!string.Equals(
                    supplied,
                    normalized,
                    StringComparison.Ordinal))
            {
                throw new DomainRuleViolationException(
                    $"Persisted {fieldName} is not in canonical form.");
            }
        }

        public TransactionEntry AddEntry(
            Guid portfolioId,
            Guid accountId,
            Guid assetId,
            QuantityDelta quantityDelta,
            EntryRole role,
            UnitPrice? unitPrice = null)
        {
            EnsureMutable();

            var entry = new TransactionEntry(
                Guid.NewGuid(),
                _entries.Count,
                portfolioId,
                accountId,
                assetId,
                quantityDelta,
                role,
                unitPrice);

            _entries.Add(entry);

            return entry;
        }

        public TransactionCostComponent AddCost(
            CostType type,
            CostTreatment treatment,
            Money amount,
            string? note = null)
        {
            EnsureMutable();

            var cost = new TransactionCostComponent(
                Guid.NewGuid(),
                type,
                treatment,
                amount,
                note);

            _costs.Add(cost);

            return cost;
        }

        public void AttachCashFlowDetail(
            CashFlowCategory category,
            Guid? householdMemberId = null)
        {
            EnsureMutable();

            if (Type != TransactionType.Contribution)
            {
                throw new DomainRuleViolationException(
                    "Cash flow detail is currently supported only for contribution transactions.");
            }

            if (CashFlowDetail is not null)
            {
                throw new DomainRuleViolationException(
                    "Cash flow detail has already been attached.");
            }

            CashFlowDetail = new CashFlowDetail(
                category,
                householdMemberId);
        }

        public void SetDates(
            DateOnly? orderDate,
            DateOnly? executionDate,
            DateOnly? settlementDate)
        {
            EnsureMutable();

            ValidateDateOrder(
                orderDate,
                executionDate,
                settlementDate);

            OrderDate = orderDate;
            ExecutionDate = executionDate;
            SettlementDate = settlementDate;
        }

        public void SetExternalReference(string? externalReference)
        {
            EnsureMutable();

            ExternalReference =
                NormalizeExternalReference(externalReference);
        }

        public void SetNote(string? note)
        {
            EnsureMutable();

            Note = NormalizeNote(note);
        }

        public void MarkOrdered()
        {
            if (Status != TransactionStatus.Draft)
            {
                throw new DomainRuleViolationException(
                    "Only draft transactions can be marked as ordered.");
            }

            if (Type is not TransactionType.Buy
                and not TransactionType.Sell)
            {
                throw new DomainRuleViolationException(
                    "Only buy and sell transactions can enter the ordered state.");
            }

            if (OrderDate is null)
            {
                throw new DomainRuleViolationException(
                    "An ordered transaction must have an order date.");
            }

            Status = TransactionStatus.Ordered;
        }

        public void Cancel()
        {
            if (Status is not TransactionStatus.Draft
                and not TransactionStatus.Ordered)
            {
                throw new DomainRuleViolationException(
                    "Only draft or ordered transactions can be cancelled.");
            }

            Status = TransactionStatus.Cancelled;
        }

        public void Post(DateTimeOffset postedAtUtc)
        {
            if (Status is not TransactionStatus.Draft
                and not TransactionStatus.Ordered)
            {
                throw new DomainRuleViolationException(
                    "Only draft or ordered transactions can be posted.");
            }

            var normalizedPostedAt =
                postedAtUtc.ToUniversalTime();

            if (normalizedPostedAt < CreatedAtUtc)
            {
                throw new DomainRuleViolationException(
                    "Posted time cannot be earlier than creation time.");
            }

            ValidateForPosting();

            Status = TransactionStatus.Posted;
            PostedAtUtc = normalizedPostedAt;
        }

        public static LedgerTransaction CreateReversal(
            Guid id,
            LedgerTransaction original,
            DateTimeOffset createdAtUtc,
            string? note = null)
        {
            ArgumentNullException.ThrowIfNull(original);

            if (original.Status != TransactionStatus.Posted)
            {
                throw new DomainRuleViolationException(
                    "Only posted transactions can be reversed.");
            }

            if (original.Type == TransactionType.Reversal)
            {
                throw new DomainRuleViolationException(
                    "A reversal transaction cannot itself be reversed directly.");
            }

            var reversal = new LedgerTransaction(
                id,
                original.HouseholdId,
                TransactionType.Reversal,
                createdAtUtc,

                // Bookkeeping correction:
                // preserve the original effective dates.
                original.OrderDate,
                original.ExecutionDate,
                original.SettlementDate,

                original.Id,
                externalReference: null,
                note);

            foreach (var originalEntry
                     in original._entries.OrderBy(x => x.Sequence))
            {
                reversal.AddEntry(
                    originalEntry.PortfolioId,
                    originalEntry.AccountId,
                    originalEntry.AssetId,
                    originalEntry.QuantityDelta.Negate(),
                    originalEntry.Role,
                    originalEntry.UnitPrice);
            }

            return reversal;
        }

        private void ValidateForPosting()
        {
            if (_entries.Count == 0)
            {
                throw new DomainRuleViolationException(
                    "A transaction cannot be posted without entries.");
            }

            if (ExecutionDate is null)
            {
                throw new DomainRuleViolationException(
                    "A posted transaction must have an execution date.");
            }

            ValidateDateOrder(
                OrderDate,
                ExecutionDate,
                SettlementDate);

            switch (Type)
            {
                case TransactionType.Contribution:
                    ValidateContribution();
                    break;

                case TransactionType.Withdrawal:
                    ValidateWithdrawal();
                    break;

                case TransactionType.Buy:
                    ValidateBuy();
                    break;

                case TransactionType.Sell:
                    ValidateSell();
                    break;

                case TransactionType.Transfer:
                    ValidateTransfer();
                    break;

                case TransactionType.Dividend:
                case TransactionType.Income:
                    ValidateIncome();
                    break;

                case TransactionType.Fee:
                    ValidateFee();
                    break;

                case TransactionType.Tax:
                    ValidateTax();
                    break;

                case TransactionType.OpeningBalance:
                    ValidateOpeningBalance();
                    break;

                case TransactionType.Adjustment:
                    ValidateAdjustment();
                    break;

                case TransactionType.Reversal:
                    ValidateReversal();
                    break;

                case TransactionType.Expense:
                    throw new DomainRuleViolationException(
                        "Expense transaction semantics have not been implemented yet.");

                case TransactionType.CorporateAction:
                    throw new DomainRuleViolationException(
                        "Corporate action semantics have not been implemented yet.");

                default:
                    throw new DomainRuleViolationException(
                        $"Transaction type '{Type}' is not supported.");
            }
        }

        private void ValidateContribution()
        {
            if (CashFlowDetail is null)
            {
                throw new DomainRuleViolationException(
                    "A contribution must have cash flow detail.");
            }

            if (_entries.Any(x =>
                    x.Role != EntryRole.Principal))
            {
                throw new DomainRuleViolationException(
                    "Contribution entries must use the principal role.");
            }

            if (_entries.Any(x =>
                    !x.QuantityDelta.IsPositive))
            {
                throw new DomainRuleViolationException(
                    "Contribution entries must increase holdings.");
            }
        }

        private void ValidateWithdrawal()
        {
            EnsureNoCashFlowDetail();

            if (_entries.Any(x =>
                    x.Role != EntryRole.Principal))
            {
                throw new DomainRuleViolationException(
                    "Withdrawal entries must use the principal role.");
            }

            if (_entries.Any(x =>
                    !x.QuantityDelta.IsNegative))
            {
                throw new DomainRuleViolationException(
                    "Withdrawal entries must decrease holdings.");
            }
        }

        private void ValidateBuy()
        {
            EnsureNoCashFlowDetail();

            var principalEntries =
                _entries
                    .Where(x => x.Role == EntryRole.Principal)
                    .ToList();

            var considerationEntries =
                _entries
                    .Where(x => x.Role == EntryRole.Consideration)
                    .ToList();

            if (principalEntries.Count == 0)
            {
                throw new DomainRuleViolationException(
                    "A buy transaction must contain at least one principal entry.");
            }

            if (considerationEntries.Count == 0)
            {
                throw new DomainRuleViolationException(
                    "A buy transaction must contain at least one consideration entry.");
            }

            if (principalEntries.Any(x =>
                    !x.QuantityDelta.IsPositive))
            {
                throw new DomainRuleViolationException(
                    "Buy principal entries must increase holdings.");
            }

            if (considerationEntries.Any(x =>
                    !x.QuantityDelta.IsNegative))
            {
                throw new DomainRuleViolationException(
                    "Buy consideration entries must decrease holdings.");
            }

            ValidateTradeSupportingEntries();
        }

        private void ValidateSell()
        {
            EnsureNoCashFlowDetail();

            var principalEntries =
                _entries
                    .Where(x => x.Role == EntryRole.Principal)
                    .ToList();

            var considerationEntries =
                _entries
                    .Where(x => x.Role == EntryRole.Consideration)
                    .ToList();

            if (principalEntries.Count == 0)
            {
                throw new DomainRuleViolationException(
                    "A sell transaction must contain at least one principal entry.");
            }

            if (considerationEntries.Count == 0)
            {
                throw new DomainRuleViolationException(
                    "A sell transaction must contain at least one consideration entry.");
            }

            if (principalEntries.Any(x =>
                    !x.QuantityDelta.IsNegative))
            {
                throw new DomainRuleViolationException(
                    "Sell principal entries must decrease holdings.");
            }

            if (considerationEntries.Any(x =>
                    !x.QuantityDelta.IsPositive))
            {
                throw new DomainRuleViolationException(
                    "Sell consideration entries must increase holdings.");
            }

            ValidateTradeSupportingEntries();
        }

        private void ValidateTradeSupportingEntries()
        {
            var allowedRoles = new[]
            {
            EntryRole.Principal,
            EntryRole.Consideration,
            EntryRole.Fee,
            EntryRole.Tax
        };

            if (_entries.Any(x =>
                    !allowedRoles.Contains(x.Role)))
            {
                throw new DomainRuleViolationException(
                    "Buy and sell transactions may contain only principal, consideration, fee and tax entries.");
            }

            if (_entries
                .Where(x =>
                    x.Role is EntryRole.Fee or EntryRole.Tax)
                .Any(x =>
                    !x.QuantityDelta.IsNegative))
            {
                throw new DomainRuleViolationException(
                    "Fee and tax entries must decrease holdings.");
            }
        }

        private void ValidateTransfer()
        {
            EnsureNoCashFlowDetail();

            var transferEntries =
                _entries
                    .Where(x => x.Role == EntryRole.Transfer)
                    .ToList();

            if (transferEntries.Count < 2)
            {
                throw new DomainRuleViolationException(
                    "A transfer must contain at least two transfer entries.");
            }

            if (_entries.Any(x =>
                    x.Role is not EntryRole.Transfer
                        and not EntryRole.Fee
                        and not EntryRole.Tax))
            {
                throw new DomainRuleViolationException(
                    "A transfer may contain only transfer, fee and tax entries.");
            }

            if (transferEntries.Any(x =>
                    x.UnitPrice is not null))
            {
                throw new DomainRuleViolationException(
                    "Transfer entries cannot have a unit price.");
            }

            if (_entries
                .Where(x =>
                    x.Role is EntryRole.Fee or EntryRole.Tax)
                .Any(x =>
                    !x.QuantityDelta.IsNegative))
            {
                throw new DomainRuleViolationException(
                    "Transfer fee and tax entries must decrease holdings.");
            }

            foreach (var assetGroup
                     in transferEntries.GroupBy(x => x.AssetId))
            {
                long net = 0;

                foreach (var entry in assetGroup)
                {
                    net = checked(
                        net + entry.QuantityDelta.RawE8);
                }

                if (net != 0)
                {
                    throw new DomainRuleViolationException(
                        $"Transfer entries for asset '{assetGroup.Key}' must net to zero.");
                }
            }
        }

        private void ValidateIncome()
        {
            EnsureNoCashFlowDetail();

            if (_entries.Any(x =>
                    x.Role != EntryRole.Income))
            {
                throw new DomainRuleViolationException(
                    "Income transactions must contain only income entries.");
            }

            if (_entries.Any(x =>
                    !x.QuantityDelta.IsPositive))
            {
                throw new DomainRuleViolationException(
                    "Income entries must increase holdings.");
            }
        }

        private void ValidateFee()
        {
            EnsureNoCashFlowDetail();

            if (_entries.Any(x =>
                    x.Role != EntryRole.Fee))
            {
                throw new DomainRuleViolationException(
                    "Fee transactions must contain only fee entries.");
            }

            if (_entries.Any(x =>
                    !x.QuantityDelta.IsNegative))
            {
                throw new DomainRuleViolationException(
                    "Fee entries must decrease holdings.");
            }
        }

        private void ValidateTax()
        {
            EnsureNoCashFlowDetail();

            if (_entries.Any(x =>
                    x.Role != EntryRole.Tax))
            {
                throw new DomainRuleViolationException(
                    "Tax transactions must contain only tax entries.");
            }

            if (_entries.Any(x =>
                    !x.QuantityDelta.IsNegative))
            {
                throw new DomainRuleViolationException(
                    "Tax entries must decrease holdings.");
            }
        }

        private void ValidateOpeningBalance()
        {
            EnsureNoCashFlowDetail();

            if (_entries.Any(x =>
                    x.Role != EntryRole.Principal))
            {
                throw new DomainRuleViolationException(
                    "Opening balance entries must use the principal role.");
            }

            if (_entries.Any(x =>
                    !x.QuantityDelta.IsPositive))
            {
                throw new DomainRuleViolationException(
                    "Opening balance entries must increase holdings.");
            }

            if (_entries.Any(x =>
                    x.UnitPrice is not null))
            {
                throw new DomainRuleViolationException(
                    "Opening balance entries cannot contain acquisition prices.");
            }
        }

        private void ValidateAdjustment()
        {
            EnsureNoCashFlowDetail();

            if (_entries.Any(x =>
                    x.Role != EntryRole.Adjustment))
            {
                throw new DomainRuleViolationException(
                    "Adjustment transactions must contain only adjustment entries.");
            }
        }

        private void ValidateReversal()
        {
            EnsureNoCashFlowDetail();

            if (ReversalOfTransactionId is null)
            {
                throw new DomainRuleViolationException(
                    "A reversal must reference the original transaction.");
            }

            if (_costs.Count != 0)
            {
                throw new DomainRuleViolationException(
                    "A reversal transaction cannot contain cost components.");
            }

            if (ExternalReference is not null)
            {
                throw new DomainRuleViolationException(
                    "A reversal transaction cannot contain an external reference.");
            }
        }

        private void EnsureNoCashFlowDetail()
        {
            if (CashFlowDetail is not null)
            {
                throw new DomainRuleViolationException(
                    "Cash flow detail is valid only for contribution transactions.");
            }
        }

        private void EnsureMutable()
        {
            if (Status is TransactionStatus.Posted
                or TransactionStatus.Cancelled)
            {
                throw new DomainRuleViolationException(
                    $"A {Status.ToString().ToLowerInvariant()} transaction cannot be modified.");
            }
        }

        private static void ValidateDateOrder(
            DateOnly? orderDate,
            DateOnly? executionDate,
            DateOnly? settlementDate)
        {
            if (orderDate.HasValue
                && executionDate.HasValue
                && orderDate.Value > executionDate.Value)
            {
                throw new ArgumentException(
                    "Order date cannot be later than execution date.");
            }

            if (executionDate.HasValue
                && settlementDate.HasValue
                && executionDate.Value > settlementDate.Value)
            {
                throw new ArgumentException(
                    "Execution date cannot be later than settlement date.");
            }
        }

        private static string? NormalizeExternalReference(
            string? externalReference)
        {
            if (string.IsNullOrWhiteSpace(externalReference))
            {
                return null;
            }

            var normalized =
                externalReference.Trim();

            if (normalized.Length > 256)
            {
                throw new ArgumentException(
                    "External reference cannot exceed 256 characters.",
                    nameof(externalReference));
            }

            return normalized;
        }

        private static string? NormalizeNote(
            string? note)
        {
            if (string.IsNullOrWhiteSpace(note))
            {
                return null;
            }

            var normalized = note.Trim();

            if (normalized.Length > 2_000)
            {
                throw new ArgumentException(
                    "Transaction note cannot exceed 2,000 characters.",
                    nameof(note));
            }

            return normalized;
        }

        private static void EnsureNonEmpty(
            Guid value,
            string parameterName)
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
