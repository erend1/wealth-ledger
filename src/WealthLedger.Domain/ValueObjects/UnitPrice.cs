namespace WealthLedger.Domain.ValueObjects
{
    public readonly record struct UnitPrice
    {
        public const long Scale = 100_000_000L;

        public long RawE8 { get; }
        public CurrencyCode Currency { get; }

        private UnitPrice(
            long rawE8,
            CurrencyCode currency)
        {
            if (rawE8 < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rawE8),
                    "Unit price cannot be negative.");
            }

            RawE8 = rawE8;
            Currency = currency;
        }

        public static UnitPrice FromRaw(
            long rawE8,
            CurrencyCode currency)
        {
            return new UnitPrice(rawE8, currency);
        }

        public static UnitPrice FromDecimal(
            decimal value,
            CurrencyCode currency)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Unit price cannot be negative.");
            }

            var scaled = value * Scale;

            if (decimal.Truncate(scaled) != scaled)
            {
                throw new ArgumentException(
                    "Unit price cannot contain more than eight decimal places.",
                    nameof(value));
            }

            return new UnitPrice(
                checked((long)scaled),
                currency);
        }

        public decimal ToDecimal()
            => RawE8 / (decimal)Scale;

        public override string ToString()
            => $"{ToDecimal():0.########} {Currency}";
    }
}
