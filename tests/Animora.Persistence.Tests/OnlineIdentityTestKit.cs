using System.Collections.Concurrent;
using System.Security.Cryptography;
using Animora.Application.Security;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Infrastructure.Local.Identity;
using Animora.Infrastructure.Local.Security;
using Animora.Server.Licensing.Issuance;
using Animora.Server.Licensing.Verification;

namespace Animora.Persistence.Tests;

// G06 shared test doubles for the online-identity planes (gate / handler / auth client / sealed cache tests).
// Deterministic: fixed instants, in-memory secret store, locally generated ECDSA licence keys — no wall clock, no network.

/// <summary>An in-memory <see cref="ISecretStore"/> standing in for the OS keystore (which does not exist on CI runners).</summary>
public sealed class MemorySecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, byte[]> _secrets = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Result<byte[]> TryRead(string name) => _secrets.TryGetValue(name, out var value)
        ? Result<byte[]>.Ok(value)
        : Result<byte[]>.Fail(ErrorCodes.NotFound);

    /// <inheritdoc />
    public Result Write(string name, byte[] value)
    {
        _secrets[name] = value;
        return Result.Ok();
    }

    /// <inheritdoc />
    public Result Delete(string name)
    {
        _secrets.TryRemove(name, out _);
        return Result.Ok();
    }
}

/// <summary>A monotonic clock whose high-water mark only moves forward via <see cref="Observe"/> (exactly the production contract).</summary>
public sealed class FakeMonotonicClock(DateTimeOffset start) : IMonotonicClock
{
    private readonly object _gate = new();
    private DateTimeOffset _highWater = start;

    /// <inheritdoc />
    public long ElapsedTicks => 0;

    /// <inheritdoc />
    public DateTimeOffset HighWaterUtc
    {
        get
        {
            lock (_gate) return _highWater;
        }
    }

    /// <inheritdoc />
    public void Observe(DateTimeOffset serverUtc)
    {
        lock (_gate)
        {
            if (serverUtc > _highWater) _highWater = serverUtc.ToUniversalTime();
        }
    }
}

/// <summary>A fingerprint source returning fixed raw components (so tests can prove those raw values never leave the machine).</summary>
public sealed class FixedFingerprintSource(params string[] components) : IDeviceFingerprintSource
{
    /// <inheritdoc />
    public IReadOnlyList<string> Collect() => components;
}

/// <summary>Cheap, transparent one-way stand-in for the PBKDF2 hasher (verifier round-trips; no crypto cost in tests).</summary>
public sealed class PlainPasswordHasher : IPasswordHasher
{
    private const string Prefix = "PLAIN$";

    /// <inheritdoc />
    public string Hash(string password) => Prefix + password;

    /// <inheritdoc />
    public PasswordVerificationOutcome Verify(string hash, string password) =>
        string.Equals(hash, Prefix + password, StringComparison.Ordinal) ? PasswordVerificationOutcome.Success : PasswordVerificationOutcome.Failed;
}

/// <summary>
/// A licence authority for tests: one throw-away ECDSA P-256 key pair that issues genuinely signed licence tokens
/// and publishes its public half in both the verification form and the wire (<c>GET /licensing/keys</c>) form.
/// </summary>
public sealed class TestLicenseAuthority
{
    private readonly byte[] _privateKey;

    /// <summary>Creates an authority whose key is valid from <paramref name="keyNotBeforeUtc"/> (open-ended).</summary>
    public TestLicenseAuthority(DateTimeOffset keyNotBeforeUtc)
    {
        KeyNotBeforeUtc = keyNotBeforeUtc;
        var pair = EcdsaLicenseSigner.GenerateKeyPair();
        PublicKeyDer = pair.PublicKeyDer;
        _privateKey = pair.PrivateKeyPkcs8;
        KeyId = IdGenerator.NewId();
    }

    /// <summary>26-char key id embedded in every token.</summary>
    public string KeyId { get; }

    /// <summary>SubjectPublicKeyInfo DER of the public key.</summary>
    public byte[] PublicKeyDer { get; }

    /// <summary>The key's not-before instant.</summary>
    public DateTimeOffset KeyNotBeforeUtc { get; }

    /// <summary>The verification-half key set.</summary>
    public ILicenseKeySet KeySet => new LicenseKeySet([new Animora.Server.Licensing.Verification.LicensePublicKey(KeyId, PublicKeyDer, KeyNotBeforeUtc, null)]);

    /// <summary>The wire key set the server would serve at <c>GET /licensing/keys</c>.</summary>
    public Animora.Contracts.Wire.Licensing.LicensePublicKeySet ToWire(DateTimeOffset serverTimeUtc) =>
        new([new Animora.Contracts.Wire.Licensing.LicensePublicKey(KeyId, EcdsaLicenseSigner.Algorithm, Convert.ToBase64String(PublicKeyDer), KeyNotBeforeUtc, null)],
            serverTimeUtc);

    /// <summary>Signs <paramref name="payload"/> into a licence token.</summary>
    public string Issue(LicensePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        using var signer = new EcdsaLicenseSigner(KeyId, _privateKey);
        var bytes = LicenseCanonicalPayload.Serialize(payload);
        var signature = signer.Sign(bytes).Value!;
        return LicenseEnvelope.Format(bytes, signature, KeyId);
    }

    /// <summary>A structurally valid payload: valid from <c>issued − 5 min</c>, expires after <paramref name="validDays"/>, then an offline grace window.</summary>
    public static LicensePayload Payload(string tenantId, string installationHash, DateTimeOffset issuedUtc, long sequence = 1,
        string? licenseId = null, int validDays = 40, int graceDays = 14) =>
        new(1, licenseId ?? IdGenerator.NewId(), tenantId, "clinic", IdGenerator.NewId(), "standard", 5, 10, installationHash,
            IdGenerator.NewId(), sequence, issuedUtc, issuedUtc.AddMinutes(-5), issuedUtc.AddDays(validDays),
            issuedUtc.AddDays(validDays + graceDays), []);
}
