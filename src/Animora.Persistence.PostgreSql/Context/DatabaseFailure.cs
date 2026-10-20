using Npgsql;

namespace Animora.Persistence.PostgreSql.Context;

/// <summary>Classifies provider exceptions without leaking Npgsql to the server project (layering gate).</summary>
public static class DatabaseFailure
{
    /// <summary>
    /// <see langword="true"/> when <paramref name="exception"/> (or an inner exception) is Npgsql's
    /// "connection pool has been exhausted" timeout — the signal to shed load with a 503 + Retry-After.
    /// </summary>
    public static bool IsPoolExhausted(Exception exception)
    {
        for (Exception? e = exception; e is not null; e = e.InnerException)
        {
            if (e is NpgsqlException npgsql
                && (npgsql.InnerException is TimeoutException
                    || npgsql.Message.Contains("connection pool has been exhausted", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <see langword="true"/> when <paramref name="exception"/> (or an inner exception) is a backend
    /// data-store fault that makes the operation impossible to complete right now but is NOT a defect in
    /// this request — the server is unreachable, shutting down, or its schema does not match this build
    /// (a migration has not been applied). These are the faults that must surface as a typed, retryable
    /// infrastructure error (503) instead of an opaque <c>UNEXPECTED</c> 500: the caller cannot fix them,
    /// and misreporting them as a generic failure hides the real cause (e.g. a login that 500s only
    /// because the deployed database is behind the build's expected schema head).
    /// </summary>
    public static bool IsBackendUnavailable(Exception exception)
    {
        for (Exception? e = exception; e is not null; e = e.InnerException)
        {
            // A broken/refused/timed-out connection: the backend cannot be reached at all.
            if (e is NpgsqlException { InnerException: System.Net.Sockets.SocketException } or TimeoutException)
            {
                return true;
            }

            if (e is PostgresException pg && IsBackendUnavailableSqlState(pg.SqlState))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// PostgreSQL SQLSTATEs that mean "the data store cannot serve this request", split into connection /
    /// availability faults (class 08, operator-intervention 57P0x) and schema-drift faults (class 42 for a
    /// table/column the applied migrations do not yet have). A schema mismatch is deliberately treated as a
    /// retryable infrastructure fault here: it is an operational deploy gap (apply the migration), not a bug
    /// in the request, and the readiness <c>schema</c> probe reports it independently.
    /// </summary>
    private static bool IsBackendUnavailableSqlState(string? sqlState) => sqlState switch
    {
        // Class 08 — connection exception.
        "08000" or "08003" or "08006" or "08001" or "08004" or "08007" or "08P01" => true,
        // Class 57 — operator intervention (admin shutdown, cannot connect now, crash shutdown).
        "57P01" or "57P02" or "57P03" => true,
        // Class 42 — schema drift: the object the build expects is not there yet (migration not applied).
        "42P01" /* undefined_table */ or "42703" /* undefined_column */ or "42P18" /* indeterminate datatype */ or "3F000" /* invalid_schema_name */ => true,
        _ => false,
    };
}
