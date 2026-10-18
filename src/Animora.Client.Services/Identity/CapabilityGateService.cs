using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Contracts.Diagnostics;
using Animora.Contracts.Identity;
using Animora.Domain.Identity;
using Animora.Domain.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Identity;

/// <summary>
/// Singleton implementation of the Desktop-facing <see cref="ICapabilityGate"/> (B04c). It never hits the store
/// synchronously and never blocks a thread (<c>.Result</c>/<c>.Wait()</c>/<c>GetAwaiter().GetResult()</c>): the
/// synchronous <see cref="Can"/>/<see cref="GetState"/> methods only read an atomically published, per-snapshot
/// view plus a bounded result cache, re-applying the time-sensitive B04b checks so an elapsed window can never
/// stay cached as Allowed.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RefreshAsync"/> (awaited by <see cref="AuthenticationService"/> right after a successful sign-in /
/// first run, before the shell is built) opens one short async scope, loads the session tenant/user/device
/// snapshot, re-verifies it with the B04b <see cref="CapabilityEvaluator"/> (signature, stamp, expiry,
/// high-water), and precomputes the decision for every declared <see cref="PermissionCodes.All"/> code —
/// including the B04a RBAC decision via <see cref="IPermissionEvaluator"/>.
/// </para>
/// <para>
/// Fail closed everywhere: before warm-up, after <see cref="Clear"/>, on a locked/expired/absent session, on a
/// missing or tampered snapshot (which is dropped whole and never re-signed), and on a tenant/user/device/stamp
/// mismatch. A tampered snapshot leaves the gate read-only/denied until a successful refresh.
/// </para>
/// </remarks>
public sealed class CapabilityGateService : ICapabilityGate
{
    /// <summary>The hard upper bound on cached decisions (42 codes × a few snapshots before eviction).</summary>
    internal const int MaxCacheEntries = 256;

    private readonly IServiceScopeFactory _scopes;
    private readonly IClock _clock;
    private readonly IMonotonicClock _monotonic;
    private readonly CapabilityResultCache _cache = new(MaxCacheEntries);

    private volatile CapabilitySnapshotView? _view;
    private volatile OnlineAccessView? _onlineAccess;
    private readonly object _publicationLock = new();
    private long _generation;
    internal long Generation => Interlocked.Read(ref _generation);

    // Online-only permissions never enter an offline snapshot. A short, monotonic lease is renewed only by
    // an authenticated server response; wall-clock rollback cannot keep it alive after connectivity is lost.
    private sealed record OnlineAccessView(string UserId, IReadOnlySet<string> Codes, long UntilTick);

    internal void SetOnlineAccess(string userId, IEnumerable<string> codes, long generation)
    {
        bool changed;
        int count;
        lock (_publicationLock)
        {
            if (generation != _generation || _view?.UserId != userId)
            {
                // A stale lease (generation moved, or the view belongs to another user) must not be applied — a
                // silent no-op here was previously invisible; it is now the first thing to look for when a live
                // lease never seems to take effect after a re-auth.
                TraceLog.Debug($"[gate] SetOnlineAccess IGNORED (stale): gen={generation} current={_generation} viewUser={_view?.UserId ?? "(none)"} reqUser={userId}", "gate");
                return;
            }
            var granted = new HashSet<string>(codes, StringComparer.Ordinal);
            count = granted.Count;
            var previous = _onlineAccess;
            changed = previous is null || previous.UntilTick <= Environment.TickCount64
                || previous.UserId != userId || !granted.SetEquals(previous.Codes);
            _onlineAccess = new OnlineAccessView(userId, granted, Environment.TickCount64 + 15000);
        }
        TraceLog.Debug($"[gate] SetOnlineAccess user={userId} codes={count} changed={changed} leaseTtl=15000ms gen={generation}", "gate");
        if (changed) RaiseChanged();
    }

    internal void ClearOnlineAccess()
    {
        bool changed;
        lock (_publicationLock)
        {
            changed = _onlineAccess is not null;
            _onlineAccess = null;
        }
        TraceLog.Debug($"[gate] ClearOnlineAccess (had lease={changed})", "gate");
        if (changed) RaiseChanged();
    }

    internal bool HasCurrentView(string userId) => _view is { Tampered: false } view && view.UserId == userId;

    public CapabilityGateService(IServiceScopeFactory scopes, IClock clock, IMonotonicClock monotonic)
    {
        _scopes = scopes;
        _clock = clock;
        _monotonic = monotonic;
    }

    /// <inheritdoc />
    public event EventHandler? CapabilitiesChanged;

    /// <inheritdoc />
    public bool Can(string permissionCode) => GetState(permissionCode) == CapabilityState.Allowed;

    /// <inheritdoc />
    public bool CanAny(params string[] codes)
    {
        if (codes is null || codes.Length == 0) return false;
        foreach (var code in codes)
        {
            if (Can(code)) return true;
        }

        return false;
    }

    /// <inheritdoc />
    public CapabilityState GetState(string permissionCode)
    {
        if (string.IsNullOrWhiteSpace(permissionCode)) return CapabilityState.Denied;
        var view = _view;
        if (view is null) return CapabilityState.Denied;
        if (view.Tampered) return CapabilityState.Denied; // a tampered snapshot grants nothing.
        if (view.SessionExpiresAtUtc is { } expired && _clock.UtcNow >= expired) return CapabilityState.Denied;
        var online = _onlineAccess;
        if (online is not null && online.UserId == view.UserId && Environment.TickCount64 < online.UntilTick)
            return online.Codes.Contains(permissionCode) ? CapabilityState.Allowed : CapabilityState.Denied;

        var key = new CapabilityCacheKey(view.TenantId, view.UserId, view.DeviceId, permissionCode, view.SnapshotId);
        if (!_cache.TryGet(key, out var cached)) return CapabilityState.Denied; // unknown / not precomputed.
        if (cached != CapabilityState.Allowed) return cached;

        // Time-sensitive re-validation: an elapsed window may not remain cached as Allowed.
        var now = _clock.UtcNow;
        if (view.SessionExpiresAtUtc is { } sessionExpiry && now >= sessionExpiry) return CapabilityState.Denied;
        var effective = now >= view.HighWaterUtc ? now : view.HighWaterUtc;
        if (view.IssuedAtUtc > effective + CapabilityEvaluator.RollbackTolerance) return CapabilityState.ExpiredOffline;
        if (view.AbsoluteExpiryUtc <= effective) return CapabilityState.ExpiredOffline;
        if (view.TryGetCodeExpiry(permissionCode, out var codeExpiry) && codeExpiry <= effective)
            return CapabilityState.ExpiredOffline;
        return CapabilityState.Allowed;
    }

    /// <summary>
    /// Warms the gate from the current local session (B04c). Called by <see cref="AuthenticationService"/> only
    /// after a successful sign-in / first run, before the shell is constructed. Never throws.
    /// </summary>
    internal async Task RefreshAsync(CancellationToken ct = default)
    {
        var generation = Generation;
        var startedTicks = Environment.TickCount64;
        TraceLog.Info($"[gate] RefreshAsync BEGIN gen={generation} ctCancelled={ct.IsCancellationRequested}", "gate");
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;

            var installation = services.GetRequiredService<ILocalInstallationIdentity>();
            var sessions = services.GetRequiredService<ISessionService>();
            var users = services.GetRequiredService<IIdentityRepository>();
            var store = services.GetRequiredService<ICapabilityStore>();
            var evaluator = services.GetRequiredService<CapabilityEvaluator>();
            var permissions = services.GetRequiredService<IPermissionEvaluator>();

            var restored = await sessions.RestoreAsync(ct).ConfigureAwait(false);
            if (restored.IsFailure || restored.Value is not { } session
                || session.UserId is not { Length: > 0 } userId || session.IsLocked)
            {
                var userIdEmpty = string.IsNullOrEmpty(restored.Value?.UserId);
                var locked = restored.Value?.IsLocked ?? false;
                TraceLog.Warn($"[gate] RefreshAsync → DENY-ALL: no usable session " +
                    $"(restoreFailed={restored.IsFailure}, sessionNull={restored.Value is null}, " +
                    $"userIdEmpty={userIdEmpty}, locked={locked}). " +
                    "Gate publishes null ⇒ every nav entry + write is gated OFF (empty sidebar + read-only banner).", "gate");
                Publish(null, generation); // no active session ⇒ deny everything.
                return;
            }

            var tenantId = installation.TenantId;
            var deviceId = installation.DeviceId;
            var user = await users.FindAsync(userId, ct).ConfigureAwait(false);
            var securityStamp = user?.SecurityStamp ?? string.Empty;
            if (string.IsNullOrEmpty(securityStamp))
            {
                TraceLog.Warn($"[gate] RefreshAsync → DENY-ALL: no security stamp (userFound={user is not null}) user={userId} tenant={tenantId} device={deviceId}.", "gate");
                Publish(null, generation); // cannot prove binding ⇒ deny.
                return;
            }

            var loaded = await store.GetAsync(tenantId, userId, deviceId, ct).ConfigureAwait(false);
            if (loaded.IsFailure)
            {
                TraceLog.Warn($"[gate] RefreshAsync → DENY-ALL: capability store read FAILED ({loaded.Error}) user={userId} tenant={tenantId} device={deviceId}.", "gate");
                Publish(null, generation);
                return;
            }

            var snapshot = loaded.Value;
            if (snapshot is null)
            {
                TraceLog.Warn($"[gate] RefreshAsync → DENY-ALL: NO capability snapshot stored for user={userId} tenant={tenantId} device={deviceId}.", "gate");
                Publish(null, generation); // missing snapshot ⇒ deny.
                return;
            }

            var now = _clock.UtcNow;
            var highWater = _monotonic.HighWaterUtc;
            var subject = new AuthorizationSubject(tenantId, userId, FrozenGrants.Empty, session.Role ?? StaffRole.Receptionist,
                deviceId, IsOffline: false);

            var results = new Dictionary<string, CapabilityState>(PermissionCodes.All.Count, StringComparer.Ordinal);
            var tampered = false;
            foreach (var code in PermissionCodes.All)
            {
                var evaluation = evaluator.Evaluate(snapshot, code, tenantId, userId, deviceId, securityStamp, now, highWater);
                if (evaluation.DiscardSnapshot)
                {
                    TraceLog.Error($"[gate] RefreshAsync → TAMPERED snapshot discarded on code='{code}': {evaluation.Result.Error} " +
                        $"('{evaluation.Result.Detail}'). snapshotId={snapshot.Id} issued={snapshot.IssuedAtUtc:O} absExpiry={snapshot.AbsoluteExpiryUtc:O} " +
                        $"revokedAt={snapshot.RevokedAtUtc?.ToString("O") ?? "(none)"}. Gate becomes read-only until a fresh mirror.", null, "gate");
                    tampered = true; // malformed/tampered row: drop the whole snapshot, never re-sign it.
                    break;
                }

                var state = Map(evaluation.State);
                if (state == CapabilityState.Allowed)
                {
                    var decision = await permissions.EvaluateAsync(code, subject, resource: null, ct).ConfigureAwait(false);
                    if (!decision.Allowed)
                    {
                        TraceLog.Debug($"[gate] code='{code}' offline-Allowed but RBAC/policy DENIED ({decision.Error}) ⇒ Denied.", "gate");
                        state = CapabilityState.Denied; // B04a RBAC/policy/overlay.
                    }
                }

                results[code] = state;
            }

            if (tampered)
            {
                Publish(CapabilitySnapshotView.ForTampered(snapshot), generation);
                return;
            }

            var view = CapabilitySnapshotView.Allowed(snapshot, session.ExpiresAtUtc, highWater, results);
            TraceLog.Info($"[gate] RefreshAsync ← PUBLISH view: snapshotId={snapshot.Id} tenant={tenantId} user={userId} device={deviceId} " +
                $"issued={snapshot.IssuedAtUtc:O} absExpiry={snapshot.AbsoluteExpiryUtc:O} sessionExpiry={session.ExpiresAtUtc?.ToString("O") ?? "(none)"} " +
                $"highWater={highWater:O} now={now:O} allowed={Count(results, CapabilityState.Allowed)}/{results.Count} " +
                $"expiredOffline={Count(results, CapabilityState.ExpiredOffline)} requiresOnline={Count(results, CapabilityState.RequiresOnline)} " +
                $"denied={Count(results, CapabilityState.Denied)}.", "gate");
            Publish(view, generation);
        }
        catch (OperationCanceledException)
        {
            TraceLog.Warn($"[gate] RefreshAsync CANCELLED (ct={ct.IsCancellationRequested}) → DENY-ALL. A warm-up cancelled mid-flight blanks the shell.", "gate");
            Publish(null, generation);
        }
        catch (Exception ex)
        {
            // Never let a warm-up failure escape into the auth path; the gate simply stays denied.
            TraceLog.Error("[gate] RefreshAsync THREW → DENY-ALL (warm-up failure blanks the shell until the next successful refresh).", ex, "gate");
            Publish(null, generation);
        }
        finally
        {
            TraceLog.Debug($"[gate] RefreshAsync END gen={generation} elapsedMs={Environment.TickCount64 - startedTicks}", "gate");
        }
    }

    /// <summary>Counts the decisions in <paramref name="results"/> matching <paramref name="state"/> (diagnostics only).</summary>
    private static int Count(IReadOnlyDictionary<string, CapabilityState> results, CapabilityState state)
    {
        var n = 0;
        foreach (var pair in results)
        {
            if (pair.Value == state) n++;
        }

        return n;
    }

    /// <summary>
    /// Full, human-readable snapshot of the gate's current authority for the read-only-banner investigation. This is
    /// the single call that answers "why is the shell blank / read-only right now?" — it reports the published view
    /// (or its absence), the live online lease, the cache size, the generation, and the per-code decision map. Safe
    /// to call from any thread; reads only the atomically published state.
    /// </summary>
    public string DescribeState()
    {
        var view = _view;
        var online = _onlineAccess;
        var sb = new System.Text.StringBuilder();
        sb.Append("gen=").Append(Generation);
        if (view is null)
        {
            sb.Append(" view=(NONE)  ⇒ fail-closed: every nav entry + write is denied (empty sidebar + read-only banner).");
        }
        else
        {
            sb.Append(" view{snapshotId=").Append(view.SnapshotId)
              .Append(" tenant=").Append(view.TenantId)
              .Append(" user=").Append(view.UserId)
              .Append(" device=").Append(view.DeviceId)
              .Append(" tampered=").Append(view.Tampered)
              .Append(" issued=").Append(view.IssuedAtUtc.ToString("O"))
              .Append(" absExpiry=").Append(view.AbsoluteExpiryUtc.ToString("O"))
              .Append(" sessionExpiry=").Append(view.SessionExpiresAtUtc?.ToString("O") ?? "(none)")
              .Append(" highWater=").Append(view.HighWaterUtc.ToString("O"))
              .Append(" allowed=").Append(Count(view.Results, CapabilityState.Allowed))
              .Append(" expiredOffline=").Append(Count(view.Results, CapabilityState.ExpiredOffline))
              .Append(" requiresOnline=").Append(Count(view.Results, CapabilityState.RequiresOnline))
              .Append(" denied=").Append(Count(view.Results, CapabilityState.Denied))
              .Append("}");
        }

        sb.Append(" onlineLease=");
        if (online is null) sb.Append("(none)");
        else sb.Append("{user=").Append(online.UserId)
                 .Append(" codes=").Append(online.Codes.Count)
                 .Append(" expiresInMs=").Append(online.UntilTick - Environment.TickCount64)
                 .Append("}");
        return sb.ToString();
    }

    /// <summary>Invalidates all authority (sign-out). The gate denies every code until a successful refresh.</summary>
    internal void Clear()
    {
        var previousGen = Generation;
        var hadView = _view is not null;
        lock (_publicationLock)
        {
            Interlocked.Increment(ref _generation);
            _onlineAccess = null;
            _cache.Clear();
            _view = null;
        }
        TraceLog.Warn($"[gate] Clear() ⇒ gen {previousGen}→{Generation}: ALL authority dropped (hadView={hadView}). " +
            "Everything the gate guards is now denied until the next successful RefreshAsync.", "gate");
        RaiseChanged();
    }

    /// <summary>Publishes a new immutable view atomically and raises <see cref="CapabilitiesChanged"/>.</summary>
    private void Publish(CapabilitySnapshotView? view, long generation)
    {
        var published = false;
        lock (_publicationLock)
        {
            // A refresh that started before sign-out must never restore authority afterwards.
            if (generation != _generation)
            {
                TraceLog.Warn($"[gate] Publish SKIPPED: refresh gen={generation} is stale (current={_generation}) — a sign-out/in raced it; authority NOT restored.", "gate");
                return;
            }
            _cache.Clear();
            if (view is { } v && !v.Tampered)
            {
                foreach (var pair in v.Results)
                    _cache.Put(new CapabilityCacheKey(v.TenantId, v.UserId, v.DeviceId, pair.Key, v.SnapshotId), pair.Value);
            }
            if (view is null || view.UserId != _view?.UserId || view.Tampered) _onlineAccess = null;
            _view = view;
            published = true;
        }
        TraceLog.Info($"[gate] Publish → {DescribeState()}", "gate");
        if (published) RaiseChanged();
    }

    private void RaiseChanged()
    {
        // Subscribers are UI ViewModels that marshal to their own thread; never let a handler break the gate.
        var handlers = CapabilitiesChanged;
        if (handlers is null) return;
        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception)
            {
                // A single misbehaving subscriber must not affect the others or the caller.
            }
        }
    }

    private static CapabilityState Map(CapabilityDecisionState state) => state switch
    {
        CapabilityDecisionState.Allowed => CapabilityState.Allowed,
        CapabilityDecisionState.ExpiredOffline => CapabilityState.ExpiredOffline,
        CapabilityDecisionState.RequiresOnline => CapabilityState.RequiresOnline,
        _ => CapabilityState.Denied,
    };

    /// <summary>Empty effective-grant set: the evaluator always recomputes grants from stored facts.</summary>
    private static class FrozenGrants
    {
        public static readonly IReadOnlySet<string> Empty = new HashSet<string>(StringComparer.Ordinal);
    }
}

/// <summary>The five-part cache key: a decision is only valid for one snapshot of one identity on one device.</summary>
internal readonly record struct CapabilityCacheKey(
    string TenantId,
    string UserId,
    string DeviceId,
    string PermissionCode,
    string SnapshotId);

/// <summary>The immutable, atomically published context of the current snapshot plus its precomputed decisions.</summary>
internal sealed class CapabilitySnapshotView
{
    private readonly Dictionary<string, DateTimeOffset> _codeExpiry;

    private CapabilitySnapshotView(string snapshotId, string tenantId, string userId, string deviceId, bool tampered,
        DateTimeOffset issuedAtUtc, DateTimeOffset absoluteExpiryUtc, DateTimeOffset? sessionExpiresAtUtc,
        DateTimeOffset highWaterUtc, Dictionary<string, DateTimeOffset> codeExpiry,
        IReadOnlyDictionary<string, CapabilityState> results)
    {
        SnapshotId = snapshotId;
        TenantId = tenantId;
        UserId = userId;
        DeviceId = deviceId;
        Tampered = tampered;
        IssuedAtUtc = issuedAtUtc;
        AbsoluteExpiryUtc = absoluteExpiryUtc;
        SessionExpiresAtUtc = sessionExpiresAtUtc;
        HighWaterUtc = highWaterUtc;
        _codeExpiry = codeExpiry;
        Results = results;
    }

    public string SnapshotId { get; }

    public string TenantId { get; }

    public string UserId { get; }

    public string DeviceId { get; }

    public bool Tampered { get; }

    public DateTimeOffset IssuedAtUtc { get; }

    public DateTimeOffset AbsoluteExpiryUtc { get; }

    public DateTimeOffset? SessionExpiresAtUtc { get; }

    public DateTimeOffset HighWaterUtc { get; }

    public IReadOnlyDictionary<string, CapabilityState> Results { get; }

    public bool TryGetCodeExpiry(string code, out DateTimeOffset expiry) => _codeExpiry.TryGetValue(code, out expiry);

    public static CapabilitySnapshotView Allowed(StoredCapabilitySnapshot snapshot, DateTimeOffset? sessionExpiresAtUtc,
        DateTimeOffset highWaterUtc, IReadOnlyDictionary<string, CapabilityState> results)
    {
        var parsed = CapabilitySnapshotCodec.Parse(snapshot.PermissionsJson, snapshot.PerCapabilityExpiryJson);
        var expiry = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        if (parsed.IsSuccess)
        {
            foreach (var pair in parsed.Value!.Expiry) expiry[pair.Key] = pair.Value;
        }

        return new CapabilitySnapshotView(snapshot.Id, snapshot.TenantId, snapshot.UserId, snapshot.DeviceId,
            tampered: false, snapshot.IssuedAtUtc, snapshot.AbsoluteExpiryUtc, sessionExpiresAtUtc, highWaterUtc,
            expiry, results);
    }

    public static CapabilitySnapshotView ForTampered(StoredCapabilitySnapshot snapshot) =>
        new(snapshot.Id, snapshot.TenantId, snapshot.UserId, snapshot.DeviceId, tampered: true, snapshot.IssuedAtUtc,
            snapshot.AbsoluteExpiryUtc, sessionExpiresAtUtc: null, highWaterUtc: default,
            new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal),
            new Dictionary<string, CapabilityState>(StringComparer.Ordinal));
}

/// <summary>
/// Bounded (max <paramref name="capacity"/> entries) decision cache; overflow evicts the oldest insertion. Not
/// thread-safe by design — every mutation happens inside the single-flight <c>RefreshAsync</c>/<c>Clear</c> path;
/// reads race harmlessly against a decision that is either the old or the new snapshot value.
/// </summary>
internal sealed class CapabilityResultCache(int capacity)
{
    private readonly Dictionary<CapabilityCacheKey, CapabilityState> _entries = [];
    private readonly Queue<CapabilityCacheKey> _order = new();

    public void Clear()
    {
        _entries.Clear();
        _order.Clear();
    }

    public void Put(CapabilityCacheKey key, CapabilityState state)
    {
        if (_entries.ContainsKey(key))
        {
            _entries[key] = state; // refresh of the same key: no new insertion, keep FIFO position stable.
            return;
        }

        while (_entries.Count >= capacity && _order.Count > 0)
        {
            _entries.Remove(_order.Dequeue()); // evict the oldest insertion.
        }

        _entries[key] = state;
        _order.Enqueue(key);
    }

    public bool TryGet(CapabilityCacheKey key, out CapabilityState state) => _entries.TryGetValue(key, out state);
}
