using System;
using System.Collections.Generic;
using Animora.Domain.Identity;
using Animora.Domain.Time;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>
/// The capability snapshot fails closed on every doubt (A-RULE 029, CONVENTIONS.md §7):
/// expired snapshot, expired capability, unknown code — all deny.
/// </summary>
public sealed class CapabilitySnapshotTests
{
    private static readonly DateTimeOffset Issued = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExpiredSnapshot_DeniesEvenALowRiskRead()
    {
        var snapshot = Snapshot(TimeSpan.FromDays(1), PermissionCodes.PatientsRead);
        var after = snapshot.SnapshotExpiryUtc + TimeSpan.FromSeconds(1);

        Assert.True(snapshot.Allows(PermissionCodes.PatientsRead, Issued));
        Assert.True(snapshot.IsExpired(after));
        Assert.False(snapshot.Allows(PermissionCodes.PatientsRead, after));
    }

    [Fact]
    public void UnknownOrUngrantedCode_IsDenied()
    {
        var snapshot = Snapshot(TimeSpan.FromDays(1), PermissionCodes.PatientsRead);

        Assert.False(snapshot.Allows("not.a.code", Issued));
        Assert.False(snapshot.Allows(PermissionCodes.SalesPost, Issued));
        Assert.False(snapshot.Allows("  ", Issued));
    }

    [Fact]
    public void CriticalCode_IsDeniedImmediately_BecauseItsWindowIsZero()
    {
        var snapshot = Snapshot(TimeSpan.FromDays(1), PermissionCodes.SalesVoid);

        Assert.Equal(Issued, snapshot.CapabilityExpiry[PermissionCodes.SalesVoid]);
        Assert.False(snapshot.IsExpired(Issued));
        Assert.False(snapshot.Allows(PermissionCodes.SalesVoid, Issued));
    }

    [Fact]
    public void CapabilityExpiry_IsEnforcedIndependentlyOfTheSnapshotWindow()
    {
        var snapshot = Snapshot(TimeSpan.FromDays(60), PermissionCodes.SalesPost);
        var withinWindow = Issued + TimeSpan.FromHours(71);
        var pastWindow = Issued + TimeSpan.FromHours(73);

        Assert.False(snapshot.IsExpired(pastWindow));
        Assert.True(snapshot.Allows(PermissionCodes.SalesPost, withinWindow));
        Assert.False(snapshot.Allows(PermissionCodes.SalesPost, pastWindow));
    }

    [Fact]
    public void OfflineWindows_AreThePublishedNumbers()
    {
        Assert.Equal(TimeSpan.FromDays(30), OfflineCapabilityPolicy.WindowFor(RiskLevel.Low));
        Assert.Equal(TimeSpan.FromDays(14), OfflineCapabilityPolicy.WindowFor(RiskLevel.Normal));
        Assert.Equal(TimeSpan.FromHours(72), OfflineCapabilityPolicy.WindowFor(RiskLevel.High));
        Assert.Equal(TimeSpan.Zero, OfflineCapabilityPolicy.WindowFor(RiskLevel.Critical));
        Assert.Equal(TimeSpan.FromDays(30), OfflineCapabilityPolicy.WindowForCode(PermissionCodes.PatientsRead));
    }

    [Fact]
    public void RevokedDevice_KeepsItsFirstRevocationRecord()
    {
        var clock = new FixedClock(Issued);
        var device = Device.Register("T1", "U1", "Reception PC", "abc123", "windows", clock);

        Assert.Equal(DeviceStatus.Pending, device.Status);
        device.Approve();
        Assert.True(device.IsUsable);

        device.Revoke("lost", clock);
        device.Revoke("second reason", clock);

        Assert.Equal(DeviceStatus.Revoked, device.Status);
        Assert.Equal("lost", device.RevocationReason);
        Assert.Equal(Issued, device.RevokedUtc);
        Assert.False(device.IsUsable);
        Assert.Throws<InvalidOperationException>(device.Approve);
    }

    private static CapabilitySnapshot Snapshot(TimeSpan snapshotWindow, params string[] codes)
    {
        var expiry = CapabilitySnapshot.BuildExpiry(codes, Issued);

        return new CapabilitySnapshot(
            TenantId: "T1",
            UserId: "U1",
            DeviceId: "D1",
            Role: StaffRole.Cashier,
            CapabilityExpiry: new Dictionary<string, DateTimeOffset>(expiry, StringComparer.Ordinal),
            IssuedUtc: Issued,
            SnapshotExpiryUtc: Issued + snapshotWindow,
            SecurityStamp: "STAMP",
            Signature: "c2ln");
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
