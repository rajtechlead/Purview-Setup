using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PurviewCopilot.Collaboration;

/// <summary>
/// SQLite cannot order or index a DateTimeOffset column. Every timestamp in the shared store is
/// UTC anyway, so it is persisted as a UTC DateTime and read back with an explicit UTC offset.
/// The same conversion works on SQL Server, where it stores datetime2.
/// </summary>
public sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, DateTime>
{
    public UtcDateTimeOffsetConverter()
        : base(
            value => value.UtcDateTime,
            value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)))
    {
    }
}

public sealed class NullableUtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset?, DateTime?>
{
    public NullableUtcDateTimeOffsetConverter()
        : base(
            value => value.HasValue ? value.Value.UtcDateTime : null,
            value => value.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
                : null)
    {
    }
}
