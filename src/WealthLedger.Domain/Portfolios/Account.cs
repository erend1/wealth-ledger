using WealthLedger.Domain.Common;

namespace WealthLedger.Domain.Portfolios
{
    public sealed class Account
    {
        public Guid Id { get; }

        public Guid HouseholdId { get; }

        public Guid? InstitutionId { get; }

        public string Code { get; }

        public string Name { get; private set; }

        public AccountType Type { get; }

        public bool IsActive { get; private set; }

        public DateOnly? OpenedOn { get; }

        public DateOnly? ClosedOn { get; private set; }

        private Account(
            Guid id,
            Guid householdId,
            Guid? institutionId,
            string code,
            string name,
            AccountType type,
            DateOnly? openedOn)
        {
            EnsureNonEmpty(id, nameof(id));

            EnsureNonEmpty(
                householdId,
                nameof(householdId));

            if (institutionId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Institution ID cannot be empty.",
                    nameof(institutionId));
            }

            Id = id;
            HouseholdId = householdId;
            InstitutionId = institutionId;

            Code = NormalizeCode(code);
            Name = NormalizeName(name);

            Type = type;
            IsActive = true;

            OpenedOn = openedOn;
        }

        public static Account Create(
            Guid id,
            Guid householdId,
            Guid? institutionId,
            string code,
            string name,
            AccountType type,
            DateOnly? openedOn = null)
        {
            return new Account(
                id,
                householdId,
                institutionId,
                code,
                name,
                type,
                openedOn);
        }

        public void Rename(string name)
        {
            EnsureActive();

            Name = NormalizeName(name);
        }

        public void Close(DateOnly closedOn)
        {
            EnsureActive();

            if (OpenedOn.HasValue
                && closedOn < OpenedOn.Value)
            {
                throw new DomainRuleViolationException(
                    "Account closing date cannot be earlier than opening date.");
            }

            ClosedOn = closedOn;
            IsActive = false;
        }

        private void EnsureActive()
        {
            if (!IsActive)
            {
                throw new DomainRuleViolationException(
                    "A closed account cannot be modified.");
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
                    "Account code cannot exceed 64 characters.",
                    nameof(code));
            }

            if (!normalized.All(IsAllowedCodeCharacter))
            {
                throw new ArgumentException(
                    "Account code may contain only A-Z, 0-9, '_' and '-'.",
                    nameof(code));
            }

            return normalized;
        }

        private static string NormalizeName(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            var normalized = name.Trim();

            if (normalized.Length > 256)
            {
                throw new ArgumentException(
                    "Account name cannot exceed 256 characters.",
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
