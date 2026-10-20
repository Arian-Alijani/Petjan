using System.Security.Cryptography;
using System.Text;
using Animora.Domain.Results;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Maintenance;
using Animora.Persistence.Sqlite.Options;
using Microsoft.Extensions.Options;

namespace Animora.Infrastructure.Local.Security;

/// <summary>
/// Local store passphrase from the OS keystore (CONVENTIONS §7). Fails closed: when encryption is on and the
/// key cannot be read, the store must not open.
/// </summary>
public sealed class LocalDbKeyService : IStoreKeyProvider, IStoreKeyInstaller
{
    public const string SecretName = "local-db-key";
    /// <summary>BAK-01: the key replaced by an escrow install, retained so the quarantined previous store stays recoverable.</summary>
    public const string PreviousSecretName = "local-db-key-previous";
    private const int KeyBytes = 32;

    private readonly ISecretStore _secrets;
    private readonly LocalStoreOptions _options;
    private readonly Lock _gate = new();
    private string? _cached;

    public LocalDbKeyService(ISecretStore secrets, IOptions<LocalStoreOptions> options)
    {
        _secrets = secrets;
        _options = options.Value;
    }

    public KeySource Source => !_options.EncryptionEnabled
        ? KeySource.None
        : _secrets is FileFallbackSecretStore ? KeySource.InsecureFallback : KeySource.OsKeystore;

    public Result<string> GetOrCreatePassphrase()
    {
        lock (_gate)
        {
            if (_cached is not null) return _cached;

            var read = _secrets.TryRead(SecretName);
            if (read.IsSuccess)
            {
                var existing = Encoding.UTF8.GetString(read.Value!);
                if (existing.Length == 0)
                    return Result<string>.Fail(ErrorCodes.StoreKeyUnavailable, "Stored key is empty.");
                return _cached = existing;
            }

            // Only a definite "absent" means first run; any other read failure fails closed.
            if (read.Error != ErrorCodes.NotFound)
                return Result<string>.Fail(ErrorCodes.StoreKeyUnavailable, read.Detail);

            var passphrase = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyBytes));
            var write = _secrets.Write(SecretName, Encoding.UTF8.GetBytes(passphrase));
            if (write.IsFailure) return Result<string>.Fail(ErrorCodes.StoreKeyUnavailable, write.Detail);
            return _cached = passphrase;
        }
    }

    /// <inheritdoc />
    /// <remarks>BAK-01: used only when restoring a backup whose key was recovered from its passphrase-wrapped sidecar.</remarks>
    public Result Install(string storeKey)
    {
        if (string.IsNullOrEmpty(storeKey)) return Result.Fail(ErrorCodes.StoreKeyUnavailable, "empty_store_key");
        lock (_gate)
        {
            // Never orphan the quarantined previous store: keep the key it was sealed with under a separate secret name.
            var previous = _secrets.TryRead(SecretName);
            if (previous.IsSuccess && previous.Value is { Length: > 0 })
                _secrets.Write(PreviousSecretName, previous.Value);

            var write = _secrets.Write(SecretName, Encoding.UTF8.GetBytes(storeKey));
            if (write.IsFailure) return Result.Fail(ErrorCodes.StoreKeyUnavailable, write.Detail);
            _cached = storeKey;
            return Result.Ok();
        }
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Encryption is on and the key is unavailable (STORE_KEY_UNAVAILABLE).</exception>
    public string? GetKey()
    {
        if (!_options.EncryptionEnabled) return null;
        var key = GetOrCreatePassphrase();
        if (key.IsFailure)
            throw new InvalidOperationException($"{ErrorCodes.StoreKeyUnavailable.Code}: {key.Detail}");
        return key.Value;
    }
}
