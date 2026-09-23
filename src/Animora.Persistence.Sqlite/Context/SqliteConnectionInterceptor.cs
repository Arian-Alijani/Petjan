using System.Data.Common;
using Animora.Persistence.Sqlite.Options;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Animora.Persistence.Sqlite.Context;

public sealed class SqliteConnectionInterceptor(IOptions<LocalStoreOptions> options, IStoreKeyProvider keys)
    : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        // A11 replaces this no-op seam with native cipher key application before any read.
        _ = keys.GetKey();
        foreach (var pragma in new[] { "journal_mode=WAL", "synchronous=NORMAL",
                     $"busy_timeout={Math.Clamp(options.Value.BusyTimeoutMs, 0, 60000)}", "foreign_keys=ON" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA {pragma};";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
