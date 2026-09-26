using Animora.Domain.Identity;
using Animora.Domain.Results;

namespace Animora.Application.Security;

/// <summary>Stable offline-capability decision state consumed by the B04c gate.</summary>
public enum CapabilityDecisionState
{
    /// <summary>The capability may be exercised now.</summary>
    Allowed = 0,

    /// <summary>Not granted (missing/mismatched/tampered/malformed snapshot, unknown code, revoked write).</summary>
    Denied = 1,

    /// <summary>The capability's own window or the snapshot's absolute window elapsed (or clock rollback).</summary>
    ExpiredOffline = 2,

    /// <summary>Critical-risk capability: online-only (zero offline window).</summary>
    RequiresOnline = 3,
}

/// <summary>
/// Outcome of <see cref="CapabilityEvaluator.Evaluate"/>. <see cref="Result"/> is Ok only for
/// <see cref="CapabilityDecisionState.Allowed"/>; <see cref="DiscardSnapshot"/> tells the caller to drop its cached
/// row (tampered or structurally unusable) and never re-sign it.
/// </summary>
public sealed record CapabilityEvaluation(CapabilityDecisionState State, Result Result, bool DiscardSnapshot)
{
    public bool IsAllowed => State == CapabilityDecisionState.Allowed;

    internal static CapabilityEvaluation Allow() => new(CapabilityDecisionState.Allowed, Result.Ok(), false);

    internal static CapabilityEvaluation Deny(ErrorCode code, string detail, bool discard = false) =>
        new(CapabilityDecisionState.Denied, Result.Fail(code, detail), discard);

    internal static CapabilityEvaluation Expired(string detail) =>
        new(CapabilityDecisionState.ExpiredOffline, Result.Fail(ErrorCodes.OfflineCapabilityExpired, detail), false);

    internal static CapabilityEvaluation Online(string detail) =>
        new(CapabilityDecisionState.RequiresOnline, Result.Fail(ErrorCodes.OfflineCapabilityExpired, detail), false);
}

/// <summary>
/// Deterministic, side-effect-free offline capability evaluation (B04b, A-RULE 029). Separate from — and never a
/// replacement for — the B04a <see cref="IPermissionEvaluator"/>. Every doubt denies (CONVENTIONS §7).
/// </summary>
/// <remarks>
/// Order: missing → binding (tenant/user/device/stamp) → payload shape → signature (tampered ⇒ discard) → unknown or
/// absent code → revocation (reads survive only on a correctly signed row; writes never) → Critical ⇒ online-only →
/// clock rollback → per-code and absolute expiry at <c>max(nowUtc, highWaterUtc)</c> (equality is expired).
/// The high-water mark is only read, never lowered or advanced here.
/// </remarks>
public sealed class CapabilityEvaluator(ICapabilitySnapshotSigner signer)
{
    /// <summary>Tolerated wall-clock skew before an issuance "in the future" is treated as a clock rollback.</summary>
    public static readonly TimeSpan RollbackTolerance = TimeSpan.FromMinutes(5);

    public CapabilityEvaluation Evaluate(StoredCapabilitySnapshot? snapshot, string code, string tenantId,
        string userId, string deviceId, string currentSecurityStamp, DateTimeOffset nowUtc, DateTimeOffset highWaterUtc)
    {
        if (snapshot is null)
            return CapabilityEvaluation.Deny(ErrorCodes.CapabilitySnapshotMissing, "no capability snapshot");

        if (!Same(snapshot.TenantId, tenantId) || !Same(snapshot.UserId, userId) || !Same(snapshot.DeviceId, deviceId))
            return CapabilityEvaluation.Deny(ErrorCodes.CapabilityNotGranted, "snapshot bound to another tenant/user/device");
        if (!Same(snapshot.SecurityStamp, currentSecurityStamp))
            return CapabilityEvaluation.Deny(ErrorCodes.CapabilityNotGranted, "security stamp changed");

        var parsed = CapabilitySnapshotCodec.Parse(snapshot.PermissionsJson, snapshot.PerCapabilityExpiryJson);
        if (parsed.IsFailure)
            return CapabilityEvaluation.Deny(ErrorCodes.CapabilityNotGranted, "malformed snapshot payload", discard: true);

        var verified = signer.Verify(snapshot);
        if (verified.IsFailure)
        {
            // Only a definite mismatch discards the row; a keystore outage denies without destroying it.
            return verified.Error == ErrorCodes.CapabilitySnapshotTampered
                ? CapabilityEvaluation.Deny(ErrorCodes.CapabilitySnapshotTampered, "signature mismatch", discard: true)
                : CapabilityEvaluation.Deny(verified.Error, verified.Detail ?? "signature could not be verified");
        }

        if (string.IsNullOrWhiteSpace(code) || !PermissionCodes.Exists(code))
            return CapabilityEvaluation.Deny(ErrorCodes.CapabilityNotGranted, "unknown permission code");
        var payload = parsed.Value!;
        if (!payload.TryGetExpiry(code, out var codeExpiry))
            return CapabilityEvaluation.Deny(ErrorCodes.CapabilityNotGranted, "capability not in snapshot");

        var risk = PermissionCodes.RiskOf(code);
        if (snapshot.RevokedAtUtc is not null && risk != RiskLevel.Low)
            return CapabilityEvaluation.Deny(ErrorCodes.CapabilityNotGranted, "snapshot revoked");

        if (risk == RiskLevel.Critical)
            return CapabilityEvaluation.Online("critical capability is online-only");

        // Never extend a lifetime: the effective instant is the later of wall clock and high-water.
        var effective = nowUtc >= highWaterUtc ? nowUtc : highWaterUtc;
        if (snapshot.IssuedAtUtc > effective + RollbackTolerance)
            return CapabilityEvaluation.Expired("clock rollback detected");
        // Defence in depth: a window longer than the authoritative policy is never honoured.
        if (codeExpiry > snapshot.IssuedAtUtc + OfflineCapabilityPolicy.WindowForCode(code))
            return CapabilityEvaluation.Deny(ErrorCodes.CapabilityNotGranted, "capability window exceeds policy");
        if (snapshot.AbsoluteExpiryUtc <= effective)
            return CapabilityEvaluation.Expired("snapshot expired");
        if (codeExpiry <= effective)
            return CapabilityEvaluation.Expired("capability expired");

        return CapabilityEvaluation.Allow();
    }

    private static bool Same(string? stored, string? expected) =>
        !string.IsNullOrEmpty(stored) && string.Equals(stored, expected, StringComparison.Ordinal);
}
