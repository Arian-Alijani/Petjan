using Animora.Persistence.PostgreSql.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class FileUploadChunkConfiguration : IEntityTypeConfiguration<FileUploadChunk>
{
    public void Configure(EntityTypeBuilder<FileUploadChunk> b)
    {
        b.ToTable("FileUploadChunks", "public"); b.HasKey(x => new { x.TenantId, x.UploadId, x.Offset });
        b.Property(x => x.TenantId).HasMaxLength(26); b.Property(x => x.UploadId).HasMaxLength(26);
    }
}
