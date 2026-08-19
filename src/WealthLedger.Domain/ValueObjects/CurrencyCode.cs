namespace WealthLedger.Domain.ValueObjects
{
    public readonly record struct CurrencyCode
    {
        public string Value { get; }

        public CurrencyCode(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

            var normalized = value.Trim().ToUpperInvariant();

            if (normalized.Length != 3 ||
                normalized.Any(c => c is < 'A' or > 'Z'))
            {
                throw new ArgumentException(
                    "Currency code must be exactly three ASCII letters.",
                    nameof(value));
            }

            Value = normalized;
        }

        public static CurrencyCode TRY => new("TRY");
        public static CurrencyCode USD => new("USD");
        public static CurrencyCode EUR => new("EUR");

        public override string ToString() => Value;
    }
}
