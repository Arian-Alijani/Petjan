using Animora.Application.Security;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Infrastructure.Local.Options;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Animora.Infrastructure.Local.Security;

/// <summary>
/// App-wide local session over the durable <see cref="LocalSession"/> row (R-B09). Singleton: each
/// operation runs in its own DI scope; operations are serialized so lock/unlock/end never race.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RestoreAsync"/> is the authoritative session read used by the capability gate's warm-up
/// (<c>CapabilityGateService.RefreshAsync</c>). It is <b>not</b> a pure read: an idle-timed-out session is
/// <b>locked as a side effect</b> and an expired/disabled one is ended. Those mutations must not stay invisible
/// to the rest of the app. The shared <see cref="SessionContextCache"/> is the single source of truth the live
/// access loop (<c>LiveAccessRefreshService</c>) reads to decide whether to keep renewing. If this service locked
/// the durable row but left the cache showing <c>IsLocked=false</c>, the two diverged permanently: the gate read
/// <c>locked=true</c> and failed closed (blank/read-only shell) while the live loop kept seeing <c>locked=false</c>,
/// re-triggered mirrors that each fail-closed, and the shell could never recover without a restart. Keeping the
/// cache in lock-step with every authoritative restore removes that split-brain.
/// </para>
/// </remarks>
public sealed class LocalSessionService(
    IServiceScopeFactory scopes,
    IClock clock,
    Application.Security.IPasswordHasher hasher,
    IOptions<LocalSessionOptions> options,
    SessionContextCache sessionCache) : ISessionService, IDisposable
{
    public const string ReasonSignOut = "SIGN_OUT";
    public const string ReasonReplaced = "REPLACED";
    public const string ReasonUserUnavailable = "USER_UNAVAILABLE";

    private readonly LocalSessionOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<Result<SessionSnapshot>> RestoreAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var session = await FindActiveAsync(db, ct).ConfigureAwait(false);
            if (session is null)
            {
                var signedOut = await SignedOutAsync(db, ct).ConfigureAwait(false);
                PublishToCache(signedOut);
                return Result<SessionSnapshot>.Ok(signedOut);
            }

            var now = clock.UtcNow;
            if (now >= session.ExpiresAtUtc)
            {
                session.End(ErrorCodes.SessionExpired.Code, now);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                PublishToCache(SessionSnapshot.SignedOut(isFirstRun: false));
                return Result<SessionSnapshot>.Fail(ErrorCodes.SessionExpired);
            }

            var user = await db.Set<User>().FirstOrDefaultAsync(u => u.Id == session.UserId, ct).ConfigureAwait(false);
            if (user is null || user.Status == UserStatus.Disabled)
            {
                session.End(ReasonUserUnavailable, now);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                PublishToCache(SessionSnapshot.SignedOut(isFirstRun: false));
                return Result<SessionSnapshot>.Fail(ErrorCodes.UserDisabled);
            }

            // NOTE (A-RULE 029, revised): inactivity NEVER locks the session. Leaving the app open and idle, or
            // not interacting for a long stretch, must never drop the shell to the read-only/locked state — only an
            // explicit lock (LockAsync) or a hard-lifetime / online-entitlement failure may end usable access. The
            // subscription check the operator must stay reachable for is enforced by the absolute lifetime above and
            // by the online live-access/entitlement loop, not by a local idle timer.
            var snapshot = await SnapshotAsync(db, session, user, ct).ConfigureAwait(false);
            // The gate's warm-up calls RestoreAsync directly; publishing the resolved lock/sign-out state here keeps
            // the shared cache (read by the live-access loop) from ever disagreeing with the gate — the split-brain
            // that otherwise strands the shell read-only after an idle-timeout lock.
            PublishToCache(snapshot);
            return Result<SessionSnapshot>.Ok(snapshot);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<SessionSnapshot>> StartAsync(string userId, string deviceId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(deviceId))
            return Result<SessionSnapshot>.Fail(ErrorCodes.ValidationFailed, "userId and deviceId are required.");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var user = await db.Set<User>().FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
            if (user is null) return Result<SessionSnapshot>.Fail(ErrorCodes.NotFound, "User not found.");
            if (user.Status == UserStatus.Disabled) return Result<SessionSnapshot>.Fail(ErrorCodes.UserDisabled);

            var now = clock.UtcNow;
            var previous = await db.Set<LocalSession>().AsTracking()
                .Where(s => s.EndedAtUtc == null).ToListAsync(ct).ConfigureAwait(false);
            foreach (var old in previous) old.End(ReasonReplaced, now);
            if (previous.Count > 0) await db.SaveChangesAsync(ct).ConfigureAwait(false); // frees the one-active index

            var session = LocalSession.Start(userId, deviceId, now, _options.AbsoluteLifetime);
            db.Add(session);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            var snapshot = await SnapshotAsync(db, session, user, ct).ConfigureAwait(false);
            PublishToCache(snapshot);
            return Result<SessionSnapshot>.Ok(snapshot);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result> LockAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var session = await FindActiveAsync(db, ct).ConfigureAwait(false);
            if (session is null) return Result.Fail(ErrorCodes.NotFound, "No active session.");
            if (session.IsLocked)
            {
                sessionCache.MarkLocked();
                return Result.Ok();
            }

            session.Lock();
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            // Mirror the lock into the shared cache so the live-access loop stops renewing authority at once,
            // rather than discovering the lock only on the next full restore.
            sessionCache.MarkLocked();
            return Result.Ok();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<SessionSnapshot>> UnlockAsync(string password, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var session = await FindActiveAsync(db, ct).ConfigureAwait(false);
            if (session is null) return Result<SessionSnapshot>.Fail(ErrorCodes.NotFound, "No active session.");

            var now = clock.UtcNow;
            if (now >= session.ExpiresAtUtc)
            {
                session.End(ErrorCodes.SessionExpired.Code, now);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return Result<SessionSnapshot>.Fail(ErrorCodes.SessionExpired);
            }

            var user = await db.Set<User>().AsTracking().FirstOrDefaultAsync(u => u.Id == session.UserId, ct).ConfigureAwait(false);
            if (user is null || user.Status == UserStatus.Disabled)
            {
                session.End(ReasonUserUnavailable, now);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return Result<SessionSnapshot>.Fail(ErrorCodes.UserDisabled);
            }

            if (user.IsLockedOut(now)) return Result<SessionSnapshot>.Fail(ErrorCodes.UserLockedOut);

            var outcome = user.PasswordHash is null
                ? PasswordVerificationOutcome.Failed
                : hasher.Verify(user.PasswordHash, password);
            if (outcome == PasswordVerificationOutcome.Failed)
            {
                var lockedNow = user.RegisterFailedAttempt(clock);
                if (lockedNow) session.End(ErrorCodes.UserLockedOut.Code, now);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return Result<SessionSnapshot>.Fail(lockedNow ? ErrorCodes.UserLockedOut : ErrorCodes.CredentialsInvalid);
            }

            // Rehash (SuccessRehashNeeded) is the sign-in handler's job (B03b); unlock does not change the stamp.
            user.ClearFailures();
            session.Unlock(now);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            var snapshot = await SnapshotAsync(db, session, user, ct).ConfigureAwait(false);
            PublishToCache(snapshot);
            return Result<SessionSnapshot>.Ok(snapshot);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result> EndAsync(string reason, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var session = await FindActiveAsync(db, ct).ConfigureAwait(false);
            if (session is null)
            {
                PublishToCache(SessionSnapshot.SignedOut(isFirstRun: false));
                return Result.Ok(); // idempotent
            }

            session.End(string.IsNullOrWhiteSpace(reason) ? ReasonSignOut : reason, clock.UtcNow);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            PublishToCache(SessionSnapshot.SignedOut(isFirstRun: false));
            return Result.Ok();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    /// <summary>
    /// Keeps the shared <see cref="SessionContextCache"/> in lock-step with the authoritative durable session an
    /// operation just resolved. This is what prevents the live-access loop and the capability gate from reading two
    /// different lock states: whoever restored the session last (the gate's warm-up included) publishes the truth.
    /// </summary>
    private void PublishToCache(SessionSnapshot snapshot) => sessionCache.Set(snapshot);

    private static Task<LocalSession?> FindActiveAsync(AnimoraLocalDbContext db, CancellationToken ct) =>
        db.Set<LocalSession>().AsTracking().FirstOrDefaultAsync(s => s.EndedAtUtc == null, ct);

    private static async Task<SessionSnapshot> SignedOutAsync(AnimoraLocalDbContext db, CancellationToken ct) =>
        SessionSnapshot.SignedOut(isFirstRun: !await db.Set<User>().AnyAsync(ct).ConfigureAwait(false));

    private static async Task<SessionSnapshot> SnapshotAsync(AnimoraLocalDbContext db, LocalSession session, User user,
        CancellationToken ct)
    {
        var role = await db.Set<StaffMembership>()
            .Where(m => m.UserId == user.Id && m.Status == MembershipStatus.Active && m.EndedUtc == null)
            .Select(m => (StaffRole?)m.Role)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return new SessionSnapshot(user.Id, user.DisplayName, role, session.IsLocked, IsFirstRun: false, session.ExpiresAtUtc);
    }
}
