using Animora.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Animora.Persistence.Sqlite.Conventions;

namespace Animora.Persistence.Sqlite.Configurations.Finance;

public sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.IncurredUtc, e.CategoryCode }).HasDatabaseName("IX_Expenses_TenantId_IncurredUtc_CategoryCode");
    }
}
