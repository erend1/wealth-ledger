namespace WealthLedger.Application.CoreLedger
{
    internal static class RecordFundPurchaseCommandCanonicalizer
    {
        internal static RecordFundPurchaseCommand Normalize(
            RecordFundPurchaseCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);

            return command with
            {
                ExternalReference =
                    NormalizeOptionalText(
                        command.ExternalReference),

                Note =
                    NormalizeOptionalText(
                        command.Note)
            };
        }

        private static string? NormalizeOptionalText(
            string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }
    }
}
