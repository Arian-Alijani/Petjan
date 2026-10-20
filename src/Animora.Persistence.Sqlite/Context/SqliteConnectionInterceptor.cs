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
    // SQL-05: the pragma set depends only on startup-fixed options, so it is built once (no per-open array/string allocation).
    private readonly string[] _pragmas =
    [
        "PRAGMA journal_mode=WAL;", "PRAGMA synchronous=NORMAL;",
        $"PRAGMA busy_timeout={Math.Clamp(options.Value.BusyTimeoutMs, 0, 60000)};", "PRAGMA foreign_keys=ON;"
    ];

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyKey(connection);
        foreach (var pragma in _pragmas)
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
        foreach (var pragma in _pragmas)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = pragma;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Same key + pragma path for connections opened outside EF (maintenance). Call right after <c>Open()</c>.
    /// </summary>
    public void Prepare(DbConnection connection, bool sessionPragmas = true)
    {
        ApplyKey(connection);
        if (!sessionPragmas) return;
        foreach (var pragma in _pragmas)
        {
            using var command = connection.CreateCommand();
            command.CommandText = pragma;
            command.ExecuteNonQuery();
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
}
