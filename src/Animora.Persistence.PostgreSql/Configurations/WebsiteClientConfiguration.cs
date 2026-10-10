using Animora.Persistence.PostgreSql.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class WebsiteClientConfiguration : IEntityTypeConfiguration<WebsiteClient>
{
    public void Configure(EntityTypeBuilder<WebsiteClient> b)
    {
        b.ToTable("WebsiteClients", "public"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasMaxLength(26);
        b.Property(x => x.KeyId).HasMaxLength(32).IsRequired(); b.HasIndex(x => x.KeyId).IsUnique();
        b.Property(x => x.ProtectedSecret).HasColumnType("bytea").IsRequired(); b.Property(x => x.Status).HasConversion<short>();
        b.Property(x => x.Label).HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedUtc).HasColumnType("timestamp(6) with time zone"); b.Property(x => x.LastUsedUtc).HasColumnType("timestamp(6) with time zone");
    }
}
