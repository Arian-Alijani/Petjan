using Animora.Domain.Cashier;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Animora.Persistence.Sqlite.Conventions;

namespace Animora.Persistence.Sqlite.Configurations.Cashier;

public sealed class CashSessionConfiguration : IEntityTypeConfiguration<CashSession>
{
    public void Configure(EntityTypeBuilder<CashSession> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.State, e.OpenedUtc }).HasDatabaseName("IX_CashSessions_TenantId_State_OpenedUtc");
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
