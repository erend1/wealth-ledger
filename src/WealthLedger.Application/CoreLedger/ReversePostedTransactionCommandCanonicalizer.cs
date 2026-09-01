namespace WealthLedger.Application.CoreLedger
{
    internal static class
        ReversePostedTransactionCommandCanonicalizer
    {
        internal const int MaxReasonLength = 2_000;

        internal static ReversePostedTransactionCommand Normalize(
            ReversePostedTransactionCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);

            if (command.OriginalTransactionId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Original transaction ID cannot be empty.",
                    nameof(command));
            }

            if (string.IsNullOrWhiteSpace(command.Reason))
            {
                throw ReversalReasonValidationException.Required();
            }

            var normalizedReason =
                command.Reason.Trim();

            if (normalizedReason.Length >
                MaxReasonLength
                || normalizedReason.Any(char.IsControl))
            {
                throw ReversalReasonValidationException.Invalid();
            }

            return command with
            {
                Reason = normalizedReason
            };
        }
    }
}
