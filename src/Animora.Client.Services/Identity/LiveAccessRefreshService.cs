using System.Net;
using Animora.Application.Security;
using Animora.Contracts.Diagnostics;
using Animora.Contracts.Wire.Auth;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Animora.Client.Services.Identity;

/// <summary>
/// Refreshes the active user's access without another login. The small caller-only read runs every 4–6 seconds;
/// signed offline authority and local RBAC are replaced only on change, sign-in, or periodic renewal.
/// No loop/socket without online identity configuration; one in-flight refresh; shutdown and logout are fenced.
/// </summary>
public sealed class LiveAccessRefreshService(OnlineApi api, ISessionStore sessions, SessionContextCache cache,
    CapabilityMirrorWriter mirror, CapabilityGateService gate, DeviceLifecycleService devices,
    IServiceScopeFactory scopes, IClock clock, ILogger<LiveAccessRefreshService> logger) : IAsyncDisposable
{
    // The routine snapshot renewal cadence. The caller-only /auth/access poll runs every 4–6s; the heavier
    // mirror (GET /auth/keys + POST /auth/capabilities) is a renewal, not a poll, so it runs at most this often.
    private static readonly long MirrorIntervalTicks = (long)TimeSpan.FromMinutes(5).TotalMilliseconds;

    // After a TRANSIENT mirror failure (server busy / rate-limited / local store blip) the signed offline snapshot
    // is still valid, so there is nothing to renew right now. Backing the next attempt off — instead of retrying on
    // every 4–6s pass — is what stops a rate-limit storm: three auth calls per pass against a 10-req/60s window
    // (GET /auth/access + GET /auth/keys + POST /auth/capabilities) otherwise feeds on its own 429s indefinitely.
    private static readonly long MirrorTransientBackoffTicks = (long)TimeSpan.FromSeconds(30).TotalMilliseconds;

    private readonly SemaphoreSlim _flight = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private string? _revision;
    private long _appliedGeneration = -1;
    private long _nextMirrorTick;

    public void Start() => _loop ??= Task.Run(() => RunAsync(_stop.Token));

    internal async Task<Result> RefreshAsync(CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);
        // A bounded online probe must not keep an offline user's UI waiting for the sync request timeout.
        linked.CancelAfter(TimeSpan.FromSeconds(8));
        var entered = false;
        try
        {
            await _flight.WaitAsync(linked.Token).ConfigureAwait(false);
            entered = true;
            var local = cache.Current;
            var generation = gate.Generation;
            TraceLog.Debug($"[live] RefreshAsync BEGIN gen={generation} localUser={local?.UserId ?? "(none)"} locked={local?.IsLocked} " +
                $"expires={local?.ExpiresAtUtc?.ToString("O") ?? "(none)"} revision='{_revision ?? "(none)"}' " +
                $"appliedGen={_appliedGeneration} nextMirrorInMs={_nextMirrorTick - Environment.TickCount64}", "live");
            if (local is not { UserId: { Length: > 0 }, IsLocked: false })
            {
                gate.ClearOnlineAccess();
                return Result.Ok();
            }
            var read = await sessions.ReadAsync(linked.Token).ConfigureAwait(false);
            if (read.IsFailure) return read.ToResult();
            if (read.Value is not { } expected || local.UserId != expected.UserId)
            {
                gate.ClearOnlineAccess();
                return Result.Ok(); // cold offline session: its original signed capability windows still govern
            }
            bool StillCurrent() => generation == gate.Generation && cache.Current is { IsLocked: false } current
                && current.UserId == expected.UserId && current.ExpiresAtUtc is { } expires && expires > clock.UtcNow;

            var exchange = await api.SendAsync(HttpMethod.Get, "auth/access", null, null, linked.Token).ConfigureAwait(false);
            using var response = exchange.Response;
            if (!StillCurrent()) return Result.Fail(ErrorCodes.SessionExpired);
            if (response is null)
            {
                gate.ClearOnlineAccess();
                TraceLog.Warn($"[live] RefreshAsync → auth/access returned NO response ({exchange.Detail}) ⇒ network-unavailable; offline snapshot kept, live lease dropped.", "live");
                return Result.Fail(ErrorCodes.NetworkUnavailable);
            }
            if (!response.IsSuccessStatusCode)
            {
                gate.ClearOnlineAccess();
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    var code = await OnlineApi.ReadErrorCodeAsync(response, linked.Token).ConfigureAwait(false);
                    if (StillCurrent())
                    {
                        cache.MarkLocked();
                        gate.Clear();
                        TraceLog.Error($"[live] RefreshAsync → auth/access {(int)response.StatusCode} ({code}) ⇒ session INVALID; " +
                            "gate.Clear() invoked — shell drops to read-only until re-auth. (deviceRevoked=" + (code == ErrorCodes.DeviceRevoked.Code) + ")", null, "live");
                        if (code == ErrorCodes.DeviceRevoked.Code)
                            await devices.HandleRevokedAsync(linked.Token).ConfigureAwait(false);
                        await using var scope = scopes.CreateAsyncScope();
                        await scope.ServiceProvider.GetRequiredService<ISessionService>()
                            .EndAsync(code ?? ErrorCodes.SessionExpired.Code, linked.Token).ConfigureAwait(false);
                    }
                    return Result.Fail(ErrorCodes.SessionExpired);
                }
                TraceLog.Warn($"[live] RefreshAsync → auth/access answered {(int)response.StatusCode}; transient={OnlineApi.IsTransient(response.StatusCode)} ⇒ live lease dropped, offline snapshot kept.", "live");
                return Result.Fail(OnlineApi.IsTransient(response.StatusCode) ? ErrorCodes.ServerBusy : ErrorCodes.SyncProtocolViolation);
            }
            var access = await OnlineApi.ReadAsync<CurrentAccessWire>(response, linked.Token).ConfigureAwait(false);
            if (access is null || access.TenantId != expected.TenantId || access.UserId != expected.UserId
                || access.DeviceId != expected.DeviceId || string.IsNullOrWhiteSpace(access.Revision)
                || access.Permissions is null || access.Permissions.Count > PermissionCodes.All.Count
                || access.Permissions.Any(code => string.IsNullOrWhiteSpace(code) || !PermissionCodes.Exists(code)))
            {
                gate.ClearOnlineAccess();
                TraceLog.Warn($"[live] RefreshAsync → auth/access payload REJECTED (null={access is null}, " +
                    $"tenantMatch={access?.TenantId == expected.TenantId}, userMatch={access?.UserId == expected.UserId}, " +
                    $"deviceMatch={access?.DeviceId == expected.DeviceId}, revision='{access?.Revision}' perms={access?.Permissions?.Count}) ⇒ live lease dropped.", "live");
                return Result.Fail(ErrorCodes.SyncProtocolViolation);
            }
            var latest = await sessions.ReadAsync(linked.Token).ConfigureAwait(false);
            if (!StillCurrent() || latest.IsFailure || latest.Value is not { } currentSession || currentSession.UserId != expected.UserId
                || currentSession.TenantId != expected.TenantId || currentSession.DeviceId != expected.DeviceId)
                return Result.Fail(ErrorCodes.SessionExpired);

            if (_revision != access.Revision || _appliedGeneration != generation || Environment.TickCount64 >= _nextMirrorTick)
            {
                // Once the server has announced different authority, even an interrupted mirror must not
                // fall back to an obsolete local grant. The next pass retries without requiring another login.
                TraceLog.Info($"[live] RefreshAsync → MIRROR TRIGGERED (revisionChanged={_revision != access.Revision} " +
                    $"genChanged={_appliedGeneration != generation} periodicDue={Environment.TickCount64 >= _nextMirrorTick}) " +
                    $"rev='{access.Revision}' gen={generation}; mirroring GET /auth/keys + POST /auth/capabilities.", "live");
                gate.ClearOnlineAccess();
                var applied = await mirror.RefreshLocalAccessAsync(access, currentSession, StillCurrent, linked.Token).ConfigureAwait(false);
                if (applied.IsFailure)
                {
                    // Only wipe the still-valid signed offline snapshot when the server (or local store) tells us the
                    // authority itself is gone/changed — a revoked device, an expired session, or grants that changed
                    // during the fetch. A merely transient failure (network down/timeout/server busy) while running the
                    // routine periodic renewal must NOT destroy a snapshot that is still inside its signed, self-expiring
                    // window: doing so blanked the whole shell (every nav entry gated off ⇒ empty sidebar + read-only
                    // banner) after the app sat idle past the 5-minute renewal, and a manual "Sync Now" could not recover
                    // it because that path never re-warms the gate. Keep the offline authority and retry next pass.
                    var authorityRemoved = IsAuthorityRemoved(applied.Error);
                    if (authorityRemoved)
                    {
                        gate.Clear();
                        TraceLog.Error($"[live] RefreshAsync → MIRROR FAILED with AUTHORITY-REMOVED error ({applied.Error} '{applied.Detail}') ⇒ " +
                            "gate.Clear(): this is a shell-blanking path (offline snapshot dropped). Verify the server actually removed authority.", null, "live");
                    }
                    else
                    {
                        // Transient renewal failure: back the NEXT mirror attempt off so the 4–6s loop does not keep
                        // firing GET /auth/keys + POST /auth/capabilities on every pass. Left unthrottled, that burst
                        // tips the 10-req/60s auth window into sustained 429s that never clear — a self-reinforcing
                        // storm that leaves the live lease permanently un-renewed and the shell stuck read-only.
                        // The signed offline snapshot is still valid; keep it and let the next window renew normally.
                        gate.ClearOnlineAccess();
                        _nextMirrorTick = Environment.TickCount64 + MirrorTransientBackoffTicks;
                        TraceLog.Warn($"[live] RefreshAsync → mirror TRANSIENT failure ({applied.Error} '{applied.Detail}'); " +
                            $"offline snapshot KEPT, next mirror attempt backed off {MirrorTransientBackoffTicks}ms. Shell should stay usable.", "live");
                    }
                    return applied;
                }
                if (!StillCurrent()) return Result.Fail(ErrorCodes.SessionExpired);
                await gate.RefreshAsync(linked.Token).ConfigureAwait(false);
                // Gate refresh is deliberately non-throwing/fail-closed. Do not mark a failed warm-up as
                // applied for five minutes: retry next pass, without another sign-in.
                if (!StillCurrent() || !gate.HasCurrentView(expected.UserId))
                {
                    // Warm-up could not publish a view (local store blip) yet the snapshot write itself succeeded.
                    // Back off too: hammering the mirror again every pass cannot fix a local-store problem and only
                    // burns the auth rate-limit budget. The already-published offline view keeps the shell usable.
                    _nextMirrorTick = Environment.TickCount64 + MirrorTransientBackoffTicks;
                    TraceLog.Warn($"[live] RefreshAsync → gate warm-up did NOT publish a view (stillCurrent={StillCurrent()}); " +
                        $"backing off {MirrorTransientBackoffTicks}ms. If no prior view exists the shell is read-only until the next good pass.", "live");
                    return Result.Fail(ErrorCodes.StoreUnavailable);
                }
                _revision = access.Revision;
                _appliedGeneration = generation;
                _nextMirrorTick = Environment.TickCount64 + MirrorIntervalTicks;
                TraceLog.Info($"[live] RefreshAsync → MIRROR APPLIED rev='{access.Revision}' gen={generation}; next mirror in {MirrorIntervalTicks}ms.", "live");
            }
            if (StillCurrent()) gate.SetOnlineAccess(expected.UserId, access.Permissions, generation);
            return Result.Ok();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && !_stop.IsCancellationRequested)
        {
            // The 8-second online probe (or the mirror) timed out — transient by definition. Drop the live lease but
            // keep the signed offline snapshot so the shell stays usable; never clear authority here even mid-replace.
            gate.ClearOnlineAccess();
            TraceLog.Warn("[live] RefreshAsync → 8s online probe/mirror TIMED OUT (transient); live lease dropped, offline snapshot kept.", "live");
            return Result.Fail(ErrorCodes.NetworkUnavailable);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An unexpected transport/store fault is also transient; keep the offline snapshot and retry next pass.
            gate.ClearOnlineAccess();
            TraceLog.Error("[live] RefreshAsync → unexpected transport/store fault (transient); live lease dropped, offline snapshot kept.", ex, "live");
            logger.LogWarning(ex, "Active access refresh failed; no online authority was renewed.");
            return Result.Fail(ErrorCodes.StoreUnavailable);
        }
        finally { if (entered) _flight.Release(); }
    }

    /// <summary>
    /// True only when a refresh failure means the authority itself is gone/changed on the server side — the device
    /// was revoked, the session expired, the user/tenant/device binding no longer matches, the server-signed snapshot
    /// is untrusted, or the grants changed during the fetch. In those cases the stale offline snapshot must be dropped
    /// so it cannot keep authorizing writes. Every other failure (network unavailable, server busy, local store
    /// unavailable) is transient: the signed offline window is still valid and must be preserved so the shell keeps
    /// working offline and recovers on the next successful pass — otherwise an idle app blanks to a read-only shell.
    /// </summary>
    private static bool IsAuthorityRemoved(ErrorCode error) =>
        error == ErrorCodes.SessionExpired
        || error == ErrorCodes.DeviceRevoked
        || error == ErrorCodes.CapabilityNotGranted
        || error == ErrorCodes.CapabilitySnapshotTampered
        || error == ErrorCodes.ConcurrencyConflict
        || error == ErrorCodes.TenantMismatch
        || error == ErrorCodes.Unauthorized
        || error == ErrorCodes.Forbidden;

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await RefreshAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                gate.ClearOnlineAccess();
                logger.LogWarning(ex, "Active access refresh failed; retrying without renewing online authority.");
            }
            try { await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(4000, 6001)), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_loop is not null) await _loop.ConfigureAwait(false);
        await _flight.WaitAsync().ConfigureAwait(false);
        _flight.Release();
        _stop.Dispose();
        _flight.Dispose();
    }
}
