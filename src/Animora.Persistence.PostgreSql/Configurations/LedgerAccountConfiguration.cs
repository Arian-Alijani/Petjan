using Animora.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Animora.Persistence.PostgreSql.Conventions;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class LedgerAccountConfiguration : IEntityTypeConfiguration<LedgerAccount>
{
    public void Configure(EntityTypeBuilder<LedgerAccount> builder)
    {
        builder.ToTable("LedgerAccounts", "public");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.Code }).HasDatabaseName("IX_LedgerAccounts_TenantId_Code").IsUnique();
    }
}
