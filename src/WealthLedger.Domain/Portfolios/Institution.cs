namespace WealthLedger.Domain.Portfolios
{
    public sealed class Institution
    {
        public Guid Id { get; }

        public string Code { get; }

        public string Name { get; private set; }

        public InstitutionType Type { get; }

        public bool IsActive { get; private set; }

        private Institution(
            Guid id,
            string code,
            string name,
            InstitutionType type)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentException(
                    "Institution ID cannot be empty.",
                    nameof(id));
            }

            Id = id;
            Code = NormalizeCode(code);
            Name = NormalizeName(name);
            Type = type;
            IsActive = true;
        }

        public static Institution Create(
            Guid id,
            string code,
            string name,
            InstitutionType type)
        {
            return new Institution(
                id,
                code,
                name,
                type);
        }

        public void Rename(string name)
        {
            Name = NormalizeName(name);
        }

        public void Activate()
        {
            IsActive = true;
        }

        public void Deactivate()
        {
            IsActive = false;
        }

        private static string NormalizeCode(string code)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);

            var normalized =
                code.Trim().ToUpperInvariant();

            if (normalized.Length > 64)
            {
                throw new ArgumentException(
                    "Institution code cannot exceed 64 characters.",
                    nameof(code));
            }

            if (!normalized.All(IsAllowedCodeCharacter))
            {
                throw new ArgumentException(
                    "Institution code may contain only A-Z, 0-9, '_' and '-'.",
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
                    "Institution name cannot exceed 256 characters.",
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
    }
}
