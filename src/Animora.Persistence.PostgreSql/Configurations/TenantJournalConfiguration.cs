using Animora.Persistence.PostgreSql.Replication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class TenantJournalConfiguration : IEntityTypeConfiguration<TenantJournalRow>
{
    public void Configure(EntityTypeBuilder<TenantJournalRow> b)
    {
        b.ToTable("TenantJournal", "public");
        b.HasKey(x => new { x.TenantId, x.Id });
        b.Property(x => x.TenantId).HasMaxLength(26);
        b.Property(x => x.Id).UseIdentityAlwaysColumn();
        b.Property(x => x.Entity).HasMaxLength(48).IsRequired();
        b.Property(x => x.EntityId).HasMaxLength(26).IsRequired();
        b.Property(x => x.Op).HasConversion<string>().HasColumnType("character(1)");
        b.Property(x => x.Version).HasColumnType("uuid");
        b.Property(x => x.CapturedUtc).HasColumnType("timestamp(6) with time zone");
        b.HasIndex(x => new { x.TenantId, x.Epoch, x.ChangeSequence }).IsUnique()
            .HasFilter("\"ChangeSequence\" IS NOT NULL").HasDatabaseName("IX_TenantJournal_TenantId_Epoch_ChangeSequence");
        b.HasIndex(x => new { x.TenantId, x.Entity, x.EntityId, x.ChangeSequence })
            .HasDatabaseName("IX_TenantJournal_TenantId_Entity_EntityId_ChangeSequence");
    }
}
