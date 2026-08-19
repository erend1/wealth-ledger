using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Assets
{
    public sealed class Asset
    {
        public Guid Id { get; }

        public string Code { get; }

        public string Name { get; }

        public AssetType Type { get; }

        public AssetUnit BaseUnit { get; }

        public CurrencyCode? BaseCurrency { get; }

        public LotTrackingMode LotTrackingMode { get; }

        public bool IsActive { get; private set; }

        private Asset(
            Guid id,
            string code,
            string name,
            AssetType type,
            AssetUnit baseUnit,
            CurrencyCode? baseCurrency,
            LotTrackingMode lotTrackingMode)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentException(
                    "Asset ID cannot be empty.",
                    nameof(id));
            }

            Id = id;
            Code = NormalizeCode(code);
            Name = NormalizeName(name);
            Type = type;
            BaseUnit = baseUnit;
            BaseCurrency = baseCurrency;
            LotTrackingMode = lotTrackingMode;
            IsActive = true;
        }

        public static Asset Create(
            Guid id,
            string code,
            string name,
            AssetType type,
            AssetUnit baseUnit,
            CurrencyCode? baseCurrency,
            LotTrackingMode lotTrackingMode)
        {
            return new Asset(
                id,
                code,
                name,
                type,
                baseUnit,
                baseCurrency,
                lotTrackingMode);
        }

        public void Deactivate()
        {
            IsActive = false;
        }

        public void Activate()
        {
            IsActive = true;
        }

        private static string NormalizeCode(string code)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);

            var normalized = code.Trim().ToUpperInvariant();

            if (normalized.Length > 64)
            {
                throw new ArgumentException(
                    "Asset code cannot exceed 64 characters.",
                    nameof(code));
            }

            if (!normalized.All(IsAllowedCodeCharacter))
            {
                throw new ArgumentException(
                    "Asset code may contain only A-Z, 0-9, '_' and '-'.",
                    nameof(code));
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

        private static string NormalizeName(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            var normalized = name.Trim();

            if (normalized.Length > 256)
            {
                throw new ArgumentException(
                    "Asset name cannot exceed 256 characters.",
                    nameof(name));
            }

            return normalized;
        }
    }
}
