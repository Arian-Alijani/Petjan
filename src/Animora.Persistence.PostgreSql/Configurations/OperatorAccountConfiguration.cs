using Animora.Persistence.PostgreSql.Operator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class OperatorAccountConfiguration : IEntityTypeConfiguration<OperatorAccount>
{
    public void Configure(EntityTypeBuilder<OperatorAccount> b)
    {
        b.ToTable("OperatorAccounts", "public"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(26); b.Property(x => x.UserName).HasMaxLength(64).IsRequired();
        b.Property(x => x.NormalizedUserName).HasMaxLength(64).IsRequired(); b.HasIndex(x => x.NormalizedUserName).IsUnique();
        b.Property(x => x.PasswordHash).IsRequired(); b.Property(x => x.SecurityStamp).HasMaxLength(26).IsConcurrencyToken();
        b.Property(x => x.Status).HasConversion<short>();
        b.Property(x => x.CreatedUtc).HasColumnType("timestamp(6) with time zone");
        b.Property(x => x.LastLoginUtc).HasColumnType("timestamp(6) with time zone");
        b.Property(x => x.LockoutEndUtc).HasColumnType("timestamp(6) with time zone");
    }
}
