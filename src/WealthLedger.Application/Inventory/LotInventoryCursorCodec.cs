using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WealthLedger.Application.Inventory;

internal static class LotInventoryCursorCodec
{
    private const int CursorVersion = 1;
    private const string Resource = "INVENTORY_LOTS";
    private const int MaximumEncodedLength = 1_024;
    private const int MaximumDecodedLength = 768;
    private const int MaximumAssetCodeLength = 64;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// Binds a cursor to every filter that changes the lot set, so a cursor
    /// cannot continue a different query.
    /// </summary>
    internal static string ComputeFingerprint(ListLotInventoryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var canonical = string.Join(
            '|',
            FormatGuid(query.PortfolioId),
            FormatGuid(query.AccountId),
            FormatGuid(query.InstitutionId),
            FormatGuid(query.AssetId),
            query.AsOf?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                ?? string.Empty,
            query.IncludeExhausted ? "1" : "0");

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    internal static string Encode(
        Guid householdId,
        string filterFingerprint,
        LotInventoryCursorKey key)
    {
        ValidateHouseholdAndFingerprint(householdId, filterFingerprint);
        ArgumentNullException.ThrowIfNull(key);

        if (!IsAssetCode(key.AssetCode))
        {
            throw new ArgumentException(
                "A valid asset code is required.",
                nameof(key));
        }

        var payload = new CursorPayload(
            CursorVersion,
            Resource,
            householdId.ToString("D"),
            filterFingerprint,
            key.AssetCode,
            key.AcquiredOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            key.AssetLotId.ToString("D"));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);

        if (bytes.Length > MaximumDecodedLength)
        {
            throw new InvalidOperationException(
                "The lot-inventory cursor payload exceeded its fixed bound.");
        }

        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    internal static LotInventoryCursorKey? Decode(
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

        DateOnly? acquiredOn = null;

        if (payload.AcquiredOn is not null)
        {
            if (!DateOnly.TryParseExact(
                    payload.AcquiredOn,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedAcquiredOn))
            {
                throw InvalidCursor();
            }

            acquiredOn = parsedAcquiredOn;
        }

        if (!IsAssetCode(payload.AssetCode)
            || !Guid.TryParseExact(
                payload.AssetLotId,
                "D",
                out var assetLotId)
            || assetLotId == Guid.Empty)
        {
            throw InvalidCursor();
        }

        return new LotInventoryCursorKey(
            payload.AssetCode!,
            acquiredOn,
            assetLotId);
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

    // Same alphabet and bound as the Domain asset code.
    private static bool IsAssetCode(string? value)
        => value is { Length: > 0 and <= MaximumAssetCodeLength }
           && value.All(
               character => character is >= 'A' and <= 'Z'
                   or >= '0' and <= '9'
                   or '_'
                   or '-');

    private static string FormatGuid(Guid? value)
        => value?.ToString("D") ?? string.Empty;

    private static InventoryRequestException InvalidCursor()
        => new(
            InventoryRequestException.CursorInvalidCode,
            "The lot-inventory cursor is malformed or unsupported.");

    private static InventoryRequestException ScopeMismatch()
        => new(
            InventoryRequestException.CursorScopeMismatchCode,
            "The lot-inventory cursor does not belong to these filters or household.");

    private sealed record CursorPayload(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("r")] string Resource,
        [property: JsonPropertyName("h")] string HouseholdId,
        [property: JsonPropertyName("f")] string FilterFingerprint,
        [property: JsonPropertyName("c")] string? AssetCode,
        [property: JsonPropertyName("a")] string? AcquiredOn,
        [property: JsonPropertyName("i")] string AssetLotId);
}
