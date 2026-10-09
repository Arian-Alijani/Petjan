using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Conventions;

/// <summary>Persist immutable aggregate child records as one JSON column, never as separate rows.</summary>
internal static class JsonFields
{
    // Cached once instead of resolving the default options on every (de)serialize. The JSON value comparer
    // runs on the hot write path (every DetectChanges/SaveChanges for each entity that owns a JSON field),
    // so avoiding repeated option resolution and short-circuiting unchanged references matters (PERFORMANCE.md).
    private static readonly JsonSerializerOptions Options = new();

    public static void Map<TEntity, TItem>(EntityTypeBuilder<TEntity> builder, string field, string column)
        where TEntity : class
    {
        var property = builder.Property<List<TItem>>(field).HasColumnName(column).HasColumnType("jsonb")
            .HasConversion(value => JsonSerializer.Serialize(value, Options),
                value => JsonSerializer.Deserialize<List<TItem>>(value, Options) ?? new List<TItem>());
        property.Metadata.SetValueComparer(new ValueComparer<List<TItem>>(
            (left, right) => ReferenceEquals(left, right)
                || (left != null && right != null
                    && JsonSerializer.Serialize(left, Options) == JsonSerializer.Serialize(right, Options)),
            value => value == null ? 0 : StringComparer.Ordinal.GetHashCode(JsonSerializer.Serialize(value, Options)),
            value => JsonSerializer.Deserialize<List<TItem>>(JsonSerializer.Serialize(value, Options), Options)!));
    }
}
