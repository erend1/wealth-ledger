using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace WealthLedger.Application.CoreLedger
{
    internal static class RecordFundPurchaseCommandFingerprint
    {
        internal const string CurrentAlgorithmCode =
            "SHA256";

        internal const int CurrentVersion = 1;

        internal static CommandFingerprint ComputeCurrent(
            RecordFundPurchaseCommand command)
        {
            return Compute(
                command,
                CurrentAlgorithmCode,
                CurrentVersion);
        }

        internal static CommandFingerprint Compute(
            RecordFundPurchaseCommand command,
            string algorithmCode,
            int version)
        {
            ArgumentNullException.ThrowIfNull(command);

            return (algorithmCode, version) switch
            {
                (CurrentAlgorithmCode, 1) =>
                    ComputeV1(command),

                _ =>
                    throw new NotSupportedException(
                        $"Fund-purchase fingerprint '{algorithmCode}' version '{version}' is not supported.")
            };
        }

        internal static CommandFingerprint ComputeV1(
            RecordFundPurchaseCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);
            ArgumentNullException.ThrowIfNull(
                command.ExecutedUnitPrice);
            ArgumentNullException.ThrowIfNull(
                command.CashConsideration);

            var normalized =
                RecordFundPurchaseCommandCanonicalizer
                    .Normalize(command);

            var buffer =
                new ArrayBufferWriter<byte>();

            using (var writer =
                   new Utf8JsonWriter(
                       buffer,
                       new JsonWriterOptions
                       {
                           Indented = false,
                           SkipValidation = false
                       }))
            {
                writer.WriteStartObject();

                writer.WriteNumber(
                    "version",
                    CurrentVersion);

                writer.WriteString(
                    "operation",
                    LedgerOperationCodes
                        .RecordFundPurchase);

                writer.WriteString(
                    "householdId",
                    normalized.HouseholdId
                        .ToString("D"));

                writer.WriteString(
                    "portfolioId",
                    normalized.PortfolioId
                        .ToString("D"));

                writer.WriteString(
                    "accountId",
                    normalized.AccountId
                        .ToString("D"));

                writer.WriteString(
                    "fundAssetId",
                    normalized.FundAssetId
                        .ToString("D"));

                writer.WriteString(
                    "cashAssetId",
                    normalized.CashAssetId
                        .ToString("D"));

                writer.WriteNumber(
                    "fundQuantityRawE8",
                    normalized.FundQuantity.RawE8);

                writer.WriteNumber(
                    "executedUnitPriceRawE8",
                    normalized.ExecutedUnitPrice.RawE8);

                writer.WriteString(
                    "executedUnitPriceCurrency",
                    normalized.ExecutedUnitPrice
                        .Currency.Value);

                writer.WriteNumber(
                    "cashConsiderationMinorUnits",
                    normalized.CashConsideration
                        .MinorUnits);

                writer.WriteString(
                    "cashConsiderationCurrency",
                    normalized.CashConsideration
                        .Currency.Value);

                writer.WriteString(
                    "executionDate",
                    normalized.ExecutionDate.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture));

                WriteNullableText(
                    writer,
                    "externalReference",
                    normalized.ExternalReference);

                WriteNullableText(
                    writer,
                    "note",
                    normalized.Note);

                writer.WriteEndObject();
                writer.Flush();
            }

            var hash =
                SHA256.HashData(
                    buffer.WrittenSpan);

            return new CommandFingerprint(
                CurrentAlgorithmCode,
                CurrentVersion,
                Convert
                    .ToHexString(hash)
                    .ToLowerInvariant());
        }

        private static void WriteNullableText(
            Utf8JsonWriter writer,
            string propertyName,
            string? value)
        {
            writer.WritePropertyName(propertyName);

            if (value is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStringValue(value);
            }
        }
    }
}
