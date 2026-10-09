using Animora.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Identity;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.HasKey(e => e.Id);

        // S04: Tenant.Code is the server's sign-in code (PostgreSQL only). The local/offline schema is
        // unchanged: the property is not mapped here, so no SQLite migration is needed.
        builder.Ignore(e => e.Code);
    }
}
