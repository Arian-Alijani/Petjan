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
public sealed class LocalSessionService(
    IServiceScopeFactory scopes,
    IClock clock,
    Application.Security.IPasswordHasher hasher,
    IOptions<LocalSessionOptions> options) : ISessionService, IDisposable
{
    public const string ReasonSignOut = "SIGN_OUT";
    public const string ReasonReplaced = "REPLACED";
    public const string ReasonUserUnavailable = "USER_UNAVAILABLE";

    private readonly LocalSessionOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset? _lastTouchWriteUtc;

    public async Task<Result<SessionSnapshot>> RestoreAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var session = await FindActiveAsync(db, ct).ConfigureAwait(false);
            if (session is null) return Result<SessionSnapshot>.Ok(await SignedOutAsync(db, ct).ConfigureAwait(false));

            var now = clock.UtcNow;
            if (now >= session.ExpiresAtUtc)
            {
                session.End(ErrorCodes.SessionExpired.Code, now);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return Result<SessionSnapshot>.Fail(ErrorCodes.SessionExpired);
            }

            var user = await db.Set<User>().FirstOrDefaultAsync(u => u.Id == session.UserId, ct).ConfigureAwait(false);
            if (user is null || user.Status == UserStatus.Disabled)
            {
                session.End(ReasonUserUnavailable, now);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return Result<SessionSnapshot>.Fail(ErrorCodes.UserDisabled);
            }

            // Idle past the timeout while the app was closed → resume locked, never signed in silently.
            if (!session.IsLocked && now - session.LastActivityUtc >= _options.IdleTimeout)
            {
                session.Lock();
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            _lastTouchWriteUtc = session.LastActivityUtc;
            return Result<SessionSnapshot>.Ok(await SnapshotAsync(db, session, user, ct).ConfigureAwait(false));
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
            _lastTouchWriteUtc = now;
            return Result<SessionSnapshot>.Ok(await SnapshotAsync(db, session, user, ct).ConfigureAwait(false));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result> TouchAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        if (_lastTouchWriteUtc is { } last && now - last < _options.TouchThrottle) return Result.Ok();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_lastTouchWriteUtc is { } again && now - again < _options.TouchThrottle) return Result.Ok();
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var session = await FindActiveAsync(db, ct).ConfigureAwait(false);
            if (session is null) return Result.Fail(ErrorCodes.NotFound, "No active session.");
            if (now >= session.ExpiresAtUtc) return Result.Fail(ErrorCodes.SessionExpired);
            if (session.IsLocked) return Result.Fail(ErrorCodes.PermissionDenied, "Session is locked.");

            session.Touch(now);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            _lastTouchWriteUtc = now;
            return Result.Ok();
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
            if (session.IsLocked) return Result.Ok();

            session.Lock();
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
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
            _lastTouchWriteUtc = now;
            return Result<SessionSnapshot>.Ok(await SnapshotAsync(db, session, user, ct).ConfigureAwait(false));
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
            if (session is null) return Result.Ok(); // idempotent

            session.End(string.IsNullOrWhiteSpace(reason) ? ReasonSignOut : reason, clock.UtcNow);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            _lastTouchWriteUtc = null;
            return Result.Ok();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

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
