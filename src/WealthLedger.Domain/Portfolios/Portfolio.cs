using WealthLedger.Domain.Common;

namespace WealthLedger.Domain.Portfolios
{
    public sealed class Portfolio
    {
        public Guid Id { get; }

        public Guid HouseholdId { get; }

        public string Code { get; }

        public string Name { get; private set; }

        public PortfolioStatus Status { get; private set; }

        public DateTimeOffset CreatedAtUtc { get; }

        public DateTimeOffset? ClosedAtUtc { get; private set; }

        private Portfolio(
            Guid id,
            Guid householdId,
            string code,
            string name,
            DateTimeOffset createdAtUtc)
        {
            EnsureNonEmpty(id, nameof(id));
            EnsureNonEmpty(
                householdId,
                nameof(householdId));

            Id = id;
            HouseholdId = householdId;

            Code = NormalizeCode(code);
            Name = NormalizeName(name);

            Status = PortfolioStatus.Active;

            CreatedAtUtc =
                createdAtUtc.ToUniversalTime();
        }

        public static Portfolio Create(
            Guid id,
            Guid householdId,
            string code,
            string name,
            DateTimeOffset createdAtUtc)
        {
            return new Portfolio(
                id,
                householdId,
                code,
                name,
                createdAtUtc);
        }

        public void Rename(string name)
        {
            EnsureActive();

            Name = NormalizeName(name);
        }

        public void Close(DateTimeOffset closedAtUtc)
        {
            EnsureActive();

            var normalized =
                closedAtUtc.ToUniversalTime();

            if (normalized < CreatedAtUtc)
            {
                throw new DomainRuleViolationException(
                    "Portfolio closing time cannot be earlier than creation time.");
            }

            Status = PortfolioStatus.Closed;
            ClosedAtUtc = normalized;
        }

        public void Archive()
        {
            if (Status != PortfolioStatus.Closed)
            {
                throw new DomainRuleViolationException(
                    "Only a closed portfolio can be archived.");
            }

            Status = PortfolioStatus.Archived;
        }

        private void EnsureActive()
        {
            if (Status != PortfolioStatus.Active)
            {
                throw new DomainRuleViolationException(
                    "Only an active portfolio can be modified.");
            }
        }

        private static string NormalizeCode(string code)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);

            var normalized =
                code.Trim().ToUpperInvariant();

            if (normalized.Length > 64)
            {
                throw new ArgumentException(
                    "Portfolio code cannot exceed 64 characters.",
                    nameof(code));
            }

            if (!normalized.All(IsAllowedCodeCharacter))
            {
                throw new ArgumentException(
                    "Portfolio code may contain only A-Z, 0-9, '_' and '-'.",
                    nameof(code));
            }

            return normalized;
        }

        private static string NormalizeName(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            var normalized =
                name.Trim();

            if (normalized.Length > 256)
            {
                throw new ArgumentException(
                    "Portfolio name cannot exceed 256 characters.",
                    nameof(name));
            }

            return normalized;
        }

        private static bool IsAllowedCodeCharacter(char value)
        {
            return value is >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '_'
                or '-';
        }

        private static void EnsureNonEmpty(
            Guid value,
            string parameterName)
        {
            if (value == Guid.Empty)
            {
                throw new ArgumentException(
                    $"{parameterName} cannot be empty.",
                    parameterName);
            }
        }
    }
}
