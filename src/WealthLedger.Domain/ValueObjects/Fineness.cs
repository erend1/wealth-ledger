namespace WealthLedger.Domain.ValueObjects
{
    public sealed record Fineness
    {
        public const int MaximumPpm = 1_000_000;

        public int Ppm { get; }

        public Fineness(int ppm)
        {
            if (ppm is <= 0 or > MaximumPpm)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ppm),
                    "Fineness must be between 1 and 1,000,000 ppm.");
            }

            Ppm = ppm;
        }

        public decimal ToRatio()
            => Ppm / 1_000_000m;

        public decimal ToPercentage()
            => Ppm / 10_000m;

        public override string ToString()
            => $"{ToPercentage():0.####}%";
    }
}
