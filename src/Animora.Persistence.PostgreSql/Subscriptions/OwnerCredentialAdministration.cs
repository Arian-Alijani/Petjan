using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Persistence.PostgreSql.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Animora.Persistence.PostgreSql.Subscriptions;

public sealed class OwnerCredentialAdministration(AnimoraOperatorDbContext db, IClock clock,
    IPasswordHasher<ServerIdentityUser> hasher)
{
    public async Task<Result<string>> ResetAsync(string tenantId, string expectedStamp, ActorRef actor, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var subscription = await db.Subscriptions.FromSqlInterpolated(
            $"SELECT * FROM public.\"Subscriptions\" WHERE \"TenantId\"={tenantId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (subscription is null) return Result<string>.Fail(ErrorCodes.SubscriptionNotFound);
        if (subscription.ConcurrencyStamp != expectedStamp.Trim('"')) return Result<string>.Fail(ErrorCodes.ConcurrencyConflict);
        var ownerId = await db.StaffMemberships.AsNoTracking().Where(x => x.TenantId == tenantId && !x.Deleted &&
            x.Role == StaffRole.Manager && x.Status == MembershipStatus.Active && x.EndedUtc == null)
            .OrderBy(x => x.JoinedUtc).Select(x => x.UserId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (ownerId is null) return Result<string>.Fail(ErrorCodes.NotFound);
        var binding = new AuthTenantContext(); binding.Bind(tenantId);
        var opts = new DbContextOptionsBuilder<AnimoraServerDbContext>().UseNpgsql(db.Database.GetDbConnection())
            .AddInterceptors(new TenantStampingInterceptor(binding, clock), new NoUnnamedIgnoreFiltersInterceptor(binding))
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)).Options;
        await using var tenant = new AnimoraServerDbContext(opts, binding);
        await tenant.Database.UseTransactionAsync(tx.GetDbTransaction(), ct).ConfigureAwait(false);
        var owner = await tenant.Users.SingleOrDefaultAsync(x => x.Id == ownerId, ct).ConfigureAwait(false);
        if (owner is null) return Result<string>.Fail(ErrorCodes.NotFound);
        var password = ProvisioningEngine.GenerateTemporaryPassword();
        // Reset the owner's password without forcing a later change: the owner can sign in and use every
        // capability right away with the freshly issued password (MustChangePassword stays false).
        owner.SetPasswordHash(hasher.HashPassword(ServerUserStore.ToAdapter(owner), password));
        var now = clock.UtcNow;
        tenant.SecurityAudit.Add(SecurityAuditEntry.Create(IdGenerator.NewId(now), tenantId, now, "operator", "owner.password_reset",
            "success", subscription.Id, detailJson: SecurityAuditDetail.Create(("ownerId", owner.Id))));
        db.Audit.Add(OperatorAudit.Create(now, actor, "owner.password_reset", tenantId, subscription.Id));
        await tenant.SaveChangesAsync(ct).ConfigureAwait(false); await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return Result<string>.Ok(password);
    }
}
