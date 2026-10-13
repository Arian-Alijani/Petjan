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
}
