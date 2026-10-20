using System.Security.Cryptography;
using System.Text;
using Animora.Domain.Results;
using Microsoft.Data.Sqlite;

namespace Animora.Persistence.Sqlite.Context;

/// <summary>
/// SQLite3 Multiple Ciphers keying. Selected when <c>EncryptionEnabled &amp;&amp; K2Approved</c> (the shipped
/// desktop turns both on whenever the cipher bundle is loaded — <see cref="Options.LocalStoreEncryptionDefaults"/>).
/// Fail-closed (SQL-01): on a plain <c>e_sqlite3</c> build <c>PRAGMA key</c> is a silent no-op, so keying is refused
/// unless the loaded native library actually carries the cipher — it can never silently write plaintext.
/// </summary>
public sealed class MultipleCiphersEncryptionProvider : IStoreEncryptionProvider
{
    private static readonly byte[] KeyDomain = Encoding.ASCII.GetBytes("Animora.LocalStore.RawKey.v1\0");

    private static byte[] Concat(byte[] prefix, byte[] value)
    {
        var buffer = new byte[prefix.Length + value.Length];
        prefix.CopyTo(buffer, 0);
        value.CopyTo(buffer, prefix.Length);
        return buffer;
    }

    public void Apply(SqliteConnection connection, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (string.IsNullOrEmpty(passphrase))
            throw new InvalidOperationException($"{ErrorCodes.StoreKeyUnavailable.Code}: empty store key.");

        if (!StoreCipherProbe.IsCipherAvailable())
            throw new InvalidOperationException(
                $"{ErrorCodes.StoreKeyUnavailable.Code}: encryption requested but the native cipher bundle (SQLite3 Multiple Ciphers) is not loaded; refusing to open a plaintext store.");

        // SQL-03: the cipher is keyed with a fixed-size RAW key (x'<64 hex>' = 32 bytes, KDF bypassed) derived from the
        // keystore passphrase, never with the passphrase itself as an interpolated SQL string literal. The derivation is a
        // domain-separated SHA-256 (the passphrase is already 256 bits of CSPRNG output from LocalDbKeyService, so no
        // stretching is needed). The key material only ever exists as a short-lived fixed-format hex literal; the byte
        // buffers are zeroed immediately and there is no quote-escaping surface left to get wrong.
        var passphraseBytes = Encoding.UTF8.GetBytes(passphrase);
        var rawKey = SHA256.HashData(Concat(KeyDomain, passphraseBytes));
        CryptographicOperations.ZeroMemory(passphraseBytes);
        try
        {
            Execute(connection, $"PRAGMA key = \"x'{Convert.ToHexString(rawKey)}'\";");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rawKey);
        }
        Execute(connection, "PRAGMA cipher_page_size = 4096;");
        try
        {
            // First real read: a wrong key surfaces here as "file is not a database".
            using var verify = connection.CreateCommand();
            verify.CommandText = "SELECT count(*) FROM sqlite_master;";
            verify.ExecuteScalar();
        }
        catch (SqliteException ex)
        {
            throw new InvalidOperationException($"{ErrorCodes.StoreKeyUnavailable.Code}: store key rejected.", ex);
        }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
