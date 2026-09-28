using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Clinical;

public sealed class SmsTemplateRowConfiguration : IEntityTypeConfiguration<SmsTemplateRow>
{
    public void Configure(EntityTypeBuilder<SmsTemplateRow> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Body).HasMaxLength(2048).IsRequired();
        builder.HasIndex(e => new { e.TenantId, e.Name }).IsUnique()
            .HasDatabaseName("IX_SmsTemplateRows_TenantId_Name");
    }
}
