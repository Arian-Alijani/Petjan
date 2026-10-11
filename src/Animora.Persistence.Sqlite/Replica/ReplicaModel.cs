using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Replica;

/// <summary>Column bounds of the replica tables; applied after the generic scalar conventions, which would otherwise pick short-text bounds.</summary>
internal static class ReplicaModel
{
    /// <summary>Max length of a stored wire payload (one record).</summary>
    internal const int PayloadMaxLength = 262144;

    /// <summary>Max length of an entity name / entity id / batch id / version on the wire.</summary>
    internal const int KeyMaxLength = 128;

    internal static void ApplyBounds(ModelBuilder modelBuilder)
    {
        var @base = modelBuilder.Entity<ReplicaBase>();
        @base.Property(e => e.Entity).HasMaxLength(KeyMaxLength);
        @base.Property(e => e.EntityId).HasMaxLength(KeyMaxLength);
        @base.Property(e => e.Version).HasMaxLength(KeyMaxLength);
        @base.Property(e => e.Payload).HasMaxLength(PayloadMaxLength);

        var staging = modelBuilder.Entity<ReplicaStaging>();
        staging.Property(e => e.BatchId).HasMaxLength(KeyMaxLength);
        staging.Property(e => e.Entity).HasMaxLength(KeyMaxLength);
        staging.Property(e => e.EntityId).HasMaxLength(KeyMaxLength);
        staging.Property(e => e.Version).HasMaxLength(KeyMaxLength);
        staging.Property(e => e.Payload).HasMaxLength(PayloadMaxLength);

        modelBuilder.Entity<ReplicaCheckpoint>().Property(e => e.Scope).HasMaxLength(64);
    }
}
