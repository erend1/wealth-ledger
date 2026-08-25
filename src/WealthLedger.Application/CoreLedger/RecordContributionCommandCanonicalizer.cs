namespace WealthLedger.Application.CoreLedger
{
    internal static class RecordContributionCommandCanonicalizer
    {
        internal static RecordContributionCommand Normalize(
            RecordContributionCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);

            return command with
            {
                ExternalReference =
                    NormalizeOptionalText(command.ExternalReference),

                Note =
                    NormalizeOptionalText(command.Note)
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
