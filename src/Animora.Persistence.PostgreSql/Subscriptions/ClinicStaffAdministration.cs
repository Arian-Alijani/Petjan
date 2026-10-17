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
