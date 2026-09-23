using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Conventions;

/// <summary>Persist immutable aggregate child records as one JSON column, never as separate rows.</summary>
internal static class JsonFields
{
    public static void Map<TEntity, TItem>(EntityTypeBuilder<TEntity> builder, string field, string column)
        where TEntity : class
    {
        var property = builder.Property<List<TItem>>(field).HasColumnName(column).HasColumnType("TEXT")
            .HasConversion(value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                value => JsonSerializer.Deserialize<List<TItem>>(value, (JsonSerializerOptions?)null) ?? new List<TItem>());
        property.Metadata.SetValueComparer(new ValueComparer<List<TItem>>(
            (left, right) => JsonSerializer.Serialize(left, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(right, (JsonSerializerOptions?)null),
            value => StringComparer.Ordinal.GetHashCode(JsonSerializer.Serialize(value, (JsonSerializerOptions?)null)),
            value => JsonSerializer.Deserialize<List<TItem>>(JsonSerializer.Serialize(value, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!));
    }
}
