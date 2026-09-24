using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>
/// Durable local sign-in session (B03, R-B09): at most one active row per device, so a crash can
/// never leave a half-authenticated state. Never replicated.
/// </summary>
[LocalOnly]
public sealed class LocalSession
{
    public const int EndReasonLength = 64;

    private LocalSession()
    {
    }

    public string Id { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public string DeviceId { get; private set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset LastActivityUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public bool IsLocked { get; private set; }
    public DateTimeOffset? EndedAtUtc { get; private set; }
    public string? EndReason { get; private set; }

    public bool IsActive => EndedAtUtc is null;

    public static LocalSession Start(string userId, string deviceId, DateTimeOffset nowUtc, TimeSpan absoluteLifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        return new LocalSession
        {
            Id = IdGenerator.NewId(nowUtc),
            UserId = userId,
            DeviceId = deviceId,
            StartedAtUtc = nowUtc,
            LastActivityUtc = nowUtc,
            ExpiresAtUtc = nowUtc + absoluteLifetime,
        };
    }

    public void Touch(DateTimeOffset nowUtc) => LastActivityUtc = nowUtc;

    public void Lock() => IsLocked = true;

    public void Unlock(DateTimeOffset nowUtc)
    {
        IsLocked = false;
        LastActivityUtc = nowUtc;
    }

    public void End(string reason, DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        EndedAtUtc = nowUtc;
        EndReason = reason.Length > EndReasonLength ? reason[..EndReasonLength] : reason;
        IsLocked = false;
    }
}
