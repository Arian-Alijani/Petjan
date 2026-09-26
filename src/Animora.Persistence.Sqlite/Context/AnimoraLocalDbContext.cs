using Animora.Domain.Boarding;
using Animora.Domain.Clinical;
using Animora.Domain.Events;
using Animora.Domain.Primitives;
using Animora.Domain.Time;
using Animora.Persistence.Sqlite.Conventions;
using Animora.Persistence.Sqlite.Entities;
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
        modelBuilder.Ignore<Animora.Domain.Inventory.StockLevel>(); // Rebuildable projection, never a table.
        modelBuilder.Ignore<StayItem>();
        modelBuilder.Ignore<StayRoomAssignment>();
        modelBuilder.Ignore<PrescriptionLine>();
        modelBuilder.Ignore<VisitServiceLine>();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AnimoraLocalDbContext).Assembly);
        ModelConventions.Apply(modelBuilder, this);
        // Conventions assign generic string bounds; this column is a fixed-width hex SHA-256.
        modelBuilder.Entity<LocalInstallation>().Property(e => e.MachineFingerprintHash)
            .HasMaxLength(LocalInstallation.FingerprintHashLength);
        // Command-pipeline payloads are arbitrary command/event JSON, far larger than the generic short-text
        // bound the conventions would otherwise assign (B02). SQLite does not enforce length, but the model
        // must state the truth for the server store and any future provider.
        modelBuilder.Entity<PendingCommand>().Property(e => e.PayloadJson).HasMaxLength(PayloadMaxLength);
        modelBuilder.Entity<PendingCommand>().Property(e => e.CommandFingerprint)
            .HasMaxLength(PendingCommand.FingerprintLength);
        modelBuilder.Entity<OutboxMessage>().Property(e => e.PayloadJson).HasMaxLength(PayloadMaxLength);
        // B04b: capability snapshot JSON/signature bounds must win over the generic short-text convention.
        var snapshot = modelBuilder.Entity<DeviceCapabilitySnapshot>();
        snapshot.Property(e => e.PermissionsJson).HasMaxLength(DeviceCapabilitySnapshot.PermissionsJsonMaxLength);
        snapshot.Property(e => e.PerCapabilityExpiryJson).HasMaxLength(DeviceCapabilitySnapshot.ExpiryJsonMaxLength);
        snapshot.Property(e => e.SnapshotSignature).HasMaxLength(DeviceCapabilitySnapshot.SignatureMaxLength);
    }

    /// <summary>Max length of a stored command/event JSON payload column.</summary>
    internal const int PayloadMaxLength = 65536;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareSave();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        PrepareSave();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PrepareSave()
    {
        // Append-only rules first: nothing is stamped when the change set is illegal.
        AppendOnlyGuard.Check(this);
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
    }
}
