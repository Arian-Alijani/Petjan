using Animora.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> b)
    {
        b.ToTable("Subscriptions", "public"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(26); b.Property(x => x.TenantId).HasMaxLength(26).IsRequired();
        b.HasIndex(x => x.TenantId).IsUnique(); b.Property(x => x.PlanId).HasMaxLength(26).IsRequired();
        b.HasOne<Plan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Status).HasConversion<short>();
        b.Property(x => x.ConcurrencyStamp).HasConversion<Animora.Persistence.PostgreSql.Concurrency.StringGuidConverter>()
            .HasColumnType("uuid").IsConcurrencyToken();
        b.HasIndex(x => new { x.Status, x.CurrentPeriodEndUtc }).HasDatabaseName("IX_Subscriptions_Status_CurrentPeriodEndUtc");
        b.HasMany(x => x.BillingPeriods).WithOne().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.BillingPeriods).HasField("_billingPeriods").UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Events).HasField("_events").UsePropertyAccessMode(PropertyAccessMode.Field);
        foreach (var name in new[] { "CurrentPeriodStartUtc", "CurrentPeriodEndUtc", "GraceEndsUtc", "CancelRequestedUtc", "CreatedUtc", "UpdatedUtc" })
            b.Property(name).HasColumnType("timestamp(6) with time zone");
    }
}
