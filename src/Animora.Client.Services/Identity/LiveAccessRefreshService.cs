using System.Net;
using Animora.Application.Security;
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
        var replacingAuthority = false;
        try
        {
            await _flight.WaitAsync(linked.Token).ConfigureAwait(false);
            entered = true;
            var local = cache.Current;
            var generation = gate.Generation;
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
                        if (code == ErrorCodes.DeviceRevoked.Code)
                            await devices.HandleRevokedAsync(linked.Token).ConfigureAwait(false);
                        await using var scope = scopes.CreateAsyncScope();
                        await scope.ServiceProvider.GetRequiredService<ISessionService>()
                            .EndAsync(code ?? ErrorCodes.SessionExpired.Code, linked.Token).ConfigureAwait(false);
                    }
                    return Result.Fail(ErrorCodes.SessionExpired);
                }
                return Result.Fail(OnlineApi.IsTransient(response.StatusCode) ? ErrorCodes.ServerBusy : ErrorCodes.SyncProtocolViolation);
            }
            var access = await OnlineApi.ReadAsync<CurrentAccessWire>(response, linked.Token).ConfigureAwait(false);
            if (access is null || access.TenantId != expected.TenantId || access.UserId != expected.UserId
                || access.DeviceId != expected.DeviceId || string.IsNullOrWhiteSpace(access.Revision)
                || access.Permissions is null || access.Permissions.Count > PermissionCodes.All.Count
                || access.Permissions.Any(code => string.IsNullOrWhiteSpace(code) || !PermissionCodes.Exists(code)))
            {
                gate.ClearOnlineAccess();
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
                replacingAuthority = true;
                gate.ClearOnlineAccess();
                var applied = await mirror.RefreshLocalAccessAsync(access, currentSession, StillCurrent, linked.Token).ConfigureAwait(false);
                if (applied.IsFailure)
                {
                    // The server explicitly removed grants: stale offline permissions must not remain usable
                    // while the new snapshot is being retried (including concurrent edits during the fetch).
                    gate.Clear();
                    return applied;
                }
                if (!StillCurrent()) return Result.Fail(ErrorCodes.SessionExpired);
                await gate.RefreshAsync(linked.Token).ConfigureAwait(false);
                // Gate refresh is deliberately non-throwing/fail-closed. Do not mark a failed warm-up as
                // applied for five minutes: retry next pass, without another sign-in.
                if (!StillCurrent() || !gate.HasCurrentView(expected.UserId))
                    return Result.Fail(ErrorCodes.StoreUnavailable);
                _revision = access.Revision;
                _appliedGeneration = generation;
                _nextMirrorTick = Environment.TickCount64 + (long)TimeSpan.FromMinutes(5).TotalMilliseconds;
            }
            if (StillCurrent()) gate.SetOnlineAccess(expected.UserId, access.Permissions, generation);
            return Result.Ok();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && !_stop.IsCancellationRequested)
        {
            if (replacingAuthority) gate.Clear();
            else gate.ClearOnlineAccess();
            return Result.Fail(ErrorCodes.NetworkUnavailable);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (replacingAuthority) gate.Clear();
            else gate.ClearOnlineAccess();
            logger.LogWarning(ex, "Active access refresh failed; no online authority was renewed.");
            return Result.Fail(ErrorCodes.StoreUnavailable);
        }
        finally { if (entered) _flight.Release(); }
    }

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
