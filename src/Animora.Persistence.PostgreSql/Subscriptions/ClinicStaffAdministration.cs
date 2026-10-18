using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Persistence.PostgreSql.Operator;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Animora.Persistence.PostgreSql.Subscriptions;

/// <summary>
/// Vendor-operator administration of ONE clinic's staff accounts: the operator is not a clinic user, so these mutations run
/// through a tenant-bound context enlisted in the operator transaction (same shape as <see cref="OwnerCredentialAdministration"/>).
/// Every success writes a tenant security-audit row AND an operator-audit row in the same transaction. Temporary passwords are
/// returned exactly once and are never audited, logged or persisted in clear text.
/// </summary>
public sealed class ClinicStaffAdministration(AnimoraOperatorDbContext db, IClock clock, IPasswordHasher<ServerIdentityUser> hasher)
{
    /// <summary>Audit action: the operator created a clinic account.</summary>
    public const string CreatedAction = "staff.created";

    /// <summary>Audit action: the operator reset a clinic account's password.</summary>
    public const string PasswordResetAction = "staff.password_reset";

    /// <summary>Audit action: the operator enabled a clinic account.</summary>
    public const string EnabledAction = "staff.enabled";

    /// <summary>Audit action: the operator disabled a clinic account.</summary>
    public const string DisabledAction = "staff.disabled";

    private const int MaxName = 200;
    private const int MaxUserName = 64;

    /// <summary>Creates a user + active membership in the clinic and returns the one-time temporary password.</summary>
    public async Task<Result<string>> CreateAsync(string tenantId, string name, string userName, string role, ActorRef actor,
        string correlationId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (string.IsNullOrWhiteSpace(tenantId) || tenantId.Length > 26
            || string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxName
            || string.IsNullOrWhiteSpace(userName) || userName.Trim().Length > MaxUserName || userName.Any(char.IsWhiteSpace)
            || !Enum.TryParse(role, ignoreCase: true, out StaffRole staffRole) || !Enum.IsDefined(staffRole))
        {
            return Result<string>.Fail(ErrorCodes.ValidationFailed);
        }

        string normalized = userName.Trim().ToLowerInvariant();
        await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (!await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId && !t.Deleted, ct).ConfigureAwait(false))
        {
            return Result<string>.Fail(ErrorCodes.NotFound);
        }

        // A clinic cannot exceed its licensed seat count even when the vendor adds the account by hand.
        Subscription? subscription = await db.Subscriptions.AsNoTracking().SingleOrDefaultAsync(s => s.TenantId == tenantId, ct).ConfigureAwait(false);
        if (subscription is not null)
        {
            int used = await db.StaffMemberships.AsNoTracking().Where(m => m.TenantId == tenantId && !m.Deleted
                && m.Status == MembershipStatus.Active && m.EndedUtc == null).Select(m => m.UserId).Distinct().CountAsync(ct).ConfigureAwait(false);
            if (used >= subscription.SeatLimit)
            {
                return Result<string>.Fail(ErrorCodes.SeatLimitExceeded);
            }
        }

        await using var tenant = TenantBoundContext.Create(db, tenantId, clock);
        await tenant.Database.UseTransactionAsync(tx.GetDbTransaction(), ct).ConfigureAwait(false);
        if (await tenant.Users.AnyAsync(u => u.UserName == normalized, ct).ConfigureAwait(false))
        {
            return Result<string>.Fail(ErrorCodes.UsernameExists);
        }

        if (!await tenant.RoleDefinitions.AnyAsync(r => r.Role == staffRole, ct).ConfigureAwait(false))
        {
            return Result<string>.Fail(ErrorCodes.PermissionDenied);
        }

        string password = ProvisioningEngine.GenerateTemporaryPassword();
        var user = User.Create(tenantId, normalized, name, clock);
        user.SetPasswordHash(hasher.HashPassword(ServerUserStore.ToAdapter(user), password));
        tenant.Users.Add(user);
        tenant.StaffMemberships.Add(StaffMembership.Create(tenantId, user.Id, staffRole, clock));
        AddAudits(tenant, tenantId, actor, CreatedAction, correlationId, user.Id, ("role", staffRole.ToString()));
        await tenant.SaveChangesAsync(ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return Result<string>.Ok(password);
    }

    /// <summary>
    /// Resets the password of ONE user of the clinic (owner or any staff member) to a freshly generated temporary password and
    /// returns it exactly once. <see cref="User.SetPasswordHash"/> rotates the security stamp so the user's live sessions end.
    /// The user must hold a non-deleted membership in the clinic; lookups run through the tenant-bound context, so a user id of
    /// another clinic can never be touched.
    /// </summary>
    public async Task<Result<string>> ResetPasswordAsync(string tenantId, string userId, ActorRef actor, string correlationId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (string.IsNullOrWhiteSpace(tenantId) || tenantId.Length > 26 || string.IsNullOrWhiteSpace(userId) || userId.Length > 26)
        {
            return Result<string>.Fail(ErrorCodes.ValidationFailed);
        }

        await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (!await db.StaffMemberships.AsNoTracking().AnyAsync(m => m.TenantId == tenantId && m.UserId == userId && !m.Deleted, ct).ConfigureAwait(false))
        {
            return Result<string>.Fail(ErrorCodes.NotFound);
        }

        await using var tenant = TenantBoundContext.Create(db, tenantId, clock);
        await tenant.Database.UseTransactionAsync(tx.GetDbTransaction(), ct).ConfigureAwait(false);
        User? user = await tenant.Users.SingleOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return Result<string>.Fail(ErrorCodes.NotFound);
        }

        string password = ProvisioningEngine.GenerateTemporaryPassword();
        user.SetPasswordHash(hasher.HashPassword(ServerUserStore.ToAdapter(user), password));
        AddAudits(tenant, tenantId, actor, PasswordResetAction, correlationId, user.Id, ("role", "-"));
        await tenant.SaveChangesAsync(ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return Result<string>.Ok(password);
    }

    /// <summary>
    /// Enables or disables ONE clinic user (owner or staff). Disabling rotates the security stamp so the user's live sessions end.
    /// The user must hold a non-deleted membership in the clinic; lookups run through the tenant-bound context, so a user id of
    /// another clinic can never be touched. Idempotent: setting the state it already has still succeeds (and is still audited).
    /// </summary>
    public async Task<Result> SetActiveAsync(string tenantId, string userId, bool active, ActorRef actor, string correlationId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (string.IsNullOrWhiteSpace(tenantId) || tenantId.Length > 26 || string.IsNullOrWhiteSpace(userId) || userId.Length > 26)
        {
            return Result.Fail(ErrorCodes.ValidationFailed);
        }

        await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (!await db.StaffMemberships.AsNoTracking().AnyAsync(m => m.TenantId == tenantId && m.UserId == userId && !m.Deleted, ct).ConfigureAwait(false))
        {
            return Result.Fail(ErrorCodes.NotFound);
        }

        await using var tenant = TenantBoundContext.Create(db, tenantId, clock);
        await tenant.Database.UseTransactionAsync(tx.GetDbTransaction(), ct).ConfigureAwait(false);
        User? user = await tenant.Users.SingleOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return Result.Fail(ErrorCodes.NotFound);
        }

        if (active) { user.Enable(); } else { user.Disable(); }
        AddAudits(tenant, tenantId, actor, active ? EnabledAction : DisabledAction, correlationId, user.Id, ("role", "-"));
        await tenant.SaveChangesAsync(ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return Result.Ok();
    }

    private void AddAudits(Context.AnimoraServerDbContext tenant, string tenantId, ActorRef actor, string action, string correlationId,
        string userId, (string Key, string Value) extra)
    {
        DateTimeOffset now = clock.UtcNow;
        string correlation = correlationId.Length > 64 ? correlationId[..64] : correlationId;
        tenant.SecurityAudit.Add(SecurityAuditEntry.Create(IdGenerator.NewId(now), tenantId, now, "operator", action, "success", correlation,
            detailJson: SecurityAuditDetail.Create(("userId", userId), extra)));
        db.Audit.Add(OperatorAudit.Create(now, actor, action, tenantId, correlation, detail: new { userId, role = extra.Value }));
    }
}
