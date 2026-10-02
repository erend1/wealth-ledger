using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WealthLedger.Application.LedgerSearch;

internal static class LedgerSearchCursorCodec
{
    private const int CursorVersion = 1;
    private const string Resource = "LEDGER_SEARCH";
    private const int MaximumEncodedLength = 1_024;
    private const int MaximumDecodedLength = 768;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static string Encode(
        Guid householdId,
        string filterFingerprint,
        LedgerSearchCursorKey key)
    {
        ValidateHouseholdAndFingerprint(householdId, filterFingerprint);
        ArgumentNullException.ThrowIfNull(key);

        var payload = new CursorPayload(
            CursorVersion,
            Resource,
            householdId.ToString("D"),
            filterFingerprint,
            key.ExecutionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            key.PostedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            key.TransactionId.ToString("D"));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);

        if (bytes.Length > MaximumDecodedLength)
        {
            throw new InvalidOperationException(
                "The ledger-search cursor payload exceeded its fixed bound.");
        }

        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    internal static LedgerSearchCursorKey? Decode(
        string? cursor,
        Guid expectedHouseholdId,
        string expectedFilterFingerprint)
    {
        ValidateHouseholdAndFingerprint(
            expectedHouseholdId,
            expectedFilterFingerprint);

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
            || !IsFingerprint(payload.FilterFingerprint))
        {
            throw InvalidCursor();
        }

        if (householdId != expectedHouseholdId
            || !string.Equals(
                payload.FilterFingerprint,
                expectedFilterFingerprint,
                StringComparison.Ordinal))
        {
            throw ScopeMismatch();
        }

        if (!DateOnly.TryParseExact(
                payload.ExecutionDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var executionDate)
            || !DateTimeOffset.TryParseExact(
                payload.PostedAtUtc,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var postedAtUtc)
            || postedAtUtc.Offset != TimeSpan.Zero
            || !Guid.TryParseExact(
                payload.TransactionId,
                "D",
                out var transactionId)
            || transactionId == Guid.Empty)
        {
            throw InvalidCursor();
        }

        return new LedgerSearchCursorKey(
            executionDate,
            postedAtUtc,
            transactionId);
    }

    private static void ValidateHouseholdAndFingerprint(
        Guid householdId,
        string filterFingerprint)
    {
        if (householdId == Guid.Empty)
        {
            throw new ArgumentException(
                "A non-empty household identity is required.",
                nameof(householdId));
        }

        if (!IsFingerprint(filterFingerprint))
        {
            throw new ArgumentException(
                "A valid filter fingerprint is required.",
                nameof(filterFingerprint));
        }
    }

    private static bool IsFingerprint(string? value)
        => value is { Length: 64 }
           && value.All(
               character => character is >= '0' and <= '9'
                   or >= 'a' and <= 'f');

    private static LedgerSearchRequestException InvalidCursor()
        => new(
            LedgerSearchRequestException.CursorInvalidCode,
            "The ledger-search cursor is malformed or unsupported.");

    private static LedgerSearchRequestException ScopeMismatch()
        => new(
            LedgerSearchRequestException.CursorScopeMismatchCode,
            "The ledger-search cursor does not belong to these filters or household.");

    private sealed record CursorPayload(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("r")] string Resource,
        [property: JsonPropertyName("h")] string HouseholdId,
        [property: JsonPropertyName("f")] string FilterFingerprint,
        [property: JsonPropertyName("d")] string ExecutionDate,
        [property: JsonPropertyName("p")] string PostedAtUtc,
        [property: JsonPropertyName("i")] string TransactionId);
}
