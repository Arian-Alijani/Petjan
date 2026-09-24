namespace Animora.Contracts.Wire.Commands;

/// <summary>
/// The machine-readable half of the sync-failure UX contract (A-RULE 024): every non-settled command must
/// answer whether it moved money. <see cref="Unknown"/> is a legitimate, explicit answer — never inferred.
/// </summary>
public enum FinancialEffect
{
    /// <summary>The command carries no financial effect at all.</summary>
    None,

    /// <summary>The command applied a financial effect.</summary>
    Applied,

    /// <summary>The command did not apply any financial effect.</summary>
    NotApplied,

    /// <summary>The financial effect could not be determined — surfaced as such, never guessed.</summary>
    Unknown,
}
