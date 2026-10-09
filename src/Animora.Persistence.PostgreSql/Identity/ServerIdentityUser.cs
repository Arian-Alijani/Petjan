namespace Animora.Persistence.PostgreSql.Identity;

/// <summary>
/// Adapter DTO used as the <c>TUser</c> for ASP.NET Core Identity's <see cref="Microsoft.AspNetCore.Identity.PasswordHasher{TUser}"/>
/// and the lockout/security-stamp store seams. It is <b>not</b> an EF entity and has <b>no</b> DbSet:
/// the <see cref="ServerUserStore"/> projects it to/from the Domain <c>User</c> row read through
/// <c>AnimoraServerDbContext</c>. Keeping it a plain DTO means the server never forks a second user model.
/// </summary>
public sealed class ServerIdentityUser
{
    /// <summary>User ULID (Domain <c>User.Id</c>).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Owning tenant ULID (Domain <c>User.TenantId</c>).</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Login name (Domain <c>User.UserName</c>, already lowercased).</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Normalized (upper-invariant) user name used by Identity lookups.</summary>
    public string NormalizedUserName { get; set; } = string.Empty;

    /// <summary>Stored password hash (Domain <c>User.PasswordHash</c>); <see langword="null"/> until set.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>Security stamp (Domain <c>User.SecurityStamp</c>, a ULID rotated on credential/role change).</summary>
    public string SecurityStamp { get; set; } = string.Empty;

    /// <summary>Lockout end (Domain <c>User.LockoutEndUtc</c>); <see langword="null"/> when not locked.</summary>
    public DateTimeOffset? LockoutEnd { get; set; }

    /// <summary>Consecutive failed attempts (Domain <c>User.FailedAttemptCount</c>).</summary>
    public int AccessFailedCount { get; set; }
}
