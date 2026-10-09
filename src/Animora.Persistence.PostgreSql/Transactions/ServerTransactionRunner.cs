using System.Data;
using Animora.Persistence.PostgreSql.Concurrency;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Animora.Persistence.PostgreSql.Transactions;

/// <summary>
/// Production <see cref="ITransactionRunner"/>. Each attempt opens a <c>ReadCommitted</c> transaction via
/// the context's EF Core execution strategy, runs the tenant-session hook (RLS-ready, off by default), runs
/// the caller's work, then commits on success or rolls back on failure. Serialization (<c>40001</c>) and
/// deadlock (<c>40P01</c>) faults are retried up to <see cref="RetryPolicy.MaxAttempts"/> times with jittered
/// back-off; a mapped store fault (concurrency / opted-in unique violation) becomes a failure
/// <see cref="Result{T}"/>; cancellation always propagates.
/// </summary>
internal sealed class ServerTransactionRunner(
    AnimoraServerDbContext context,
    ITenantContext tenantContext,
    ITenantSessionInitializer sessionInitializer) : ITransactionRunner
{
    // S03 default: callers do not opt into unique-violation mapping. A future overload threads this through.
    private static readonly UniqueViolationMapping UniqueMapping = UniqueViolationMapping.None;

    public async Task<Result<T>> RunAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(work);

        try
        {
            return await RetryPolicy.ExecuteAsync(
                attempt: (_, token) => RunOnceAsync(work, token),
                isTransient: IsRetryable,
                rng: Random.Shared.NextDouble,
                delay: static (span, token) => Task.Delay(span, token),
                ct: ct).ConfigureAwait(false);
        }
        catch (ReservationConcurrencyException) { return Result<T>.Fail(ErrorCodes.ConcurrencyConflict); }
    }

    private static bool IsRetryable(Exception ex) => ex is ReservationConcurrencyException
        || ex is PostgresException pg && RetryPolicy.IsTransient(pg.SqlState)
        || ex is DbUpdateException { InnerException: PostgresException inner } && RetryPolicy.IsTransient(inner.SqlState);

    private async Task<Result<T>> RunOnceAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct)
    {
        // The execution strategy owns one attempt's begin→work→commit; RetryPolicy owns the retry cadence,
        // so the strategy stays non-retrying (no double jitter) and the whole transaction is re-run as a unit.
        IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();
        try { return await strategy.ExecuteAsync(() => ExecuteTransactionAsync(work, ct)).ConfigureAwait(false); }
        finally { context.ChangeTracker.Clear(); }
    }

    private async Task<Result<T>> ExecuteTransactionAsync<T>(
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken ct)
    {
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);

        // RLS-ready hook: bound on every begin AND every retry (each retry re-enters here). Noop by default.
        await sessionInitializer
            .InitializeAsync(context.Database.GetDbConnection(), tenantContext.TenantId ?? string.Empty, ct)
            .ConfigureAwait(false);

        Result<T> outcome;
        try
        {
            outcome = await work(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Never swallowed: let the transaction dispose (roll back) and propagate.
            throw;
        }
        catch (Exception ex)
        {
            // A mapped store fault (concurrency / opted-in unique) becomes a failure Result and commits
            // nothing; anything else (incl. transient 40001/40P01) propagates so RetryPolicy can act.
            Result<T>? mapped = ServerUnitOfWork.TryMapStoreFailure<T>(ex, UniqueMapping);
            if (mapped is { } failure)
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                return failure;
            }

            throw;
        }

        if (outcome.IsSuccess)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        else
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
        }

        return outcome;
    }
}
