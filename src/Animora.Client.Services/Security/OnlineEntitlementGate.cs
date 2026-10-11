using Animora.Application.Abstractions;
using Animora.Application.Security;
using Animora.Client.Services.Identity;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Infrastructure.Local.Identity;
using Animora.Server.Licensing.Verification;

namespace Animora.Client.Services.Security;

/// <summary>
/// G06 entitlement plane (A-RULE 023) decided <b>only from the sealed local cache</b> — it never calls the server, so the app stays fully usable
/// offline for as long as the sealed licence allows. Per call: read the sealed token → verify its ECDSA signature against the cached public keys
/// → <see cref="LicensePolicy.Evaluate"/> at <c>max(now, high-water)</c> (a backward system clock can never revive an expired licence, because the
/// <see cref="HighWaterClock"/> is the max of the OS-monotonic observation and the value sealed with the token) → <see cref="CapabilityGate.IsAllowed"/>
/// for the request's <see cref="RiskLevel"/>. No sealed licence / no keys / unverifiable token ⇒ <see cref="LicenseState.Locked"/> (fail closed).
/// </summary>
public sealed class OnlineEntitlementGate(ISealedEntitlementStore sealedStore, LicenseKeyCache keyCache, IDeviceFingerprintSource fingerprint,
    IClock clock, IMonotonicClock monotonic) : IEntitlementGate
{
    private readonly Lazy<string> _installationHash = new(() => InstallationHash.Compute(fingerprint));
    private volatile VerifiedCache? _verified;

    /// <inheritdoc />
    public async ValueTask<Result> EvaluateAsync(object command, CommandContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await DecideAsync(RiskOf(command), isRead: false, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<Result> EvaluateAsync(object query, QueryContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await DecideAsync(RiskLevel.Low, isRead: true, ct).ConfigureAwait(false);
    }

    /// <summary>The current licence verdict (no server call); exposed for diagnostics and tests.</summary>
    public async Task<LicenseVerdict> GetVerdictAsync(CancellationToken ct = default)
    {
        var read = await sealedStore.ReadAsync(ct).ConfigureAwait(false);
        if (read.IsFailure || read.Value is not { } sealedValue) return new LicenseVerdict(LicenseState.Locked, "NO_LICENSE");

        var payload = Verify(sealedValue.LicenseToken);
        if (payload is null) return new LicenseVerdict(LicenseState.Locked, ErrorCodes.LicenseInvalid.Code);

        var sealedHighWater = DateTimeOffset.FromUnixTimeMilliseconds(sealedValue.HighWaterUnixMs);
        var clockState = new HighWaterClock(monotonic.HighWaterUtc).Advance(sealedHighWater);
        return LicensePolicy.Evaluate(payload, clock.UtcNow, clockState, _installationHash.Value);
    }

    private async ValueTask<Result> DecideAsync(RiskLevel risk, bool isRead, CancellationToken ct)
    {
        var verdict = await GetVerdictAsync(ct).ConfigureAwait(false);
        return CapabilityGate.IsAllowed(verdict.State, risk, isRead)
            ? Result.Ok()
            : Result.Fail(ErrorCodes.EntitlementRequired, $"{verdict.State}:{verdict.Reason ?? "NONE"}");
    }

    /// <summary>Command → risk: the declared permission's catalog risk, defaulting to <see cref="RiskLevel.Normal"/> (never lower than a write).</summary>
    private static RiskLevel RiskOf(object command)
    {
        var attribute = (RequiresPermissionAttribute?)Attribute.GetCustomAttribute(command.GetType(), typeof(RequiresPermissionAttribute), inherit: false);
        if (attribute is null || !PermissionCodes.Exists(attribute.PermissionCode)) return RiskLevel.Normal;
        var risk = PermissionCodes.RiskOf(attribute.PermissionCode);
        return risk == RiskLevel.Low ? RiskLevel.Normal : risk; // a command is never read-class
    }

    private LicensePayload? Verify(string token)
    {
        if (_verified is { } cached && string.Equals(cached.Token, token, StringComparison.Ordinal)) return cached.Payload;
        if (keyCache.Get() is not { } keys) return null;
        var parsed = LicenseEnvelope.Parse(token);
        if (parsed.IsFailure) return null;
        var verified = LicenseVerifier.Verify(parsed.Value!, keys);
        if (verified.IsFailure) return null;
        _verified = new VerifiedCache(token, verified.Value!);
        return verified.Value;
    }

    private sealed record VerifiedCache(string Token, LicensePayload Payload);
}
