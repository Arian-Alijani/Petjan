using System.Security.Cryptography;
using Animora.Domain.Results;
using Animora.Infrastructure.Local.Security;

namespace Animora.Infrastructure.Local.Identity;

/// <summary>
/// Protects bearer tokens at rest: AES-256-GCM under a random key that lives only in the OS keystore (<see cref="ISecretStore"/>), so the
/// SQLite column never holds a usable token. Output is <c>base64(nonce‖tag‖ciphertext)</c>. Never throws on expected failures.
/// </summary>
internal sealed class TokenProtector(ISecretStore secrets)
{
    private const string KeyName = "session-token-key";
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly object _gate = new();
    private byte[]? _key;

    public Result<string> Protect(string plain)
    {
        if (plain.Length == 0) return Result<string>.Ok(string.Empty);
        var key = GetKey();
        if (key.IsFailure) return Result<string>.Fail(key.Error, key.Detail);

        var data = System.Text.Encoding.UTF8.GetBytes(plain);
        var packed = new byte[NonceSize + TagSize + data.Length];
        var nonce = packed.AsSpan(0, NonceSize);
        var tag = packed.AsSpan(NonceSize, TagSize);
        var cipher = packed.AsSpan(NonceSize + TagSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key.Value!, TagSize);
        aes.Encrypt(nonce, data, cipher, tag);
        return Result<string>.Ok(Convert.ToBase64String(packed));
    }

    public Result<string> Unprotect(string protectedText)
    {
        if (protectedText.Length == 0) return Result<string>.Ok(string.Empty);
        var key = GetKey();
        if (key.IsFailure) return Result<string>.Fail(key.Error, key.Detail);
        try
        {
            var packed = Convert.FromBase64String(protectedText);
            if (packed.Length < NonceSize + TagSize) return Result<string>.Fail(ErrorCodes.SessionExpired, "token_corrupt");
            var plain = new byte[packed.Length - NonceSize - TagSize];
            using var aes = new AesGcm(key.Value!, TagSize);
            aes.Decrypt(packed.AsSpan(0, NonceSize), packed.AsSpan(NonceSize + TagSize), packed.AsSpan(NonceSize, TagSize), plain);
            return Result<string>.Ok(System.Text.Encoding.UTF8.GetString(plain));
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return Result<string>.Fail(ErrorCodes.SessionExpired, "token_corrupt"); // wrong key / tampered: treat as no session
        }
    }

    private Result<byte[]> GetKey()
    {
        lock (_gate)
        {
            if (_key is not null) return Result<byte[]>.Ok(_key);
            var read = secrets.TryRead(KeyName);
            if (read.IsSuccess && read.Value is { Length: KeySize } existing)
            {
                _key = existing;
                return Result<byte[]>.Ok(existing);
            }

            var created = RandomNumberGenerator.GetBytes(KeySize);
            var written = secrets.Write(KeyName, created);
            if (written.IsFailure) return Result<byte[]>.Fail(written.Error, written.Detail);
            _key = created;
            return Result<byte[]>.Ok(created);
        }
    }
}
