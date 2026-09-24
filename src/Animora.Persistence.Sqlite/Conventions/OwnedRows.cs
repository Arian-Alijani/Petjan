using System.Linq.Expressions;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Conventions;

/// <summary>
/// Maps an aggregate's child rows to a real relational table keyed by (OwnerId, child key). Rows are
/// dependent entities (EF Core 10 owned types cannot carry complex Money/Quantity columns) reachable only
/// through the root's field-backed, auto-included navigation. Values are declared explicitly; no reflection.
/// </summary>
internal static class OwnedRows
{
    public const string OwnerKey = "OwnerId";

    /// <summary>Root side: required cascade relationship, field access, children loaded with the aggregate.</summary>
    public static void Map<TRoot, TChild>(EntityTypeBuilder<TRoot> root, Expression<Func<TRoot, IEnumerable<TChild>?>> navigation)
        where TRoot : class where TChild : class
    {
        root.HasMany(navigation).WithOne().HasForeignKey(OwnerKey).IsRequired().OnDelete(DeleteBehavior.Cascade);
        root.Navigation(navigation).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }

    /// <summary>Child side: table, stable composite key and explicit scalar conventions.</summary>
    public static void Row<TChild>(EntityTypeBuilder<TChild> child, string table, Expression<Func<TChild, string>> key)
        where TChild : class
    {
        child.ToTable(table);
        child.Property<string>(OwnerKey).HasColumnType("TEXT").HasMaxLength(FieldLengths.Id).IsRequired();
        var keyName = ((MemberExpression)key.Body).Member.Name;
        child.HasKey(OwnerKey, keyName);
        child.Property(key).ValueGeneratedNever();
        ApplyOwnedScalarConventions(child);
    }

    public static void Money<TChild>(EntityTypeBuilder<TChild> child, Expression<Func<TChild, Money>> property) where TChild : class
    {
        var name = ((MemberExpression)property.Body).Member.Name;
        child.ComplexProperty(property, value => ConfigureMoney(value, name));
    }

    public static void Quantity<TChild>(EntityTypeBuilder<TChild> child, Expression<Func<TChild, Quantity>> property, bool summable)
        where TChild : class
    {
        var name = ((MemberExpression)property.Body).Member.Name;
        child.ComplexProperty(property, value => ConfigureQuantity(value, name, summable));
    }

    public static void ConfigureMoney(ComplexPropertyBuilder<Money> value, string name)
    {
        value.Property(v => v.Minor).HasColumnName(name + "Minor").HasColumnType("INTEGER");
        value.Property(v => v.Currency).HasColumnName(name + "Currency").HasColumnType("TEXT").HasMaxLength(FieldLengths.Currency);
        value.Ignore(v => v.Scale);
        value.Ignore(v => v.IsZero);
        value.Ignore(v => v.IsNegative);
    }

    /// <summary>Summable quantities use exact INTEGER ×1000; others keep invariant decimal TEXT.</summary>
    public static void ConfigureQuantity(ComplexPropertyBuilder<Quantity> value, string name, bool summable)
    {
        if (summable)
            value.Property(v => v.Value).HasColumnName(name + "Scaled").HasConversion(new FixedScaleQuantityConverter()).HasColumnType("INTEGER");
        else
            value.Property(v => v.Value).HasColumnName(name + "Value").HasConversion(new DecimalStringConverter()).HasColumnType("TEXT");
        value.Property(v => v.UnitCode).HasColumnName(name + "Unit").HasColumnType("TEXT").HasMaxLength(FieldLengths.UnitCode);
        value.Ignore(v => v.IsZero);
        value.Ignore(v => v.IsNegative);
    }

    /// <summary>UTC ticks, bounded TEXT and int enums for the row's own scalar columns (EF metadata, no CLR reflection).</summary>
    private static void ApplyOwnedScalarConventions<TChild>(EntityTypeBuilder<TChild> child) where TChild : class
    {
        foreach (var property in child.Metadata.GetDeclaredProperties().ToArray())
        {
            if (property.Name == OwnerKey) continue;
            ModelConventions.ApplyScalar(child.Property(property.ClrType, property.Name), property.ClrType, property.Name, property.IsNullable);
        }
    }
}
