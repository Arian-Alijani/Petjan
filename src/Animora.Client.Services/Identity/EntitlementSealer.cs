using System.Net.Http;
using Animora.Contracts.Wire.Licensing;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Infrastructure.Local.Identity;
using Animora.Server.Licensing.Verification;

namespace Animora.Client.Services.Identity;

/// <summary>
/// Obtains this installation's licence from the server (<c>POST /licensing/activate</c>, or <c>/renew</c> once a sealed licence exists), verifies
/// the ECDSA signature against the cached/fresh public keys, feeds the trusted server time into the monotonic high-water clock and seals the
/// token through <see cref="ISealedEntitlementStore"/> (forward-only merge). Nothing unverifiable is ever sealed.
/// </summary>
public sealed class EntitlementSealer(OnlineApi api, LicenseKeyCache keyCache, ISealedEntitlementStore sealedStore,
    IDeviceFingerprintSource fingerprint, IClock clock, IMonotonicClock monotonic)
{
    /// <summary>Activates/renews and seals; returns the verified payload.</summary>
    public async Task<Result<LicensePayload>> SealAsync(string bearer, CancellationToken ct = default)
    {
        var keys = await FetchKeysAsync(ct).ConfigureAwait(false);
        if (keys.IsFailure) return Result<LicensePayload>.Fail(keys.Error, keys.Detail);

        var installationHash = InstallationHash.Compute(fingerprint);
        var existing = await sealedStore.ReadAsync(ct).ConfigureAwait(false);
        if (existing.IsFailure) return Result<LicensePayload>.Fail(existing.Error, existing.Detail);
        var current = existing.Value;

        var path = current is null ? "licensing/activate" : "licensing/renew";
        var request = new LicenseRequest(installationHash, current?.HighestSequenceSeen);
        var exchange = await api.SendAsync(HttpMethod.Post, path, request, bearer, ct).ConfigureAwait(false);
        if (exchange.Response is null) return Result<LicensePayload>.Fail(ErrorCodes.NetworkUnavailable, exchange.Detail);
        using var response = exchange.Response;
        if (!response.IsSuccessStatusCode)
        {
            var code = await OnlineApi.ReadErrorCodeAsync(response, ct).ConfigureAwait(false);
            return Result<LicensePayload>.Fail(OnlineApi.IsTransient(response.StatusCode) ? ErrorCodes.ServerBusy : ErrorCodes.EntitlementRequired,
                code ?? $"{path} answered {(int)response.StatusCode}.");
        }

        var license = await OnlineApi.ReadAsync<LicenseResponse>(response, ct).ConfigureAwait(false);
        if (license is null) return Result<LicensePayload>.Fail(ErrorCodes.SyncProtocolViolation, "License response is empty.");

        var parsed = LicenseEnvelope.Parse(license.Token);
        if (parsed.IsFailure) return Result<LicensePayload>.Fail(parsed.Error, parsed.Detail);
        var verified = LicenseVerifier.Verify(parsed.Value!, keys.Value!);
        if (verified.IsFailure) return Result<LicensePayload>.Fail(verified.Error, verified.Detail);
        var payload = verified.Value!;
        if (!string.Equals(payload.InstallationIdHash, installationHash, StringComparison.Ordinal))
            return Result<LicensePayload>.Fail(ErrorCodes.LicenseDeviceMismatch);

        monotonic.Observe(license.ServerTimeUtc);
        var highWater = new HighWaterClock(monotonic.HighWaterUtc).Advance(clock.UtcNow).Advance(license.ServerTimeUtc).HighWaterUtc;
        var sealedValue = new SealedEntitlement(license.Token, highWater.ToUnixTimeMilliseconds(), payload.Sequence, payload.TenantId,
            installationHash, clock.UtcNow.ToUnixTimeMilliseconds());
        var written = await sealedStore.WriteAsync(sealedValue, ct).ConfigureAwait(false);
        return written.IsFailure ? Result<LicensePayload>.Fail(written.Error, written.Detail) : Result<LicensePayload>.Ok(payload);
    }

    private async Task<Result<ILicenseKeySet>> FetchKeysAsync(CancellationToken ct)
    {
        var exchange = await api.SendAsync(HttpMethod.Get, "licensing/keys", null, null, ct).ConfigureAwait(false);
        if (exchange.Response is null)
        {
            // Unreachable: a previously cached set still lets a (re)seal verify; with none there is nothing to verify against.
            return keyCache.Get() is { } cached
                ? Result<ILicenseKeySet>.Ok(cached)
                : Result<ILicenseKeySet>.Fail(ErrorCodes.NetworkUnavailable, exchange.Detail);
        }

        using var response = exchange.Response;
        if (!response.IsSuccessStatusCode) return Result<ILicenseKeySet>.Fail(ErrorCodes.ServerBusy, $"licensing/keys answered {(int)response.StatusCode}.");
        var set = await OnlineApi.ReadAsync<LicensePublicKeySet>(response, ct).ConfigureAwait(false);
        if (set is null) return Result<ILicenseKeySet>.Fail(ErrorCodes.SyncProtocolViolation, "License key set is empty.");
        var stored = keyCache.Store(set);
        if (stored.IsFailure) return Result<ILicenseKeySet>.Fail(stored.Error, stored.Detail);
        return Result<ILicenseKeySet>.Ok(LicenseKeyCache.ToKeySet(set));
    }
}
