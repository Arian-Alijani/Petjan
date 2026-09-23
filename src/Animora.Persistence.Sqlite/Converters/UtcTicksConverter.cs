using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Animora.Persistence.Sqlite.Converters;

public sealed class UtcTicksConverter() : ValueConverter<DateTimeOffset, long>(
    value => value.ToUniversalTime().UtcTicks,
    ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

public sealed class NullableUtcTicksConverter() : ValueConverter<DateTimeOffset?, long?>(
    value => value.HasValue ? value.Value.ToUniversalTime().UtcTicks : null,
    ticks => ticks.HasValue ? new DateTimeOffset(ticks.Value, TimeSpan.Zero) : null);
