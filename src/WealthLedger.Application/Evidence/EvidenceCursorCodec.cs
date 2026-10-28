using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WealthLedger.Application.Evidence;

/// <summary>
/// Opaque, versioned evidence-list cursor bound to one household.
/// </summary>
internal static class EvidenceCursorCodec
{
    private const int CursorVersion = 1;
    private const string Resource = "EVIDENCE_LIST";
    private const int MaximumEncodedLength = 512;
    private const int MaximumDecodedLength = 384;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static string Encode(
        Guid householdId,
        EvidenceCursorKey key)
    {
        if (householdId == Guid.Empty)
        {
            throw new ArgumentException(
                "A non-empty household identity is required.",
                nameof(householdId));
        }

        ArgumentNullException.ThrowIfNull(key);

        var payload = new CursorPayload(
            CursorVersion,
            Resource,
            householdId.ToString("D"),
            key.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            key.CreatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            key.EvidenceId.ToString("D"));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);

        if (bytes.Length > MaximumDecodedLength)
        {
            throw new InvalidOperationException(
                "The evidence cursor payload exceeded its fixed bound.");
        }

        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    internal static EvidenceCursorKey? Decode(
        string? cursor,
        Guid expectedHouseholdId)
    {
        if (cursor is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(cursor)
            || cursor.Length > MaximumEncodedLength
            || cursor.Any(
                value => !(
                    value is >= 'A' and <= 'Z'
                    or >= 'a' and <= 'z'
                    or >= '0' and <= '9'
                    or '-'
                    or '_')))
        {
            throw InvalidCursor();
        }

        byte[] bytes;

        try
        {
            var base64 = cursor
                .Replace('-', '+')
                .Replace('_', '/');
            var remainder = base64.Length % 4;

            if (remainder == 1)
            {
                throw InvalidCursor();
            }

            if (remainder != 0)
            {
                base64 = base64.PadRight(
                    base64.Length + (4 - remainder),
                    '=');
            }

            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw InvalidCursor();
        }

        if (bytes.Length == 0 || bytes.Length > MaximumDecodedLength)
        {
            throw InvalidCursor();
        }

        CursorPayload payload;

        try
        {
            var json = StrictUtf8.GetString(bytes);
            payload = JsonSerializer.Deserialize<CursorPayload>(
                          json,
                          new JsonSerializerOptions
                          {
                              UnmappedMemberHandling =
                                  JsonUnmappedMemberHandling.Disallow
                          })
                      ?? throw InvalidCursor();
        }
        catch (Exception exception)
            when (exception is JsonException
                  or DecoderFallbackException)
        {
            throw InvalidCursor();
        }

        if (payload.Version != CursorVersion
            || !string.Equals(payload.Resource, Resource, StringComparison.Ordinal))
        {
            throw ScopeMismatch();
        }

        if (!Guid.TryParseExact(payload.HouseholdId, "D", out var householdId)
            || householdId == Guid.Empty)
        {
            throw InvalidCursor();
        }

        if (householdId != expectedHouseholdId)
        {
            throw ScopeMismatch();
        }

        if (!DateOnly.TryParseExact(
                payload.EffectiveDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var effectiveDate)
            || !DateTimeOffset.TryParseExact(
                payload.CreatedAtUtc,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var createdAtUtc)
            || createdAtUtc.Offset != TimeSpan.Zero
            || !Guid.TryParseExact(
                payload.EvidenceId,
                "D",
                out var evidenceId)
            || evidenceId == Guid.Empty)
        {
            throw InvalidCursor();
        }

        return new EvidenceCursorKey(
            effectiveDate,
            createdAtUtc,
            evidenceId);
    }

    private static EvidenceException InvalidCursor()
        => EvidenceException.Invalid(
            EvidenceErrorCodes.CursorInvalid,
            "The evidence cursor is malformed or unsupported.");

    private static EvidenceException ScopeMismatch()
        => EvidenceException.Invalid(
            EvidenceErrorCodes.CursorScopeMismatch,
            "The evidence cursor does not belong to this household.");

    private sealed record CursorPayload(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("r")] string Resource,
        [property: JsonPropertyName("h")] string HouseholdId,
        [property: JsonPropertyName("d")] string EffectiveDate,
        [property: JsonPropertyName("c")] string CreatedAtUtc,
        [property: JsonPropertyName("i")] string EvidenceId);
}
