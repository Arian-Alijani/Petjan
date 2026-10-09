using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Animora.Server.Signing;

/// <summary>
/// Loads / creates the vendor-global signing keys in <c>public."SigningKeys"</c>. The private half is stored only as
/// Data-Protection-protected PKCS#8 bytes (the same PostgreSQL-backed key ring as the bearer tokens); the table is
/// not tenant-scoped, so these reads never depend on the request tenant.
/// </summary>
internal sealed class SigningKeyStore(
    AnimoraServerDbContext context,
    IDataProtectionProvider dataProtection,
    IClock clock) : ISnapshotSignerProvider
{
    /// <summary>Data-Protection purpose of the private key bytes. Changing it orphans every stored key.</summary>
    public const string ProtectorPurpose = "Animora.Server.SigningKeys.v1";

    /// <inheritdoc />
    public async Task<ISnapshotSigner> GetAsync(CancellationToken ct = default)
    {
        DateTimeOffset now = clock.UtcNow;
        SigningKey? key = await FindActiveAsync(now, ct).ConfigureAwait(false) ?? await CreateAsync(now, ct).ConfigureAwait(false);

        IDataProtector protector = dataProtection.CreateProtector(ProtectorPurpose);
        byte[] pkcs8 = protector.Unprotect(key.ProtectedPrivateKey);
        return EcdsaSnapshotSigner.FromPkcs8(key.KeyId, pkcs8);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SigningKey>> ListPublicAsync(string purpose, CancellationToken ct = default) =>
        await context.SigningKeys
            .AsNoTracking()
            .Where(k => k.Purpose == purpose)
            .OrderByDescending(k => k.CreatedUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    private async Task<SigningKey?> FindActiveAsync(DateTimeOffset now, CancellationToken ct)
    {
        List<SigningKey> candidates = await context.SigningKeys
            .AsNoTracking()
            .Where(k => k.Purpose == SigningKey.Purposes.Snapshot && k.RetiredUtc == null)
            .OrderByDescending(k => k.CreatedUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return candidates.FirstOrDefault(k => k.IsUsable(now));
    }

    private async Task<SigningKey> CreateAsync(DateTimeOffset now, CancellationToken ct)
    {
        (byte[] publicKey, byte[] privateKey) = EcdsaSnapshotSigner.GenerateKeyPair();
        byte[] protectedKey = dataProtection.CreateProtector(ProtectorPurpose).Protect(privateKey);
        Array.Clear(privateKey);

        SigningKey created = SigningKey.Create(
            SigningKey.Purposes.Snapshot,
            EcdsaSnapshotSigner.AlgorithmName,
            publicKey,
            protectedKey,
            new FixedClock(now));

        context.SigningKeys.Add(created);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
        return created;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
