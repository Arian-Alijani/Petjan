namespace Animora.Server.Licensing.Verification;

public sealed record LicensePublicKey(string KeyId, byte[] PublicKeyDer, DateTimeOffset NotBeforeUtc, DateTimeOffset? NotAfterUtc);
public interface ILicenseKeySet { LicensePublicKey? Find(string keyId); }
public sealed class LicenseKeySet(IReadOnlyList<LicensePublicKey> keys) : ILicenseKeySet
{
    public LicensePublicKey? Find(string keyId) => keys.SingleOrDefault(x => x.KeyId == keyId);
}
