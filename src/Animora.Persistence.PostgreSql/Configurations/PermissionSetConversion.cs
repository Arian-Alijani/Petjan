using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Animora.Persistence.PostgreSql.Configurations;

/// <summary>
/// Maps a Domain <c>IReadOnlySet&lt;string&gt;</c> permission-code collection (exposed over a private
/// <c>HashSet&lt;string&gt;</c> backing field) to a single PostgreSQL <c>text[]</c> column. EF cannot map a
/// set of plain strings to a child table without a second entity model, and the aggregates must stay
/// the single source of truth, so the codes are stored inline as a sorted array (deterministic output).
/// </summary>
internal static class PermissionSetConversion
{
    private static readonly ValueConverter<IReadOnlySet<string>, string[]> Converter = new(
        set => set.OrderBy(static c => c, StringComparer.Ordinal).ToArray(),
        array => new HashSet<string>(array, StringComparer.Ordinal));

    private static readonly ValueComparer<IReadOnlySet<string>> Comparer = new(
        (a, b) => ReferenceEquals(a, b) || (a != null && b != null && a.SetEquals(b)),
        set => set.Aggregate(0, static (hash, code) => hash ^ StringComparer.Ordinal.GetHashCode(code)),
        set => new HashSet<string>(set, StringComparer.Ordinal));

    /// <summary>Applies the set↔<c>text[]</c> mapping to <paramref name="property"/>.</summary>
    public static PropertyBuilder<IReadOnlySet<string>> AsTextArray(this PropertyBuilder<IReadOnlySet<string>> property)
    {
        ArgumentNullException.ThrowIfNull(property);

        property.HasConversion(Converter, Comparer);
        property.HasColumnType("text[]");
        property.IsRequired();
        return property;
    }
}
