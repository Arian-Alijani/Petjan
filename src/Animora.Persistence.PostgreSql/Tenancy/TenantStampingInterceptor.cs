using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Animora.Persistence.PostgreSql.Tenancy;

/// <summary>
/// Stamps <see cref="SyncableEntity.TenantId"/> from <see cref="ITenantContext"/> on every
/// added/modified row. The tenant is NEVER copied from the incoming entity: a pre-set value
/// that differs from the context is rejected with <see cref="TenantMismatchException"/>.
/// Operators may write <see cref="Tenant"/> rows only.
/// </summary>
public sealed class TenantStampingInterceptor(ITenantContext tenantContext) : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    // Internal so unit tests can drive stamping without a database connection.
    internal void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (EntityEntry<SyncableEntity> entry in context.ChangeTracker.Entries<SyncableEntity>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            SyncableEntity entity = entry.Entity;
            string? contextTenant = tenantContext.TenantId;

            if (tenantContext.IsOperator)
            {
                // Operators are limited to the Tenant aggregate; their own tenant id is not stamped.
                if (entity is not Tenant)
                {
                    throw new TenantMismatchException(contextTenant, entity.TenantId);
                }

                continue;
            }

            if (string.IsNullOrEmpty(entity.TenantId))
            {
                if (string.IsNullOrEmpty(contextTenant))
                {
                    throw new TenantContextMissingException();
                }

                entity.TenantId = contextTenant;
            }
            else if (!string.Equals(entity.TenantId, contextTenant, StringComparison.Ordinal))
            {
                throw new TenantMismatchException(contextTenant, entity.TenantId);
            }
        }
    }
}
