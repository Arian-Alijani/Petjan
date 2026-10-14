using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Animora.Persistence.PostgreSql.Tenancy;

/// <summary>
/// Stamps <see cref="SyncableEntity.TenantId"/> from <see cref="ITenantContext"/> and the transport
/// <see cref="SyncableEntity.UpdatedAt"/> cursor from <see cref="IClock"/> on every added/modified row —
/// this is the server's single save-time stamping step. The tenant is NEVER copied from the incoming
/// entity: a pre-set value that differs from the context is rejected with
/// <see cref="TenantMismatchException"/>. Operators may write <see cref="Tenant"/> rows only.
/// </summary>
/// <remarks>
/// <para><b>Why <see cref="SyncableEntity.UpdatedAt"/> must be stamped here:</b> every synced row is
/// written through this context, so leaving <c>UpdatedAt</c> at <c>default</c> (<c>0001-01-01</c>) makes
/// Npgsql persist it as PostgreSQL <c>-infinity</c>. The replication batch builder
/// (<c>RecordProjector</c>) then reads that column back as the JSON string <c>"-infinity"</c>, which is
/// unparseable as a timestamp and degrades the row to a tombstone — so a freshly-created owner/pet syncs
/// down as a <c>delete</c> and vanishes from every replica. Stamping a real UTC instant here (mirroring
/// the client's <c>AnimoraLocalDbContext</c>) keeps the row a live upsert on the wire.</para>
/// </remarks>
public sealed class TenantStampingInterceptor(ITenantContext tenantContext, IClock clock) : SaveChangesInterceptor
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

        // One instant for the whole save so every row written in a single SaveChanges shares the same
        // transport cursor value (the delta-sync watermark is per-row monotonic, not per-entity).
        DateTimeOffset now = clock.UtcNow;

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

                // The Tenant row is still a synced root: stamp its transport cursor so it never lands
                // with UpdatedAt=default (-infinity) and degrades to a tombstone on the wire.
                StampUpdatedAt(entry, now);
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

            StampUpdatedAt(entry, now);
        }
    }

    /// <summary>
    /// Sets the transport <see cref="SyncableEntity.UpdatedAt"/> cursor to <paramref name="now"/>.
    /// Only overwrites a value the caller did not explicitly supply: a row that already carries a real
    /// (non-default) timestamp — e.g. a replay path that deliberately preserves the original write
    /// instant — is left untouched, while the common <c>default</c> case is corrected so the column is
    /// never persisted as PostgreSQL <c>-infinity</c>.
    /// </summary>
    private static void StampUpdatedAt(EntityEntry<SyncableEntity> entry, DateTimeOffset now)
    {
        if (entry.Entity.UpdatedAt == default)
        {
            entry.Entity.UpdatedAt = now;
        }
    }
}
