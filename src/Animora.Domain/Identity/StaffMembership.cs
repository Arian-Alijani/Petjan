using Animora.Domain.Primitives;
using Animora.Domain.Time;

namespace Animora.Domain.Identity;

/// <summary>Lifecycle state of a staff membership.</summary>
public enum MembershipStatus
{
    /// <summary>The user may act inside the tenant.</summary>
    Active = 0,

    /// <summary>Temporarily blocked; the link is retained for audit and can be restored.</summary>
    Suspended = 1,

    /// <summary>The user has left the clinic; historical rows keep referencing this row.</summary>
    Ended = 2,
}

/// <summary>
/// The <b>only</b> place a <c>(User, Tenant)</c> relationship exists. Authorization resolves a
/// caller's <c>TenantId</c> and <see cref="StaffRole"/> from an <see cref="MembershipStatus.Active"/>
/// membership, server-side, on every request — a client-supplied <c>TenantId</c> is ignored and
/// audited (<b>A-RULE 012</b>).
/// </summary>
/// <remarks>
/// Effective permissions = the tenant's <see cref="RoleDefinition"/> set for <see cref="Role"/>,
/// plus <see cref="ExtraPermissionCodes"/>, minus <see cref="DeniedPermissionCodes"/> — denies
/// always win (fail closed, <c>CONVENTIONS.md</c> §7). The evaluator itself is B04's
/// <c>IPermissionEvaluator</c>; this aggregate only stores the inputs.
/// </remarks>
public sealed class StaffMembership : AggregateRoot
{
    private readonly HashSet<string> _extraPermissionCodes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _deniedPermissionCodes = new(StringComparer.Ordinal);

    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private StaffMembership()
    {
    }

    /// <summary>The <see cref="User"/> this membership belongs to.</summary>
    public string UserId { get; private set; } = string.Empty;

    /// <summary>The role the user holds inside <see cref="SyncableEntity.TenantId"/>.</summary>
    public StaffRole Role { get; private set; }

    /// <summary>Lifecycle state.</summary>
    public MembershipStatus Status { get; private set; } = MembershipStatus.Active;

    /// <summary>When the membership started, UTC.</summary>
    public DateTimeOffset JoinedUtc { get; private set; }

    /// <summary>When the membership ended, UTC; <see langword="null"/> while it has not.</summary>
    public DateTimeOffset? EndedUtc { get; private set; }

    /// <summary>Codes granted to this person beyond their role's set.</summary>
    public IReadOnlySet<string> ExtraPermissionCodes => _extraPermissionCodes;

    /// <summary>Codes withheld from this person despite their role's set. Denies win.</summary>
    public IReadOnlySet<string> DeniedPermissionCodes => _deniedPermissionCodes;

    /// <summary>Creates an active membership.</summary>
    public static StaffMembership Create(string tenantId, string userId, StaffRole role, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.UtcNow;
        return new StaffMembership
        {
            Id = IdGenerator.NewId(now),
            TenantId = tenantId,
            UserId = userId,
            Role = role,
            Status = MembershipStatus.Active,
            JoinedUtc = now,
        };
    }

    /// <summary><see langword="true"/> only when the membership is currently active.</summary>
    public bool IsActive => Status == MembershipStatus.Active && EndedUtc is null;

    /// <summary>Changes the held role.</summary>
    public void ChangeRole(StaffRole role) => Role = role;

    /// <summary>Suspends the membership. Idempotent.</summary>
    public void Suspend()
    {
        if (Status == MembershipStatus.Ended)
        {
            return;
        }

        Status = MembershipStatus.Suspended;
    }

    /// <summary>Restores a suspended membership; an ended membership is never reopened.</summary>
    public void Reinstate()
    {
        if (Status == MembershipStatus.Suspended)
        {
            Status = MembershipStatus.Active;
        }
    }

    /// <summary>Ends the membership at the clock's instant. Idempotent.</summary>
    public void End(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (Status == MembershipStatus.Ended)
        {
            return;
        }

        Status = MembershipStatus.Ended;
        EndedUtc = clock.UtcNow;
    }

    /// <summary>Grants an extra code to this person and clears any matching deny.</summary>
    public void GrantExtra(string code)
    {
        EnsureKnown(code);
        _deniedPermissionCodes.Remove(code);
        _extraPermissionCodes.Add(code);
    }

    /// <summary>Denies a code to this person and clears any matching extra grant.</summary>
    public void Deny(string code)
    {
        EnsureKnown(code);
        _extraPermissionCodes.Remove(code);
        _deniedPermissionCodes.Add(code);
    }

    /// <summary>Removes both the extra grant and the deny for <paramref name="code"/>.</summary>
    public void ClearOverride(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        _extraPermissionCodes.Remove(code);
        _deniedPermissionCodes.Remove(code);
    }

    private static void EnsureKnown(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (!PermissionCodes.Exists(code))
        {
            throw new ArgumentException($"Unknown permission code '{code}'.", nameof(code));
        }
    }
}
