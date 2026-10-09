using Animora.Persistence.PostgreSql.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

/// <summary>
/// Maps <see cref="RefreshToken"/> to <c>public."RefreshTokens"</c> (S04). Not a
/// <c>SyncableEntity</c>: the tenant query filter is applied by <c>AnimoraServerDbContext</c>.
/// Only the SHA-256 hex of the wire token is stored, and it is globally unique.
/// </summary>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    private const string Id26 = "character varying(26)";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RefreshTokens", "public");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnType(Id26).HasMaxLength(26);
        builder.Property(e => e.TenantId).IsRequired().HasColumnType(Id26).HasMaxLength(26);
        builder.Property(e => e.UserId).IsRequired().HasColumnType(Id26).HasMaxLength(26);
        builder.Property(e => e.DeviceId).IsRequired().HasColumnType(Id26).HasMaxLength(26);
        builder.Property(e => e.FamilyId).IsRequired().HasColumnType(Id26).HasMaxLength(26);
        builder.Property(e => e.RotatedToId).HasColumnType(Id26).HasMaxLength(26);
        builder.Property(e => e.TokenHash).IsRequired().HasColumnType("character varying(64)").HasMaxLength(64);
        builder.Property(e => e.IssuedUtc).HasColumnType("timestamp with time zone").HasPrecision(6);
        builder.Property(e => e.ExpiresUtc).HasColumnType("timestamp with time zone").HasPrecision(6);
        builder.Property(e => e.RevokedUtc).HasColumnType("timestamp with time zone").HasPrecision(6);

        builder.HasIndex(e => e.TokenHash).IsUnique().HasDatabaseName("IX_RefreshTokens_TokenHash");
        builder.HasIndex(e => new { e.TenantId, e.UserId }).HasDatabaseName("IX_RefreshTokens_TenantId_UserId");
        builder.HasIndex(e => new { e.TenantId, e.FamilyId }).HasDatabaseName("IX_RefreshTokens_TenantId_FamilyId");
    }
}
