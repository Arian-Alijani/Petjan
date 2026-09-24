using System.Text;

namespace Animora.Application.Validation;

/// <summary>
/// One failed rule. <paramref name="Code"/> is a machine code (e.g. <c>REQUIRED</c>); the Desktop maps it
/// to Persian text — never a localized string here.
/// </summary>
public sealed record ValidationFailure(string Field, string Code)
{
    private const string Marker = "validation: ";

    /// <summary>
    /// Invariant <c>Result.Detail</c> encoding of a failure set: <c>validation: Field=CODE; Field2=CODE2</c>.
    /// <c>Result&lt;T&gt;</c> carries no extra payload, so <c>VALIDATION_FAILED</c> transports its
    /// failures in <c>Detail</c>; <see cref="ParseDetail"/> recovers them.
    /// </summary>
    public static string FormatDetail(IReadOnlyList<ValidationFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        var sb = new StringBuilder(Marker);
        for (var i = 0; i < failures.Count; i++)
        {
            if (i > 0) sb.Append("; ");
            sb.Append(failures[i].Field).Append('=').Append(failures[i].Code);
        }
        return sb.ToString();
    }

    /// <summary>Inverse of <see cref="FormatDetail"/>; tolerates a leading correlation prefix.</summary>
    public static IReadOnlyList<ValidationFailure> ParseDetail(string? detail)
    {
        if (string.IsNullOrEmpty(detail)) return [];
        var start = detail.IndexOf(Marker, StringComparison.Ordinal);
        if (start < 0) return [];
        var body = detail[(start + Marker.Length)..];
        var parts = body.Split("; ", StringSplitOptions.RemoveEmptyEntries);
        var list = new List<ValidationFailure>(parts.Length);
        foreach (var part in parts)
        {
            var eq = part.LastIndexOf('=');
            if (eq > 0) list.Add(new ValidationFailure(part[..eq], part[(eq + 1)..]));
        }
        return list;
    }
}
