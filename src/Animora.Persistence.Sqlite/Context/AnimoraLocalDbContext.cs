using Animora.Domain.Boarding;
using Animora.Domain.Clinical;
using Animora.Domain.Events;
using Animora.Domain.Primitives;
using Animora.Domain.Time;
using Animora.Persistence.Sqlite.Conventions;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Context;

public sealed class AnimoraLocalDbContext(
    DbContextOptions<AnimoraLocalDbContext> options, ILocalTenantContext tenant, IClock clock) : DbContext(options)
{
    internal string CurrentTenantId => tenant.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Domain events and immutable child records are not independent tables.
        modelBuilder.Ignore<DomainEvent>();
        modelBuilder.Ignore<StayItem>();
        modelBuilder.Ignore<StayRoomAssignment>();
        modelBuilder.Ignore<PrescriptionLine>();
        modelBuilder.Ignore<VisitServiceLine>();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AnimoraLocalDbContext).Assembly);
        ModelConventions.Apply(modelBuilder, this);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var changes = ChangeTracker.Entries<SyncableEntity>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified).ToArray();
        // Validate all rows before stamping any of them.
        foreach (var entry in changes)
        {
            if (entry.Entity.TenantId.Length != 0 && entry.Entity.TenantId != tenant.TenantId)
                throw new InvalidOperationException("Cannot write a record belonging to another tenant.");
        }
        var now = clock.UtcNow;
        foreach (var entry in changes)
        {
            if (entry.Entity.TenantId.Length == 0) entry.Entity.TenantId = tenant.TenantId;
            entry.Entity.UpdatedAt = now;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
