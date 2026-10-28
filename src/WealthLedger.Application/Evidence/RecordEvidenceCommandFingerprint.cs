using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Evidence;

namespace WealthLedger.Application.Evidence;

/// <summary>
/// Versioned retry identity of a normalized evidence command.
/// </summary>
/// <remarks>
/// The attachment contributes its sanitized name, type, length and SHA-256,
/// so a retry with different bytes under the same key is a conflict rather
/// than a second retained copy.
/// </remarks>
internal static class RecordEvidenceCommandFingerprint
{
    internal const string CurrentAlgorithmCode = "SHA256";
    internal const int CurrentVersion = 1;

    // Version 1 hashes this literal, never CurrentVersion.
    private const int Version1 = 1;

    internal static CommandFingerprint ComputeCurrent(
        NormalizedEvidenceCommand command)
        => Compute(command, CurrentAlgorithmCode, CurrentVersion);

    internal static CommandFingerprint Compute(
        NormalizedEvidenceCommand command,
        string algorithmCode,
        int version)
    {
        ArgumentNullException.ThrowIfNull(command);

        return (algorithmCode, version) switch
        {
            (CurrentAlgorithmCode, 1) => ComputeV1(command),

            _ => throw new NotSupportedException(
                $"Evidence fingerprint '{algorithmCode}' version '{version}' is not supported.")
        };
    }

    private static CommandFingerprint ComputeV1(
        NormalizedEvidenceCommand command)
    {
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
            writer.WriteNumber("version", Version1);
            writer.WriteString("operation", EvidenceOperationCodes.RecordEvidence);
            writer.WriteString("householdId", command.HouseholdId.ToString("D"));
            writer.WriteString("sourceKind", ToSourceKindCode(command.SourceKind));
            writer.WriteString(
                "effectiveDate",
                command.EffectiveDate.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture));
            writer.WriteString("label", command.Label);
            WriteNullableText(writer, "reference", command.Reference);
            WriteNullableText(writer, "note", command.Note);
            WriteNullableGuid(writer, "institutionId", command.InstitutionId);
            WriteNullableGuid(writer, "accountId", command.AccountId);
            WriteNullableGuid(
                writer,
                "supersedesEvidenceId",
                command.SupersedesEvidenceId);

            writer.WriteStartArray("transactionIds");

            foreach (var transactionId in command.TransactionIds)
            {
                writer.WriteStringValue(transactionId.ToString("D"));
            }

            writer.WriteEndArray();
            writer.WritePropertyName("attachment");

            if (command.Attachment is { } attachment)
            {
                writer.WriteStartObject();
                writer.WriteString("fileName", attachment.FileName);
                writer.WriteString("mediaType", ToMediaTypeCode(attachment.MediaType));
                writer.WriteNumber("byteLength", attachment.ByteLength);
                writer.WriteString("sha256", attachment.Sha256);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNullValue();
            }

            writer.WriteEndObject();
            writer.Flush();
        }

        var hash = SHA256.HashData(buffer.WrittenSpan);

        return new CommandFingerprint(
            CurrentAlgorithmCode,
            Version1,
            Convert.ToHexString(hash).ToLowerInvariant());
    }

    private static string ToSourceKindCode(EvidenceSourceKind sourceKind)
        => sourceKind switch
        {
            EvidenceSourceKind.Statement => "STATEMENT",
            EvidenceSourceKind.Receipt => "RECEIPT",
            EvidenceSourceKind.PhysicalCount => "PHYSICAL_COUNT",
            EvidenceSourceKind.ContractOrCertificate => "CONTRACT_OR_CERTIFICATE",
            EvidenceSourceKind.Other => "OTHER",
            _ => throw new ArgumentOutOfRangeException(
                nameof(sourceKind),
                sourceKind,
                "Unsupported evidence source kind.")
        };

    private static string ToMediaTypeCode(EvidenceMediaType mediaType)
        => mediaType switch
        {
            EvidenceMediaType.Pdf => "PDF",
            EvidenceMediaType.Png => "PNG",
            EvidenceMediaType.Jpeg => "JPEG",
            EvidenceMediaType.PlainText => "PLAIN_TEXT",
            EvidenceMediaType.Csv => "CSV",
            _ => throw new ArgumentOutOfRangeException(
                nameof(mediaType),
                mediaType,
                "Unsupported evidence media type.")
        };

    private static void WriteNullableGuid(
        Utf8JsonWriter writer,
        string propertyName,
        Guid? value)
    {
        writer.WritePropertyName(propertyName);

        if (value is Guid guid)
        {
            writer.WriteStringValue(guid.ToString("D"));
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
