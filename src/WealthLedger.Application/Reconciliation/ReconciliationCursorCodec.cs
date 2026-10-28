using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WealthLedger.Application.Reconciliation;

/// <summary>
/// Opaque, versioned run-list cursor bound to one household and to the
/// optional evidence filter it was issued for.
/// </summary>
internal static class ReconciliationCursorCodec
{
    private const int CursorVersion = 1;
    private const string Resource = "RECONCILIATION_RUN_LIST";
    private const int MaximumEncodedLength = 512;
    private const int MaximumDecodedLength = 384;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static string Encode(
        Guid householdId,
        Guid? evidenceId,
        ReconciliationRunCursorKey key)
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
            evidenceId?.ToString("D") ?? string.Empty,
            key.RunAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            key.ReconciliationRunId.ToString("D"));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);

        if (bytes.Length > MaximumDecodedLength)
        {
            throw new InvalidOperationException(
                "The reconciliation cursor payload exceeded its fixed bound.");
        }

        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    internal static ReconciliationRunCursorKey? Decode(
        string? cursor,
        Guid expectedHouseholdId,
        Guid? expectedEvidenceId)
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
            || householdId == Guid.Empty
            || payload.EvidenceId is null)
        {
            throw InvalidCursor();
        }

        Guid? evidenceId = null;

        if (payload.EvidenceId.Length > 0)
        {
            if (!Guid.TryParseExact(payload.EvidenceId, "D", out var parsedEvidenceId)
                || parsedEvidenceId == Guid.Empty)
            {
                throw InvalidCursor();
            }

            evidenceId = parsedEvidenceId;
        }

        if (householdId != expectedHouseholdId
            || evidenceId != expectedEvidenceId)
        {
            throw ScopeMismatch();
        }

        if (!DateTimeOffset.TryParseExact(
                payload.RunAtUtc,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var runAtUtc)
            || runAtUtc.Offset != TimeSpan.Zero
            || !Guid.TryParseExact(
                payload.RunId,
                "D",
                out var runId)
            || runId == Guid.Empty)
        {
            throw InvalidCursor();
        }

        return new ReconciliationRunCursorKey(runAtUtc, runId);
    }

    private static ReconciliationException InvalidCursor()
        => ReconciliationException.Invalid(
            ReconciliationErrorCodes.CursorInvalid,
            "The reconciliation cursor is malformed or unsupported.");

    private static ReconciliationException ScopeMismatch()
        => ReconciliationException.Invalid(
            ReconciliationErrorCodes.CursorScopeMismatch,
            "The reconciliation cursor does not belong to this household and filter.");

    private sealed record CursorPayload(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("r")] string Resource,
        [property: JsonPropertyName("h")] string HouseholdId,
        [property: JsonPropertyName("e")] string EvidenceId,
        [property: JsonPropertyName("c")] string RunAtUtc,
        [property: JsonPropertyName("i")] string RunId);
}
