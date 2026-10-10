using System.Linq.Expressions;
using Animora.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
namespace Animora.Persistence.PostgreSql.Conventions;

internal static class OwnedRows
{
    public static void Money<T>(EntityTypeBuilder<T> builder, Expression<Func<T, Money>> property) where T : class
    {
        var name = ((MemberExpression)property.Body).Member.Name;
        builder.ComplexProperty(property, money =>
        {
            money.Property(v => v.Minor).HasColumnName(name + "Minor").HasColumnType("bigint");
            money.Property(v => v.Currency).HasColumnName(name + "Currency").HasColumnType("character varying(3)").HasMaxLength(3);
            money.Ignore(v => v.Scale); money.Ignore(v => v.IsZero); money.Ignore(v => v.IsNegative);
        });
    }
    public static void Quantity<T>(EntityTypeBuilder<T> builder, Expression<Func<T, Quantity>> property) where T : class
    {
        var name = ((MemberExpression)property.Body).Member.Name;
        builder.ComplexProperty(property, quantity =>
        {
            quantity.Property(v => v.Value).HasColumnName(name + "Scaled").HasColumnType("bigint")
                .HasConversion(new ValueConverter<decimal, long>(v => Scale(v), v => v / 1000m));
            quantity.Property(v => v.UnitCode).HasColumnName(name + "Unit").HasColumnType("character varying(12)").HasMaxLength(12);
            quantity.Ignore(v => v.IsZero); quantity.Ignore(v => v.IsNegative);
        });
    }
    public static void Map<TRoot, TChild>(EntityTypeBuilder<TRoot> root, Expression<Func<TRoot, IEnumerable<TChild>?>> navigation)
        where TRoot : class where TChild : class
    {
        root.HasMany(navigation).WithOne().HasForeignKey("OwnerId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        root.Navigation(navigation).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }

    public static void Row<TChild>(EntityTypeBuilder<TChild> child, string table, Expression<Func<TChild, string>> key)
        where TChild : class
    {
        child.ToTable(table, "public");
        child.Property<string>("OwnerId").HasColumnType("character varying(26)").HasMaxLength(26).IsRequired();
        child.HasKey("OwnerId", ((MemberExpression)key.Body).Member.Name);
        child.Property(key).HasMaxLength(26).ValueGeneratedNever();
    }

    private static long Scale(decimal value)
    {
        var scaled = value * 1000m;
        if (scaled != decimal.Truncate(scaled)) throw new InvalidOperationException("Quantity exceeds 3 decimal places.");
        return checked((long)scaled);
    }
}
