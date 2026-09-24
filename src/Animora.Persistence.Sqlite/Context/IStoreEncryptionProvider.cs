using Microsoft.Data.Sqlite;

namespace Animora.Persistence.Sqlite.Context;

/// <summary>
/// Applies the store cipher key to a freshly opened connection, before any other statement runs.
/// Seam only (STACK.md §12 K2): the real cipher cannot be switched on until K2 is recorded (E08).
/// </summary>
public interface IStoreEncryptionProvider
{
    void Apply(SqliteConnection connection, string passphrase);
}
