namespace Animora.Contracts.Wire.Auth;

/// <summary>
/// Body of the anonymous <c>GET /auth/keys</c> (S05): the <b>public</b> halves of the server signing keys, addressed
/// by <see cref="PublicKeyInfo.KeyId"/>. A client verifies a capability snapshot with the key named by the
/// snapshot's <c>KeyId</c>; retired keys stay listed so older snapshots remain verifiable. Never carries private
/// material.
/// </summary>
/// <param name="Keys">Published keys, newest first.</param>
public sealed record PublicKeySet(IReadOnlyList<PublicKeyInfo> Keys);

/// <summary>One published public key.</summary>
/// <param name="KeyId">Key ULID.</param>
/// <param name="Algorithm">Signature algorithm moniker, e.g. <c>ECDSA-P256</c>.</param>
/// <param name="PublicKeyDerBase64">SubjectPublicKeyInfo (DER), base64.</param>
/// <param name="NotAfterUtc">Hard expiry of the key, or <see langword="null"/> when none.</param>
public sealed record PublicKeyInfo(string KeyId, string Algorithm, string PublicKeyDerBase64, DateTimeOffset? NotAfterUtc);
