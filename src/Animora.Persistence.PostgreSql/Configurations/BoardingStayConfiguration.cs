using Animora.Domain.Boarding;
using Animora.Persistence.PostgreSql.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class BoardingStayConfiguration : IEntityTypeConfiguration<BoardingStay>
{
    public void Configure(EntityTypeBuilder<BoardingStay> builder)
    {
        builder.ToTable("BoardingStays", "public");
        builder.HasKey(e => e.Id);
        ServerModelConventions.TenantIndex(builder, nameof(BoardingStay.State), nameof(BoardingStay.CheckInUtc));
        ServerModelConventions.TenantIndex(builder, nameof(BoardingStay.RoomId), nameof(BoardingStay.State));
        OwnedRows.Money(builder, e => e.DailyRate);
        builder.HasMany(e => e.Items).WithOne().HasForeignKey("OwnerId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        builder.Ignore(e => e.RoomHistory);
        JsonFields.Map<BoardingStay, StayRoomAssignment>(builder, "_roomHistory", "RoomHistory");
        builder.Ignore(e => e.AreItemsLocked);
    }
}
public sealed class StayItemConfiguration : IEntityTypeConfiguration<StayItem>
{
    public void Configure(EntityTypeBuilder<StayItem> builder)
    {
        builder.ToTable("BoardingStayItems", "public");
        builder.Property<string>("OwnerId").HasColumnType("character varying(26)").HasMaxLength(26);
        builder.Property(e => e.ItemId).HasMaxLength(26).ValueGeneratedNever();
        builder.HasKey("OwnerId", nameof(StayItem.ItemId));
        builder.Property(e => e.Kind).HasConversion<int>();
        OwnedRows.Money(builder, e => e.UnitPrice);
        OwnedRows.Quantity(builder, e => e.Quantity);
    }
}
