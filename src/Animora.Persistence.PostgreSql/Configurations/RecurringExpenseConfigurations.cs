using Animora.Persistence.PostgreSql.Conventions;
using Animora.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class RecurringExpenseConfiguration : IEntityTypeConfiguration<RecurringExpense>
{
    public void Configure(EntityTypeBuilder<RecurringExpense> builder)
    {
        builder.ToTable("RecurringExpenses", "public");
        OwnedRows.Money(builder, e => e.Amount);
        builder.HasIndex(e => new { e.TenantId, e.Title }).HasDatabaseName("IX_RecurringExpenses_TenantId_Title");
        builder.HasKey(e => e.Id);
    }
}

public sealed class RecurringExpensePaymentConfiguration : IEntityTypeConfiguration<RecurringExpensePayment>
{
    public void Configure(EntityTypeBuilder<RecurringExpensePayment> builder)
    {
        builder.ToTable("RecurringExpensePayments", "public");
        builder.HasKey(e => e.Id);
        // The dedupe guard: a period of a template can be settled exactly once.
        builder.HasIndex(e => new { e.TenantId, e.RecurringExpenseId, e.PeriodCode })
            .IsUnique().HasDatabaseName("IX_RecurringExpensePayments_TenantId_RecurringExpenseId_PeriodCode");
    }
}
