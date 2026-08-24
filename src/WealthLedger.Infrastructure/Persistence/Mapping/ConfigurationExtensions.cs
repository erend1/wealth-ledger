using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace WealthLedger.Infrastructure.Persistence.Mapping;

internal static class ConfigurationExtensions
{
    internal static PropertyBuilder<Guid> HasUuidTextConversion(
        this PropertyBuilder<Guid> builder)
        => builder
            .HasColumnType("TEXT")
            .HasConversion(SqliteValueConverters.GuidToText);

    internal static PropertyBuilder<Guid?> HasUuidTextConversion(
        this PropertyBuilder<Guid?> builder)
        => builder
            .HasColumnType("TEXT")
            .HasConversion(SqliteValueConverters.NullableGuidToText);

    internal static PropertyBuilder<DateOnly> HasDateTextConversion(
        this PropertyBuilder<DateOnly> builder)
        => builder
            .HasColumnType("TEXT")
            .HasConversion(SqliteValueConverters.DateOnlyToText);

    internal static PropertyBuilder<DateOnly?> HasDateTextConversion(
        this PropertyBuilder<DateOnly?> builder)
        => builder
            .HasColumnType("TEXT")
            .HasConversion(SqliteValueConverters.NullableDateOnlyToText);

    internal static PropertyBuilder<DateTime> HasUtcTimestampTextConversion(
        this PropertyBuilder<DateTime> builder)
        => builder
            .HasColumnType("TEXT")
            .HasConversion(SqliteValueConverters.UtcDateTimeToText);

    internal static PropertyBuilder<DateTime?> HasUtcTimestampTextConversion(
        this PropertyBuilder<DateTime?> builder)
        => builder
            .HasColumnType("TEXT")
            .HasConversion(SqliteValueConverters.NullableUtcDateTimeToText);
}
