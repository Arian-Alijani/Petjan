using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Security;

namespace Animora.Server.Signing;

/// <summary>
/// Asymmetric, server-only signer of capability snapshots (S05). Distinct from the client-side HMAC signer of
/// the local store: here the private key never leaves the server and clients hold only the public half.
/// </summary>
internal interface ISnapshotSigner
{
    /// <summary>Id of the active signing key (ULID).</summary>
    string KeyId { get; }

    /// <summary>Signature algorithm moniker.</summary>
    string Algorithm { get; }

    /// <summary>Signs <paramref name="canonicalPayload"/>; returns the detached signature, base64.</summary>
    Result<string> Sign(ReadOnlySpan<byte> canonicalPayload);

    /// <summary>Verifies a detached base64 signature with this key's public half (used by tests / self-checks).</summary>
    bool Verify(ReadOnlySpan<byte> canonicalPayload, string signatureBase64);
}

/// <summary>Resolves the signer for the currently active snapshot key and lists the published public keys.</summary>
internal interface ISnapshotSignerProvider
{
    /// <summary>The signer over the active <c>snapshot</c> key (the first key is created when none exists).</summary>
    Task<ISnapshotSigner> GetAsync(CancellationToken ct = default);

    /// <summary>All keys of <paramref name="purpose"/>, newest first, retired ones included (public halves are published).</summary>
    Task<IReadOnlyList<SigningKey>> ListPublicAsync(string purpose, CancellationToken ct = default);
}
