using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Microsoft.AspNetCore.Identity;

namespace Animora.Persistence.PostgreSql.Identity;

/// <summary>
/// ASP.NET Core Identity store over the Domain <see cref="User"/> aggregate (no second user model): reads
/// project <see cref="User"/> to the <see cref="ServerIdentityUser"/> adapter and writes are applied back
/// through the aggregate's own methods. Lookups are tenant-scoped by the bound tenant context. The
/// aggregate owns lockout (<c>5</c> failures / <c>15 min</c>); the store never lets Identity unlock an
/// account (<see cref="ResetAccessFailedCountAsync"/> and <see cref="SetLockoutEndDateAsync"/> respect it).
/// </summary>
public sealed class ServerUserStore(IIdentityRepository repository, IClock clock) :
    IUserStore<ServerIdentityUser>,
    IUserPasswordStore<ServerIdentityUser>,
    IUserSecurityStampStore<ServerIdentityUser>,
    IUserLockoutStore<ServerIdentityUser>
{
    /// <summary>Projects the aggregate to the Identity adapter.</summary>
    public static ServerIdentityUser ToAdapter(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new ServerIdentityUser
        {
            Id = user.Id,
            TenantId = user.TenantId,
            UserName = user.UserName,
            NormalizedUserName = Normalize(user.UserName),
            PasswordHash = user.PasswordHash,
            SecurityStamp = user.SecurityStamp,
            LockoutEnd = user.LockoutEndUtc,
            AccessFailedCount = user.FailedAttemptCount,
        };
    }

    /// <summary>Identity-style normalization (upper invariant) of a login name.</summary>
    public static string Normalize(string userName) => userName.Trim().ToUpperInvariant();

    #region IUserStore

    /// <inheritdoc />
    public Task<string> GetUserIdAsync(ServerIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.Id);

    /// <inheritdoc />
    public Task<string?> GetUserNameAsync(ServerIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.UserName);

    /// <inheritdoc />
    public Task SetUserNameAsync(ServerIdentityUser user, string? userName, CancellationToken cancellationToken) =>
        Task.CompletedTask; // Login names are immutable in the aggregate.

    /// <inheritdoc />
    public Task<string?> GetNormalizedUserNameAsync(ServerIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.NormalizedUserName);

    /// <inheritdoc />
    public Task SetNormalizedUserNameAsync(ServerIdentityUser user, string? normalizedName, CancellationToken cancellationToken) =>
        Task.CompletedTask; // Derived from the aggregate's lowercase user name.

    /// <summary>Not supported: accounts are created only by <c>TenantProvisioner</c> (no public sign-up).</summary>
    public Task<IdentityResult> CreateAsync(ServerIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "NotSupported", Description = "Accounts are provisioned server-side only." }));

    /// <summary>Not supported: accounts are disabled, never deleted.</summary>
    public Task<IdentityResult> DeleteAsync(ServerIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "NotSupported", Description = "Accounts are disabled, not deleted." }));

    /// <inheritdoc />
    public async Task<IdentityResult> UpdateAsync(ServerIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        User? domain = await repository.FindUserByIdAsync(user.Id, cancellationToken).ConfigureAwait(false);
        if (domain is null)
        {
            return IdentityResult.Failed(new IdentityError { Code = "NotFound", Description = "User not found." });
        }

        bool passwordChanged = !string.IsNullOrEmpty(user.PasswordHash)
            && !string.Equals(user.PasswordHash, domain.PasswordHash, StringComparison.Ordinal);

        if (passwordChanged)
        {
            domain.SetPasswordHash(user.PasswordHash!); // rotates the stamp itself
        }
        else if (!string.Equals(user.SecurityStamp, domain.SecurityStamp, StringComparison.Ordinal))
        {
            domain.RotateSecurityStamp();
        }

        Result saved = await repository.SaveAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
        {
            return IdentityResult.Failed(new IdentityError { Code = saved.Error.Code, Description = "The user was modified concurrently." });
        }

        user.SecurityStamp = domain.SecurityStamp;
        return IdentityResult.Success;
    }

    /// <inheritdoc />
    public async Task<ServerIdentityUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        User? user = await repository.FindUserByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        return user is null ? null : ToAdapter(user);
    }

    /// <inheritdoc />
    public async Task<ServerIdentityUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(normalizedUserName);

        User? user = await repository
            .FindUserByNameAsync(normalizedUserName.Trim().ToLowerInvariant(), cancellationToken)
            .ConfigureAwait(false);
        return user is null ? null : ToAdapter(user);
    }

    #endregion

    #region IUserPasswordStore

    /// <inheritdoc />
    public Task SetPasswordHashAsync(ServerIdentityUser user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask; // Persisted by UpdateAsync via User.SetPasswordHash.
    }

    /// <inheritdoc />
    public Task<string?> GetPasswordHashAsync(ServerIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PasswordHash);

    /// <inheritdoc />
    public Task<bool> HasPasswordAsync(ServerIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));

    #endregion

    #region IUserSecurityStampStore

    /// <inheritdoc />
    public Task SetSecurityStampAsync(ServerIdentityUser user, string stamp, CancellationToken cancellationToken)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask; // UpdateAsync rotates the aggregate's stamp when it differs.
    }

    /// <inheritdoc />
    public Task<string?> GetSecurityStampAsync(ServerIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.SecurityStamp);

    #endregion

    #region IUserLockoutStore

    /// <inheritdoc />
    public Task<DateTimeOffset?> GetLockoutEndDateAsync(ServerIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LockoutEnd);

    /// <summary>No-op: the aggregate sets the lockout end itself when the failure threshold is reached.</summary>
    public Task SetLockoutEndDateAsync(ServerIdentityUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public async Task<int> IncrementAccessFailedCountAsync(ServerIdentityUser user, CancellationToken cancellationToken)
    {
        User? domain = await repository.FindUserByIdAsync(user.Id, cancellationToken).ConfigureAwait(false);
        if (domain is null)
        {
            return user.AccessFailedCount;
        }

        domain.RegisterFailedAttempt(clock);
        await repository.SaveAsync(cancellationToken).ConfigureAwait(false);

        user.AccessFailedCount = domain.FailedAttemptCount;
        user.LockoutEnd = domain.LockoutEndUtc;
        return domain.FailedAttemptCount;
    }

    /// <summary>
    /// Clears failure counters, but never lifts an active lockout (Identity calls this right after it
    /// locks an account; the aggregate's lockout must survive that).
    /// </summary>
    public async Task ResetAccessFailedCountAsync(ServerIdentityUser user, CancellationToken cancellationToken)
    {
        User? domain = await repository.FindUserByIdAsync(user.Id, cancellationToken).ConfigureAwait(false);
        if (domain is null || domain.IsLockedOut(clock.UtcNow))
        {
            return;
        }

        domain.ClearFailures();
        await repository.SaveAsync(cancellationToken).ConfigureAwait(false);
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
    }

    /// <inheritdoc />
    public Task<int> GetAccessFailedCountAsync(ServerIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.AccessFailedCount);

    /// <inheritdoc />
    public Task<bool> GetLockoutEnabledAsync(ServerIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(true);

    /// <summary>No-op: lockout is always enabled for every server account.</summary>
    public Task SetLockoutEnabledAsync(ServerIdentityUser user, bool enabled, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    #endregion

    /// <inheritdoc />
    public void Dispose()
    {
        // The repository's lifetime belongs to the DI scope.
    }
}
