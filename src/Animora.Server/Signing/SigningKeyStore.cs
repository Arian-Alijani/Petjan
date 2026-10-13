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
    IClock clock,
    ActiveSignerCache cache) : ISnapshotSignerProvider
{
    /// <summary>Data-Protection purpose of the private key bytes. Changing it orphans every stored key.</summary>
    public const string ProtectorPurpose = "Animora.Server.SigningKeys.v1";

    /// <inheritdoc />
    public async Task<ISnapshotSigner> GetAsync(CancellationToken ct = default)
    {
        DateTimeOffset now = clock.UtcNow;

        // Steady state: the resolved signer (private key already unprotected + imported) is reused, so signing
        // does no DB read and no repeated Data-Protection unprotect.
        if (cache.TryGet(now) is { } cached)
        {
            return cached;
        }

        SigningKey key = await FindActiveAsync(now, ct).ConfigureAwait(false)
            ?? await CreateGuardedAsync(now, ct).ConfigureAwait(false);

        IDataProtector protector = dataProtection.CreateProtector(ProtectorPurpose);
        byte[] pkcs8 = protector.Unprotect(key.ProtectedPrivateKey);
        ISnapshotSigner signer = EcdsaSnapshotSigner.FromPkcs8(key.KeyId, pkcs8);
        return cache.Set(signer, key.NotAfterUtc, now);
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

    /// <summary>
    /// Creates the first active key exactly once even when several instances / requests hit a cold database at the
    /// same moment: a transaction-scoped PostgreSQL advisory lock serialises creators, and the active key is
    /// re-read under the lock so every loser adopts the winner's key instead of inserting a second "active" one.
    /// </summary>
    private async Task<SigningKey> CreateGuardedAsync(DateTimeOffset now, CancellationToken ct)
    {
        bool ownsTransaction = context.Database.CurrentTransaction is null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            context.Database.CurrentTransaction
            ?? await context.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({"animora.signing-keys"}), hashtext({SigningKey.Purposes.Snapshot}))", ct)
                .ConfigureAwait(false);

            SigningKey? winner = await FindActiveAsync(now, ct).ConfigureAwait(false);
            if (winner is not null)
            {
                return winner;
            }

            SigningKey created = Create(now);
            context.SigningKeys.Add(created);
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
            if (ownsTransaction)
            {
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }

            return created;
        }
        finally
        {
            if (ownsTransaction)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private SigningKey Create(DateTimeOffset now)
    {
        (byte[] publicKey, byte[] privateKey) = EcdsaSnapshotSigner.GenerateKeyPair();
        byte[] protectedKey = dataProtection.CreateProtector(ProtectorPurpose).Protect(privateKey);
        Array.Clear(privateKey);

        return SigningKey.Create(
            SigningKey.Purposes.Snapshot,
            EcdsaSnapshotSigner.AlgorithmName,
            publicKey,
            protectedKey,
            new FixedClock(now));
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}

/// <summary>
/// Process-wide holder of the resolved active snapshot signer. Per-process state (documented single-instance
/// component): it is refreshed from the database every <see cref="RefreshAfter"/> so a key rotation performed on
/// another instance is picked up within that window, and it is never used past the key's own <c>NotAfterUtc</c>.
/// </summary>
internal sealed class ActiveSignerCache
{
    /// <summary>How long a resolved signer is trusted before the key table is consulted again.</summary>
    public static readonly TimeSpan RefreshAfter = TimeSpan.FromMinutes(5);

    private readonly object _gate = new();
    private ISnapshotSigner? _signer;
    private DateTimeOffset _validUntil;

    /// <summary>The cached signer, or <see langword="null"/> when none is cached / it is stale.</summary>
    public ISnapshotSigner? TryGet(DateTimeOffset now)
    {
        lock (_gate)
        {
            return _signer is not null && now < _validUntil ? _signer : null;
        }
    }

    /// <summary>Caches <paramref name="signer"/> and returns it.</summary>
    public ISnapshotSigner Set(ISnapshotSigner signer, DateTimeOffset? keyNotAfterUtc, DateTimeOffset now)
    {
        DateTimeOffset until = now + RefreshAfter;
        if (keyNotAfterUtc is { } notAfter && notAfter < until)
        {
            until = notAfter;
        }

        lock (_gate)
        {
            _signer = signer;
            _validUntil = until;
        }

        return signer;
    }

    /// <summary>Drops the cached signer (call on key rotation).</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _signer = null;
        }
    }
}
