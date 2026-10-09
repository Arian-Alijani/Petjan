using Animora.Domain.Identity;
using Animora.Persistence.PostgreSql.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

/// <summary>
/// Maps <see cref="StaffMembership"/> to <c>public."StaffMemberships"</c> (S04). The extra/denied
/// permission-code overrides are stored inline as <c>text[]</c> columns (see
/// <see cref="PermissionSetConversion"/>).
/// </summary>
public sealed class StaffMembershipConfiguration : IEntityTypeConfiguration<StaffMembership>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StaffMembership> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("StaffMemberships", "public");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.UserId).IsRequired().HasColumnType("character varying(26)").HasMaxLength(26);
        builder.Property(e => e.Role).HasConversion<int>().HasColumnType("integer");
        builder.Property(e => e.Status).HasConversion<int>().HasColumnType("integer");
        builder.Property(e => e.JoinedUtc).HasColumnType("timestamp with time zone").HasPrecision(6);
        builder.Property(e => e.EndedUtc).HasColumnType("timestamp with time zone").HasPrecision(6);
        builder.Property(e => e.WorkStartMinute).HasColumnType("integer");
        builder.Property(e => e.WorkEndMinute).HasColumnType("integer");
        builder.Property(e => e.WorkDaysMask).HasColumnType("integer");

        // The public IReadOnlySet views are read-only projections; the HashSet fields are the mapped state.
        builder.Ignore(e => e.ExtraPermissionCodes);
        builder.Ignore(e => e.DeniedPermissionCodes);
        builder.MapPermissionSet("_extraPermissionCodes", "ExtraPermissionCodes");
        builder.MapPermissionSet("_deniedPermissionCodes", "DeniedPermissionCodes");

        // IsActive is derived (Status + EndedUtc), never stored.
        builder.Ignore(e => e.IsActive);

        ServerModelConventions.TenantIndex(builder, nameof(StaffMembership.UserId));
    }
}
