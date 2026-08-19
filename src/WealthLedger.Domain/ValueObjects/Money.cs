using WealthLedger.Domain.Common;

namespace WealthLedger.Domain.ValueObjects
{
    public readonly record struct Money
    {
        public long MinorUnits { get; }
        public CurrencyCode Currency { get; }

        private Money(long minorUnits, CurrencyCode currency)
        {
            MinorUnits = minorUnits;
            Currency = currency;
        }

        public static Money FromMinorUnits(
            long minorUnits,
            CurrencyCode currency)
        {
            return new Money(minorUnits, currency);
        }

        public static Money Zero(CurrencyCode currency)
        {
            return new Money(0, currency);
        }

        public Money Add(Money other)
        {
            EnsureSameCurrency(other);

            return new Money(
                checked(MinorUnits + other.MinorUnits),
                Currency);
        }

        public Money Subtract(Money other)
        {
            EnsureSameCurrency(other);

            return new Money(
                checked(MinorUnits - other.MinorUnits),
                Currency);
        }

        public Money Negate()
        {
            return new Money(
                checked(-MinorUnits),
                Currency);
        }

        public decimal ToDecimal(int minorUnitDigits)
        {
            if (minorUnitDigits is < 0 or > 8)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minorUnitDigits));
            }

            var divisor = Pow10(minorUnitDigits);

            return MinorUnits / (decimal)divisor;
        }

        private void EnsureSameCurrency(Money other)
        {
            if (Currency != other.Currency)
            {
                throw new DomainRuleViolationException(
                    $"Cannot perform arithmetic between " +
                    $"{Currency} and {other.Currency}.");
            }
        }

        private static long Pow10(int exponent)
        {
            long result = 1;

            for (var i = 0; i < exponent; i++)
            {
                result = checked(result * 10);
            }

            return result;
        }

        public override string ToString()
            => $"{MinorUnits} minor units {Currency}";
    }
}
