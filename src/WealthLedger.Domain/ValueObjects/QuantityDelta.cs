namespace WealthLedger.Domain.ValueObjects
{
    public readonly record struct QuantityDelta
    {
        public const long Scale = Quantity.Scale;

        public long RawE8 { get; }

        private QuantityDelta(long rawE8)
        {
            RawE8 = rawE8;
        }

        public static QuantityDelta Zero => new(0);

        public static QuantityDelta FromRaw(long rawE8)
            => new(rawE8);

        public static QuantityDelta FromDecimal(decimal value)
        {
            var scaled = value * Scale;

            if (decimal.Truncate(scaled) != scaled)
            {
                throw new ArgumentException(
                    "Quantity delta cannot contain more than eight decimal places.",
                    nameof(value));
            }

            return new QuantityDelta(
                checked((long)scaled));
        }

        public decimal ToDecimal()
            => RawE8 / (decimal)Scale;

        public bool IsPositive => RawE8 > 0;
        public bool IsNegative => RawE8 < 0;
        public bool IsZero => RawE8 == 0;

        public QuantityDelta Negate()
            => new(
                checked(-RawE8));

        public QuantityDelta Add(QuantityDelta other)
            => new(
                checked(RawE8 + other.RawE8));

        public override string ToString()
            => ToDecimal().ToString("0.########");
    }
}
