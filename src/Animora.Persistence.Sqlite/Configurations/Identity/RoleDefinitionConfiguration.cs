using Animora.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Identity;

public sealed class RoleDefinitionConfiguration : IEntityTypeConfiguration<RoleDefinition>
{
    public void Configure(EntityTypeBuilder<RoleDefinition> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Ignore(e => e.PermissionCodes);
        Animora.Persistence.Sqlite.Conventions.JsonSets.Map<RoleDefinition>(builder, "_permissionCodes", "PermissionCodes");
    }
}
