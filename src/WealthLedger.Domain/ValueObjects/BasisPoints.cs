namespace WealthLedger.Domain.ValueObjects
{
    public readonly record struct BasisPoints
    {
        public const int FullPercentage = 10_000;

        public int Value { get; }

        public BasisPoints(int value)
        {
            if (value is < 0 or > FullPercentage)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Basis points must be between 0 and 10,000.");
            }

            Value = value;
        }

        public decimal ToPercentage()
            => Value / 100m;

        public decimal ToRatio()
            => Value / 10_000m;

        public override string ToString()
            => $"{ToPercentage():0.##}%";
    }
}
