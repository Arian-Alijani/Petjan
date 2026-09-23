namespace Animora.Domain.Results;

/// <summary>
/// A stable, machine-readable failure identifier plus its category. The Desktop maps
/// <see cref="Code"/> to Persian text; no layer below the Desktop produces user-facing
/// localized strings.
/// </summary>
/// <param name="Code">SCREAMING_SNAKE identifier, declared once in <see cref="ErrorCodes"/>.</param>
/// <param name="Category">One of the category constants declared on this type.</param>
public readonly record struct ErrorCode(string Code, string Category)
{
    /// <summary>Input failed validation; the caller can correct and retry.</summary>
    public const string Validation = "Validation";

    /// <summary>The addressed entity does not exist (or is not visible to this tenant).</summary>
    public const string NotFound = "NotFound";

    /// <summary>Concurrent or divergent state prevented the operation.</summary>
    public const string Conflict = "Conflict";

    /// <summary>The actor is authenticated but not permitted.</summary>
    public const string Forbidden = "Forbidden";

    /// <summary>A domain invariant or data-integrity rule would be broken.</summary>
    public const string Integrity = "Integrity";

    /// <summary>Storage, network or another external dependency faulted.</summary>
    public const string Infrastructure = "Infrastructure";

    /// <summary>The tenant's licence/plan does not include the requested capability.</summary>
    public const string Entitlement = "Entitlement";

    /// <summary>The absence of an error — the value carried by a successful result.</summary>
    public static readonly ErrorCode None = new(string.Empty, string.Empty);

    /// <summary><see langword="true"/> when this instance carries no error.</summary>
    public bool IsNone => string.IsNullOrEmpty(Code);

    /// <inheritdoc />
    public override string ToString() => IsNone ? "<none>" : $"{Category}/{Code}";
}
