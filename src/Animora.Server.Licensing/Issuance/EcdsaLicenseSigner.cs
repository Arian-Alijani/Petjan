using System.Security.Cryptography;
using Animora.Domain.Results;
namespace Animora.Server.Licensing.Issuance;

public sealed class EcdsaLicenseSigner : ILicenseSigner
{
    private readonly ECDsa _key;
    public string KeyId { get; }
    public const string Algorithm = "ECDSA-P256";
    public EcdsaLicenseSigner(string keyId, ReadOnlySpan<byte> pkcs8)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        KeyId = keyId; _key = ECDsa.Create();
        try
        {
            _key.ImportPkcs8PrivateKey(pkcs8, out var read);
            if (read != pkcs8.Length || _key.KeySize != 256) throw new CryptographicException("Expected P-256 key.");
        }
        catch { _key.Dispose(); throw; }
    }
    public Result<byte[]> Sign(ReadOnlySpan<byte> payload)
    {
        try { return Result<byte[]>.Ok(_key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)); }
        catch (CryptographicException) { return Result<byte[]>.Fail(ErrorCodes.LicenseInvalid, "signing_failed"); }
    }
    public static (byte[] PublicKeyDer, byte[] PrivateKeyPkcs8) GenerateKeyPair()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportSubjectPublicKeyInfo(), key.ExportPkcs8PrivateKey());
    }
    public void Dispose() => _key.Dispose();
}
