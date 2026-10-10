using Animora.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Animora.Persistence.PostgreSql.Conventions;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class ChequeConfiguration : IEntityTypeConfiguration<Cheque>
{
    public void Configure(EntityTypeBuilder<Cheque> builder)
    {
        builder.ToTable("Cheques", "public");
        OwnedRows.Money(builder, e => e.Amount);
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.State, e.DueUtc }).HasDatabaseName("IX_Cheques_TenantId_State_DueUtc");
        OwnedRows.Map(builder, e => e.History);
    }
}

/// <summary>Child rows of <see cref="ChequeEvent"/>'s aggregate, table <c>ChequeEvents</c>.</summary>
public sealed class ChequeEventConfiguration : IEntityTypeConfiguration<ChequeEvent>
{
    public void Configure(EntityTypeBuilder<ChequeEvent> builder)
    {
        OwnedRows.Row(builder, "ChequeEvents", e => e.EventId);
    }
}
