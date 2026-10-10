using Animora.Domain.Results;
namespace Animora.Server.Licensing.Verification;

public enum LicenseState { Valid, Grace, ReadOnly, Locked }
public sealed record LicenseVerdict(LicenseState State, string? Reason = null, TimeSpan? RemainingUntilExpiry = null,
    TimeSpan? RemainingGrace = null, bool ClockRollbackDetected = false);
public sealed record HighWaterClock(DateTimeOffset HighWaterUtc)
{
    public HighWaterClock Advance(DateTimeOffset nowUtc) => nowUtc > HighWaterUtc ? new(nowUtc.ToUniversalTime()) : this;
}
public static class LicensePolicy
{
    public static LicenseVerdict Evaluate(LicensePayload p, DateTimeOffset nowUtc, HighWaterClock clock, string localInstallationIdHash)
    {
        if (!p.IsValid()) return new(LicenseState.Locked, ErrorCodes.LicenseInvalid.Code);
        if (!string.Equals(localInstallationIdHash, p.InstallationIdHash, StringComparison.Ordinal)) return new(LicenseState.Locked, "WRONG_INSTALLATION");
        var t = nowUtc > clock.HighWaterUtc ? nowUtc : clock.HighWaterUtc;
        var rollback = clock.HighWaterUtc - nowUtc > TimeSpan.FromMinutes(5);
        if (t < p.NotBeforeUtc) return new(LicenseState.Locked, "NOT_YET_VALID", ClockRollbackDetected: rollback);
        if (t <= p.ExpiresUtc)
            return clock.HighWaterUtc - nowUtc > TimeSpan.FromHours(24)
                ? new(LicenseState.ReadOnly, "CLOCK_ROLLBACK", ClockRollbackDetected: true)
                : new(LicenseState.Valid, RemainingUntilExpiry: p.ExpiresUtc - t, ClockRollbackDetected: rollback);
        if (t <= p.OfflineGraceUntilUtc) return new(LicenseState.Grace, RemainingGrace: p.OfflineGraceUntilUtc - t, ClockRollbackDetected: rollback);
        // Subtraction avoids overflow on malformed extreme timestamps.
        return new(t - p.OfflineGraceUntilUtc <= TimeSpan.FromDays(60) ? LicenseState.ReadOnly : LicenseState.Locked, "EXPIRED", ClockRollbackDetected: rollback);
    }
    public static LicenseVerdict FromVerificationFailure(ErrorCode error) => new(LicenseState.Locked, error.Code);
}
