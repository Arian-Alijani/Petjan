using System.Security.Cryptography;
using Animora.Application.Security;
using Animora.Domain.Results;

namespace Animora.Infrastructure.Local.Security;

/// <summary>
/// HMAC-SHA256 signer for local capability snapshots (B04b). The 32-byte key lives only in the OS keystore
/// (<see cref="ISecretStore"/>, name <see cref="SecretName"/>) — never in code or SQLite. Only a definite
/// <c>NOT_FOUND</c> generates a fresh key; any other keystore failure fails closed. Sign and Verify share
/// <see cref="CapabilitySnapshotCodec.Canonicalize"/> exactly; comparison is constant-time.
/// </summary>
public sealed class CapabilitySnapshotSigner(ISecretStore secrets) : ICapabilitySnapshotSigner
{
    public const string SecretName = "capability-snapshot-key";
    private const int KeyBytes = 32;

    private readonly Lock _gate = new();
    private byte[]? _key;

    public Result<string> Sign(StoredCapabilitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var mac = Compute(snapshot);
        return mac.IsFailure ? Result<string>.Fail(mac.Error, mac.Detail) : Result<string>.Ok(Convert.ToBase64String(mac.Value!));
    }

    public Result Verify(StoredCapabilitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.SnapshotSignature))
            return Result.Fail(ErrorCodes.CapabilitySnapshotTampered, "missing signature");

        var provided = new byte[KeyBytes];
        if (!Convert.TryFromBase64String(snapshot.SnapshotSignature, provided, out var written) || written != KeyBytes)
            return Result.Fail(ErrorCodes.CapabilitySnapshotTampered, "malformed signature");

        var expected = Compute(snapshot);
        if (expected.IsFailure) return Result.Fail(expected.Error, expected.Detail);
        return CryptographicOperations.FixedTimeEquals(expected.Value!, provided)
            ? Result.Ok()
            : Result.Fail(ErrorCodes.CapabilitySnapshotTampered, "signature mismatch");
    }

    private Result<byte[]> Compute(StoredCapabilitySnapshot snapshot)
    {
        var canonical = CapabilitySnapshotCodec.Canonicalize(snapshot);
        if (canonical.IsFailure) return canonical;
        var key = GetKey();
        if (key.IsFailure) return key;
        return Result<byte[]>.Ok(HMACSHA256.HashData(key.Value!, canonical.Value!));
    }

    private Result<byte[]> GetKey()
    {
        lock (_gate)
        {
            if (_key is not null) return Result<byte[]>.Ok(_key);

            var read = secrets.TryRead(SecretName);
            if (read.IsSuccess)
            {
                if (read.Value is not { Length: KeyBytes })
                    return Result<byte[]>.Fail(ErrorCodes.StoreKeyUnavailable, "Stored capability key has an invalid length.");
                return Result<byte[]>.Ok(_key = read.Value);
            }

            // Only a definite "absent" means first use; any other read failure fails closed (no silent new key).
            if (read.Error != ErrorCodes.NotFound)
                return Result<byte[]>.Fail(ErrorCodes.StoreKeyUnavailable, read.Detail);

            var fresh = RandomNumberGenerator.GetBytes(KeyBytes);
            var write = secrets.Write(SecretName, fresh);
            if (write.IsFailure) return Result<byte[]>.Fail(ErrorCodes.StoreKeyUnavailable, write.Detail);
            return Result<byte[]>.Ok(_key = fresh);
        }
    }
}
