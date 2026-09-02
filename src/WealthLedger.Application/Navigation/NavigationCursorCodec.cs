using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WealthLedger.Application.Navigation;

internal static class NavigationCursorResources
{
    internal const string Households = "HOUSEHOLDS";
    internal const string HouseholdMembers = "HOUSEHOLD_MEMBERS";
    internal const string Institutions = "INSTITUTIONS";
    internal const string Portfolios = "PORTFOLIOS";
    internal const string Accounts = "ACCOUNTS";
    internal const string Currencies = "CURRENCIES";
    internal const string Assets = "ASSETS";
    internal const string RecentLedgerTransactions =
        "RECENT_LEDGER_TRANSACTIONS";

    internal static bool IsKnown(string value)
        => value is Households
            or HouseholdMembers
            or Institutions
            or Portfolios
            or Accounts
            or Currencies
            or Assets
            or RecentLedgerTransactions;
}

internal static class NavigationCursorCodec
{
    private const int CursorVersion = 1;
    private const int MaximumEncodedLength = 1_024;
    private const int MaximumDecodedLength = 768;
    private const int RequiredPropertyCount = 7;

    private static readonly UTF8Encoding StrictUtf8 =
        new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

    internal static string EncodeCreatedAt(
        string resource,
        Guid? householdId,
        bool? includeInactive,
        NavigationCreatedAtKey key)
        => Encode(
            new CursorPayload(
                CursorVersion,
                resource,
                FormatGuid(householdId),
                includeInactive,
                FormatTimestamp(key.CreatedAtUtc),
                Code: null,
                FormatGuid(key.Id)));

    internal static string EncodeCode(
        string resource,
        Guid? householdId,
        bool? includeInactive,
        NavigationCodeKey key)
        => Encode(
            new CursorPayload(
                CursorVersion,
                resource,
                FormatGuid(householdId),
                includeInactive,
                Timestamp: null,
                key.Code,
                FormatGuid(key.Id)));

    internal static string EncodeCurrency(
        NavigationCurrencyKey key)
        => Encode(
            new CursorPayload(
                CursorVersion,
                NavigationCursorResources.Currencies,
                HouseholdId: null,
                IncludeInactive: null,
                Timestamp: null,
                key.Code,
                Id: null));

    internal static string EncodeRecentLedger(
        Guid householdId,
        RecentLedgerNavigationKey key)
        => Encode(
            new CursorPayload(
                CursorVersion,
                NavigationCursorResources.RecentLedgerTransactions,
                FormatGuid(householdId),
                IncludeInactive: null,
                FormatTimestamp(key.PostedAtUtc),
                Code: null,
                FormatGuid(key.TransactionId)));

    internal static NavigationCreatedAtKey? DecodeCreatedAt(
        string? cursor,
        string expectedResource,
        Guid? expectedHouseholdId,
        bool? expectedIncludeInactive)
    {
        if (cursor is null)
        {
            return null;
        }

        var payload = DecodeAndValidateScope(
            cursor,
            expectedResource,
            expectedHouseholdId,
            expectedIncludeInactive);
        EnsureNull(payload.Code);

        return new NavigationCreatedAtKey(
            ParseTimestamp(payload.Timestamp),
            ParseId(payload.Id));
    }

    internal static NavigationCodeKey? DecodeCode(
        string? cursor,
        string expectedResource,
        Guid? expectedHouseholdId,
        bool expectedIncludeInactive)
    {
        if (cursor is null)
        {
            return null;
        }

        var payload = DecodeAndValidateScope(
            cursor,
            expectedResource,
            expectedHouseholdId,
            expectedIncludeInactive);
        EnsureNull(payload.Timestamp);

        return new NavigationCodeKey(
            ParseCode(payload.Code, maximumLength: 64),
            ParseId(payload.Id));
    }

    internal static NavigationCurrencyKey? DecodeCurrency(string? cursor)
    {
        if (cursor is null)
        {
            return null;
        }

        var payload = DecodeAndValidateScope(
            cursor,
            NavigationCursorResources.Currencies,
            expectedHouseholdId: null,
            expectedIncludeInactive: null);
        EnsureNull(payload.Timestamp);
        EnsureNull(payload.Id);

        var code = ParseCode(payload.Code, maximumLength: 3);

        if (code.Length != 3 || code.Any(x => x is < 'A' or > 'Z'))
        {
            throw InvalidCursor();
        }

        return new NavigationCurrencyKey(code);
    }

    internal static RecentLedgerNavigationKey? DecodeRecentLedger(
        string? cursor,
        Guid householdId)
    {
        if (cursor is null)
        {
            return null;
        }

        var payload = DecodeAndValidateScope(
            cursor,
            NavigationCursorResources.RecentLedgerTransactions,
            householdId,
            expectedIncludeInactive: null);
        EnsureNull(payload.Code);

        return new RecentLedgerNavigationKey(
            ParseTimestamp(payload.Timestamp),
            ParseId(payload.Id));
    }

    private static string Encode(CursorPayload payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);

        if (bytes.Length > MaximumDecodedLength)
        {
            throw new InvalidOperationException(
                "The navigation cursor payload exceeded its fixed bound.");
        }

        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static CursorPayload DecodeAndValidateScope(
        string cursor,
        string expectedResource,
        Guid? expectedHouseholdId,
        bool? expectedIncludeInactive)
    {
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
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 2
                });

            ValidateFrozenShape(document.RootElement);
            payload = JsonSerializer.Deserialize<CursorPayload>(json)
                ?? throw InvalidCursor();
        }
        catch (Exception exception)
            when (exception is JsonException
                  or DecoderFallbackException
                  or InvalidOperationException)
        {
            throw InvalidCursor();
        }

        if (payload.Version != CursorVersion
            || string.IsNullOrEmpty(payload.Resource)
            || !NavigationCursorResources.IsKnown(payload.Resource))
        {
            throw InvalidCursor();
        }

        Guid? payloadHouseholdId = null;

        if (payload.HouseholdId is not null)
        {
            if (!Guid.TryParseExact(payload.HouseholdId, "D", out var parsed)
                || parsed == Guid.Empty)
            {
                throw InvalidCursor();
            }

            payloadHouseholdId = parsed;
        }

        if (!string.Equals(
                payload.Resource,
                expectedResource,
                StringComparison.Ordinal)
            || payloadHouseholdId != expectedHouseholdId
            || payload.IncludeInactive != expectedIncludeInactive)
        {
            throw ScopeMismatch();
        }

        return payload;
    }

    private static void ValidateFrozenShape(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw InvalidCursor();
        }

        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in root.EnumerateObject())
        {
            if (!names.Add(property.Name)
                || property.Name is not (
                    "v" or "r" or "h" or "f" or "t" or "c" or "i"))
            {
                throw InvalidCursor();
            }
        }

        if (names.Count != RequiredPropertyCount
            || !names.Contains("v")
            || !names.Contains("r")
            || !names.Contains("h")
            || !names.Contains("f")
            || !names.Contains("t")
            || !names.Contains("c")
            || !names.Contains("i"))
        {
            throw InvalidCursor();
        }

        EnsureJsonKind(root.GetProperty("v"), JsonValueKind.Number);
        EnsureJsonKind(root.GetProperty("r"), JsonValueKind.String);
        EnsureNullableString(root.GetProperty("h"));
        EnsureNullableBoolean(root.GetProperty("f"));
        EnsureNullableString(root.GetProperty("t"));
        EnsureNullableString(root.GetProperty("c"));
        EnsureNullableString(root.GetProperty("i"));
    }

    private static void EnsureJsonKind(
        JsonElement element,
        JsonValueKind expected)
    {
        if (element.ValueKind != expected)
        {
            throw InvalidCursor();
        }
    }

    private static void EnsureNullableString(JsonElement element)
    {
        if (element.ValueKind is not (
            JsonValueKind.String or JsonValueKind.Null))
        {
            throw InvalidCursor();
        }
    }

    private static void EnsureNullableBoolean(JsonElement element)
    {
        if (element.ValueKind is not (
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null))
        {
            throw InvalidCursor();
        }
    }

    private static DateTimeOffset ParseTimestamp(string? value)
    {
        if (value is null
            || !DateTimeOffset.TryParseExact(
                value,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var timestamp)
            || timestamp.Offset != TimeSpan.Zero)
        {
            throw InvalidCursor();
        }

        return timestamp;
    }

    private static Guid ParseId(string? value)
    {
        if (value is null
            || !Guid.TryParseExact(value, "D", out var id)
            || id == Guid.Empty)
        {
            throw InvalidCursor();
        }

        return id;
    }

    private static string ParseCode(string? value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > maximumLength
            || value.Any(
                character => character is not (
                    >= 'A' and <= 'Z'
                    or >= '0' and <= '9'
                    or '_'
                    or '-')))
        {
            throw InvalidCursor();
        }

        return value;
    }

    private static void EnsureNull(string? value)
    {
        if (value is not null)
        {
            throw InvalidCursor();
        }
    }

    private static string FormatTimestamp(DateTimeOffset value)
        => value
            .ToUniversalTime()
            .ToString("O", CultureInfo.InvariantCulture);

    private static string? FormatGuid(Guid? value)
        => value?.ToString("D", CultureInfo.InvariantCulture)
            .ToLowerInvariant();

    private static NavigationRequestException InvalidCursor()
        => new(
            NavigationRequestException.CursorInvalidCode,
            "The navigation cursor is malformed or unsupported.");

    private static NavigationRequestException ScopeMismatch()
        => new(
            NavigationRequestException.CursorScopeMismatchCode,
            "The navigation cursor does not match this resource scope or filter.");

    private sealed record CursorPayload(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("r")] string Resource,
        [property: JsonPropertyName("h")] string? HouseholdId,
        [property: JsonPropertyName("f")] bool? IncludeInactive,
        [property: JsonPropertyName("t")] string? Timestamp,
        [property: JsonPropertyName("c")] string? Code,
        [property: JsonPropertyName("i")] string? Id);
}
