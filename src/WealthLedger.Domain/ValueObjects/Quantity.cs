namespace WealthLedger.Domain.ValueObjects
{
    public readonly record struct Quantity
    {
        public const long Scale = 100_000_000L;

        public long RawE8 { get; }

        private Quantity(long rawE8)
        {
            if (rawE8 < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rawE8),
                    "Quantity cannot be negative.");
            }

            RawE8 = rawE8;
        }

        public static Quantity Zero => new(0);

        public static Quantity FromRaw(long rawE8)
            => new(rawE8);

        public static Quantity FromDecimal(decimal value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Quantity cannot be negative.");
            }

            var scaled = value * Scale;

            if (decimal.Truncate(scaled) != scaled)
            {
                throw new ArgumentException(
                    "Quantity cannot contain more than eight decimal places.",
                    nameof(value));
            }

            return new Quantity(
                checked((long)scaled));
        }

        public decimal ToDecimal()
            => RawE8 / (decimal)Scale;

        public Quantity Add(Quantity other)
            => new(
                checked(RawE8 + other.RawE8));

        public Quantity Subtract(Quantity other)
        {
            var result = checked(RawE8 - other.RawE8);

            if (result < 0)
            {
                throw new InvalidOperationException(
                    "Quantity cannot become negative.");
            }

            return new Quantity(result);
        }

        public override string ToString()
            => ToDecimal().ToString("0.########");
    }
}
