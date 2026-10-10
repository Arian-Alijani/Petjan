using Animora.Persistence.PostgreSql.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class BlobRefConfiguration : IEntityTypeConfiguration<BlobRef>
{
    public void Configure(EntityTypeBuilder<BlobRef> b)
    {
        b.ToTable("BlobRefs", "public"); b.HasKey(x => x.Sha256);
        b.Property(x => x.Sha256).HasColumnType("character(64)");
        b.Property(x => x.FirstSeenUtc).HasColumnType("timestamp(6) with time zone");
        b.Property(x => x.UnreferencedUtc).HasColumnType("timestamp(6) with time zone");
    }
}
