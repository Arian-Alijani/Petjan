using Animora.Domain.Results;
using Microsoft.Data.Sqlite;

namespace Animora.Persistence.Sqlite.Context;

/// <summary>
/// SQLite3 Multiple Ciphers keying. Compiled but unreachable: selected only when
/// <c>EncryptionEnabled &amp;&amp; K2Approved</c>; the native cipher bundle is pinned in E08, not here.
/// </summary>
public sealed class MultipleCiphersEncryptionProvider : IStoreEncryptionProvider
{
    public void Apply(SqliteConnection connection, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (string.IsNullOrEmpty(passphrase))
            throw new InvalidOperationException($"{ErrorCodes.StoreKeyUnavailable.Code}: empty store key.");

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
