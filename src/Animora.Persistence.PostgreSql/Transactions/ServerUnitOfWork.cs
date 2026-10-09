using Animora.Application.Abstractions;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Animora.Persistence.PostgreSql.Transactions;

/// <summary>
/// The server's PostgreSQL <see cref="IUnitOfWork"/>: every server command transaction runs through here.
/// <see cref="SaveAsync"/> is a plain <c>SaveChangesAsync</c> with store-exception → <see cref="Result"/>
/// mapping; <see cref="ExecuteAsync{T}"/> delegates to an <see cref="ITransactionRunner"/> that wraps the
/// work in an EF Core execution strategy, a <c>ReadCommitted</c> transaction and the tenant-session hook,
/// retrying serialization/deadlock failures. A handler's own failure <see cref="Result{T}"/> is returned
/// without throwing; cancellation is never swallowed.
/// </summary>
public sealed class ServerUnitOfWork(AnimoraServerDbContext context, ITransactionRunner runner) : IUnitOfWork
{
    /// <inheritdoc />
    public async Task<Result> SaveAsync(CancellationToken ct = default)
    {
        try
        {
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Fail(ErrorCodes.ConcurrencyConflict);
        }
    }

    /// <inheritdoc />
    public Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return runner.RunAsync(work, ct);
    }

    /// <summary>
    /// Shared store-exception → <see cref="Result{T}"/> mapping used by the runner. A
    /// <see cref="DbUpdateConcurrencyException"/> is a detected version conflict; a <c>23505</c>
    /// unique-violation is only mapped when the caller opted in via <paramref name="uniqueMapping"/>,
    /// otherwise it is rethrown. Returns <see langword="null"/> when <paramref name="exception"/> is not a
    /// mapped store fault, so the caller can decide whether it is transient/retryable or must propagate.
    /// </summary>
    internal static Result<T>? TryMapStoreFailure<T>(Exception exception, UniqueViolationMapping uniqueMapping)
    {
        switch (exception)
        {
            case DbUpdateConcurrencyException:
                return Result<T>.Fail(ErrorCodes.ConcurrencyConflict);

            case DbUpdateException { InnerException: PostgresException { SqlState: RetryPolicy.UniqueViolation } }
                when uniqueMapping.MapToConcurrencyConflict:
                return Result<T>.Fail(ErrorCodes.ConcurrencyConflict, "unique");

            default:
                return null;
        }
    }
}
