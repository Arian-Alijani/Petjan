using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>Column bounds of the G06 tables; applied after the generic scalar conventions, which would otherwise pick short-text bounds.</summary>
internal static class OnlineEntitlementModel
{
    internal static void ApplyBounds(ModelBuilder modelBuilder)
    {
        var session = modelBuilder.Entity<ClientSessionRow>();
        session.Property(e => e.AccessToken).HasMaxLength(ClientSessionRow.TokenMaxLength);
        session.Property(e => e.RefreshToken).HasMaxLength(ClientSessionRow.TokenMaxLength);
        session.Property(e => e.OfflineCredentialVerifier).HasMaxLength(ClientSessionRow.VerifierMaxLength);

        var sealedRow = modelBuilder.Entity<SealedEntitlementRow>();
        sealedRow.Property(e => e.LicenseToken).HasMaxLength(SealedEntitlementRow.TokenMaxLength);
        sealedRow.Property(e => e.InstallationIdHash).HasMaxLength(SealedEntitlementRow.InstallationHashLength);
    }
}
