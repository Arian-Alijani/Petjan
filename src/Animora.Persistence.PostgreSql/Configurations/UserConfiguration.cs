using Animora.Domain.Identity;
using Animora.Persistence.PostgreSql.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

/// <summary>
/// Maps the Domain <see cref="User"/> to <c>public."Users"</c> (S04). Shared tenant/version/timestamp
/// shape comes from <see cref="ServerModelConventions"/>. The aggregate's private
/// <c>_firstFailureUtc</c> lockout-window anchor has no public property; it is mapped as a field-only
/// property (<c>FirstFailureUtc</c>) so the failure window survives across requests.
/// </summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Users", "public");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.UserName).IsRequired().HasMaxLength(128);
        builder.Property(e => e.DisplayName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.PasswordHash).HasMaxLength(256);
        builder.Property(e => e.SecurityStamp).IsRequired().HasColumnType("character varying(26)").HasMaxLength(26);
        builder.Property(e => e.Status).HasConversion<int>().HasColumnType("integer");
        builder.Property(e => e.LastLoginUtc).HasColumnType("timestamp with time zone").HasPrecision(6);
        builder.Property(e => e.FailedAttemptCount).HasColumnType("integer").HasDefaultValue(0);
        builder.Property(e => e.LockoutEndUtc).HasColumnType("timestamp with time zone").HasPrecision(6);
        builder.Property(e => e.MustChangePassword).HasColumnType("boolean").HasDefaultValue(false);

        builder.Property<DateTimeOffset?>("_firstFailureUtc")
            .HasColumnName("FirstFailureUtc")
            .HasColumnType("timestamp with time zone")
            .HasPrecision(6);

        // UserName is lowercased at User.Create, so a plain unique tenant-first index is case-insensitive.
        ServerModelConventions.TenantIndex(builder, nameof(User.UserName)).IsUnique();
    }
}
