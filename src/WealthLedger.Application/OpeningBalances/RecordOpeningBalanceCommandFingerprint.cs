using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using WealthLedger.Application.CoreLedger;

namespace WealthLedger.Application.OpeningBalances;

internal static class RecordOpeningBalanceCommandFingerprint
{
    internal const string CurrentAlgorithmCode = "SHA256";
    internal const int CurrentVersion = 1;

    internal static CommandFingerprint ComputeCurrent(
        RecordOpeningBalanceCommand command)
        => Compute(
            command,
            CurrentAlgorithmCode,
            CurrentVersion);

    internal static CommandFingerprint Compute(
        RecordOpeningBalanceCommand command,
        string algorithmCode,
        int version)
        => (algorithmCode, version) switch
        {
            (CurrentAlgorithmCode, CurrentVersion) => ComputeV1(command),
            _ => throw new NotSupportedException(
                $"Opening-balance fingerprint '{algorithmCode}' version '{version}' is not supported.")
        };

    private static CommandFingerprint ComputeV1(
        RecordOpeningBalanceCommand command)
    {
        var normalized =
            OpeningBalanceCommandCanonicalizer.Normalize(command);
        var orderedLots =
            OpeningBalanceCommandCanonicalizer.OrderLots(normalized.Lots);
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(
                   buffer,
                   new JsonWriterOptions
                   {
                       Indented = false,
                       SkipValidation = false
                   }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", CurrentVersion);
            writer.WriteString(
                "operation",
                LedgerOperationCodes.RecordOpeningBalance);
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
                "assetId",
                normalized.AssetId.ToString("D"));
            writer.WriteString(
                "asOfDate",
                normalized.AsOfDate.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture));
            writer.WriteNumber(
                "quantityRawE8",
                normalized.Quantity.RawE8);
            WriteNullableText(
                writer,
                "externalReference",
                normalized.ExternalReference);
            writer.WriteString("note", normalized.Note);
            writer.WriteStartArray("lots");

            foreach (var lot in orderedLots)
            {
                writer.WriteStartObject();
                writer.WriteNumber("quantityRawE8", lot.Quantity.RawE8);
                WriteNullableDate(writer, "acquiredOn", lot.AcquiredOn);
                writer.WriteString(
                    "costBasisStatus",
                    OpeningBalanceCommandCanonicalizer
                        .ToCostBasisStatusCode(lot.CostBasis.Status));
                WriteNullableInt64(
                    writer,
                    "originalCostBasisMinorUnits",
                    lot.CostBasis.Amount?.MinorUnits);
                WriteNullableText(
                    writer,
                    "costBasisCurrencyCode",
                    lot.CostBasis.Amount?.Currency.Value);
                WriteGoldDetail(writer, lot.PhysicalGoldDetail);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        return new CommandFingerprint(
            CurrentAlgorithmCode,
            CurrentVersion,
            Convert.ToHexString(
                    SHA256.HashData(buffer.WrittenSpan))
                .ToLowerInvariant());
    }

    private static void WriteGoldDetail(
        Utf8JsonWriter writer,
        Domain.Lots.PhysicalGoldLotDetail? detail)
    {
        writer.WritePropertyName("physicalGoldDetail");

        if (detail is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteNumber(
            "finenessPartsPerMillion",
            detail.Fineness.Ppm);
        writer.WriteNumber("pieceCount", detail.PieceCount);
        WriteNullableText(writer, "hallmark", detail.Hallmark);
        WriteNullableText(
            writer,
            "certificateReference",
            detail.CertificateReference);
        WriteNullableText(writer, "note", detail.Note);
        writer.WriteEndObject();
    }

    private static void WriteNullableDate(
        Utf8JsonWriter writer,
        string propertyName,
        DateOnly? value)
    {
        writer.WritePropertyName(propertyName);

        if (value is DateOnly date)
        {
            writer.WriteStringValue(
                date.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNullValue();
        }
    }

    private static void WriteNullableInt64(
        Utf8JsonWriter writer,
        string propertyName,
        long? value)
    {
        writer.WritePropertyName(propertyName);

        if (value is long number)
        {
            writer.WriteNumberValue(number);
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
