using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Lots
{
    public sealed record CostBasis
    {
        public CostBasisStatus Status { get; }

        public Money? Amount { get; }

        private CostBasis(
            CostBasisStatus status,
            Money? amount)
        {
            if (status == CostBasisStatus.Known)
            {
                ArgumentNullException.ThrowIfNull(amount);

                if (amount.MinorUnits < 0)
                {
                    throw new ArgumentException(
                        "Known cost basis cannot be negative.",
                        nameof(amount));
                }
            }
            else if (amount is not null)
            {
                throw new ArgumentException(
                    "Only a known cost basis may contain an amount.",
                    nameof(amount));
            }

            Status = status;
            Amount = amount;
        }

        public static CostBasis Known(Money amount)
            => new(
                CostBasisStatus.Known,
                amount);

        public static CostBasis Unknown()
            => new(
                CostBasisStatus.Unknown,
                null);

        public static CostBasis NotApplicable()
            => new(
                CostBasisStatus.NotApplicable,
                null);
    }
}
