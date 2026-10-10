using Animora.Persistence.PostgreSql.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class FileObjectConfiguration : IEntityTypeConfiguration<FileObject>
{
    public void Configure(EntityTypeBuilder<FileObject> b)
    {
        b.ToTable("FileObjects", "public"); b.HasKey(x => new { x.TenantId, x.Id });
        b.Property(x => x.TenantId).HasMaxLength(26); b.Property(x => x.Id).HasMaxLength(26);
        b.Property(x => x.UploadId).HasMaxLength(26); b.Property(x => x.CreatedByUserId).HasMaxLength(26);
        b.Property(x => x.Sha256).HasColumnType("character(64)");
        b.Property(x => x.ContentType).HasMaxLength(128); b.Property(x => x.OriginalName).HasMaxLength(256);
        b.Property(x => x.State).HasConversion<short>();
        b.Property(x => x.CreatedUtc).HasColumnType("timestamp(6) with time zone");
        b.Property(x => x.UpdatedUtc).HasColumnType("timestamp(6) with time zone");
        b.HasIndex(x => new { x.TenantId, x.State, x.UpdatedUtc });
        b.HasIndex(x => new { x.TenantId, x.Sha256 });
        b.HasIndex(x => new { x.TenantId, x.UploadId }).IsUnique();
    }
}
