using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Ledger
{
    public sealed class TransactionEntry
    {
        public Guid Id { get; }

        public int Sequence { get; }

        public Guid PortfolioId { get; }

        public Guid AccountId { get; }

        public Guid AssetId { get; }

        public QuantityDelta QuantityDelta { get; }

        public EntryRole Role { get; }

        public UnitPrice? UnitPrice { get; }

        internal TransactionEntry(
            Guid id,
            int sequence,
            Guid portfolioId,
            Guid accountId,
            Guid assetId,
            QuantityDelta quantityDelta,
            EntryRole role,
            UnitPrice? unitPrice)
        {
            EnsureNonEmpty(id, nameof(id));
            EnsureNonEmpty(portfolioId, nameof(portfolioId));
            EnsureNonEmpty(accountId, nameof(accountId));
            EnsureNonEmpty(assetId, nameof(assetId));

            if (sequence < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sequence),
                    "Entry sequence cannot be negative.");
            }

            if (quantityDelta.IsZero)
            {
                throw new ArgumentException(
                    "Transaction entry quantity delta cannot be zero.",
                    nameof(quantityDelta));
            }

            Id = id;
            Sequence = sequence;
            PortfolioId = portfolioId;
            AccountId = accountId;
            AssetId = assetId;
            QuantityDelta = quantityDelta;
            Role = role;
            UnitPrice = unitPrice;
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
