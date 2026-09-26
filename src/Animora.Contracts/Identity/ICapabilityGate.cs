namespace Animora.Contracts.Identity;

/// <summary>
/// Synchronous capability decision state consumed by the shell (B04c). Mirrors the Application-layer
/// <c>CapabilityDecisionState</c> but lives in Contracts (which references no <c>Animora.*</c> project) so the
/// Desktop never sees the Application/Domain security types.
/// </summary>
public enum CapabilityState
{
    /// <summary>The capability may be exercised now.</summary>
    Allowed = 0,

    /// <summary>Not granted: missing/mismatched/tampered snapshot, unknown code, ordinary RBAC/policy denial.</summary>
    Denied = 1,

    /// <summary>The capability's own offline window (or the snapshot's absolute window) elapsed, or clock rollback.</summary>
    ExpiredOffline = 2,

    /// <summary>A critical capability: online-only (zero offline window).</summary>
    RequiresOnline = 3,
}

/// <summary>
/// Desktop-facing, synchronous authorization seam (B04c). The UI asks only "may this be shown / done now?" —
/// it never performs authorization itself (the role chip is presentation, not authority). Implemented by the
/// Client.Services <c>CapabilityGateService</c>, which caches a pre-verified per-snapshot view and re-validates
/// time-sensitive windows at use time. Every doubt (no warm-up, sign-out, locked/expired session, missing or
/// tampered snapshot, tenant/user/device/stamp mismatch) denies — fail closed.
/// </summary>
public interface ICapabilityGate
{
    /// <summary><see langword="true"/> only when <paramref name="permissionCode"/> is allowed right now.</summary>
    bool Can(string permissionCode);

    /// <summary><see langword="true"/> when at least one of <paramref name="codes"/> is allowed. False for null/empty.</summary>
    bool CanAny(params string[] codes);

    /// <summary>The decision state of <paramref name="permissionCode"/>; an unknown code is always <see cref="CapabilityState.Denied"/>.</summary>
    CapabilityState GetState(string permissionCode);

    /// <summary>
    /// Raised (background thread possible) whenever the gate's authority changes: a new snapshot, sign-in,
    /// sign-out, or revocation. UI subscribers marshal to their own thread.
    /// </summary>
    event EventHandler? CapabilitiesChanged;
}
