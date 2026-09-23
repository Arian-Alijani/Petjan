using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Conventions;

internal static class JsonSets
{
    public static void Map<TEntity>(EntityTypeBuilder<TEntity> builder, string field, string column)
        where TEntity : class
    {
        var property = builder.Property<HashSet<string>>(field).HasColumnName(column).HasColumnType("TEXT")
            .HasConversion(value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                value => JsonSerializer.Deserialize<HashSet<string>>(value, (JsonSerializerOptions?)null) ?? new HashSet<string>());
        property.Metadata.SetValueComparer(new ValueComparer<HashSet<string>>(
            (left, right) => left != null && right != null && left.SetEquals(right),
            value => value.OrderBy(code => code, StringComparer.Ordinal)
                .Aggregate(0, (hash, code) => HashCode.Combine(hash, StringComparer.Ordinal.GetHashCode(code))),
            value => new HashSet<string>(value, StringComparer.Ordinal)));
    }
}
