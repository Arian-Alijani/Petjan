using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Animora.Domain.Results;

namespace Animora.Persistence.Sqlite.Maintenance;

/// <summary>
/// BAK-01: passphrase-wrapped escrow of the local store key, so an encrypted backup can be opened on a machine whose OS
/// keystore never held the key (PC death / reinstall / new machine). The store key itself is NEVER written in clear: the
/// <c>.keywrap</c> sidecar carries it AES-256-GCM-encrypted under a key derived from an owner-supplied backup passphrase with
/// PBKDF2-SHA256 (high iteration count, random salt). Wrong passphrase / tampering fails authentication (GCM tag).
/// </summary>
public static class StoreKeyEscrow
{
    /// <summary>Sidecar suffix written next to a backup <c>.db</c>.</summary>
    public const string Suffix = ".keywrap";

    private const int Version = 1;
    private const int Iterations = 600_000;
    private const int SaltBytes = 16;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private static readonly byte[] Aad = Encoding.ASCII.GetBytes("Animora.LocalStore.KeyWrap.v1");

    /// <summary>Wraps <paramref name="storeKey"/> under <paramref name="backupPassphrase"/>; returns the sidecar JSON bytes.</summary>
    public static byte[] ExportWrappedKey(string storeKey, string backupPassphrase)
    {
        ArgumentException.ThrowIfNullOrEmpty(storeKey);
        ArgumentException.ThrowIfNullOrEmpty(backupPassphrase);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var kek = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(backupPassphrase), salt, Iterations, HashAlgorithmName.SHA256, 32);
        var plain = Encoding.UTF8.GetBytes(storeKey);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagBytes];
        try
        {
            using var aes = new AesGcm(kek, TagBytes);
            aes.Encrypt(nonce, plain, cipher, tag, Aad);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
            CryptographicOperations.ZeroMemory(plain);
        }

        return JsonSerializer.SerializeToUtf8Bytes(new Envelope(
            Version, "PBKDF2-SHA256", Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag), Convert.ToBase64String(cipher)));
    }

    /// <summary>Unwraps a sidecar produced by <see cref="ExportWrappedKey"/>; fails closed on a wrong passphrase or a damaged file.</summary>
    public static Result<string> ImportWrappedKey(byte[] wrapped, string backupPassphrase)
    {
        if (string.IsNullOrEmpty(backupPassphrase))
            return Result<string>.Fail(ErrorCodes.StoreKeyUnavailable, "backup_passphrase_required");
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(wrapped);
            if (envelope is null || envelope.V != Version || envelope.Iter < 100_000 || envelope.Iter > 10_000_000)
                return Result<string>.Fail(ErrorCodes.StoreKeyUnavailable, "keywrap_unsupported");

            var salt = Convert.FromBase64String(envelope.Salt);
            var nonce = Convert.FromBase64String(envelope.Nonce);
            var tag = Convert.FromBase64String(envelope.Tag);
            var cipher = Convert.FromBase64String(envelope.Ct);
            if (nonce.Length != NonceBytes || tag.Length != TagBytes)
                return Result<string>.Fail(ErrorCodes.StoreKeyUnavailable, "keywrap_malformed");

            var kek = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(backupPassphrase), salt, envelope.Iter, HashAlgorithmName.SHA256, 32);
            var plain = new byte[cipher.Length];
            try
            {
                using var aes = new AesGcm(kek, TagBytes);
                aes.Decrypt(nonce, cipher, tag, plain, Aad);
                return Result<string>.Ok(Encoding.UTF8.GetString(plain));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(kek);
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch (CryptographicException)
        {
            return Result<string>.Fail(ErrorCodes.StoreKeyUnavailable, "backup_passphrase_rejected");
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return Result<string>.Fail(ErrorCodes.StoreKeyUnavailable, "keywrap_malformed");
        }
    }

    private sealed record Envelope(int V, string Kdf, int Iter, string Salt, string Nonce, string Tag, string Ct);
}

/// <summary>
/// BAK-01: installs a store key recovered from a <c>.keywrap</c> sidecar into the machine's OS keystore so the restored
/// (encrypted) backup can be opened. Implemented by the keystore-backed key provider; absent in plaintext/test compositions.
/// </summary>
public interface IStoreKeyInstaller
{
    /// <summary>Replaces the machine's store key with <paramref name="storeKey"/> (the keystore passphrase form).</summary>
    Result Install(string storeKey);
}
