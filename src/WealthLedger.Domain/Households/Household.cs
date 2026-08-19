using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Households
{
    public sealed class Household
    {
        public Guid Id { get; }

        public string Name { get; private set; }

        public CurrencyCode BaseCurrency { get; }

        public DateTimeOffset CreatedAtUtc { get; }

        private Household(
            Guid id,
            string name,
            CurrencyCode baseCurrency,
            DateTimeOffset createdAtUtc)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentException(
                    "Household ID cannot be empty.",
                    nameof(id));
            }

            ArgumentNullException.ThrowIfNull(baseCurrency);

            Id = id;
            Name = NormalizeName(name);
            BaseCurrency = baseCurrency;
            CreatedAtUtc = createdAtUtc.ToUniversalTime();
        }

        public static Household Create(
            Guid id,
            string name,
            CurrencyCode baseCurrency,
            DateTimeOffset createdAtUtc)
        {
            return new Household(
                id,
                name,
                baseCurrency,
                createdAtUtc);
        }

        public void Rename(string name)
        {
            Name = NormalizeName(name);
        }

        private static string NormalizeName(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            var normalized = name.Trim();

            if (normalized.Length > 256)
            {
                throw new ArgumentException(
                    "Household name cannot exceed 256 characters.",
                    nameof(name));
            }

            return normalized;
        }
    }
}
