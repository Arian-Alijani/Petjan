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
    public void Apply(SqliteConnection connection, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (string.IsNullOrEmpty(passphrase))
            throw new InvalidOperationException($"{ErrorCodes.StoreKeyUnavailable.Code}: empty store key.");

        if (!StoreCipherProbe.IsCipherAvailable())
            throw new InvalidOperationException(
                $"{ErrorCodes.StoreKeyUnavailable.Code}: encryption requested but the native cipher bundle (SQLite3 Multiple Ciphers) is not loaded; refusing to open a plaintext store.");

        // PRAGMA does not accept parameters; the passphrase is embedded as an escaped SQL literal.
        Execute(connection, $"PRAGMA key = '{passphrase.Replace("'", "''", StringComparison.Ordinal)}';");
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
