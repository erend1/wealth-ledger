using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using WealthLedger.Application.FundTrades;

namespace WealthLedger.Application.CoreLedger
{
    internal static class RecordFundPurchaseCommandFingerprint
    {
        internal const string CurrentAlgorithmCode =
            "SHA256";

        internal const int CurrentVersion = 2;

        /*
         * Version 1 hashes this literal, never CurrentVersion.
         *
         * Hashing the moving constant would change every existing
         * version-1 fingerprint the moment CurrentVersion advanced, turning
         * legitimate legacy retries into idempotency conflicts.
         */
        private const int Version1 = 1;

        private const int Version2 = 2;

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

                (CurrentAlgorithmCode, 2) =>
                    ComputeV2(command),

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

            /*
             * Version 1 predates separate accounts, order and settlement
             * dates, and cost components, so it cannot represent them.
             *
             * Hashing such a command as version 1 would silently ignore
             * meaningful facts and could report a different trade as an
             * equivalent retry. Replaying a version-1 receipt is therefore
             * only valid when the command still means what version 1 meant.
             */
            if (!command.HasOnlyLegacyFacts)
            {
                throw new IdempotencyConflictException();
            }

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
                    Version1);

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
                Version1,
                Convert
                    .ToHexString(hash)
                    .ToLowerInvariant());
        }

        /// <summary>
        /// Hashes every source fact a fund purchase can now carry.
        /// </summary>
        /// <remarks>
        /// Costs are written in canonical order so two requests that list the
        /// same components in a different order still produce one fingerprint.
        /// </remarks>
        internal static CommandFingerprint ComputeV2(
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

                writer.WriteNumber("version", Version2);

                writer.WriteString(
                    "operation",
                    LedgerOperationCodes.RecordFundPurchase);

                writer.WriteString(
                    "householdId",
                    normalized.HouseholdId.ToString("D"));

                writer.WriteString(
                    "portfolioId",
                    normalized.PortfolioId.ToString("D"));

                writer.WriteString(
                    "fundAccountId",
                    normalized.AccountId.ToString("D"));

                writer.WriteString(
                    "cashAccountId",
                    normalized.ResolvedCashAccountId.ToString("D"));

                writer.WriteString(
                    "fundAssetId",
                    normalized.FundAssetId.ToString("D"));

                writer.WriteString(
                    "cashAssetId",
                    normalized.CashAssetId.ToString("D"));

                writer.WriteNumber(
                    "fundQuantityRawE8",
                    normalized.FundQuantity.RawE8);

                writer.WriteNumber(
                    "executedUnitPriceRawE8",
                    normalized.ExecutedUnitPrice.RawE8);

                writer.WriteString(
                    "executedUnitPriceCurrency",
                    normalized.ExecutedUnitPrice.Currency.Value);

                writer.WriteNumber(
                    "cashConsiderationMinorUnits",
                    normalized.CashConsideration.MinorUnits);

                writer.WriteString(
                    "cashConsiderationCurrency",
                    normalized.CashConsideration.Currency.Value);

                WriteNullableDate(
                    writer,
                    "orderDate",
                    normalized.OrderDate);

                writer.WriteString(
                    "executionDate",
                    normalized.ExecutionDate.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture));

                WriteNullableDate(
                    writer,
                    "settlementDate",
                    normalized.SettlementDate);

                writer.WriteStartArray("costs");

                foreach (var cost in
                         FundTradeCanonicalizer.Order(
                             normalized.Costs ?? []))
                {
                    writer.WriteStartObject();

                    writer.WriteString(
                        "type",
                        FundTradeCanonicalizer.ToCostTypeCode(
                            cost.Type));

                    writer.WriteString(
                        "treatment",
                        FundTradeCanonicalizer.ToTreatmentCode(
                            cost.Treatment));

                    writer.WriteNumber(
                        "amountMinorUnits",
                        cost.Amount.MinorUnits);

                    writer.WriteString(
                        "amountCurrency",
                        cost.Amount.Currency.Value);

                    WriteNullableText(
                        writer,
                        "note",
                        cost.Note);

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();

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

            return new CommandFingerprint(
                CurrentAlgorithmCode,
                Version2,
                Convert
                    .ToHexString(
                        SHA256.HashData(buffer.WrittenSpan))
                    .ToLowerInvariant());
        }

        private static void WriteNullableDate(
            Utf8JsonWriter writer,
            string propertyName,
            DateOnly? value)
        {
            writer.WritePropertyName(propertyName);

            if (value is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStringValue(
                    value.Value.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture));
            }
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
