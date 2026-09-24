using System.Data.Common;
using Animora.Domain.Results;
using Animora.Persistence.Sqlite.Options;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Animora.Persistence.Sqlite.Context;

/// <summary>
/// Runs on every connection open: (a) cipher key when encryption is enabled, then (b) session pragmas.
/// Singleton-safe: every dependency is a singleton, so it fits the shared <c>IDbContextFactory</c>.
/// </summary>
public sealed class SqliteConnectionInterceptor(
    IOptions<LocalStoreOptions> options, IStoreKeyProvider keys, IStoreEncryptionProvider encryption)
    : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyKey(connection);
        foreach (var pragma in Pragmas())
        {
            using var command = connection.CreateCommand();
            command.CommandText = pragma;
            command.ExecuteNonQuery();
        }
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ApplyKey(connection);
        foreach (var pragma in Pragmas())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = pragma;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private void ApplyKey(DbConnection connection)
    {
        if (!options.Value.EncryptionEnabled) return;
        // Fail closed: an encrypted store is never opened without its key.
        var key = keys.GetKey();
        if (string.IsNullOrEmpty(key))
            throw new InvalidOperationException($"{ErrorCodes.StoreKeyUnavailable.Code}: no store key available.");
        encryption.Apply((SqliteConnection)connection, key);
    }

    private string[] Pragmas() =>
    [
        "PRAGMA journal_mode=WAL;", "PRAGMA synchronous=NORMAL;",
        $"PRAGMA busy_timeout={Math.Clamp(options.Value.BusyTimeoutMs, 0, 60000)};", "PRAGMA foreign_keys=ON;"
    ];
}
