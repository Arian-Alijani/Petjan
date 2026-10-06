using Animora.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Finance;

public sealed class RecurringExpenseConfiguration : IEntityTypeConfiguration<RecurringExpense>
{
    public void Configure(EntityTypeBuilder<RecurringExpense> builder)
    {
        builder.HasKey(e => e.Id);
    }
}

public sealed class RecurringExpensePaymentConfiguration : IEntityTypeConfiguration<RecurringExpensePayment>
{
    public void Configure(EntityTypeBuilder<RecurringExpensePayment> builder)
    {
        builder.HasKey(e => e.Id);
        // The dedupe guard: a period of a template can be settled exactly once.
        builder.HasIndex(e => new { e.TenantId, e.RecurringExpenseId, e.PeriodCode })
            .IsUnique().HasDatabaseName("IX_RecurringExpensePayments_TenantId_RecurringExpenseId_PeriodCode");
    }
}
