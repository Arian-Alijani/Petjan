using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Sync;

// G06 [LocalOnly] online-identity tables. Scalar bounds that differ from the generic conventions are applied in
// OnlineEntitlementModel.ApplyBounds; the [LocalOnly] tag and table names come from ModelConventions.

/// <summary>Maps <see cref="ClientSessionRow"/> to <c>ClientSessions</c>: ULID key + one row per (tenant, user, device).</summary>
public sealed class ClientSessionRowConfiguration : IEntityTypeConfiguration<ClientSessionRow>
{
    public void Configure(EntityTypeBuilder<ClientSessionRow> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.UserId, e.DeviceId })
            .IsUnique()
            .HasDatabaseName("IX_ClientSessions_TenantId_UserId_DeviceId");
    }
}

/// <summary>Maps <see cref="SealedEntitlementRow"/> to <c>SealedEntitlements</c>: one row per tenant.</summary>
public sealed class SealedEntitlementRowConfiguration : IEntityTypeConfiguration<SealedEntitlementRow>
{
    public void Configure(EntityTypeBuilder<SealedEntitlementRow> builder) => builder.HasKey(e => e.TenantId);
}
