using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace WealthLedger.Infrastructure.Persistence.Mapping;

internal static class SqliteValueConverters
{
    internal static readonly ValueConverter<Guid, string> GuidToText = new(
        value => FormatGuid(value),
        value => ParseGuid(value));

    internal static readonly ValueConverter<Guid?, string?> NullableGuidToText = new(
        value => value.HasValue ? FormatGuid(value.Value) : null,
        value => value == null ? null : ParseGuid(value));

    internal static readonly ValueConverter<DateOnly, string> DateOnlyToText = new(
        value => FormatDate(value),
        value => ParseDate(value));

    internal static readonly ValueConverter<DateOnly?, string?> NullableDateOnlyToText = new(
        value => value.HasValue ? FormatDate(value.Value) : null,
        value => value == null ? null : ParseDate(value));

    internal static readonly ValueConverter<DateTime, string> UtcDateTimeToText = new(
        value => FormatUtcTimestamp(value),
        value => ParseUtcTimestamp(value));

    internal static readonly ValueConverter<DateTime?, string?> NullableUtcDateTimeToText = new(
        value => value.HasValue ? FormatUtcTimestamp(value.Value) : null,
        value => value == null ? null : ParseUtcTimestamp(value));

    private static string FormatGuid(Guid value)
        => value.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant();

    private static Guid ParseGuid(string value)
        => Guid.ParseExact(value, "D");

    private static string FormatDate(DateOnly value)
        => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly ParseDate(string value)
        => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatUtcTimestamp(DateTime value)
        => NormalizeUtc(value).ToString("O", CultureInfo.InvariantCulture);

    private static DateTime ParseUtcTimestamp(string value)
        => NormalizeUtc(
            DateTime.Parse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind));

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
