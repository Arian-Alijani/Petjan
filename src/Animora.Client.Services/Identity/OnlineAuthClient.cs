using System.Net;
using Animora.Application.Security;
using Animora.Contracts.Wire.Auth;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Infrastructure.Local.Identity;

namespace Animora.Client.Services.Identity;

/// <summary>
/// <see cref="IOnlineAuthClient"/> + <see cref="ISessionRefresher"/> over <c>/auth/*</c>. A definitive server answer is final; only an
/// unreachable/failing server (no response, 408/429/5xx) falls back to <see cref="OfflineSessionAuthorizer"/>. After a successful online login
/// the session is persisted FIRST, then the device is registered, the capability snapshot mirrored, the licence sealed and the one-way password
/// verifier written. Mirroring is best effort for the online session (a mirror failure only means no offline start later — fail closed),
/// except a revoked device which aborts the login and wipes all offline authority, and a still-<c>Pending</c> (never approved) device which
/// aborts the login with <c>DEVICE_PENDING_APPROVAL</c>. The offline fallback can therefore only ever authorise a previously approved device:
/// the credential mirror, capability snapshot and sealed licence it requires are written only after the device was approved.
/// </summary>
public sealed class OnlineAuthClient(OnlineApi api, ISessionStore sessions, IOfflineCredentialMirrorStore mirrors, DeviceLifecycleService devices,
    CapabilityMirrorWriter capabilities, EntitlementSealer sealer, OfflineSessionAuthorizer offline, IDeviceFingerprintSource fingerprint, IClock clock, ISessionTenantBinding? binding = null)
    : IOnlineAuthClient, ISessionRefresher
{
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    /// <inheritdoc />
    public async Task<Result<OnlineLoginOutcome>> LoginAsync(string tenantCode, string userName, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantCode) || string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
            return Result<OnlineLoginOutcome>.Fail(ErrorCodes.ValidationFailed);

        var installationHash = InstallationHash.Compute(fingerprint);
        var request = new LoginRequest(tenantCode.Trim(), userName.Trim(), password, installationHash);
        var exchange = await api.SendAsync(HttpMethod.Post, "auth/login", request, bearer: null, ct).ConfigureAwait(false);

        if (exchange.Response is null)
            return await offline.AuthorizeAsync(tenantCode, userName, password, installationHash, ct).ConfigureAwait(false);

        using var response = exchange.Response;
        if (OnlineApi.IsTransient(response.StatusCode))
            return await offline.AuthorizeAsync(tenantCode, userName, password, installationHash, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return await FailAsync(response, ct).ConfigureAwait(false);

        var login = await OnlineApi.ReadAsync<LoginResponse>(response, ct).ConfigureAwait(false);
        if (login is null || string.IsNullOrEmpty(login.AccessToken) || string.IsNullOrEmpty(login.RefreshToken))
            return Result<OnlineLoginOutcome>.Fail(ErrorCodes.SyncProtocolViolation, "Login response is incomplete.");

        var stored = await sessions.WriteAsync(ToSession(login, stamp: string.Empty), ct).ConfigureAwait(false);
        if (stored.IsFailure) return Result<OnlineLoginOutcome>.Fail(stored.Error, stored.Detail);

        var registered = await devices.RegisterAsync(login.AccessToken, ct).ConfigureAwait(false);
        if (registered.IsFailure && registered.Error.Code == ErrorCodes.DeviceRevoked.Code)
        {
            await sessions.ClearAsync(ct).ConfigureAwait(false);
            return Result<OnlineLoginOutcome>.Fail(ErrorCodes.DeviceRevoked);
        }

        // A never-approved device (non-manager staff; managers are auto-approved server-side) is NOT a login success: the capability
        // endpoint would reject it with DEVICE_REVOKED, which the user must not see as "revoked" or as a credential error. Fail closed with a
        // distinct, actionable state and drop the session so nothing downstream (sync, commands) runs on a pending device. The server keeps the
        // registration, so the next sign-in after a manager approves it re-reads the status and proceeds.
        if (IsPending(registered.Value))
        {
            await sessions.ClearAsync(ct).ConfigureAwait(false);
            return Result<OnlineLoginOutcome>.Fail(ErrorCodes.DevicePendingApproval);
        }

        // Best-effort mirrors: failure here must not undo a valid online login — it only leaves offline start unavailable (fail closed).
        var snapshot = await capabilities.MirrorAsync(login.AccessToken, ct).ConfigureAwait(false);
        var stamp = snapshot.IsSuccess ? snapshot.Value!.SecurityStamp : string.Empty;
        await sessions.WriteAsync(ToSession(login, stamp), ct).ConfigureAwait(false);
        await sealer.SealAsync(login.AccessToken, ct).ConfigureAwait(false);
        if (snapshot.IsSuccess)
            await mirrors.WriteVerifierAsync(login.TenantId, login.UserId, login.DeviceId,
                offline.CreateVerifier(tenantCode, userName, password), ct).ConfigureAwait(false);

        return Result<OnlineLoginOutcome>.Ok(new OnlineLoginOutcome(login.TenantId, login.UserId, login.DeviceId, login.Role,
            IsOffline: false, login.MustChangePassword));
    }

    /// <inheritdoc />
    public async Task<Result> RefreshAsync(CancellationToken ct = default)
    {
        await _refreshGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (binding is { CanSync: false }) return Result.Fail(ErrorCodes.SessionExpired);
            var current = await sessions.ReadAsync(ct).ConfigureAwait(false);
            if (current.IsFailure) return current.ToResult();
            if (current.Value is not { } session) return Result.Fail(ErrorCodes.SessionExpired, "no_session");

            var exchange = await api.SendAsync(HttpMethod.Post, "auth/refresh", new RefreshRequest(session.RefreshToken), bearer: null, ct).ConfigureAwait(false);
            if (exchange.Response is null) return Result.Fail(ErrorCodes.NetworkUnavailable, exchange.Detail);

            using var response = exchange.Response;
            if (response.IsSuccessStatusCode)
            {
                var body = await OnlineApi.ReadAsync<LoginResponse>(response, ct).ConfigureAwait(false);
                if (body is null || string.IsNullOrEmpty(body.AccessToken) || string.IsNullOrEmpty(body.RefreshToken))
                    return Result.Fail(ErrorCodes.SyncProtocolViolation, "Refresh response is incomplete.");
                return await sessions.WriteAsync(ToSession(body, session.SecurityStamp), ct).ConfigureAwait(false);
            }

            if (OnlineApi.IsTransient(response.StatusCode)) return Result.Fail(ErrorCodes.NetworkUnavailable, $"Server answered {(int)response.StatusCode}.");

            var code = await OnlineApi.ReadErrorCodeAsync(response, ct).ConfigureAwait(false);
            if (string.Equals(code, ErrorCodes.DeviceRevoked.Code, StringComparison.Ordinal))
            {
                await devices.HandleRevokedAsync(ct).ConfigureAwait(false);
                return Result.Fail(ErrorCodes.DeviceRevoked);
            }

            // Any other definitive refusal (401 / reuse detected / user disabled): the session is gone.
            await sessions.ClearAsync(ct).ConfigureAwait(false);
            return Result.Fail(ErrorCodes.SessionExpired, code);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Result> LogoutAsync(CancellationToken ct = default)
    {
        binding?.Suspend();
        await _refreshGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            var current = await sessions.ReadAsync(ct).ConfigureAwait(false);
            if (current.IsSuccess && current.Value is { } session)
            {
                var exchange = await api.SendAsync(HttpMethod.Post, "auth/logout", body: null, session.AccessToken, ct).ConfigureAwait(false);
                exchange.Response?.Dispose();
            }
        }
        finally
        {
            try { await sessions.ClearAsync(CancellationToken.None).ConfigureAwait(false); }
            finally { _refreshGate.Release(); }
        }
        return Result.Ok();
    }

    private static bool IsPending(DeviceRegisterResponse? device) =>
        device is not null && string.Equals(device.Status, "Pending", StringComparison.OrdinalIgnoreCase);

    private ClientSession ToSession(LoginResponse r, string stamp) => new(r.AccessToken, clock.UtcNow.AddSeconds(Math.Max(0, r.ExpiresInSeconds)),
        r.RefreshToken, r.UserId, r.TenantId, r.DeviceId, r.Role, stamp);

    private async Task<Result<OnlineLoginOutcome>> FailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var code = await OnlineApi.ReadErrorCodeAsync(response, ct).ConfigureAwait(false);
        var error = Map(code, response.StatusCode);
        if (error.Code == ErrorCodes.DeviceRevoked.Code) await devices.HandleRevokedAsync(ct).ConfigureAwait(false);
        return Result<OnlineLoginOutcome>.Fail(error);
    }

    private static ErrorCode Map(string? code, HttpStatusCode status)
    {
        foreach (var known in new[]
                 {
                     ErrorCodes.CredentialsInvalid, ErrorCodes.UserLockedOut, ErrorCodes.DeviceRevoked, ErrorCodes.UserDisabled,
                     ErrorCodes.TenantSuspended, ErrorCodes.MembershipNotActive, ErrorCodes.SessionExpired, ErrorCodes.ValidationFailed,
                     ErrorCodes.ContractVersionUnsupported,
                 })
        {
            if (string.Equals(known.Code, code, StringComparison.Ordinal)) return known;
        }

        return (int)status switch
        {
            401 => ErrorCodes.CredentialsInvalid,
            423 => ErrorCodes.UserLockedOut,
            403 => ErrorCodes.Unauthorized,
            400 => ErrorCodes.ValidationFailed,
            426 => ErrorCodes.ContractVersionUnsupported,
            _ => ErrorCodes.SyncProtocolViolation,
        };
    }
}
