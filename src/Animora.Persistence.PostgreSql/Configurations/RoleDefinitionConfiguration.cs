using Animora.Domain.Identity;
using Animora.Persistence.PostgreSql.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

/// <summary>
/// Maps <see cref="RoleDefinition"/> to <c>public."RoleDefinitions"</c> (S04), one row per
/// <c>(TenantId, Role)</c>. The granted permission codes are stored inline as a <c>text[]</c> column.
/// </summary>
public sealed class RoleDefinitionConfiguration : IEntityTypeConfiguration<RoleDefinition>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RoleDefinition> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RoleDefinitions", "public");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Role).HasConversion<int>().HasColumnType("integer");
        builder.Property(e => e.DisplayKey).IsRequired().HasMaxLength(64);
        builder.Property(e => e.IsSystem).HasColumnType("boolean");
        builder.Ignore(e => e.PermissionCodes);
        builder.MapPermissionSet("_permissionCodes", "PermissionCodes");

        ServerModelConventions.TenantIndex(builder, nameof(RoleDefinition.Role)).IsUnique();
    }
}
