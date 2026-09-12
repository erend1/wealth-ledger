namespace WealthLedger.Domain.ValueObjects
{
    public sealed record Fineness
    {
        public const int MaximumPpm = 1_000_000;

        public const decimal MinimumPerMille = 0.001m;

        public const decimal MaximumPerMille = 1_000m;

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

        public static Fineness FromPerMille(decimal perMille)
        {
            if (perMille is < MinimumPerMille or > MaximumPerMille)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(perMille),
                    "Fineness must be between 0.001 and 1,000 per mille.");
            }

            var scaled = checked(
                perMille * 1_000m);

            if (decimal.Truncate(scaled) != scaled)
            {
                throw new ArgumentException(
                    "Fineness cannot contain more than three decimal places in per-mille form.",
                    nameof(perMille));
            }

            return new Fineness(
                checked((int)scaled));
        }

        public decimal ToRatio()
            => Ppm / 1_000_000m;

        public decimal ToPerMille()
            => Ppm / 1_000m;

        public decimal ToPercentage()
            => Ppm / 10_000m;

        public override string ToString()
            => $"{ToPercentage():0.####}%";
    }
}
