using Animora.Domain.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class OwnerConfiguration : IEntityTypeConfiguration<Owner>
{
    public void Configure(EntityTypeBuilder<Owner> builder)
    {
        builder.ToTable("Owners", "public");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.SearchKey).IsRequired();
        builder.HasIndex(e => new { e.TenantId, e.PhonePrimary }).HasDatabaseName("IX_Owners_TenantId_Phone");
        builder.HasIndex(e => new { e.TenantId, e.FullName }).HasDatabaseName("IX_Owners_TenantId_FullName");
        builder.HasIndex(e => new { e.TenantId, e.SearchKey }).HasDatabaseName("IX_Owners_TenantId_SearchKey");
        // Backs the directory "Recent"/"Age" sort (OrderBy[Descending] RegisteredUtc) — index, not file-sort.
        builder.HasIndex(e => new { e.TenantId, e.RegisteredUtc }).HasDatabaseName("IX_Owners_TenantId_RegisteredUtc");
    }
}
