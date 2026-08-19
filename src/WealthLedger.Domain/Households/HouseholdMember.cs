namespace WealthLedger.Domain.Households
{
    public sealed class HouseholdMember
    {
        public Guid Id { get; }

        public Guid HouseholdId { get; }

        public string DisplayName { get; private set; }

        public bool IsActive { get; private set; }

        public DateTimeOffset CreatedAtUtc { get; }

        private HouseholdMember(
            Guid id,
            Guid householdId,
            string displayName,
            DateTimeOffset createdAtUtc)
        {
            EnsureNonEmpty(id, nameof(id));
            EnsureNonEmpty(householdId, nameof(householdId));

            Id = id;
            HouseholdId = householdId;
            DisplayName = NormalizeDisplayName(displayName);
            IsActive = true;
            CreatedAtUtc = createdAtUtc.ToUniversalTime();
        }

        public static HouseholdMember Create(
            Guid id,
            Guid householdId,
            string displayName,
            DateTimeOffset createdAtUtc)
        {
            return new HouseholdMember(
                id,
                householdId,
                displayName,
                createdAtUtc);
        }

        public void Rename(string displayName)
        {
            DisplayName =
                NormalizeDisplayName(displayName);
        }

        public void Activate()
        {
            IsActive = true;
        }

        public void Deactivate()
        {
            IsActive = false;
        }

        private static string NormalizeDisplayName(
            string displayName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(
                displayName);

            var normalized =
                displayName.Trim();

            if (normalized.Length > 128)
            {
                throw new ArgumentException(
                    "Household member display name cannot exceed 128 characters.",
                    nameof(displayName));
            }

            return normalized;
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
