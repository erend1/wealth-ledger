using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Lots
{
    public sealed class PhysicalGoldLotDetail
    {
        public Fineness Fineness { get; }

        public int PieceCount { get; }

        public string? Hallmark { get; }

        public string? CertificateReference { get; }

        public string? Note { get; }

        public PhysicalGoldLotDetail(
            Fineness fineness,
            int pieceCount,
            string? hallmark = null,
            string? certificateReference = null,
            string? note = null)
        {
            ArgumentNullException.ThrowIfNull(fineness);

            if (pieceCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pieceCount),
                    "Piece count must be greater than zero.");
            }

            Fineness = fineness;
            PieceCount = pieceCount;

            Hallmark = Normalize(
                hallmark,
                128,
                nameof(hallmark));

            CertificateReference = Normalize(
                certificateReference,
                256,
                nameof(certificateReference));

            Note = Normalize(
                note,
                1000,
                nameof(note));
        }

        private static string? Normalize(
            string? value,
            int maximumLength,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = value.Trim();

            if (normalized.Length > maximumLength)
            {
                throw new ArgumentException(
                    $"{parameterName} cannot exceed {maximumLength} characters.",
                    parameterName);
            }

            return normalized;
        }
    }
}
