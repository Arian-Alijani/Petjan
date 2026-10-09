using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Animora.Persistence.PostgreSql.Concurrency;

/// <summary>
/// Maps the domain's opaque <c>string? Version</c> to the PostgreSQL <c>uuid</c> column.
/// The column is NOT NULL and always written by the <c>animora_set_version()</c> trigger,
/// so <see cref="Guid.Empty"/> is never persisted for a real row (a <c>null</c> model value
/// only exists before the first save). Canonical wire form is lowercase hyphenated.
/// </summary>
public sealed class StringGuidConverter : ValueConverter<string?, Guid>
{
    /// <summary>Creates the converter.</summary>
    public StringGuidConverter()
        : base(
            s => s == null ? Guid.Empty : Guid.Parse(s),
            g => g.ToString("D"))
    {
    }
}
