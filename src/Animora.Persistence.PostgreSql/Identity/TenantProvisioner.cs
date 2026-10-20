using Animora.Persistence.PostgreSql.Replication;
using Animora.Application.Security;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Identity;

/// <summary>Identifiers of a freshly provisioned tenant.</summary>
/// <param name="TenantId">Tenant ULID.</param>
/// <param name="TenantCode">Normalized sign-in code.</param>
/// <param name="OwnerUserId">The Manager user ULID.</param>
public sealed record ProvisionedTenant(string TenantId, string TenantCode, string OwnerUserId);

/// <summary>
/// Creates a tenant together with its system role definitions and its Manager owner (user + membership)
/// in <b>one</b> transaction. Used by S15/S19 and tests; there is deliberately <b>no</b> public sign-up
/// endpoint. Binds the new tenant on the scope's <see cref="AuthTenantContext"/>, so use a dedicated scope
/// per provisioning call.
/// </summary>
public sealed class TenantProvisioner(
    AnimoraServerDbContext context,
    AuthTenantContext tenantContext,
    IPasswordHasher<ServerIdentityUser> passwordHasher,
    IClock clock)
{
    private const int MaxCodeLength = 32;

    /// <summary>Creates the tenant, its five system roles and the Manager owner.</summary>
    public async Task<Result<ProvisionedTenant>> CreateTenantWithOwnerAsync(
        string displayName,
        string tenantCode,
        string ownerUserName,
        string ownerDisplayName,
        string ownerPassword,
        CancellationToken ct = default,
        string? legalName = null,
        string timezone = Tenant.DefaultTimezone)
    {
        if (string.IsNullOrWhiteSpace(displayName)
            || string.IsNullOrWhiteSpace(tenantCode)
            || string.IsNullOrWhiteSpace(ownerUserName)
            || string.IsNullOrWhiteSpace(ownerDisplayName))
        {
            return Result<ProvisionedTenant>.Fail(ErrorCodes.ValidationFailed, "required field missing");
        }

        // ISO-04: provisioning re-binds the scope's tenant context to the NEW tenant, which would silently clobber a binding the caller
        // (or a pooled/reused scope) already relied on. The "dedicated scope per call" rule is therefore enforced, not just documented:
        // an already-bound context is a programming error and fails loudly before any state is touched.
        if (tenantContext.TenantId is not null)
        {
            throw new InvalidOperationException(
                "TenantProvisioner requires a dedicated scope with an unbound tenant context (ISO-04); this scope is already bound.");
        }

        string code = TenantDirectory.NormalizeCode(tenantCode);
        if (code.Length > MaxCodeLength || !code.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            return Result<ProvisionedTenant>.Fail(ErrorCodes.ValidationFailed, "tenant code must be 1-32 chars of [a-z0-9-_]");
        }

        Result policy = PasswordPolicy.Validate(ownerPassword);
        if (policy.IsFailure)
        {
            return Result<ProvisionedTenant>.Fail(policy.Error, policy.Detail);
        }

        var tenant = Tenant.Create(displayName, clock, legalName, timezone);
        tenant.SetCode(code);
        tenantContext.Bind(tenant.Id);

        var owner = User.Create(tenant.Id, ownerUserName, ownerDisplayName, clock);
        owner.SetPasswordHash(passwordHasher.HashPassword(ServerUserStore.ToAdapter(owner), ownerPassword));
        // The owner may use every capability immediately after first login: we no longer force a
        // password change on a freshly provisioned clinic owner (MustChangePassword stays false).

        // Save the serialization row first inside the same transaction. EF cannot infer
        // ordering between unrelated tables; the Tenant INSERT capture needs this row.
        // When wrapped by ProvisioningEngine, participate in its transaction and never commit it here.
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(ct).ConfigureAwait(false)
            : null;
        context.Set<TenantFeedStateRow>().Add(new TenantFeedStateRow { TenantId = tenant.Id });
        await context.SaveChangesAsync(ct).ConfigureAwait(false);

        context.Tenants.Add(tenant);
        foreach (StaffRole role in Enum.GetValues<StaffRole>())
        {
            context.RoleDefinitions.Add(RoleDefinition.CreateSystem(tenant.Id, role, clock));
        }

        context.Users.Add(owner);
        context.StaffMemberships.Add(StaffMembership.Create(tenant.Id, owner.Id, StaffRole.Manager, clock));

        try
        {
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
            if (transaction is not null) await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (transaction is not null && ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            return Result<ProvisionedTenant>.Fail(ErrorCodes.ValidationFailed, "tenant code already in use");
        }

        return Result<ProvisionedTenant>.Ok(new ProvisionedTenant(tenant.Id, code, owner.Id));
    }
}
