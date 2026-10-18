using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Animora.Application.Security;
using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Contracts.Diagnostics;
using Animora.Domain.Identity;
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
    public async Task<Result<StoredCapabilitySnapshot>> MirrorAsync(string? bearer, CancellationToken ct = default)
    {
        var keys = await FetchAsync<PublicKeySet>(HttpMethod.Get, "auth/keys", null, null, ct).ConfigureAwait(false);
        if (keys.IsFailure)
        {
            TraceLog.Warn($"[mirror] MirrorAsync → GET auth/keys FAILED ({keys.Error} '{keys.Detail}').", "mirror");
            return Result<StoredCapabilitySnapshot>.Fail(keys.Error, keys.Detail);
        }
        var response = await FetchAsync<CapabilitySnapshotResponse>(HttpMethod.Post, "auth/capabilities", new { }, bearer, ct).ConfigureAwait(false);
        if (response.IsFailure)
        {
            TraceLog.Warn($"[mirror] MirrorAsync → POST auth/capabilities FAILED ({response.Error} '{response.Detail}') keys={keys.Value!.Keys.Count}.", "mirror");
            return Result<StoredCapabilitySnapshot>.Fail(response.Error, response.Detail);
        }

        var stored = Convert(response.Value!, keys.Value!);
        if (stored.IsFailure)
        {
            TraceLog.Error($"[mirror] MirrorAsync → snapshot CONVERT/VERIFY FAILED ({stored.Error} '{stored.Detail}') keyId='{response.Value!.KeyId}'. Not stored.", null, "mirror");
            return stored;
        }

        using var di = scopes.CreateScope();
        var store = di.ServiceProvider.GetRequiredService<ICapabilityStore>();
        var replaced = await store.ReplaceAsync(stored.Value!, ct).ConfigureAwait(false);
        if (replaced.IsFailure)
        {
            TraceLog.Error($"[mirror] MirrorAsync → capability store REPLACE FAILED ({replaced.Error} '{replaced.Detail}').", null, "mirror");
            return Result<StoredCapabilitySnapshot>.Fail(replaced.Error, replaced.Detail);
        }
        TraceLog.Info($"[mirror] MirrorAsync ← STORED snapshot id={stored.Value!.Id} tenant={stored.Value.TenantId} user={stored.Value.UserId} " +
            $"device={stored.Value.DeviceId} issued={stored.Value.IssuedAtUtc:O} absExpiry={stored.Value.AbsoluteExpiryUtc:O}.", "mirror");
        return stored;
    }

    /// <summary>
    /// Mirrors verified server access into the active local membership and installation-bound snapshot atomically.
    /// Local and server devices/stamps are different identities; copying the server row alone cannot refresh the
    /// local gate, and issuing from local role defaults would silently restore server-denied permissions.
    /// </summary>
    internal async Task<Result> RefreshLocalAccessAsync(CurrentAccessWire access, ClientSession expected,
        Func<bool> stillCurrent, CancellationToken ct)
    {
        var mirrored = await MirrorAsync(bearer: null, ct).ConfigureAwait(false);
        if (mirrored.IsFailure)
        {
            TraceLog.Warn($"[mirror] RefreshLocalAccessAsync → inner MirrorAsync FAILED ({mirrored.Error} '{mirrored.Detail}').", "mirror");
            return mirrored.ToResult();
        }
        var snapshot = mirrored.Value!;
        if (snapshot.TenantId != expected.TenantId || snapshot.UserId != expected.UserId || snapshot.DeviceId != expected.DeviceId)
        {
            TraceLog.Error($"[mirror] RefreshLocalAccessAsync → BINDING MISMATCH: snapshot(tenant={snapshot.TenantId} user={snapshot.UserId} device={snapshot.DeviceId}) " +
                $"vs expected(tenant={expected.TenantId} user={expected.UserId} device={expected.DeviceId}) ⇒ CapabilityNotGranted.", null, "mirror");
            return Result.Fail(ErrorCodes.CapabilityNotGranted, "server_snapshot_binding_mismatch");
        }
        var parsed = CapabilitySnapshotCodec.Parse(snapshot.PermissionsJson, snapshot.PerCapabilityExpiryJson);
        if (parsed.IsFailure)
        {
            TraceLog.Error($"[mirror] RefreshLocalAccessAsync → snapshot payload UNPARSEABLE ({parsed.Error}) ⇒ abort.", null, "mirror");
            return parsed.ToResult();
        }
        var offline = access.Permissions.Where(code => OfflineCapabilityPolicy.WindowForCode(code) > TimeSpan.Zero);
        if (!new HashSet<string>(offline, StringComparer.Ordinal).SetEquals(parsed.Value!.Permissions))
        {
            TraceLog.Error("[mirror] RefreshLocalAccessAsync → ACCESS CHANGED DURING REFRESH: the online permission set and the signed offline snapshot disagree " +
                "⇒ ConcurrencyConflict (retried next pass; never applied).", null, "mirror");
            return Result.Fail(ErrorCodes.ConcurrencyConflict, "access_changed_during_refresh");
        }
        if (!stillCurrent())
        {
            TraceLog.Warn("[mirror] RefreshLocalAccessAsync → session no longer current after mirror ⇒ SessionExpired.", "mirror");
            return Result.Fail(ErrorCodes.SessionExpired);
        }

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var installation = services.GetRequiredService<ILocalInstallationIdentity>();
        var users = services.GetRequiredService<IIdentityRepository>();
        var memberships = services.GetRequiredService<IStaffMembershipRepository>();
        var roles = services.GetRequiredService<IRoleDefinitionRepository>();
        if (installation.TenantId != expected.TenantId
            || !Enum.TryParse<StaffRole>(access.Role, out var staffRole) || !Enum.IsDefined(staffRole))
        {
            TraceLog.Error($"[mirror] RefreshLocalAccessAsync → TENANT/ROLE MISMATCH (installTenant={installation.TenantId} expectedTenant={expected.TenantId} role='{access.Role}') ⇒ TenantMismatch.", null, "mirror");
            return Result.Fail(ErrorCodes.TenantMismatch);
        }
        var user = await users.FindAsync(expected.UserId, ct).ConfigureAwait(false);
        var active = await memberships.FindActiveAsync(expected.TenantId, expected.UserId, ct).ConfigureAwait(false);
        var member = active is null ? null : await memberships.FindAsync(active.Id, ct).ConfigureAwait(false);
        var role = await roles.FindByRoleAsync(expected.TenantId, staffRole, ct).ConfigureAwait(false);
        if (user is null || user.TenantId != expected.TenantId || member is null || role is null || !stillCurrent())
        {
            TraceLog.Error($"[mirror] RefreshLocalAccessAsync → local identity incomplete (userNull={user is null} userTenant={user?.TenantId} " +
                $"memberNull={member is null} roleNull={role is null} stillCurrent={stillCurrent()}) ⇒ SessionExpired.", null, "mirror");
            return Result.Fail(ErrorCodes.SessionExpired);
        }

        // Rebind only AFTER verification, preserving every server-issued time bound, never extending a window.
        var local = snapshot with { Id = IdGenerator.NewId(clock.UtcNow), DeviceId = installation.DeviceId,
            SecurityStamp = user.SecurityStamp, SnapshotSignature = string.Empty };
        var signed = signer.Sign(local);
        if (signed.IsFailure)
        {
            TraceLog.Error($"[mirror] RefreshLocalAccessAsync → local re-sign FAILED ({signed.Error}) ⇒ abort.", null, "mirror");
            return signed.ToResult();
        }
        local = local with { SnapshotSignature = signed.Value! };
        var granted = new HashSet<string>(access.Permissions, StringComparer.Ordinal);
        var saved = await services.GetRequiredService<IUnitOfWork>().ExecuteAsync(async token =>
        {
            if (!stillCurrent()) return Result<bool>.Fail(ErrorCodes.SessionExpired);
            member.ChangeRole(staffRole);
            foreach (var code in PermissionCodes.All)
            {
                member.ClearOverride(code);
                if (granted.Contains(code) && !role.Has(code)) member.GrantExtra(code);
                else if (!granted.Contains(code) && role.Has(code)) member.Deny(code);
            }
            var replaced = await services.GetRequiredService<ICapabilityStore>().ReplaceAsync(local, token).ConfigureAwait(false);
            return replaced.IsFailure ? Result<bool>.Fail(replaced.Error, replaced.Detail) : Result<bool>.Ok(true);
        }, ct).ConfigureAwait(false);
        if (saved.IsSuccess && stillCurrent())
        {
            var cache = services.GetRequiredService<SessionContextCache>();
            if (cache.Current is { } current && current.UserId == expected.UserId)
                cache.TryUpdate(current, current with { Role = staffRole });
        }
        TraceLog.Info($"[mirror] RefreshLocalAccessAsync ← saved={saved.IsSuccess} error={saved.Error} role={staffRole} granted={granted.Count} " +
            $"snapshotId={local.Id} absExpiry={local.AbsoluteExpiryUtc:O}.", "mirror");
        return saved.ToResult();
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

            // The server's absolute bound is authoritative even when some individual code windows are longer.
            // Taking Max(per-code expiry) here extended a signed server snapshot beyond its actual lifetime.
            var absolute = snapshotExpiry.ToUniversalTime();
            if (expiry.Count > 0 && expiry.Values.Max() < absolute) absolute = expiry.Values.Max();
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
