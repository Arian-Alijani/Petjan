using Animora.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Identity;

public sealed class StaffMembershipConfiguration : IEntityTypeConfiguration<StaffMembership>
{
    public void Configure(EntityTypeBuilder<StaffMembership> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.UserId, e.Role }).HasDatabaseName("IX_StaffMemberships_TenantId_UserId_Role").IsUnique();
        builder.Ignore(e => e.ExtraPermissionCodes);
        Animora.Persistence.Sqlite.Conventions.JsonSets.Map<StaffMembership>(builder, "_extraPermissionCodes", "ExtraPermissionCodes");
        builder.Ignore(e => e.DeniedPermissionCodes);
        Animora.Persistence.Sqlite.Conventions.JsonSets.Map<StaffMembership>(builder, "_deniedPermissionCodes", "DeniedPermissionCodes");
    }
}
