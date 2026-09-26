using Animora.Application.Security;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>
/// B04c acceptance for the B04b <see cref="CapabilityEvaluator"/>: the authoritative offline windows, expiry at
/// equality, critical online-only, clock-rollback (high-water) defence, tamper discard, revocation, and the
/// missing-snapshot code. Durations come from <see cref="OfflineCapabilityPolicy"/> — never a fabricated value
/// (the B04 phase text's "High = 24h" is stale; the real High window is 72 hours).
/// </summary>
public sealed class CapabilityExpiryTests
{
    private const string Tenant = "T-CAP";
    private const string User = "U-1";
    private const string Device = "D-1";
    private const string Stamp = "STAMP-1";
    private static readonly DateTimeOffset Base = new(2026, 9, 26, 8, 0, 0, TimeSpan.Zero);

    private static readonly TestCapabilitySigner Signer = new();
    private static readonly CapabilityEvaluator Evaluator = new(Signer);

    private static StoredCapabilitySnapshot Signed(IEnumerable<string> codes, DateTimeOffset? issued = null,
        DateTimeOffset? revokedAt = null)
    {
        var at = issued ?? Base;
        var expiry = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var code in codes) expiry[code] = at + OfflineCapabilityPolicy.WindowForCode(code);
        var absolute = expiry.Count == 0 ? at : expiry.Values.Max();
        var unsigned = new StoredCapabilitySnapshot(
            "SNAP-1", Tenant, User, Device,
            CapabilitySnapshotCodec.WritePermissions(codes),
            at,
            CapabilitySnapshotCodec.WriteExpiry(expiry),
            absolute,
            SnapshotSignature: string.Empty,
            SecurityStamp: Stamp,
            RevokedAtUtc: revokedAt);
        return unsigned with { SnapshotSignature = Signer.Sign(unsigned).Value! };
    }

    private static CapabilityEvaluation Evaluate(StoredCapabilitySnapshot? snapshot, string code,
        DateTimeOffset now, DateTimeOffset? highWater = null) =>
        Evaluator.Evaluate(snapshot, code, Tenant, User, Device, Stamp, now, highWater ?? Base);

    [Fact]
    public void AuthoritativeWindows_AreThirtyDays_FourteenDays_SeventyTwoHours_AndOnlineOnly()
    {
        Assert.Equal(TimeSpan.FromDays(30), OfflineCapabilityPolicy.LowWindow);
        Assert.Equal(TimeSpan.FromDays(14), OfflineCapabilityPolicy.NormalWindow);
        Assert.Equal(TimeSpan.FromHours(72), OfflineCapabilityPolicy.HighWindow); // NOT 24h.
        Assert.Equal(TimeSpan.Zero, OfflineCapabilityPolicy.CriticalWindow);

        Assert.Equal(TimeSpan.FromDays(30), OfflineCapabilityPolicy.WindowForCode(PermissionCodes.PatientsRead));
        Assert.Equal(TimeSpan.FromDays(14), OfflineCapabilityPolicy.WindowForCode(PermissionCodes.PatientsWrite));
        Assert.Equal(TimeSpan.FromHours(72), OfflineCapabilityPolicy.WindowForCode(PermissionCodes.SalesPost));
        Assert.Equal(TimeSpan.Zero, OfflineCapabilityPolicy.WindowForCode(PermissionCodes.SalesVoid));
    }

    [Theory]
    [InlineData(PermissionCodes.PatientsRead, 30)] // Low — 30 days
    [InlineData(PermissionCodes.PatientsWrite, 14)] // Normal — 14 days
    public void ReadAndNormalWrite_ExpireExactlyAtTheirPolicyWindow(string code, int days)
    {
        var snapshot = Signed([code]);
        var window = TimeSpan.FromDays(days);

        Assert.Equal(CapabilityDecisionState.Allowed, Evaluate(snapshot, code, Base).State);
        Assert.Equal(CapabilityDecisionState.Allowed, Evaluate(snapshot, code, Base + window - TimeSpan.FromSeconds(1)).State);
        // Equality is already expired.
        var atEquality = Evaluate(snapshot, code, Base + window);
        Assert.Equal(CapabilityDecisionState.ExpiredOffline, atEquality.State);
        Assert.Equal(ErrorCodes.OfflineCapabilityExpired, atEquality.Result.Error);
    }

    [Fact]
    public void HighRisk_UsesTheSeventyTwoHourWindow()
    {
        var snapshot = Signed([PermissionCodes.SalesPost]);
        var window = OfflineCapabilityPolicy.HighWindow;

        Assert.Equal(CapabilityDecisionState.Allowed,
            Evaluate(snapshot, PermissionCodes.SalesPost, Base + window - TimeSpan.FromMinutes(1)).State);
        Assert.Equal(CapabilityDecisionState.ExpiredOffline,
            Evaluate(snapshot, PermissionCodes.SalesPost, Base + window).State);
    }

    [Fact]
    public void CriticalCapability_RequiresOnline()
    {
        var snapshot = Signed([PermissionCodes.SalesVoid]);

        var evaluation = Evaluate(snapshot, PermissionCodes.SalesVoid, Base);

        Assert.Equal(CapabilityDecisionState.RequiresOnline, evaluation.State);
        Assert.False(evaluation.IsAllowed);
    }

    [Fact]
    public void ClockRollback_WithLaterHighWater_DoesNotReviveAnExpiredCapability()
    {
        var snapshot = Signed([PermissionCodes.PatientsRead]); // 30-day window
        var rewoundNow = Base - TimeSpan.FromDays(30);
        var laterHighWater = Base + TimeSpan.FromDays(31);

        var evaluation = Evaluate(snapshot, PermissionCodes.PatientsRead, rewoundNow, laterHighWater);

        Assert.Equal(CapabilityDecisionState.ExpiredOffline, evaluation.State);
    }

    [Fact]
    public void ClockRollback_IssuedInTheFuture_IsTreatedAsExpired()
    {
        var issuedLater = Base + TimeSpan.FromDays(10);
        var snapshot = Signed([PermissionCodes.PatientsRead], issued: issuedLater);

        // Wall clock rewound to before issuance; high-water agrees with the rewound clock ⇒ rollback detected.
        var evaluation = Evaluate(snapshot, PermissionCodes.PatientsRead, Base, Base);

        Assert.Equal(CapabilityDecisionState.ExpiredOffline, evaluation.State);
    }

    [Fact]
    public void MutatedPermissionsJson_FailsSignature_IsDeniedAndDiscarded()
    {
        var snapshot = Signed([PermissionCodes.PatientsRead, PermissionCodes.PatientsWrite]);
        // A self-consistent but different payload: parses cleanly, so only the signature can catch the edit.
        var mutatedCodes = new[] { PermissionCodes.PatientsRead, PermissionCodes.PatientsWrite, PermissionCodes.VisitsRead };
        var expiry = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var code in mutatedCodes) expiry[code] = Base + OfflineCapabilityPolicy.WindowForCode(code);
        var tampered = snapshot with
        {
            PermissionsJson = CapabilitySnapshotCodec.WritePermissions(mutatedCodes),
            PerCapabilityExpiryJson = CapabilitySnapshotCodec.WriteExpiry(expiry),
        };

        var evaluation = Evaluate(tampered, PermissionCodes.PatientsRead, Base);

        Assert.Equal(CapabilityDecisionState.Denied, evaluation.State);
        Assert.Equal(ErrorCodes.CapabilitySnapshotTampered, evaluation.Result.Error);
        Assert.True(evaluation.DiscardSnapshot);
    }

    [Fact]
    public void RevokedSnapshot_DeniesWrites_ButReadsSurvive()
    {
        var snapshot = Signed([PermissionCodes.PatientsRead, PermissionCodes.PatientsWrite], revokedAt: Base + TimeSpan.FromHours(1));

        Assert.Equal(CapabilityDecisionState.Denied,
            Evaluate(snapshot, PermissionCodes.PatientsWrite, Base + TimeSpan.FromHours(2)).State);
        Assert.True(Evaluate(snapshot, PermissionCodes.PatientsRead, Base + TimeSpan.FromHours(2)).IsAllowed);
    }

    [Fact]
    public void MissingSnapshot_IsDeniedWithSnapshotMissing()
    {
        var evaluation = Evaluate(null, PermissionCodes.PatientsRead, Base);

        Assert.Equal(CapabilityDecisionState.Denied, evaluation.State);
        Assert.Equal(ErrorCodes.CapabilitySnapshotMissing, evaluation.Result.Error);
    }

    [Fact]
    public void CodeAbsentFromSnapshot_IsDenied()
    {
        var snapshot = Signed([PermissionCodes.PatientsRead]);

        var evaluation = Evaluate(snapshot, PermissionCodes.PatientsWrite, Base);

        Assert.Equal(CapabilityDecisionState.Denied, evaluation.State);
        Assert.Equal(ErrorCodes.CapabilityNotGranted, evaluation.Result.Error);
    }

    [Fact]
    public void StampMismatch_IsDenied()
    {
        var snapshot = Signed([PermissionCodes.PatientsRead]);

        var evaluation = Evaluator.Evaluate(snapshot, PermissionCodes.PatientsRead, Tenant, User, Device,
            "A-DIFFERENT-STAMP", Base, Base);

        Assert.Equal(CapabilityDecisionState.Denied, evaluation.State);
        Assert.Equal(ErrorCodes.CapabilityNotGranted, evaluation.Result.Error);
    }
}
