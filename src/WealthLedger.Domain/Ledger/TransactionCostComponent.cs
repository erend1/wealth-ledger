using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Ledger
{
    public sealed class TransactionCostComponent
    {
        public Guid Id { get; }

        public CostType Type { get; }

        public CostTreatment Treatment { get; }

        public Money Amount { get; }

        public string? Note { get; }

        internal TransactionCostComponent(
            Guid id,
            CostType type,
            CostTreatment treatment,
            Money amount,
            string? note)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentException(
                    "Cost component ID cannot be empty.",
                    nameof(id));
            }

            ArgumentNullException.ThrowIfNull(amount);

            if (amount.MinorUnits < 0)
            {
                throw new ArgumentException(
                    "Transaction cost amount cannot be negative.",
                    nameof(amount));
            }

            Id = id;
            Type = type;
            Treatment = treatment;
            Amount = amount;
            Note = NormalizeNote(note);
        }

        private static string? NormalizeNote(string? note)
        {
            if (string.IsNullOrWhiteSpace(note))
            {
                return null;
            }

            var normalized = note.Trim();

            if (normalized.Length > 1000)
            {
                throw new ArgumentException(
                    "Transaction cost note cannot exceed 1,000 characters.",
                    nameof(note));
            }

            return normalized;
        }
    }
}
