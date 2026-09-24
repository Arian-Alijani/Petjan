using System.Data.Common;
using Animora.Domain.Results;

namespace Animora.Application.Abstractions;

/// <summary>
/// The single exception → <see cref="ErrorCode"/> table, applied only by
/// <c>ExceptionMappingBehavior</c>. Application references neither EF Core nor
/// Microsoft.Data.Sqlite (CONVENTIONS §2), so provider exceptions are matched by full type name
/// along the inheritance chain (failure path only — never on the hot path).
/// </summary>
public static class ErrorMapping
{
    private const string EfConcurrency = "Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException";
    private const string SqliteException = "Microsoft.Data.Sqlite.SqliteException";

    /// <summary>
    /// <c>DbUpdateConcurrencyException</c> → <c>CONCURRENCY_CONFLICT</c>;
    /// <c>SqliteException</c> / <see cref="DbException"/> / <see cref="IOException"/> → <c>STORE_UNAVAILABLE</c>;
    /// everything else → <c>UNEXPECTED</c>. <see cref="OperationCanceledException"/> is never mapped:
    /// the behavior rethrows it before calling this.
    /// </summary>
    public static ErrorCode FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (IsOrDerivesFrom(exception, EfConcurrency)) return ErrorCodes.ConcurrencyConflict;
        if (exception is DbException or IOException || IsOrDerivesFrom(exception, SqliteException))
            return ErrorCodes.StoreUnavailable;
        return ErrorCodes.Unexpected;
    }

    private static bool IsOrDerivesFrom(Exception exception, string fullName)
    {
        for (var type = exception.GetType(); type is not null; type = type.BaseType)
            if (string.Equals(type.FullName, fullName, StringComparison.Ordinal)) return true;
        return false;
    }
}
