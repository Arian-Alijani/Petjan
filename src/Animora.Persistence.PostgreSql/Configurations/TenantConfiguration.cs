using Animora.Domain.Identity;
using Animora.Persistence.PostgreSql.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

/// <summary>
/// Maps <see cref="Tenant"/> to <c>public."Tenants"</c>. A tenant's own <c>TenantId</c> equals
/// its <c>Id</c>, so the uniform tenant filter also covers the tenant row. Shared column/version
/// shape comes from <see cref="ServerModelConventions"/>.
/// </summary>
public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Tenants", "public");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.DisplayName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.LegalName).HasMaxLength(200);
        builder.Property(e => e.Timezone).IsRequired().HasMaxLength(64);
        builder.Property(e => e.DefaultCurrency).IsRequired().HasColumnType("character varying(3)").HasMaxLength(3);
        builder.Property(e => e.Status).HasConversion<int>().HasColumnType("integer");

        ServerModelConventions.TenantIndex(builder, nameof(Tenant.Status));
    }
}
