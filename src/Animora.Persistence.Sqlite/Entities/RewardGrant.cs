using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>
/// A subscription reward an administrator granted to one user (n free days). Device-local record only: binding the
/// reward to the vendor's real subscription/licence grant is server work (backend Tracks D-E) and is not wired yet,
/// so this row is the durable audit trail that a later sync/licensing phase can replay.
/// </summary>
[LocalOnly]
public sealed class RewardGrant
{
    /// <summary>Upper bound of <see cref="Reason"/>.</summary>
    public const int ReasonMaxLength = 400;

    private RewardGrant() { }

    /// <summary>ULID primary key.</summary>
    public string Id { get; private set; } = string.Empty;

    /// <summary>Owning tenant (explicit predicate in every query).</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>The user the reward is for.</summary>
    public string TargetUserId { get; private set; } = string.Empty;

    /// <summary>Granted subscription days (positive).</summary>
    public int Days { get; private set; }

    /// <summary>Why the reward was given (shown to the recipient).</summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>The administrator who granted it.</summary>
    public string GrantedByUserId { get; private set; } = string.Empty;

    /// <summary>When it was granted.</summary>
    public DateTimeOffset GrantedAtUtc { get; private set; }

    /// <summary>Creates a reward record.</summary>
    public static RewardGrant Create(string tenantId, string targetUserId, int days, string reason, string grantedByUserId,
        DateTimeOffset grantedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(grantedByUserId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);
        return new()
        {
            Id = IdGenerator.NewId(),
            TenantId = tenantId,
            TargetUserId = targetUserId,
            Days = days,
            Reason = (reason ?? string.Empty).Trim(),
            GrantedByUserId = grantedByUserId,
            GrantedAtUtc = grantedAtUtc,
        };
    }
}
