using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using WealthLedger.Domain.Ledger;

namespace WealthLedger.Application.CoreLedger
{
    internal static class RecordContributionCommandFingerprint
    {
        internal const string CurrentAlgorithmCode = "SHA256";
        internal const int CurrentVersion = 1;

        internal static CommandFingerprint ComputeCurrent(
            RecordContributionCommand command)
        {
            return Compute(
                command,
                CurrentAlgorithmCode,
                CurrentVersion);
        }

        internal static CommandFingerprint Compute(
            RecordContributionCommand command,
            string algorithmCode,
            int version)
        {
            ArgumentNullException.ThrowIfNull(command);

            if (!string.Equals(
                    algorithmCode,
                    CurrentAlgorithmCode,
                    StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"Fingerprint algorithm '{algorithmCode}' is not supported.");
            }

            if (version != CurrentVersion)
            {
                throw new NotSupportedException(
                    $"Contribution fingerprint version '{version}' is not supported.");
            }

            var normalized =
                RecordContributionCommandCanonicalizer.Normalize(command);

            var buffer =
                new ArrayBufferWriter<byte>();

            using (var writer = new Utf8JsonWriter(
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
                    version);

                writer.WriteString(
                    "operation",
                    LedgerOperationCodes.RecordContribution);

                writer.WriteString(
                    "householdId",
                    normalized.HouseholdId.ToString("D"));

                writer.WriteString(
                    "portfolioId",
                    normalized.PortfolioId.ToString("D"));

                writer.WriteString(
                    "accountId",
                    normalized.AccountId.ToString("D"));

                writer.WriteString(
                    "cashAssetId",
                    normalized.CashAssetId.ToString("D"));

                writer.WriteNumber(
                    "amountMinorUnits",
                    normalized.Amount.MinorUnits);

                writer.WriteString(
                    "amountCurrency",
                    normalized.Amount.Currency.Value);

                writer.WriteString(
                    "cashFlowCategory",
                    ToStableCategoryCode(normalized.Category));

                writer.WriteString(
                    "executionDate",
                    normalized.ExecutionDate.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture));

                WriteNullableGuid(
                    writer,
                    "householdMemberId",
                    normalized.HouseholdMemberId);

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
                SHA256.HashData(buffer.WrittenSpan);

            return new CommandFingerprint(
                algorithmCode,
                version,
                Convert
                    .ToHexString(hash)
                    .ToLowerInvariant());
        }

        private static string ToStableCategoryCode(
            CashFlowCategory category)
        {
            return category switch
            {
                CashFlowCategory.Salary =>
                    "SALARY",

                CashFlowCategory.Bonus =>
                    "BONUS",

                CashFlowCategory.AcademicIncome =>
                    "ACADEMIC_INCOME",

                CashFlowCategory.Gift =>
                    "GIFT",

                CashFlowCategory.ExternalSale =>
                    "EXTERNAL_SALE",

                CashFlowCategory.Other =>
                    "OTHER",

                _ => throw new ArgumentOutOfRangeException(
                    nameof(category),
                    category,
                    "Unsupported cash-flow category.")
            };
        }

        private static void WriteNullableGuid(
            Utf8JsonWriter writer,
            string propertyName,
            Guid? value)
        {
            writer.WritePropertyName(propertyName);

            if (value is Guid guid)
            {
                writer.WriteStringValue(
                    guid.ToString("D"));
            }
            else
            {
                writer.WriteNullValue();
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
