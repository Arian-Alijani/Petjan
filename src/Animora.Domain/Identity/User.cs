using Animora.Domain.Primitives;
using Animora.Domain.Time;

namespace Animora.Domain.Identity;

/// <summary>Lifecycle state of a credential holder.</summary>
public enum UserStatus
{
    /// <summary>May sign in.</summary>
    Active = 0,

    /// <summary>Administratively disabled; sign-in is refused until re-enabled.</summary>
    Disabled = 1,

    /// <summary>Temporarily locked out by the failed-attempt policy.</summary>
    Locked = 2,
}

/// <summary>
/// A credential holder — the <b>authentication</b> plane only (A-RULE 023). A user carries no
/// role and no tenant: authority comes from <see cref="StaffMembership"/>, entitlement from the
/// license (D11).
/// </summary>
/// <remarks>
/// Hashing itself is out of scope here (B03 supplies ASP.NET Core Identity's
/// <c>PasswordHasher&lt;T&gt;</c>); this aggregate only stores the resulting hash and owns the
/// lockout state machine. No method reads the ambient clock — <see cref="IClock"/> is passed in
/// (<c>CONVENTIONS.md</c> §4).
/// </remarks>
public sealed class User : AggregateRoot
{
    /// <summary>Consecutive failed attempts that trigger a lockout.</summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>How long a triggered lockout lasts.</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>Window inside which failed attempts accumulate; an older streak is discarded.</summary>
    public static readonly TimeSpan DefaultFailureWindow = TimeSpan.FromMinutes(15);

    private DateTimeOffset? _firstFailureUtc;

    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private User()
    {
    }

    /// <summary>Login name, unique per tenant, stored lowercase invariant.</summary>
    public string UserName { get; private set; } = string.Empty;

    /// <summary>Human-readable name for the UI and audit entries.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Opaque password hash; <see langword="null"/> until a credential is set (B03).</summary>
    public string? PasswordHash { get; private set; }

    /// <summary>
    /// ULID rotated on every credential or role change. A <see cref="CapabilitySnapshot"/>
    /// whose stamp no longer matches is stale and must be re-issued.
    /// </summary>
    public string SecurityStamp { get; private set; } = string.Empty;

    /// <summary>Lifecycle state.</summary>
    public UserStatus Status { get; private set; } = UserStatus.Active;

    /// <summary>Last successful sign-in, UTC.</summary>
    public DateTimeOffset? LastLoginUtc { get; private set; }

    /// <summary>Consecutive failed attempts inside the current failure window.</summary>
    public int FailedAttemptCount { get; private set; }

    /// <summary>When the current lockout ends, UTC; <see langword="null"/> when not locked.</summary>
    public DateTimeOffset? LockoutEndUtc { get; private set; }

    /// <summary>The user must set a new password before any other action (B03).</summary>
    public bool MustChangePassword { get; private set; }

    /// <summary>Creates an active user with a fresh security stamp and no credential.</summary>
    public static User Create(string tenantId, string userName, string displayName, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.UtcNow;
        return new User
        {
            Id = IdGenerator.NewId(now),
            TenantId = tenantId,
            UserName = Normalize(userName),
            DisplayName = displayName.Trim(),
            SecurityStamp = IdGenerator.NewId(now),
            Status = UserStatus.Active,
        };
    }

    /// <summary>
    /// Records one failed sign-in attempt. Attempts older than <paramref name="window"/> do not
    /// count towards the streak; reaching <paramref name="threshold"/> sets
    /// <see cref="UserStatus.Locked"/> and <see cref="LockoutEndUtc"/>.
    /// </summary>
    /// <returns><see langword="true"/> when this attempt triggered the lockout.</returns>
    public bool RegisterFailedAttempt(IClock clock, int threshold = MaxFailedAttempts, TimeSpan? window = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfLessThan(threshold, 1);

        var now = clock.UtcNow;
        var effectiveWindow = window ?? DefaultFailureWindow;

        if (_firstFailureUtc is { } first && now - first > effectiveWindow)
        {
            FailedAttemptCount = 0;
            _firstFailureUtc = null;
        }

        _firstFailureUtc ??= now;
        FailedAttemptCount++;

        if (FailedAttemptCount < threshold)
        {
            return false;
        }

        Status = UserStatus.Locked;
        LockoutEndUtc = now + LockoutDuration;
        return true;
    }

    /// <summary>
    /// Clears the failure streak and any lockout — called on a successful authentication.
    /// A <see cref="UserStatus.Disabled"/> user is <b>not</b> re-activated by this.
    /// </summary>
    public void ClearFailures()
    {
        FailedAttemptCount = 0;
        _firstFailureUtc = null;
        LockoutEndUtc = null;

        if (Status == UserStatus.Locked)
        {
            Status = UserStatus.Active;
        }
    }

    /// <summary>
    /// <see langword="true"/> while a lockout is still in force at <paramref name="nowUtc"/>.
    /// Fail-closed: a <see cref="UserStatus.Locked"/> user with no recorded end stays locked.
    /// </summary>
    public bool IsLockedOut(DateTimeOffset nowUtc) =>
        Status == UserStatus.Locked && (LockoutEndUtc is null || LockoutEndUtc > nowUtc);

    /// <summary>Records a successful sign-in and clears the failure state.</summary>
    public void RegisterSuccessfulLogin(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        LastLoginUtc = clock.UtcNow;
        ClearFailures();
    }

    /// <summary>Issues a new security stamp, invalidating every outstanding capability snapshot.</summary>
    public void RotateSecurityStamp() => SecurityStamp = IdGenerator.NewId();

    /// <summary>Replaces the stored credential hash and rotates the security stamp.</summary>
    public void SetPasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
        MustChangePassword = false;
        RotateSecurityStamp();
    }

    /// <summary>Forces a password change on the next sign-in.</summary>
    public void RequirePasswordChange() => MustChangePassword = true;

    /// <summary>Administratively disables the account and rotates the security stamp.</summary>
    public void Disable()
    {
        Status = UserStatus.Disabled;
        RotateSecurityStamp();
    }

    /// <summary>Re-enables a disabled or locked account.</summary>
    public void Enable()
    {
        Status = UserStatus.Active;
        LockoutEndUtc = null;
        FailedAttemptCount = 0;
        _firstFailureUtc = null;
    }

    private static string Normalize(string userName) => userName.Trim().ToLowerInvariant();
}
