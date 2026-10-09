using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Animora.Persistence.PostgreSql.Configurations;

/// <summary>
/// Maps a Domain permission-code collection to a single PostgreSQL <c>text[]</c> column. The aggregates
/// expose an <c>IReadOnlySet&lt;string&gt;</c> over a private <c>HashSet&lt;string&gt;</c> backing field;
/// EF maps that <b>field</b> directly (the public read-only view is ignored) so the aggregates stay the
/// single source of truth and no second entity model / child table is needed. Codes are written sorted
/// for deterministic output.
/// </summary>
internal static class PermissionSetConversion
{
    private static readonly ValueConverter<HashSet<string>, string[]> Converter = new(
        set => set.OrderBy(static c => c, StringComparer.Ordinal).ToArray(),
        array => new HashSet<string>(array, StringComparer.Ordinal));

    private static readonly ValueComparer<HashSet<string>> Comparer = new(
        (a, b) => ReferenceEquals(a, b) || (a != null && b != null && a.SetEquals(b)),
        set => set.Aggregate(0, static (hash, code) => hash ^ StringComparer.Ordinal.GetHashCode(code)),
        set => new HashSet<string>(set, StringComparer.Ordinal));

    /// <summary>Maps the private set field <paramref name="fieldName"/> to column <paramref name="columnName"/> as <c>text[]</c>.</summary>
    public static PropertyBuilder<HashSet<string>> MapPermissionSet<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        string fieldName,
        string columnName)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        PropertyBuilder<HashSet<string>> property = builder.Property<HashSet<string>>(fieldName);
        property.HasColumnName(columnName);
        property.HasConversion(Converter, Comparer);
        property.HasColumnType("text[]");
        property.IsRequired();
        return property;
    }
}
