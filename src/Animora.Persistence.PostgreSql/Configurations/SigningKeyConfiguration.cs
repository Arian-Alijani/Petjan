using Animora.Persistence.PostgreSql.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

/// <summary>
/// Maps <see cref="SigningKey"/> to <c>public."SigningKeys"</c> (S05). The table is vendor-global:
/// it is <b>not</b> a <see cref="Animora.Domain.Primitives.SyncableEntity"/>, so
/// <c>ServerModelConventions.ApplyTenancy</c> skips it and no tenant / soft-delete filter is applied —
/// exactly the intended behaviour (operator/system only, never tenant-scoped).
/// </summary>
public sealed class SigningKeyConfiguration : IEntityTypeConfiguration<SigningKey>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SigningKey> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SigningKeys", "public");
        builder.HasKey(e => e.KeyId);

        builder.Property(e => e.KeyId).HasColumnType("character varying(26)").HasMaxLength(26);
        builder.Property(e => e.Purpose).IsRequired().HasMaxLength(32);
        builder.Property(e => e.Algorithm).IsRequired().HasMaxLength(32);
        builder.Property(e => e.PublicKeyDer).HasColumnType("bytea").IsRequired();
        builder.Property(e => e.ProtectedPrivateKey).HasColumnType("bytea").IsRequired();

        builder.Property(e => e.CreatedUtc).HasColumnType("timestamp with time zone").HasPrecision(6);
        builder.Property(e => e.NotAfterUtc).HasColumnType("timestamp with time zone").HasPrecision(6);
        builder.Property(e => e.RetiredUtc).HasColumnType("timestamp with time zone").HasPrecision(6);

        // Resolve the active signing key per purpose quickly.
        builder.HasIndex(e => new { e.Purpose, e.RetiredUtc })
            .HasDatabaseName("IX_SigningKeys_Purpose_RetiredUtc");
    }
}
