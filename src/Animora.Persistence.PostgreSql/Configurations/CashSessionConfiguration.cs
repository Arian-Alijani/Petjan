using Animora.Domain.Cashier;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Animora.Persistence.PostgreSql.Conventions;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class CashSessionConfiguration : IEntityTypeConfiguration<CashSession>
{
    public void Configure(EntityTypeBuilder<CashSession> builder)
    {
        builder.ToTable("CashSessions", "public");
        builder.HasKey(e => e.Id);
        OwnedRows.Money(builder, e => e.OpeningFloat);
        OwnedRows.Money(builder, e => e.DeclaredClosing);
        OwnedRows.Money(builder, e => e.ExpectedClosing);
        OwnedRows.Money(builder, e => e.Variance);
        builder.Ignore(e => e.IsClosed);
        builder.Ignore(e => e.Currency);
        builder.HasIndex(e => new { e.TenantId, e.State, e.OpenedUtc }).HasDatabaseName("IX_CashSessions_TenantId_State_OpenedUtc");
        builder.HasIndex(e => new { e.TenantId, e.CashierUserId }).IsUnique()
            .HasFilter("\"State\" = 0 AND NOT \"Deleted\"")
            .HasDatabaseName("IX_CashSessions_TenantId_CashierUserId_Open");
        OwnedRows.Map(builder, e => e.Movements);
    }
}

/// <summary>Child rows of <see cref="CashMovement"/>'s aggregate, table <c>CashMovements</c>.</summary>
public sealed class CashMovementConfiguration : IEntityTypeConfiguration<CashMovement>
{
    public void Configure(EntityTypeBuilder<CashMovement> builder)
    {
        OwnedRows.Row(builder, "CashMovements", e => e.MovementId);
        OwnedRows.Money(builder, e => e.Amount);
    }
}
