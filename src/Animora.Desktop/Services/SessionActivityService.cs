using System;
using System.Threading;
using System.Threading.Tasks;
using Animora.Application.Security;
using Animora.Desktop.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Desktop.Services;

/// <summary>
/// Keeps the durable local session's idle-timeout alive from <b>real</b> user input so the capability gate never
/// fails closed to the read-only shell while the operator is actively working.
/// </summary>
/// <remarks>
/// <para>
/// <b>Root cause this fixes.</b> The local session auto-locks after <see cref="ISessionService"/>'s idle timeout
/// (30 min) inside <c>RestoreAsync</c> — the authoritative read the capability gate's warm-up performs on every
/// periodic live-access mirror (~every 5 min). Nothing in the client ever called <see cref="ISessionService.TouchAsync"/>,
/// so a session's <c>LastActivityUtc</c> was only ever set at sign-in: exactly 30 minutes later the periodic mirror's
/// warm-up found the session idle, locked it as a side effect, and the gate published an empty view
/// (<c>DENY-ALL</c>) — every nav entry + write gated off ⇒ empty sidebar + read-only banner, unrecoverable until restart.
/// </para>
/// <para>
/// <b>Why here.</b> Idle-lock must be driven by actual interaction, which belongs in the desktop shell, not in the
/// shared dispatch pipeline (that runs server-side too, with no local session). The shell's global input surface is
/// the single place that sees all pointer/keyboard activity, so it pings this service, and this service forwards the
/// activity to <see cref="ISessionService.TouchAsync"/>.
/// </para>
/// <para>
/// <b>Cheap by construction.</b> A client-side throttle drops the flood of input events to at most one forward per
/// <see cref="MinForwardInterval"/> (well under the 30-min idle window and the session service's own 60 s write
/// throttle), and a single-flight guard means at most one <c>TouchAsync</c> is in flight at a time. <c>TouchAsync</c>
/// itself no-ops cheaply when its own throttle window has not elapsed, so an idle app is never written to.
/// </para>
/// </remarks>
public sealed class SessionActivityService : IDisposable
{
    // Forward at most one activity touch per this interval. Far below the 30-minute idle window, and at or just under
    // the session service's own 60 s persisted write throttle, so this never becomes a write amplifier.
    private static readonly TimeSpan MinForwardInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceProvider _services;
    private long _lastForwardTick;
    private int _inFlight;

    public SessionActivityService(IServiceProvider services)
    {
        _services = services;
        // Seed the throttle window so the very first interaction is forwarded immediately.
        _lastForwardTick = Environment.TickCount64 - (long)MinForwardInterval.TotalMilliseconds;
    }

    /// <summary>
    /// Records a real user activity (pointer/keyboard) and, throttled, keeps the session's idle window alive.
    /// Never throws; safe to call from the UI thread on every input event.
    /// </summary>
    public void Ping()
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastForwardTick);
        if (now - last < (long)MinForwardInterval.TotalMilliseconds) return;

        // Claim the turn; a concurrent ping that loses the race is simply dropped — one touch is enough.
        if (Interlocked.CompareExchange(ref _lastForwardTick, now, last) != last) return;
        if (Interlocked.Exchange(ref _inFlight, 1) == 1) return;

        _ = ForwardAsync();
    }

    private async Task ForwardAsync()
    {
        try
        {
            // ISessionService is an app-wide singleton; resolve it directly and let TouchAsync open its own scope.
            // A missing registration (e.g. a trimmed/headless composition) must never fault the shell.
            var sessions = _services.GetService<ISessionService>();
            if (sessions is null) return;

            var result = await sessions.TouchAsync().ConfigureAwait(false);
            // Failure is expected and harmless (locked/expired/no-session) — it must not surface as UI noise.
            if (result.IsSuccess)
                AppTrace.Debug("[activity] session touched from user input.", "activity");
        }
        catch (Exception ex)
        {
            // Activity pinging is best-effort; a transient local-store blip must never crash or block the shell.
            AppTrace.Warn($"[activity] session touch skipped (transient): {ex.Message}", "activity");
        }
        finally
        {
            Interlocked.Exchange(ref _inFlight, 0);
        }
    }

    public void Dispose() => Interlocked.Exchange(ref _inFlight, 0);
}
