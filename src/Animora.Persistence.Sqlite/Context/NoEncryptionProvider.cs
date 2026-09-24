using Microsoft.Data.Sqlite;

namespace Animora.Persistence.Sqlite.Context;

/// <summary>Default provider while encryption is off or K2 is not approved: leaves the connection untouched.</summary>
public sealed class NoEncryptionProvider : IStoreEncryptionProvider
{
    public void Apply(SqliteConnection connection, string passphrase)
    {
    }
}
