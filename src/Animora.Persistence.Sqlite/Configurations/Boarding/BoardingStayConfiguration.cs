using Animora.Domain.Boarding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Boarding;

public sealed class BoardingStayConfiguration : IEntityTypeConfiguration<BoardingStay>
{
    public void Configure(EntityTypeBuilder<BoardingStay> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.FrozenTotalMinor);
        builder.HasIndex(e => new { e.TenantId, e.State, e.CheckInUtc }).HasDatabaseName("IX_BoardingStays_TenantId_State_CheckInUtc");
        builder.Ignore(e => e.Items);
        Animora.Persistence.Sqlite.Conventions.JsonFields.Map<BoardingStay, StayItem>(builder, "_items", "Items");
        builder.Ignore(e => e.RoomHistory);
        Animora.Persistence.Sqlite.Conventions.JsonFields.Map<BoardingStay, StayRoomAssignment>(builder, "_roomHistory", "RoomHistory");
    }
}
