using System.Linq.Expressions;
using System.Text.Json;
using Animora.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Conventions;

internal static class OwnedRows
{
    public static void Map<TRoot, TChild>(EntityTypeBuilder<TRoot> root,
        Expression<Func<TRoot, IEnumerable<TChild>>> navigation, string field, string table, string key)
        where TRoot : class where TChild : class
    {
        root.Navigation(((MemberExpression)navigation.Body).Member.Name).UsePropertyAccessMode(PropertyAccessMode.Field);
        root.OwnsMany(navigation, owned =>
        {
            owned.ToTable(table);
            owned.WithOwner().HasForeignKey("OwnerId");
            owned.HasKey("OwnerId", key);
            foreach (var property in typeof(TChild).GetProperties())
            {
                if (property.PropertyType == typeof(Money))
                    owned.Property<Money>(property.Name).HasConversion(
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                        v => JsonSerializer.Deserialize<Money>(v, (JsonSerializerOptions?)null));
                else if (property.PropertyType == typeof(Quantity))
                    owned.Property<Quantity>(property.Name).HasConversion(
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                        v => JsonSerializer.Deserialize<Quantity>(v, (JsonSerializerOptions?)null));
            }
        });
    }
}
