namespace Animora.Contracts.Wire.Auth;

/// <summary>
/// Response of <c>POST /auth/capabilities</c> (S05): a server-signed offline capability snapshot.
/// The client verifies <see cref="Signature"/> over the exact UTF-8 bytes of <see cref="PayloadJson"/> with the
/// public key named <see cref="KeyId"/> (see <c>GET /auth/keys</c>).
/// </summary>
/// <param name="PayloadJson">Canonical (deterministic) JSON of the snapshot — the signed bytes.</param>
/// <param name="KeyId">Signing key ULID.</param>
/// <param name="Signature">Detached base64 signature of <see cref="PayloadJson"/>.</param>
public sealed record CapabilitySnapshotResponse(string PayloadJson, string KeyId, string Signature);
