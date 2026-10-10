using Animora.Domain.Primitives;
using Animora.Domain.Time;

namespace Animora.Persistence.PostgreSql.Security;

/// <summary>
/// A vendor-global asymmetric signing key (S05). <b>Not</b> a tenant-scoped entity: these keys are
/// owned by the platform, never exposed through the tenant context and never replicated to a tenant.
/// The public half is published by <c>GET /auth/keys</c>; the private half is stored only in
/// Data-Protection-protected form (<see cref="ProtectedPrivateKey"/>) and never leaves the server.
/// </summary>
/// <remarks>
/// S05 uses <see cref="Purposes.Snapshot"/> to sign offline capability snapshots. S16 reuses
/// <see cref="Purposes.License"/> for license signing. Rotation is append-only: a new key is created
/// and the previous one is retired (<see cref="RetiredUtc"/>) — rows are never deleted, so a snapshot
/// signed by an older key can still be verified against its published public half.
/// </remarks>
public sealed class SigningKey
{
    /// <summary>Known <see cref="Purpose"/> values.</summary>
    public static class Purposes
    {
        /// <summary>Signs offline capability snapshots (S05).</summary>
        public const string Snapshot = "snapshot";

        /// <summary>Signs licenses (S16).</summary>
        public const string License = "license";
    }

    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private SigningKey()
    {
    }

    /// <summary>ULID primary key (also the published <c>KeyId</c>).</summary>
    public string KeyId { get; private set; } = string.Empty;

    /// <summary>What the key signs: <see cref="Purposes.Snapshot"/> or <see cref="Purposes.License"/>.</summary>
    public string Purpose { get; private set; } = string.Empty;

    /// <summary>Signature algorithm moniker, e.g. <c>"ECDSA-P256"</c>.</summary>
    public string Algorithm { get; private set; } = string.Empty;

    /// <summary>The public key in SubjectPublicKeyInfo (DER) form.</summary>
    public byte[] PublicKeyDer { get; private set; } = [];

    /// <summary>The PKCS#8 private key, Data-Protection protected. Never published.</summary>
    public byte[] ProtectedPrivateKey { get; private set; } = [];

    /// <summary>When the key was created, UTC.</summary>
    public DateTimeOffset CreatedUtc { get; private set; }

    /// <summary>Hard expiry after which the key must not be used to sign; <see langword="null"/> = none.</summary>
    public DateTimeOffset? NotAfterUtc { get; private set; }

    /// <summary>When the key was retired from signing; <see langword="null"/> while it is active.</summary>
    public DateTimeOffset? RetiredUtc { get; private set; }

    /// <summary>Creates a new signing key. The caller provides the already-protected private bytes.</summary>
    public static SigningKey Create(
        string purpose,
        string algorithm,
        byte[] publicKeyDer,
        byte[] protectedPrivateKey,
        IClock clock,
        DateTimeOffset? notAfterUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);
        ArgumentNullException.ThrowIfNull(publicKeyDer);
        ArgumentNullException.ThrowIfNull(protectedPrivateKey);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.UtcNow.ToUniversalTime();
        return new SigningKey
        {
            KeyId = IdGenerator.NewId(now),
            Purpose = purpose,
            Algorithm = algorithm,
            PublicKeyDer = publicKeyDer,
            ProtectedPrivateKey = protectedPrivateKey,
            CreatedUtc = now,
            NotAfterUtc = notAfterUtc,
        };
    }

    /// <summary><see langword="true"/> when the key may still be used to sign at <paramref name="nowUtc"/>.</summary>
    public bool IsUsable(DateTimeOffset nowUtc) =>
        RetiredUtc is null && (NotAfterUtc is null || NotAfterUtc > nowUtc);

    /// <summary>Retains the public key through the complete licence offline/read-only horizon.</summary>
    public void RetireLicense(IClock clock, DateTimeOffset verifyUntilUtc)
    {
        if (Purpose != Purposes.License || verifyUntilUtc < clock.UtcNow)
            throw new ArgumentException("Invalid licence rotation horizon.");
        Retire(clock);
        if (NotAfterUtc is null || NotAfterUtc < verifyUntilUtc) NotAfterUtc = verifyUntilUtc.ToUniversalTime();
    }

    /// <summary>Retires the key from signing. Idempotent: keeps the original instant.</summary>
    public void Retire(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        RetiredUtc ??= clock.UtcNow.ToUniversalTime();
    }
}
