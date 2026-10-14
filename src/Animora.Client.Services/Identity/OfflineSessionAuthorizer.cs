using Animora.Application.Security;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Maintenance;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Server.Licensing.Verification;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Identity;

/// <summary>
/// Decides, with <b>no network</b>, whether a cold offline start is allowed: the credential mirror must verify (one-way, via
/// <see cref="IPasswordHasher"/>), the mirrored capability snapshot must be present, signed, unrevoked, same security stamp and unexpired
/// at <c>max(now, high-water)</c>, and the sealed licence must still verify and not be <see cref="LicenseState.Locked"/>. Anything else
/// fails closed (<c>OFFLINE_CREDENTIAL_MISMATCH</c> / <c>OFFLINE_SESSION_UNAVAILABLE</c>).
/// </summary>
public sealed class OfflineSessionAuthorizer(IOfflineCredentialMirrorStore mirrors, IPasswordHasher hasher, IServiceScopeFactory scopes,
    ICapabilitySnapshotSigner signer, ISealedEntitlementStore sealedStore, LicenseKeyCache keyCache, IClock clock, IMonotonicClock monotonic)
{
    /// <summary>The composite secret the verifier is derived from: tenant + user + password, so any of the three being wrong fails.</summary>
    public static string VerifierMaterial(string tenantCode, string userName, string password) =>
        string.Join('\u001f', tenantCode.Trim().ToUpperInvariant(), userName.Trim().ToUpperInvariant(), password);

    /// <summary>One-way verifier stored in the mirror.</summary>
    public string CreateVerifier(string tenantCode, string userName, string password) => hasher.Hash(VerifierMaterial(tenantCode, userName, password));

    /// <summary>Authorises (or refuses) an offline start for the supplied credentials.</summary>
    public async Task<Result<OnlineLoginOutcome>> AuthorizeAsync(string tenantCode, string userName, string password, string installationHash,
        CancellationToken ct = default)
    {
        var mirror = await mirrors.ReadAsync(ct).ConfigureAwait(false);
        if (mirror.IsFailure) return Result<OnlineLoginOutcome>.Fail(ErrorCodes.OfflineSessionUnavailable, mirror.Detail);
        if (mirror.Value is not { } m) return Result<OnlineLoginOutcome>.Fail(ErrorCodes.NetworkUnavailable, "no_credential_mirror");

        if (hasher.Verify(m.Verifier, VerifierMaterial(tenantCode, userName, password)) == PasswordVerificationOutcome.Failed)
            return Result<OnlineLoginOutcome>.Fail(ErrorCodes.OfflineCredentialMismatch);

        var now = clock.UtcNow;
        var highWater = new HighWaterClock(monotonic.HighWaterUtc).Advance(now);
        var effective = highWater.HighWaterUtc > now ? highWater.HighWaterUtc : now;

        using (var di = scopes.CreateScope())
        {
            var state = di.ServiceProvider.GetRequiredService<LocalInstallationState>();
            if (state.TenantId != m.TenantId) return Unavailable("wrong_local_tenant");
            var bound = await di.ServiceProvider.GetRequiredService<ILocalStoreMaintenance>()
                .EnsureBoundToAsync(m.TenantId, ct).ConfigureAwait(false);
            if (bound.IsFailure) return Unavailable("local_tenant_unavailable");
            var store = di.ServiceProvider.GetRequiredService<ICapabilityStore>();
            var snapshot = await store.GetAsync(m.TenantId, m.UserId, m.DeviceId, ct).ConfigureAwait(false);
            if (snapshot.IsFailure || snapshot.Value is not { } s) return Unavailable("no_capability_snapshot");
            if (s.RevokedAtUtc is not null) return Unavailable("capability_revoked");
            if (!string.Equals(s.SecurityStamp, m.SecurityStamp, StringComparison.Ordinal)) return Unavailable("security_stamp_changed");
            if (signer.Verify(s).IsFailure) return Unavailable("capability_tampered");
            if (s.AbsoluteExpiryUtc <= effective) return Unavailable("capability_expired");
        }

        var sealedRead = await sealedStore.ReadAsync(ct).ConfigureAwait(false);
        if (sealedRead.IsFailure || sealedRead.Value is not { } sealedValue) return Unavailable("no_sealed_entitlement");
        if (!string.Equals(sealedValue.InstallationIdHash, installationHash, StringComparison.Ordinal)) return Unavailable("wrong_installation");
        if (keyCache.Get() is not { } keys) return Unavailable("no_license_keys");
        var parsed = LicenseEnvelope.Parse(sealedValue.LicenseToken);
        if (parsed.IsFailure) return Unavailable("license_malformed");
        var verified = LicenseVerifier.Verify(parsed.Value!, keys);
        if (verified.IsFailure) return Unavailable("license_unverifiable");
        var sealedClock = new HighWaterClock(DateTimeOffset.FromUnixTimeMilliseconds(sealedValue.HighWaterUnixMs)).Advance(highWater.HighWaterUtc);
        var verdict = LicensePolicy.Evaluate(verified.Value!, now, sealedClock, installationHash);
        if (verdict.State == LicenseState.Locked) return Unavailable($"license_locked:{verdict.Reason}");

        return Result<OnlineLoginOutcome>.Ok(new OnlineLoginOutcome(m.TenantId, m.UserId, m.DeviceId, m.Role, IsOffline: true, MustChangePassword: false));
    }

    private static Result<OnlineLoginOutcome> Unavailable(string detail) => Result<OnlineLoginOutcome>.Fail(ErrorCodes.OfflineSessionUnavailable, detail);
}
