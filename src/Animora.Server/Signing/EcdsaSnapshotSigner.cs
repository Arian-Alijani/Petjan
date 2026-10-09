using System.Security.Cryptography;
using Animora.Domain.Results;

namespace Animora.Server.Signing;

/// <summary>
/// ECDSA P-256 / SHA-256 signer over <see cref="System.Security.Cryptography"/> (no custom crypto). Signatures use
/// the fixed-length IEEE P1363 encoding so any client platform can verify them.
/// </summary>
internal sealed class EcdsaSnapshotSigner : ISnapshotSigner, IDisposable
{
    /// <summary>Algorithm moniker stored in <c>SigningKeys.Algorithm</c> and published by <c>/auth/keys</c>.</summary>
    public const string AlgorithmName = "ECDSA-P256";

    private readonly ECDsa _key;

    /// <summary>Creates a signer over an already-imported private key.</summary>
    public EcdsaSnapshotSigner(string keyId, ECDsa key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentNullException.ThrowIfNull(key);
        KeyId = keyId;
        _key = key;
    }

    /// <inheritdoc />
    public string KeyId { get; }

    /// <inheritdoc />
    public string Algorithm => AlgorithmName;

    /// <summary>Generates a fresh P-256 key pair: (SPKI public DER, PKCS#8 private DER).</summary>
    public static (byte[] PublicKeyDer, byte[] PrivateKeyPkcs8) GenerateKeyPair()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportSubjectPublicKeyInfo(), key.ExportPkcs8PrivateKey());
    }

    /// <summary>Imports a PKCS#8 private key into a signer.</summary>
    public static EcdsaSnapshotSigner FromPkcs8(string keyId, byte[] pkcs8)
    {
        ArgumentNullException.ThrowIfNull(pkcs8);
        var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(pkcs8, out _);
        return new EcdsaSnapshotSigner(keyId, key);
    }

    /// <summary>Verifies a signature against a public key published as SPKI DER.</summary>
    public static bool VerifyWithPublicKey(byte[] publicKeyDer, ReadOnlySpan<byte> payload, string signatureBase64)
    {
        ArgumentNullException.ThrowIfNull(publicKeyDer);
        if (string.IsNullOrEmpty(signatureBase64))
        {
            return false;
        }

        Span<byte> signature = new byte[64];
        if (!Convert.TryFromBase64String(signatureBase64, signature, out int written) || written != 64)
        {
            return false;
        }

        using ECDsa key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(publicKeyDer, out _);
        return key.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <inheritdoc />
    public Result<string> Sign(ReadOnlySpan<byte> canonicalPayload)
    {
        byte[] signature = _key.SignData(canonicalPayload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Result<string>.Ok(Convert.ToBase64String(signature));
    }

    /// <inheritdoc />
    public bool Verify(ReadOnlySpan<byte> canonicalPayload, string signatureBase64) =>
        VerifyWithPublicKey(_key.ExportSubjectPublicKeyInfo(), canonicalPayload, signatureBase64);

    /// <inheritdoc />
    public void Dispose() => _key.Dispose();
}
