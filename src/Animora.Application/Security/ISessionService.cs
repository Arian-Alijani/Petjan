using Animora.Domain.Identity;
using Animora.Domain.Results;

namespace Animora.Application.Security;

/// <summary>Current local session state. All-null identity fields mean "signed out".</summary>
public sealed record SessionSnapshot(string? UserId, string? DisplayName, StaffRole? Role,
    bool IsLocked, bool IsFirstRun, DateTimeOffset? ExpiresAtUtc)
{
    public static SessionSnapshot SignedOut(bool isFirstRun) => new(null, null, null, false, isFirstRun, null);
}

/// <summary>
/// Durable, policy-driven local session (A-RULE 029, R-B09): absolute lifetime → sign-out with
/// <c>SESSION_EXPIRED</c>; never silently extended. Inactivity does <b>not</b> lock the session — only an
/// explicit <see cref="LockAsync"/> or a hard-lifetime/online-entitlement failure may end usable access.
/// </summary>
public interface ISessionService
{
    /// <summary>Restores the persisted session; an expired one is ended with <c>SESSION_EXPIRED</c>.</summary>
    Task<Result<SessionSnapshot>> RestoreAsync(CancellationToken ct = default);

    /// <summary>Ends any active session and starts a new one for <paramref name="userId"/>.</summary>
    Task<Result<SessionSnapshot>> StartAsync(string userId, string deviceId, CancellationToken ct = default);

    Task<Result> LockAsync(CancellationToken ct = default);

    Task<Result<SessionSnapshot>> UnlockAsync(string password, CancellationToken ct = default);

    Task<Result> EndAsync(string reason, CancellationToken ct = default);
}
