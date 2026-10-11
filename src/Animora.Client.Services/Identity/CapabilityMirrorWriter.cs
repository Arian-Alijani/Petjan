using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Animora.Application.Security;
using Animora.Contracts.Wire.Auth;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Identity;

/// <summary>
/// Mirrors the server's offline capability snapshot into the local <see cref="ICapabilityStore"/>: fetches <c>POST /auth/capabilities</c> and
/// <c>GET /auth/keys</c>, <b>verifies the server's ECDSA signature</b> over the exact payload bytes, then re-signs the snapshot with the local
/// device key (<see cref="ICapabilitySnapshotSigner"/>) so the existing B04b evaluator treats it like any local snapshot. A snapshot that
/// fails verification is never stored.
/// </summary>
public sealed class CapabilityMirrorWriter(OnlineApi api, ICapabilitySnapshotSigner signer, IServiceScopeFactory scopes, IClock clock)
{
    /// <summary>Fetches, verifies and stores the snapshot of the signed-in user; returns the stored snapshot.</summary>
    public async Task<Result<StoredCapabilitySnapshot>> MirrorAsync(string bearer, CancellationToken ct = default)
    {
        var keys = await FetchAsync<PublicKeySet>(HttpMethod.Get, "auth/keys", null, null, ct).ConfigureAwait(false);
        if (keys.IsFailure) return Result<StoredCapabilitySnapshot>.Fail(keys.Error, keys.Detail);
        var response = await FetchAsync<CapabilitySnapshotResponse>(HttpMethod.Post, "auth/capabilities", new { }, bearer, ct).ConfigureAwait(false);
        if (response.IsFailure) return Result<StoredCapabilitySnapshot>.Fail(response.Error, response.Detail);

        var stored = Convert(response.Value!, keys.Value!);
        if (stored.IsFailure) return stored;

        using var di = scopes.CreateScope();
        var store = di.ServiceProvider.GetRequiredService<ICapabilityStore>();
        var replaced = await store.ReplaceAsync(stored.Value!, ct).ConfigureAwait(false);
        return replaced.IsFailure ? Result<StoredCapabilitySnapshot>.Fail(replaced.Error, replaced.Detail) : stored;
    }

    /// <summary>Verifies <paramref name="response"/> against <paramref name="keys"/> and converts it to a locally signed snapshot.</summary>
    internal Result<StoredCapabilitySnapshot> Convert(CapabilitySnapshotResponse response, PublicKeySet keys)
    {
        var key = keys.Keys.FirstOrDefault(k => string.Equals(k.KeyId, response.KeyId, StringComparison.Ordinal));
        if (key is null || !VerifyServerSignature(key, response)) return Result<StoredCapabilitySnapshot>.Fail(ErrorCodes.CapabilitySnapshotTampered, "server_signature_invalid");

        try
        {
            using var doc = JsonDocument.Parse(response.PayloadJson);
            var root = doc.RootElement;
            var tenant = root.GetProperty("tenantId").GetString();
            var user = root.GetProperty("userId").GetString();
            var device = root.GetProperty("deviceId").GetString();
            var stamp = root.GetProperty("securityStamp").GetString();
            var issued = DateTimeOffset.Parse(root.GetProperty("issuedUtc").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            var snapshotExpiry = DateTimeOffset.Parse(root.GetProperty("snapshotExpiryUtc").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(device) || string.IsNullOrEmpty(stamp))
                return Result<StoredCapabilitySnapshot>.Fail(ErrorCodes.CapabilitySnapshotTampered, "snapshot_identity_missing");

            var expiry = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
            foreach (var p in root.GetProperty("capabilityExpiry").EnumerateObject())
                expiry[p.Name] = DateTimeOffset.Parse(p.Value.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

            // Same absolute-expiry rule as the local issuer: per-code windows govern; the absolute bound is the latest of them.
            var absolute = expiry.Count == 0 ? snapshotExpiry.ToUniversalTime() : expiry.Values.Max();
            var unsigned = new StoredCapabilitySnapshot(IdGenerator.NewId(clock.UtcNow), tenant, user, device,
                CapabilitySnapshotCodec.WritePermissions(expiry.Keys), issued.ToUniversalTime(), CapabilitySnapshotCodec.WriteExpiry(expiry),
                absolute, string.Empty, stamp, RevokedAtUtc: null);

            var signature = signer.Sign(unsigned);
            return signature.IsFailure
                ? Result<StoredCapabilitySnapshot>.Fail(signature.Error, signature.Detail)
                : Result<StoredCapabilitySnapshot>.Ok(unsigned with { SnapshotSignature = signature.Value! });
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            return Result<StoredCapabilitySnapshot>.Fail(ErrorCodes.CapabilitySnapshotTampered, "snapshot_payload_malformed");
        }
    }

    private static bool VerifyServerSignature(PublicKeyInfo key, CapabilitySnapshotResponse response)
    {
        try
        {
            using var ec = ECDsa.Create();
            ec.ImportSubjectPublicKeyInfo(System.Convert.FromBase64String(key.PublicKeyDerBase64), out _);
            return ec.VerifyData(Encoding.UTF8.GetBytes(response.PayloadJson), System.Convert.FromBase64String(response.Signature),
                HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    private async Task<Result<T>> FetchAsync<T>(HttpMethod method, string path, object? body, string? bearer, CancellationToken ct) where T : class
    {
        var exchange = await api.SendAsync(method, path, body, bearer, ct).ConfigureAwait(false);
        if (exchange.Response is null) return Result<T>.Fail(ErrorCodes.NetworkUnavailable, exchange.Detail);
        using var http = exchange.Response;
        if (!http.IsSuccessStatusCode)
            return Result<T>.Fail(OnlineApi.IsTransient(http.StatusCode) ? ErrorCodes.ServerBusy : ErrorCodes.Unauthorized, $"{path} answered {(int)http.StatusCode}.");
        var parsed = await OnlineApi.ReadAsync<T>(http, ct).ConfigureAwait(false);
        return parsed is null ? Result<T>.Fail(ErrorCodes.SyncProtocolViolation, $"{path} returned an empty body.") : Result<T>.Ok(parsed);
    }
}
