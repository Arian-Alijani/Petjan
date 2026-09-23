namespace Animora.Domain.Identity;

/// <summary>
/// The authoritative offline window per <see cref="RiskLevel"/>: how long a capability stays
/// usable on a device that has not reached the server (<c>ARCHITECTURE.md</c> §10.4, A-RULE 029).
/// </summary>
/// <remarks>
/// These numbers are a product decision, not a tuning knob: no phase changes them without an ADR.
/// <see cref="RiskLevel.Critical"/> is zero — an irreversible or security-altering action is
/// never performed on cached authority.
/// </remarks>
public static class OfflineCapabilityPolicy
{
    /// <summary>Read-only capabilities survive 30 days offline.</summary>
    public static readonly TimeSpan LowWindow = TimeSpan.FromDays(30);

    /// <summary>Ordinary writes survive 14 days offline.</summary>
    public static readonly TimeSpan NormalWindow = TimeSpan.FromDays(14);

    /// <summary>Financial/stock-effective writes survive 72 hours offline.</summary>
    public static readonly TimeSpan HighWindow = TimeSpan.FromHours(72);

    /// <summary>Critical capabilities are never available offline.</summary>
    public static readonly TimeSpan CriticalWindow = TimeSpan.Zero;

    /// <summary>The offline window for <paramref name="risk"/>; an unknown level fails closed at zero.</summary>
    public static TimeSpan WindowFor(RiskLevel risk) => risk switch
    {
        RiskLevel.Low => LowWindow,
        RiskLevel.Normal => NormalWindow,
        RiskLevel.High => HighWindow,
        RiskLevel.Critical => CriticalWindow,
        _ => TimeSpan.Zero,
    };

    /// <summary>The offline window of a permission code, resolved through <see cref="PermissionCodes.RiskOf"/>.</summary>
    public static TimeSpan WindowForCode(string code) => WindowFor(PermissionCodes.RiskOf(code));
}
