using Animora.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Animora.Persistence.PostgreSql.Conventions;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("Expenses", "public");
        OwnedRows.Money(builder, e => e.Amount);
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.IncurredUtc, e.CategoryCode }).HasDatabaseName("IX_Expenses_TenantId_IncurredUtc_CategoryCode");
    }
}
