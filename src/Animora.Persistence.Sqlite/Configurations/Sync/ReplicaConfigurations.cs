using Animora.Persistence.Sqlite.Replica;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Sync;

// G04 [LocalOnly] replica tables (repl_*): composite keys, explicit tenant predicate in every query. Table names and scalar
// bounds are fixed by ModelConventions (Pluralize special cases) and ReplicaModel.ApplyBounds.

/// <summary>Maps <see cref="ReplicaBase"/> to <c>repl_base</c>.</summary>
public sealed class ReplicaBaseConfiguration : IEntityTypeConfiguration<ReplicaBase>
{
    public void Configure(EntityTypeBuilder<ReplicaBase> builder) =>
        builder.HasKey(e => new { e.TenantId, e.Generation, e.Entity, e.EntityId });
}

/// <summary>Maps <see cref="ReplicaStaging"/> to <c>repl_staging</c>.</summary>
public sealed class ReplicaStagingConfiguration : IEntityTypeConfiguration<ReplicaStaging>
{
    public void Configure(EntityTypeBuilder<ReplicaStaging> builder) =>
        builder.HasKey(e => new { e.TenantId, e.BatchId, e.Entity, e.EntityId });
}

/// <summary>Maps <see cref="ReplicaCheckpoint"/> to <c>repl_checkpoint</c>.</summary>
public sealed class ReplicaCheckpointConfiguration : IEntityTypeConfiguration<ReplicaCheckpoint>
{
    public void Configure(EntityTypeBuilder<ReplicaCheckpoint> builder) =>
        builder.HasKey(e => new { e.TenantId, e.Scope });
}
