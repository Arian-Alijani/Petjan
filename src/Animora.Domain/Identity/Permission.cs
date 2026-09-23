namespace Animora.Domain.Identity;

/// <summary>
/// How damaging a permission is if it is exercised wrongly. Drives the offline window
/// (<see cref="OfflineCapabilityPolicy"/>) and the audit/approval expectations of later phases.
/// </summary>
public enum RiskLevel
{
    /// <summary>Read-only; no state change.</summary>
    Low = 0,

    /// <summary>Ordinary operational write.</summary>
    Normal = 1,

    /// <summary>Financial or stock effect that is correctable but visible.</summary>
    High = 2,

    /// <summary>Irreversible, money-reversing or security-altering; never allowed offline.</summary>
    Critical = 3,
}

/// <summary>
/// One grantable authorization capability, keyed by its stable <see cref="Code"/>.
/// Codes are declared once in <see cref="PermissionCodes"/> and never renamed.
/// </summary>
public sealed class Permission
{
    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private Permission()
    {
    }

    /// <summary>Primary key, e.g. <c>"invoice.post"</c>. A member of <see cref="PermissionCodes.All"/>.</summary>
    public string Code { get; private init; } = string.Empty;

    /// <summary>Grouping key for the settings UI, e.g. <c>"sales"</c> — the code's first segment.</summary>
    public string Category { get; private init; } = string.Empty;

    /// <summary>Risk classification of this capability.</summary>
    public RiskLevel Risk { get; private init; }

    /// <summary>Creates a permission; <paramref name="category"/> defaults to the code's first segment.</summary>
    public static Permission Create(string code, RiskLevel risk, string? category = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return new Permission
        {
            Code = code,
            Category = category ?? CategoryOf(code),
            Risk = risk,
        };
    }

    /// <summary>The leading segment of a dotted permission code.</summary>
    public static string CategoryOf(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        var dot = code.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? code : code[..dot];
    }
}
