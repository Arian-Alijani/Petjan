using Animora.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Animora.Persistence.PostgreSql.Conventions;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class AccountingPeriodConfiguration : IEntityTypeConfiguration<AccountingPeriod>
{
    public void Configure(EntityTypeBuilder<AccountingPeriod> builder)
    {
        builder.ToTable("AccountingPeriods", "public");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.PeriodCode }).HasDatabaseName("IX_AccountingPeriods_TenantId_PeriodCode").IsUnique();
    }
}
